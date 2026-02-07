# Sparse ベクトル検索 設計書

## 1. 概要

Sparse ベクトル検索は、キーワード特徴量ベースのスパースベクトルを用いた検索機能を提供する。
外部の Embedding モデル（SPLADE, ELSER 等）が生成する高次元・疎なベクトルを入力として受け取り、
転置インデックスによる高速なスコアリングを行う。

Dense ベクトル検索（[02-core-design.md](./02-core-design.md)）がセマンティックな類似性を捉えるのに対し、
Sparse ベクトル検索はキーワードレベルの一致・重要度に基づくランキングを行う。
両者を [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) の RRF ReRanker で統合することで、
意味的類似性とキーワード適合性の両方を考慮したハイブリッド検索が実現される。

### 特徴

- 外部モデルが生成したスパースベクトルをそのまま索引化
- 転置インデックスによる効率的な検索（非ゼロ次元のみ走査）
- 内積 (dot product) ベースのスコアリング
- Burst/Jobs 最適化による高速実行
- NativeContainer ベースのメモリ管理（GC フリー）

## 2. スパースベクトル表現

### SparseElement 構造体

スパースベクトルの非ゼロ要素を表す値型構造体。
1 つのドキュメントのスパースベクトルは `NativeArray<SparseElement>` として表現され、
非ゼロ要素のみを格納する（典型的に数十〜数百要素）。

```csharp
/// <summary>
/// スパースベクトルの非ゼロ要素を表す。
/// </summary>
public struct SparseElement
{
    /// <summary>次元インデックス（トークン ID 等）。</summary>
    public int Index;

    /// <summary>その次元の重み（SPLADE 等のモデル出力値）。</summary>
    public float Value;
}
```

### 設計判断

- **非ゼロ要素のみ格納**: SPLADE 等のモデル出力は語彙サイズ（30,000〜）の次元を持つが、
  非ゼロ要素は典型的に 100〜300 程度。密ベクトルとして格納するとメモリが膨大になるため、
  非ゼロ要素のみの疎表現を採用する。
- **値型 (struct)**: Burst 互換性のため、マネージド型は使用しない。
- **int Index**: 次元インデックスは語彙サイズに依存するが、`int` (32-bit) で十分。

### 入力バリデーション

`SparseElement.Index` は非負整数であることを前提とする。Add 時に以下の検証を行う:

- `Index < 0` の場合、`InvalidParameter` エラーを返す
- 上限値の制約は設けない（語彙サイズに依存するため）が、実用上 `int.MaxValue` 以下であること

詳細な入力バリデーション要件は [11-security-guidelines.md](./11-security-guidelines.md) を参照。

## 3. 転置インデックス設計

### 概要

転置インデックスは「次元 Index -> その次元に非ゼロ値を持つドキュメント群」のマッピングを保持する。
検索時にはクエリベクトルの非ゼロ次元のみを走査し、関連ドキュメントのスコアを累積する。

### SparsePosting 構造体

ポスティングリストの各エントリを表す。

```csharp
/// <summary>
/// 転置インデックスのポスティングエントリ。
/// 特定の次元に非ゼロ値を持つドキュメントとその重みを記録する。
/// </summary>
public struct SparsePosting
{
    /// <summary>ドキュメントの内部 ID。</summary>
    public int InternalId;

    /// <summary>この次元におけるドキュメントの重み値。</summary>
    public float Value;
}
```

### SparseIndex 構造体

転置インデックス本体。`NativeParallelMultiHashMap<int, SparsePosting>` を用いて
次元 Index からポスティングリストへのマッピングを実現する。

```csharp
/// <summary>
/// スパースベクトルの転置インデックス。
/// </summary>
public struct SparseIndex : IDisposable
{
    /// <summary>
    /// 転置インデックス本体。
    /// Key: 次元 Index, Value: その次元に非ゼロ値を持つドキュメントのポスティング。
    /// </summary>
    public NativeParallelMultiHashMap<int, SparsePosting> InvertedIndex;

    /// <summary>
    /// ソフト削除済みドキュメントの ID セット。
    /// 検索時に DeletedIds.Contains(docId) でスキップ判定に使用する。
    /// </summary>
    public NativeParallelHashSet<int> DeletedIds;

    /// <summary>索引済みドキュメント数（ソフト削除含む）。</summary>
    public int DocumentCount;

    /// <summary>
    /// ドキュメントを転置インデックスに追加する。
    /// </summary>
    /// <param name="internalId">ドキュメントの内部 ID。</param>
    /// <param name="vector">スパースベクトル（非ゼロ要素の配列）。</param>
    public void Add(int internalId, NativeArray<SparseElement> vector) { ... }

    /// <summary>
    /// ドキュメントをソフト削除する。
    /// 転置インデックスからの物理削除は行わず、DeletedIds に記録する。
    /// 検索時に DeletedIds.Contains(docId) でスキップされる。
    /// </summary>
    /// <param name="internalId">削除対象の内部 ID。</param>
    public void Remove(int internalId) { ... }

    /// <summary>
    /// リソースを解放する。
    /// </summary>
    public void Dispose()
    {
        if (InvertedIndex.IsCreated) InvertedIndex.Dispose();
        if (DeletedIds.IsCreated) DeletedIds.Dispose();
    }
}
```

### NativeParallelMultiHashMap の選定理由

- Unity.Collections が提供する Burst 互換のハッシュマップ
- 1 つの Key に対して複数の Value を格納可能（ポスティングリストとして機能）
- `TryGetFirstValue` / `TryGetNextValue` によるイテレーションで各次元のポスティングを走査
- Burst Job 内で `[ReadOnly]` として安全に並行読み取り可能

### Add / Remove の処理フロー

**Add**:
1. スパースベクトルの各 `SparseElement` について `InvertedIndex.Add(element.Index, posting)` を呼び出す
2. `DocumentCount` をインクリメント

**Remove — ソフト削除戦略**:

> **重要**: `NativeParallelMultiHashMap` は特定の Key-Value ペアを直接削除する API を提供していない。`Remove(key)` は Key に関連するすべての Value を削除する。個別の InternalId に対応するポスティングのみを削除するには、Key（次元 Index）に対する全 Value をイテレーションし、一致するものを見つけて削除する必要がある。

この制約を踏まえ、Remove 操作は以下の **ソフト削除 + バッチ再構築** 戦略を採用する:

1. **即時削除は行わない**: 転置インデックスからの個別エントリ削除は行わない
2. **ソフト削除**: `Remove(internalId)` は `DeletedIds.Add(internalId)` のみを行う（vector パラメータ不要）
3. **検索時フィルタ**: DAAT スコア累積ループ内で `DeletedIds.Contains(docId)` をチェックし、削除済みドキュメントをスキップする（セクション 4 の擬似コード参照）
4. **バッチ再構築**: 削除率が閾値（例: 20%）を超えた場合、有効なドキュメントのみで転置インデックスを再構築する

この方式は HNSW のソフト削除方針（[03-hnsw-design.md](./03-hnsw-design.md) セクション 7）と一致し、
UniCortex 全体で統一的な削除戦略を提供する。

## 4. DAAT (Document-At-A-Time) 検索アルゴリズム

### アルゴリズム概要

クエリのスパースベクトルに対し、Document-At-A-Time (DAAT) 方式でスコアを累積する。
DAAT はクエリの各非ゼロ次元について転置インデックスを引き、
ドキュメント単位でスコアを加算していく方式である。

スコアリングは内積 (dot product) を使用する:

```
score(query, doc) = sum( query[i] * doc[i] ) for all i where both are non-zero
```

### 疑似コード

```
function Search(queryVector, invertedIndex, deletedIds, K):
    // 一時スコア累積用ハッシュマップ
    scores = NativeParallelHashMap<int, float>()

    // クエリの各非ゼロ次元について転置インデックスを走査
    for each (queryDim, queryValue) in queryVector:
        for each (docId, docValue) in invertedIndex[queryDim]:
            // ソフト削除済みドキュメントをスキップ
            if deletedIds.Contains(docId):
                continue

            float newScore = queryValue * docValue;
            // Burst 互換: TryGetValue + Remove + Add パターン
            if scores.TryGetValue(docId, out float existing):
                scores.Remove(docId)
                scores.Add(docId, existing + newScore)
            else:
                scores.Add(docId, newScore)

    // Top-K 選択（スコアを負値変換して SearchResult に格納）
    return TopK(scores, K)
```

### 処理フロー

1. **クエリ解析**: クエリのスパースベクトルの非ゼロ要素を取得
2. **転置インデックス走査**: 各非ゼロ次元 `(queryDim, queryValue)` について
   `InvertedIndex.TryGetFirstValue(queryDim, ...)` で該当ポスティングリストを走査
3. **スコア累積**: `NativeParallelHashMap<int, float>` にドキュメント ID をキーとしてスコアを加算
4. **Top-K 選択**: 累積スコアから上位 K 件を選択（NativeMaxHeap を使用）
5. **結果返却**: `NativeArray<SearchResult>` に結果を格納

### Top-K 選択の実装

Top-K 選択には最小ヒープ（NativeMaxHeap）を使用する。
ヒープサイズを K に固定し、各候補のスコアが現在のヒープ最小値を超える場合のみ挿入・置換を行う。
これにより全候補をソートせずに O(N log K) で Top-K を取得できる。

```
function TopK(scores, K):
    // スコア符号規約: 内積スコアを負値変換して SearchResult.Score に格納する。
    // これにより「Score が小さいほど関連度が高い」統一規約を維持する。
    // （02-core-design.md「統一スコア符号規約」参照）

    heap = NativeMaxHeap(capacity: K)  // Score が最大 (= 関連度最低) の要素を Pop で除去
    for each (docId, dotScore) in scores:
        negScore = -dotScore  // 負値変換
        if heap.Count < K:
            heap.Push(SearchResult(docId, negScore))
        else if negScore < heap.Peek().Score:
            heap.Pop()
            heap.Push(SearchResult(docId, negScore))
    return heap.ToSortedArray()  // Score 昇順 (= 関連度降順)
```

> NativeMaxHeap の実装詳細は [02-core-design.md](./02-core-design.md) を参照。

## 5. Burst Job 構造

### SparseSearchJob

検索処理を Burst Job として定義し、メインスレッド外で実行可能にする。

```csharp
/// <summary>
/// Sparse ベクトル検索を実行する Burst Job。
/// DAAT アルゴリズムにより転置インデックスを走査し、Top-K を返す。
/// </summary>
[BurstCompile]
public struct SparseSearchJob : IJob
{
    /// <summary>クエリのスパースベクトル（非ゼロ要素）。</summary>
    [ReadOnly] public NativeArray<SparseElement> Query;

    /// <summary>転置インデックス（読み取り専用）。</summary>
    [ReadOnly] public NativeParallelMultiHashMap<int, SparsePosting> InvertedIndex;

    /// <summary>ソフト削除済み ID セット（読み取り専用）。</summary>
    [ReadOnly] public NativeParallelHashSet<int> DeletedIds;

    /// <summary>検索結果の出力先。サイズは K。</summary>
    public NativeArray<SearchResult> Results;

    /// <summary>返却する上位件数。</summary>
    public int K;

    public void Execute()
    {
        // 1. NativeParallelHashMap<int, float> で一時スコアを累積
        // 2. クエリの各非ゼロ次元について InvertedIndex を走査
        // 3. NativeMaxHeap で Top-K を選択
        // 4. Results に書き込み
    }
}
```

### Job のスケジューリング

```csharp
// 使用例
var job = new SparseSearchJob
{
    Query = queryVector,
    InvertedIndex = sparseIndex.InvertedIndex,
    Results = results,
    K = k
};
JobHandle handle = job.Schedule();
handle.Complete();
```

### Burst 互換性の考慮

- Job 内で使用するすべてのデータは NativeContainer または値型のみ
- `NativeParallelHashMap<int, float>` は Job 内で `Allocator.Temp` で確保し、
  Execute 完了時に自動解放
- マネージド例外の代わりにエラーコードを使用
- `NativeMaxHeap` は `Allocator.Temp` で確保

## 6. メモリ概算

### ポスティングデータ

| パラメータ | 値 |
|---|---|
| ドキュメント数 | 50,000 |
| 平均非ゼロ要素数 / ドキュメント | 100 |
| 総ポスティング数 | 5,000,000 |
| `SparsePosting` サイズ | 8 bytes (`int` + `float`) |
| ポスティングデータ合計 | ~40 MB |

### NativeParallelMultiHashMap オーバーヘッド

`NativeParallelMultiHashMap` はオープンアドレッシング + チェイニングを併用しており、
ロードファクタとバケット管理のために追加メモリが必要:

| 項目 | 概算 |
|---|---|
| バケット配列 | ~10 MB |
| next ポインタ配列 | ~10 MB |
| **合計（オーバーヘッド込み）** | **~60 MB** |

### 検索時の一時メモリ

| 項目 | 概算 |
|---|---|
| スコア累積用 `NativeParallelHashMap<int, float>` | クエリの非ゼロ次元にヒットするドキュメント数に依存。最大 ~400 KB (50K entries) |
| Top-K ヒープ | K * 12 bytes (negligible) |

### メモリ最適化の方針

- 初期キャパシティを `DocumentCount * AverageNonZeroCount` で事前確保し、リハッシュを回避
- 不要になったインデックスは `Dispose()` で即座に解放
- 永続化時は [08-persistence-design.md](./08-persistence-design.md) の MemoryMappedFile を用いて
  オンデマンド読み込みに切り替え可能

## 7. パフォーマンス考慮

### 計算量

| 操作 | 計算量 |
|---|---|
| Add (1 ドキュメント) | O(S) -- S: 非ゼロ要素数 |
| Remove (1 ドキュメント) | O(S * B) -- B: 平均バケットサイズ |
| Search | O(Q * P_avg + N_hit * log K) |

- **Q**: クエリの非ゼロ要素数
- **P_avg**: 各次元の平均ポスティングリスト長
- **N_hit**: スコアが計算されたドキュメント数
- **K**: 返却件数

### ボトルネックと対策

#### クエリのスパース度が低い場合

クエリベクトルの非ゼロ要素数が多い場合（例: 500 以上）、走査する次元数が増えて検索が遅くなる。
対策:
- クエリベクトルの非ゼロ要素を重みの上位 N 件に打ち切る（query pruning）
- 呼び出し側で非ゼロ要素数の上限を設けることを推奨

#### 高頻度次元のポスティングリスト肥大化

特定の次元（一般的な単語に対応）のポスティングリストが極端に長くなる場合がある。
対策:
- 将来的にポスティングリストの長さによるスキップ（WAND アルゴリズム）を検討
- 現時点では DAAT の全走査で十分な性能を見込む（50K ドキュメント規模）

#### キャッシュ局所性

`NativeParallelMultiHashMap` のイテレーションはハッシュテーブルの構造上、
メモリアクセスがランダムになりやすい。50K ドキュメント規模では許容範囲だが、
スケールアップ時にはソート済みポスティングリスト（`NativeList<SparsePosting>` の配列）への
移行を検討する。

### ベンチマーク指標

実装後に以下の指標を計測する:

| 指標 | ターゲット |
|---|---|
| 検索レイテンシ (50K docs, K=10) | < 5 ms |
| Add スループット | > 10,000 docs/sec |
| メモリ使用量 (50K docs, avg 100 sparse elements) | < 80 MB |

## 8. API 設計

### 公開 API の概要

```csharp
public struct SparseIndex : IDisposable
{
    // 構築
    public SparseIndex(int initialCapacity, Allocator allocator);

    // ドキュメント操作
    public void Add(int internalId, NativeArray<SparseElement> vector);
    public void Remove(int internalId);  // ソフト削除（DeletedIds に記録）

    // 検索
    public JobHandle Search(
        NativeArray<SparseElement> query,
        NativeArray<SearchResult> results,
        int k,
        JobHandle dependency = default);

    // リソース管理
    public void Dispose();
}
```

### SearchResult の共通型

検索結果は Dense / Sparse / BM25 で共通の `SearchResult` 型を使用する。
これにより [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) の RRF ReRanker で
統一的に結果を統合できる。

```csharp
/// <summary>
/// 検索結果の 1 エントリ。全検索方式で共通。
/// </summary>
public struct SearchResult
{
    /// <summary>ドキュメントの内部 ID。</summary>
    public int InternalId;

    /// <summary>スコア（内積値、BM25 スコア等）。</summary>
    public float Score;
}
```

> `SearchResult` の定義は [02-core-design.md](./02-core-design.md) を参照。

## 9. 関連ドキュメント

| ドキュメント | 関連内容 |
|---|---|
| [00-project-overview.md](./00-project-overview.md) | プロジェクト全体概要 |
| [02-core-design.md](./02-core-design.md) | 共通データ構造（SearchResult, NativeMaxHeap 等） |
| [03-hnsw-design.md](./03-hnsw-design.md) | Dense ベクトル検索（HNSW） |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 全文検索（同じく転置インデックスを使用） |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | ハイブリッド検索（RRF で Sparse 結果を統合） |
| [08-persistence-design.md](./08-persistence-design.md) | 永続化（転置インデックスのシリアライズ） |
| [11-security-guidelines.md](./11-security-guidelines.md) | 入力バリデーション・セキュリティ指針 |
| [12-test-plan.md](./12-test-plan.md) | テスト計画 |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット |
