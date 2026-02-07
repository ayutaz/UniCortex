# HNSW Dense ベクトル検索 設計書

## 1. HNSW アルゴリズム概要

HNSW (Hierarchical Navigable Small World) は、高次元ベクトル空間における近似最近傍探索 (Approximate Nearest Neighbor, ANN) のためのグラフベースアルゴリズムである。

- **論文**: "Efficient and robust approximate nearest neighbor search using Hierarchical Navigable Small World graphs" (Malkov & Yashunin, 2018)
- **基本原理**: Skip List に着想を得た多層グラフ構造を構築し、上位レイヤーから下位レイヤーへ段階的に探索を絞り込む

### 多層グラフの直観

```
Layer 3:  [A] ─────────────────── [D]              (少数ノード, 長距離ジャンプ)
Layer 2:  [A] ──── [B] ─────────── [D]
Layer 1:  [A] ──── [B] ──── [C] ── [D] ── [E]
Layer 0:  [A] ─ [B] ─ [C] ─ [D] ─ [E] ─ [F] ─ [G]  (全ノード, 精密な接続)
```

- **上位レイヤー**: ノード数が少なく、グラフのエッジは長距離をカバーする。クエリベクトルに近い領域へ高速にナビゲーションする役割を持つ。
- **下位レイヤー (Layer 0)**: 全ノードが存在し、近傍ノード同士が密に接続される。精密な最近傍探索を行う。
- 各ノードが存在するレイヤーは挿入時に確率的に決定される (exponential decay)。大半のノードは Layer 0 のみに存在し、上位レイヤーに存在するノードは指数的に減少する。

探索はエントリポイントから最上位レイヤーで開始し、各レイヤーで greedy に最近傍を辿りながら下位レイヤーへ降りていく。この階層的なアプローチにより、O(log N) に近い探索計算量を実現する。

## 2. デフォルトパラメータ

| パラメータ | デフォルト値 | 説明 |
|---|---|---|
| `M` | 16 | Layer 1 以上の各ノードの最大接続数 |
| `M0` | 32 (`2 * M`) | Layer 0 の各ノードの最大接続数 |
| `efConstruction` | 200 | 構築時の探索幅 (候補リストサイズ) |
| `efSearch` | 50 | 検索時の探索幅 (候補リストサイズ) |
| `mL` | `1 / ln(M)` ≈ 0.36 | レイヤー選択の確率パラメータ |
| `maxLevel` | ~6 (50K ノードの場合) | 最大レイヤー数 (理論値) |

### パラメータの影響

- **M の増加**: 検索精度が向上するが、メモリ使用量と構築時間が増加する。M=16 は精度・メモリのバランスが良い標準的な値。
- **efConstruction の増加**: 構築時の精度が向上するが、構築速度が低下する。efConstruction >= 2*M を推奨。
- **efSearch の増加**: 検索精度 (Recall) が向上するが、検索レイテンシが増加する。アプリケーションの要件に応じてチューニングする。
- **mL**: 論文推奨の `1/ln(M)` を使用。この値により、各レイヤーのノード数が指数的に減少し、Skip List と同様の最適な構造になる。

### maxLevel の理論的導出

50,000 ノードの場合:
```
maxLevel = floor(ln(N) * mL) = floor(ln(50000) * (1/ln(16))) ≈ floor(10.82 * 0.36) ≈ 3.9
```
実装では安全マージンを加え、maxLevel = 6 程度を上限とする。

## 3. データ構造設計

### ノードメタデータ

```csharp
/// <summary>
/// HNSW グラフ内の各ノードのメタデータ。
/// Burst 互換の値型として定義する。
/// </summary>
public struct HnswNodeMeta
{
    /// <summary>このノードが存在する最大レイヤー。</summary>
    public int MaxLayer;

    /// <summary>Neighbors 配列内の隣接リスト開始オフセット。</summary>
    public int NeighborOffset;
}
```

### グラフ構造

```csharp
/// <summary>
/// HNSW グラフの全体構造。
/// NativeContainer ベースで Burst/Jobs 互換。
/// </summary>
public struct HnswGraph : IDisposable
{
    /// <summary>全ノードのメタデータ。インデックス = ノード ID。</summary>
    public NativeArray<HnswNodeMeta> Nodes;

    /// <summary>flat 隣接リスト。全レイヤーの全ノードの隣接ノード ID を格納。</summary>
    public NativeArray<int> Neighbors;

    /// <summary>各スロットの実際の接続数。Neighbors と同じインデックス体系。</summary>
    public NativeArray<int> NeighborCounts;

    /// <summary>ベクトルデータ。SoA レイアウトで格納 (Core の VectorStore を参照)。</summary>
    /// <remarks>Core モジュールの VectorStore が管理するため、ここでは参照のみ。</remarks>

    /// <summary>現在のエントリポイント (最上位レイヤーのノード ID)。</summary>
    public int EntryPoint;

    /// <summary>現在のグラフの最大レイヤー。</summary>
    public int MaxLayer;

    /// <summary>Layer 1 以上の最大接続数。</summary>
    public int M;

    /// <summary>Layer 0 の最大接続数 (通常 2*M)。</summary>
    public int M0;

    /// <summary>登録済みノード数。</summary>
    public int Count;

    /// <summary>ソフト削除フラグ。</summary>
    public NativeArray<bool> Deleted;

    public void Dispose()
    {
        if (Nodes.IsCreated) Nodes.Dispose();
        if (Neighbors.IsCreated) Neighbors.Dispose();
        if (NeighborCounts.IsCreated) NeighborCounts.Dispose();
        if (Deleted.IsCreated) Deleted.Dispose();
    }
}
```

### 探索時のワークバッファ

```csharp
/// <summary>
/// 探索中に使用する一時バッファ。
/// Job ごとにスレッドローカルで確保し、再利用する。
/// </summary>
public struct HnswSearchBuffer : IDisposable
{
    /// <summary>訪問済みノードのビットセット。</summary>
    public NativeBitArray Visited;

    /// <summary>候補リスト (MinHeap)。</summary>
    public NativeList<HnswCandidate> Candidates;

    /// <summary>結果リスト (MaxHeap, 距離の大きい順)。</summary>
    public NativeList<HnswCandidate> Results;

    public void Dispose() { /* ... */ }
}

public struct HnswCandidate
{
    public int NodeId;
    public float Distance;
}
```

## 4. グラフメモリレイアウト (flat 隣接リスト)

グラフの隣接リストは単一の `NativeArray<int> Neighbors` に flat に格納する。ノードごとに各レイヤーのスロットを固定長で事前確保し、`HnswNodeMeta.NeighborOffset` でアクセスする。

### レイアウト方針

- **Layer 0**: 各ノードに `M0` スロット分を確保 (固定長)
- **Layer 1 以上**: 各ノードに `M` スロット分を確保 (固定長)
- 未使用スロットは `-1` (無効値) で埋める

### メモリレイアウト図

```
Neighbors 配列 (flat):

ノード 0 (MaxLayer=2):
┌─────────────────────────┬──────────────────┬──────────────────┐
│  Layer 0 (M0=32 slots)  │ Layer 1 (M=16)   │ Layer 2 (M=16)   │
│  offset: 0              │ offset: 32       │ offset: 48       │
└─────────────────────────┴──────────────────┴──────────────────┘

ノード 1 (MaxLayer=0):
┌─────────────────────────┐
│  Layer 0 (M0=32 slots)  │
│  offset: 64             │
└─────────────────────────┘

ノード 2 (MaxLayer=1):
┌─────────────────────────┬──────────────────┐
│  Layer 0 (M0=32 slots)  │ Layer 1 (M=16)   │
│  offset: 96             │ offset: 128      │
└─────────────────────────┴──────────────────┘

...
```

### オフセット計算

ノード `i` の `NeighborOffset` は挿入時に決定される:

```
NeighborOffset(i) = 前ノードまでの累積スロット数
各ノードのスロット数 = M0 + M * MaxLayer(i)
```

ノード `i` のレイヤー `l` の隣接リスト開始位置:

```
layer_offset(i, l) =
    l == 0 の場合: NeighborOffset(i)
    l >= 1 の場合: NeighborOffset(i) + M0 + M * (l - 1)
```

### オフセット計算のオーバーフロー検証

`NeighborOffset` の計算 `M0 + M * MaxLayer(i)` は、大量ノードの累積で `int` の最大値を超える可能性がある。

- **検証**: ノード追加時に累積オフセットが `int.MaxValue` を超えないことを確認する
- **対策**: 累積計算に `long` を使用し、`int` 範囲を超える場合は `CapacityExceeded` エラーを返す
- **実用上の安全性**: 50,000 ノード × 平均 37.8 スロット × 4 bytes ≈ 7.56 MB であり、`int.MaxValue` (2GB) の範囲内で十分に収まる。ただし防御的コーディングとして検証を実装する

詳細なセキュリティガイドラインは [11-security-guidelines.md](./11-security-guidelines.md) を参照。

### NeighborCounts 配列

`NeighborCounts` は `Neighbors` と同様の flat 構造で、各スロットの実際の接続数を保持する。これにより、固定長スロット内の有効な隣接ノード数を O(1) で取得できる。

> **設計判断**: 固定長スロット方式を採用する理由は、動的なメモリ割り当てを回避し Burst 互換性を維持するためである。トレードオフとして、MaxLayer が低いノードでも上位レイヤーのスロット分のメモリは消費しないが、Layer 0 の M0 スロットは全ノードで確保される。50,000 ノード・M0=32 の場合、Layer 0 だけで `50,000 * 32 * 4 bytes = 6.4 MB` を消費する。

> **設計検討**: NeighborCounts 配列の代わりに、Neighbors 配列の未使用スロットを `-1` でマークする方式も考えられる。この場合 NeighborCounts 配列（~7.56 MB）を削減でき、メモリ効率が向上する。トレードオフとして、有効な隣接ノード数の取得に O(M) のスキャンが必要になる。現時点では NeighborCounts 方式を採用するが、メモリ制約が厳しい環境では -1 マーク方式への移行を検討する。

## 5. INSERT アルゴリズム

新しいベクトル `q` をグラフに挿入する手順を以下に示す。

### 疑似コード

```
INSERT(hnsw, q, M, M0, efConstruction, mL):

    // Step 1: 新ノードの挿入レイヤーを確率的に決定
    l = floor(-ln(uniform(0, 1)) * mL)

    // Step 2: ノードを登録し、隣接リストのスロットを確保
    nodeId = hnsw.AllocateNode(l)  // NeighborOffset を計算・設定
    hnsw.StoreVector(nodeId, q)

    // Step 3: エントリポイントが未設定なら、このノードをエントリポイントにして終了
    if hnsw.Count == 1:
        hnsw.EntryPoint = nodeId
        hnsw.MaxLayer = l
        return

    ep = hnsw.EntryPoint
    L = hnsw.MaxLayer

    // Step 4: 上位レイヤーを greedy search で降りる (ef=1)
    //         挿入レイヤーより上のレイヤーではエッジを張らない
    for lc = L downto l+1:
        ep = SEARCH_LAYER(hnsw, q, ep, ef=1, lc)
        ep = nearest(ep)  // 最近傍 1 ノードだけ保持

    // Step 5: 挿入レイヤーから Layer 0 まで、候補を探索しエッジを追加
    for lc = min(L, l) downto 0:
        candidates = SEARCH_LAYER(hnsw, q, ep, efConstruction, lc)

        // NeighborSelection: M 個 (Layer 0 なら M0 個) を選択
        maxConn = (lc == 0) ? M0 : M
        neighbors = SELECT_NEIGHBORS(hnsw, q, candidates, maxConn)

        // 双方向エッジを追加
        for each n in neighbors:
            ADD_EDGE(hnsw, nodeId, n, lc)
            ADD_EDGE(hnsw, n, nodeId, lc)

            // 既存ノードの接続数が maxConn を超えたらプルーニング
            if NEIGHBOR_COUNT(hnsw, n, lc) > maxConn:
                existingNeighbors = GET_NEIGHBORS(hnsw, n, lc)
                pruned = SELECT_NEIGHBORS(hnsw, n.vector, existingNeighbors, maxConn)
                SET_NEIGHBORS(hnsw, n, lc, pruned)

        ep = nearest(candidates)

    // Step 6: 新ノードのレイヤーが現在の最大レイヤーを超えたらエントリポイントを更新
    if l > L:
        hnsw.EntryPoint = nodeId
        hnsw.MaxLayer = l
```

### INSERT の計算量

- 上位レイヤーの greedy search: O(log N) -- レイヤー数に比例
- 各レイヤーの探索: O(efConstruction * M) -- 候補数 x 接続数
- 全体: O(efConstruction * M * log N)

## 6. SEARCH アルゴリズム

クエリベクトル `q` に対する Top-K 近傍検索の手順を以下に示す。

### 疑似コード

```
SEARCH(hnsw, q, K, efSearch):

    // ガード: 空グラフの場合は空配列を返す
    if hnsw.Count == 0:
        return empty array

    // ガード: K > 有効ノード数の場合、有効ノード数にクランプする
    // 有効ノード数 = Count - DeletedCount
    effectiveCount = hnsw.Count - hnsw.DeletedCount
    if effectiveCount == 0:
        return empty array
    actualK = min(K, effectiveCount)

    ep = hnsw.EntryPoint
    L = hnsw.MaxLayer

    // Step 1: 上位レイヤーを greedy search で降りる (ef=1)
    for lc = L downto 1:
        ep = SEARCH_LAYER(hnsw, q, ep, ef=1, lc)
        ep = nearest(ep)

    // Step 2: Layer 0 で efSearch 個の候補を収集
    candidates = SEARCH_LAYER(hnsw, q, ep, efSearch, layer=0)

    // Step 3: 候補から Top-actualK を選択 (距離の小さい順)
    results = TOP_K(candidates, actualK)

    return results  // 結果件数は min(K, effectiveCount) 以下
```

### SEARCH_LAYER の疑似コード

```
SEARCH_LAYER(hnsw, q, entryPoints, ef, layer):

    visited = new BitSet(hnsw.Count)
    candidates = new MinHeap()   // 未探索の候補 (距離が小さい順に取り出す)
    results = new MaxHeap()      // 現在の Top-ef 結果 (距離が大きい順に取り出す)

    for each ep in entryPoints:
        visited.Set(ep)
        dist = Distance(q, hnsw.GetVector(ep))
        candidates.Push(ep, dist)
        results.Push(ep, dist)

    while candidates is not empty:
        c = candidates.Pop()  // 最も距離が小さい未探索候補

        // 探索打ち切り条件: 候補の距離が結果の最大距離より大きい
        if c.Distance > results.Peek().Distance:
            break

        for each neighbor n in GET_NEIGHBORS(hnsw, c.NodeId, layer):
            if n == -1: continue        // 無効スロット
            if visited.Get(n): continue  // 訪問済み
            if hnsw.Deleted[n]: continue // ソフト削除済み
            visited.Set(n)

            dist = Distance(q, hnsw.GetVector(n))

            if results.Count < ef OR dist < results.Peek().Distance:
                candidates.Push(n, dist)
                results.Push(n, dist)
                if results.Count > ef:
                    results.Pop()  // 最も遠い候補を除去

    return results
```

### SEARCH の計算量

- 上位レイヤーの greedy search: O(log N)
- Layer 0 の探索: O(efSearch * M0)
- 全体: O(efSearch * M0 + log N)

## 7. DELETE アルゴリズム

### ソフト削除方式

UniCortex では**ソフト削除**を採用する。ノードの `Deleted` フラグを `true` に設定し、検索時にスキップする。

```
DELETE(hnsw, nodeId):

    // Step 1: 削除フラグを設定
    hnsw.Deleted[nodeId] = true
    hnsw.DeletedCount++

    // Step 2: エントリポイントが削除された場合、代替を探す
    if nodeId == hnsw.EntryPoint:
        newEntry = FIND_NON_DELETED_ENTRY(hnsw, nodeId)
        if newEntry == -1:
            // 全ノードが削除済み → グラフは空
            hnsw.EntryPoint = -1
            hnsw.MaxLayer = -1
        else:
            hnsw.EntryPoint = newEntry
            // MaxLayer を新エントリポイントのレイヤーに更新
            hnsw.MaxLayer = hnsw.Nodes[newEntry].MaxLayer
```

### FIND_NON_DELETED_ENTRY 補助関数

エントリポイントが削除された場合に、新しいエントリポイントを探索する。
まず同レイヤーの隣接ノードを確認し、見つからなければ下位レイヤーに降りて探索する。

```
FIND_NON_DELETED_ENTRY(hnsw, deletedEntryId):

    // Step 1: 現在の MaxLayer から降順に探索
    for layer = hnsw.MaxLayer downto 0:
        // 削除されたエントリの隣接ノードを確認
        neighbors = GET_NEIGHBORS(hnsw, deletedEntryId, layer)
        for each n in neighbors:
            if n != -1 AND NOT hnsw.Deleted[n]:
                // 非削除ノードのうち最大レイヤーが最も高いものを選択
                if hnsw.Nodes[n].MaxLayer >= layer:
                    return n

    // Step 2: 隣接ノードから見つからない場合、全ノードを線形スキャン
    bestNode = -1
    bestLayer = -1
    for i = 0 to hnsw.Count - 1:
        if NOT hnsw.Deleted[i]:
            if hnsw.Nodes[i].MaxLayer > bestLayer:
                bestLayer = hnsw.Nodes[i].MaxLayer
                bestNode = i

    return bestNode  // -1 の場合は全ノード削除済み
```

### MaxLayer 更新ロジック

エントリポイント変更時、新エントリポイントの MaxLayer がグラフ全体の MaxLayer となる。
ただし、他にも高レイヤーに存在する非削除ノードがある可能性がある。

**方針**: `FIND_NON_DELETED_ENTRY` は最大レイヤーが最も高い非削除ノードを優先して選択するため、
新エントリポイントの MaxLayer をグラフの MaxLayer として採用する。
厳密な MaxLayer 計算（全ノードスキャン）はリビルド時に行う。

### Rebuild API

```csharp
/// <summary>
/// ソフト削除されたノードを物理的に除去し、グラフを再構築する。
/// 削除率が高い場合 (例: 20% 以上) に呼び出すことを推奨する。
/// </summary>
/// <param name="vectorStorage">有効なベクトルデータを保持する VectorStorage。</param>
/// <param name="config">HNSW パラメータ (M, M0, efConstruction, mL)。</param>
/// <returns>
/// 成功時: 新しい HnswGraph を含む Result。
///         呼び出し元は旧 HnswGraph を Dispose すること。
/// 失敗時: CapacityExceeded (有効ノード数が新グラフの容量を超過)。
/// </returns>
public static Result<HnswGraph> Rebuild(
    HnswGraph oldGraph,
    VectorStorage vectorStorage,
    HnswConfig config) { ... }
```

Rebuild の処理フロー:
1. 旧グラフの非削除ノード一覧を取得
2. 新しい HnswGraph を作成（容量 = 有効ノード数）
3. 有効ノードを順に INSERT
4. 新グラフを返却（呼び出し元が旧グラフを Dispose）

### 設計判断: 接続の修復を行わない

削除されたノードの隣接ノード同士を再接続する「修復」処理は、以下の理由から**実装しない**:

1. **計算コスト**: 修復処理はノードの接続数に比例する計算が必要で、リアルタイム性を損なう
2. **複雑性**: 多層グラフでの修復は実装が複雑になり、バグのリスクが高い
3. **実用上の影響**: 削除率が低い (~10% 以下) 場合、Recall への影響は軽微

### 定期的なリビルド

削除率が高くなった場合 (例: 20% 以上)、以下の手順でグラフをリビルドする:

1. 有効なノードのベクトルを全て取得
2. 新しい `HnswGraph` を作成
3. 有効なノードを順に INSERT
4. 旧グラフを `Dispose()`

リビルドのタイミングはアプリケーション側で制御する (例: ロード画面中、バックグラウンド処理)。

### ソフト削除のスレッドセーフティ

ソフト削除は `Deleted[nodeId] = true` のフラグ設定のみで構成される。並行する検索スレッドとの安全性について:

- **読み取りの安全性**: `bool` 型の読み書きは x86/ARM アーキテクチャでアトミックであり、torn read は発生しない
- **可視性**: Jobs System のスケジューリングにより、`Schedule()` / `Complete()` の前後でメモリバリアが挿入される。削除操作と検索操作を正しくスケジュールすれば、削除フラグは検索 Job に可視となる
- **検索中の削除**: 検索 Job 実行中に別スレッドで削除フラグが設定された場合、その検索結果に削除済みノードが含まれる可能性がある。これは許容される動作であり、呼び出し元で削除済みノードをフィルタすることで対処できる
- **推奨パターン**: 削除操作は検索 Job の `Complete()` 後に行い、次回の検索から削除が反映されるようにする

## 8. NeighborSelection ヒューリスティック

隣接ノードの選択方式として 2 種類を提供する。

### Simple Selection

距離が近い順に `M` 個を選択する。実装が単純で高速。

```
SELECT_NEIGHBORS_SIMPLE(q, candidates, M):
    return candidates.OrderBy(Distance).Take(M)
```

### Heuristic Selection (推奨)

論文のアルゴリズム 4 に基づく。選択済みノード群との距離を考慮し、**多様性**を確保する。

```
SELECT_NEIGHBORS_HEURISTIC(hnsw, q, candidates, M):
    result = []
    workingSet = candidates.OrderBy(Distance(q, .))  // 距離の小さい順

    while workingSet is not empty AND result.Count < M:
        e = workingSet.PopNearest()

        // e が、既に選択済みの全ノードよりもクエリに近い場合に選択
        if result is empty OR Distance(q, e) < min(Distance(e, r) for r in result):
            result.Add(e)

    return result
```

#### Heuristic Selection の利点

- クラスタの境界付近で、異なるクラスタへの「橋渡し」となるエッジを優先的に保持する
- これにより、グラフの到達性 (navigability) が向上し、Recall が改善される
- 特にデータ分布が不均一な場合に効果が顕著

### UniCortex での選択

デフォルトでは **Heuristic Selection** を使用する。Simple Selection は `HnswConfig` のオプションとして提供し、構築速度を優先したい場合に選択可能とする。

## 9. 距離関数

ベクトル間の距離計算は Core モジュールで定義し、HNSW モジュールは関数ポインタ経由で呼び出す。Burst 互換のため、マネージドデリゲートではなく `FunctionPointer` を使用する。

サポートする距離関数:

| 距離関数 | 用途 |
|---|---|
| Cosine Distance (`1 - cosine_similarity`) | テキスト埋め込み等の正規化済みベクトル |
| Euclidean Distance (L2) | 空間座標、画像特徴量 |
| Dot Product (負値) | 内積類似度 (値が大きいほど類似) |

距離計算の実装詳細は [02-core-design.md](./02-core-design.md) を参照。`Unity.Mathematics.float4` による SIMD auto-vectorize を活用し、Burst が SSE/AVX (x86) および NEON (ARM) を自動生成する。

## 10. Burst Job 構造

### 検索 Job

```csharp
[BurstCompile]
public struct HnswSearchJob : IJob
{
    // --- 入力 (ReadOnly) ---

    /// <summary>クエリベクトル。長さ = Dimension。</summary>
    [ReadOnly] public NativeArray<float> QueryVector;

    /// <summary>全ベクトルデータ。SoA レイアウト。長さ = NodeCount * Dimension。</summary>
    [ReadOnly] public NativeArray<float> VectorData;

    /// <summary>全ノードのメタデータ。</summary>
    [ReadOnly] public NativeArray<HnswNodeMeta> Nodes;

    /// <summary>flat 隣接リスト。</summary>
    [ReadOnly] public NativeArray<int> Neighbors;

    /// <summary>各スロットの実際の接続数。</summary>
    [ReadOnly] public NativeArray<int> NeighborCounts;

    /// <summary>ソフト削除フラグ。</summary>
    [ReadOnly] public NativeArray<bool> Deleted;

    // --- パラメータ ---

    /// <summary>返却する近傍数。</summary>
    public int K;

    /// <summary>探索幅。</summary>
    public int EfSearch;

    /// <summary>ベクトル次元数。</summary>
    public int Dimension;

    /// <summary>エントリポイントのノード ID。</summary>
    public int EntryPoint;

    /// <summary>グラフの最大レイヤー。</summary>
    public int MaxLayer;

    /// <summary>Layer 1 以上の最大接続数。</summary>
    public int M;

    /// <summary>Layer 0 の最大接続数。</summary>
    public int M0;

    /// <summary>距離関数の種別。</summary>
    public DistanceType DistanceType;

    // --- 出力 ---

    /// <summary>検索結果。長さ = K。</summary>
    [WriteOnly] public NativeArray<SearchResult> Results;

    // --- ワークバッファ ---

    /// <summary>訪問済みビットセット。</summary>
    public NativeBitArray Visited;

    public void Execute()
    {
        // 1. 上位レイヤーを greedy search で降りる
        // 2. Layer 0 で efSearch 個の候補を収集
        // 3. Top-K を結果に格納
    }
}
```

### 挿入 Job

```csharp
[BurstCompile]
public struct HnswInsertJob : IJob
{
    /// <summary>挿入するベクトル。</summary>
    [ReadOnly] public NativeArray<float> Vector;

    /// <summary>全ベクトルデータ (読み書き可能)。</summary>
    public NativeArray<float> VectorData;

    /// <summary>グラフのメタデータ。</summary>
    public NativeArray<HnswNodeMeta> Nodes;

    /// <summary>flat 隣接リスト。</summary>
    public NativeArray<int> Neighbors;

    /// <summary>各スロットの実際の接続数。</summary>
    public NativeArray<int> NeighborCounts;

    /// <summary>新ノードの ID。</summary>
    public int NewNodeId;

    /// <summary>新ノードの挿入レイヤー。</summary>
    public int InsertLayer;

    // パラメータ: M, M0, EfConstruction, Dimension, DistanceType, ...

    public void Execute()
    {
        // INSERT アルゴリズム (Section 5) を実行
    }
}
```

### バッチ検索 Job

複数クエリの並列検索には `IJobParallelFor` を使用する:

```csharp
[BurstCompile]
public struct HnswBatchSearchJob : IJobParallelFor
{
    /// <summary>クエリベクトル群。長さ = QueryCount * Dimension。</summary>
    [ReadOnly] public NativeArray<float> QueryVectors;

    /// <summary>グラフデータ (ReadOnly)。</summary>
    [ReadOnly] public NativeArray<float> VectorData;
    [ReadOnly] public NativeArray<HnswNodeMeta> Nodes;
    [ReadOnly] public NativeArray<int> Neighbors;
    [ReadOnly] public NativeArray<int> NeighborCounts;
    [ReadOnly] public NativeArray<bool> Deleted;

    /// <summary>検索結果。長さ = QueryCount * K。</summary>
    [NativeDisableParallelForRestriction]
    public NativeArray<SearchResult> Results;

    // パラメータ: K, EfSearch, Dimension, M, M0, EntryPoint, MaxLayer, DistanceType

    /// <summary>スレッドごとのワークバッファ。</summary>
    [NativeDisableParallelForRestriction]
    public NativeBitArray Visited;  // スレッドローカル化が必要

    public void Execute(int queryIndex)
    {
        // queryIndex 番目のクエリに対して SEARCH を実行
        // Results[queryIndex * K .. (queryIndex + 1) * K] に結果を格納
    }
}
```

> **注意**: `IJobParallelFor` でのワークバッファ (Visited ビットセット) はスレッドローカルで確保する必要がある。`NativeStream` や分割済み `NativeBitArray` を使用して競合を回避する。

## 11. メモリ概算

### ベクトルデータ

ベクトルデータは SoA レイアウトで `NativeArray<float>` に格納する。

```
ベクトルデータサイズ = ノード数 * 次元数 * sizeof(float)
                     = N * D * 4 bytes
```

### グラフデータ

グラフのメモリ消費は、ノードごとの隣接リストスロット数に依存する。

大半のノード (~99%) は Layer 0 のみに存在するため、平均スロット数は概ね M0 に近い:

```
平均スロット数/ノード ≈ M0 + M * (平均 MaxLayer)
                     ≈ M0 + M * mL
                     = 32 + 16 * 0.36
                     ≈ 37.8 スロット

グラフデータサイズ ≈ N * 37.8 * sizeof(int)
                   = 50,000 * 37.8 * 4
                   ≈ 7.56 MB
```

メタデータ:
```
HnswNodeMeta サイズ = N * sizeof(HnswNodeMeta)
                    = 50,000 * 8 bytes
                    = 0.4 MB

NeighborCounts ≈ N * 37.8 * sizeof(int) ≈ 7.56 MB  (Neighbors と同サイズ)
Deleted = N * 1 byte = 0.05 MB
```

### 合計メモリ概算表

| 次元数 | ベクトルデータ (50K) | グラフ (M=16) | メタデータ | 合計 |
|---|---|---|---|---|
| 128 | 25.6 MB | ~15.1 MB | ~0.5 MB | **~41.2 MB** |
| 384 | 76.8 MB | ~15.1 MB | ~0.5 MB | **~92.4 MB** |
| 768 | 153.6 MB | ~15.1 MB | ~0.5 MB | **~169.2 MB** |

### 内訳の詳細 (128 次元, 50K ノードの場合)

| 項目 | 計算 | サイズ |
|---|---|---|
| ベクトルデータ | 50,000 * 128 * 4 | 25.6 MB |
| Neighbors (flat) | 50,000 * 37.8 * 4 | 7.56 MB |
| NeighborCounts | 50,000 * 37.8 * 4 | 7.56 MB |
| HnswNodeMeta | 50,000 * 8 | 0.4 MB |
| Deleted フラグ | 50,000 * 1 | 0.05 MB |
| **合計** | | **~41.2 MB** |

> **注意**: 探索時のワークバッファ (Visited ビットセット等) は上記に含まれない。Visited は `ceil(N / 8)` bytes ≈ 6.25 KB/スレッドであり、無視できるサイズ。

## 12. パフォーマンス目標

| 操作 | 目標レイテンシ (50K, 128次元) | 備考 |
|---|---|---|
| Search (単一クエリ, K=10, ef=50) | < 1 ms | Burst + SIMD 最適化 |
| Insert (単一ノード) | < 5 ms | efConstruction=200 |
| Delete (ソフト削除) | < 0.01 ms | フラグ設定のみ |
| Batch Search (100 クエリ) | < 20 ms | IJobParallelFor |

## 13. efSearch パラメータ感度分析

efSearch はクエリ時の検索品質とレイテンシのトレードオフを制御する最重要パラメータである。

### 理論的な影響

- efSearch が大きいほど、Layer 0 で探索する候補ノード数が増え、最適解に到達する確率が向上する
- 計算量は O(efSearch × M0) であり、efSearch に対して線形にレイテンシが増加する
- efSearch >= K を満たす必要がある（K 件の結果を返すには最低 K 個の候補が必要）

### 推定パフォーマンステーブル (50K ノード, dim=128)

| efSearch | 推定 Recall@10 | 推定レイテンシ | 用途 |
|---|---|---|---|
| 10 | ~0.70 | < 0.3 ms | リアルタイム性最重視（精度妥協） |
| 50 | ~0.95 | < 1.0 ms | 標準（推奨） |
| 100 | ~0.98 | < 2.0 ms | 高精度 |
| 200 | ~0.99 | < 4.0 ms | 最高精度 |
| 500 | ~1.00 | < 10 ms | ほぼ完全（ベンチマーク用） |

> **注意**: 上記は理論的推定値であり、実際の性能はデータ分布、M パラメータ、Burst 最適化の効果に依存する。Phase 9（パフォーマンス最適化）で実測データを取得し、このテーブルを更新する。

### プラットフォーム別考慮

| プラットフォーム | 推奨 efSearch | 理由 |
|---|---|---|
| デスクトップ | 50-100 | Burst + SIMD で高速処理可能 |
| モバイル | 30-50 | CPU 性能制約 |
| WebGL | 20-50 | シングルスレッド + WASM オーバーヘッド |

WebGL ではデスクトップの約 3 倍のレイテンシを見込む必要がある。詳細は [10-technical-constraints.md](./10-technical-constraints.md) を参照。

## 14. 相互参照

- [00-project-overview.md](./00-project-overview.md) -- プロジェクト概要
- [02-core-design.md](./02-core-design.md) -- Core モジュール設計 (VectorStore, 距離関数, SearchResult 等)
- [10-technical-constraints.md](./10-technical-constraints.md) -- Burst/IL2CPP/WebGL の技術的制約
- [11-security-guidelines.md](./11-security-guidelines.md) -- セキュリティガイドライン
- [12-test-plan.md](./12-test-plan.md) -- テスト計画
- [13-memory-budget.md](./13-memory-budget.md) -- メモリバジェット
