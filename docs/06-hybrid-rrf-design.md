# 06 - ハイブリッド検索・RRF 設計書

## 1. 概要

ハイブリッド検索は、複数の検索手法（Dense, Sparse, BM25）の結果を統合して検索精度を向上させる手法である。
各検索手法はそれぞれ異なる強みを持つ:

| 検索手法 | 強み | 弱み |
|---|---|---|
| **Dense ベクトル検索** | セマンティックな意味理解、類義語・言い換えに強い | 未知の固有名詞・専門用語に弱い |
| **Sparse ベクトル検索** | キーワード特徴量の重み付きマッチ | 学習済みモデルに依存 |
| **BM25 全文検索** | 正確なキーワードマッチ、高速 | 意味的な類似性を捉えられない |

ハイブリッド検索はこれらを組み合わせることで、単一手法では得られない高い検索精度を実現する。
結果の統合には **Reciprocal Rank Fusion (RRF)** アルゴリズムを採用する。

各サブ検索の詳細設計は以下を参照:
- Dense ベクトル検索: [03-hnsw-design.md](./03-hnsw-design.md)
- Sparse ベクトル検索: [04-sparse-design.md](./04-sparse-design.md)
- BM25 全文検索: [05-bm25-design.md](./05-bm25-design.md)

---

## 2. Reciprocal Rank Fusion (RRF) アルゴリズム

### 2.1 基本公式

```
RRF_score(d) = Sigma_i ( weight_i / (k + rank_i(d)) )
```

| パラメータ | 説明 | デフォルト値 |
|---|---|---|
| `k` | Rank constant。上位と下位の順位差の影響度を調整する定数 | 60 |
| `rank_i(d)` | 検索手法 `i` におけるドキュメント `d` の順位（1-based） | - |
| `weight_i` | 検索手法 `i` の重み | 1.0（均等） |

### 2.2 RRF の利点

- **スコア正規化が不要**: ランクベースの統合であるため、各検索手法のスコア尺度が異なっていても直接統合可能
- **異種スコアの統合**: Cosine 類似度 (Dense)、内積 (Sparse)、BM25 スコアといった異なる尺度を統一的に扱える
- **パラメータの安定性**: `k = 60` は多くのベンチマークで良好な結果を示す標準値であり、チューニングの必要性が低い
- **計算コストが低い**: ランクの逆数和という単純な計算のため、マージ処理のオーバーヘッドが極めて小さい

### 2.3 k パラメータの影響

`k` の値は上位結果と下位結果のスコア差に影響する:

```
k が小さい場合 (例: k = 10):
  rank 1 → 1/(10+1) = 0.091
  rank 10 → 1/(10+10) = 0.050
  差: 0.041 → 上位と下位のスコア差が大きい（上位重視）

k が大きい場合 (例: k = 100):
  rank 1 → 1/(100+1) = 0.0099
  rank 10 → 1/(100+10) = 0.0091
  差: 0.0008 → 上位と下位のスコア差が小さい（均等に近い）

標準値 (k = 60):
  rank 1 → 1/(60+1) = 0.0164
  rank 10 → 1/(60+10) = 0.0143
  → バランスの取れた重み付け
```

---

## 3. データ構造定義

### 3.1 RrfConfig

RRF マージの設定パラメータ。各検索手法の重みと rank constant を定義する。

```csharp
public struct RrfConfig
{
    /// <summary>Rank constant k。上位と下位の順位差の影響度を調整する。</summary>
    public float RankConstant;

    /// <summary>Dense ベクトル検索の重み。</summary>
    public float DenseWeight;

    /// <summary>Sparse ベクトル検索の重み。</summary>
    public float SparseWeight;

    /// <summary>BM25 全文検索の重み。</summary>
    public float Bm25Weight;

    /// <summary>デフォルト設定を返す (k=60, 各重み=1.0)。</summary>
    public static RrfConfig Default => new RrfConfig
    {
        RankConstant = 60f,
        DenseWeight = 1.0f,
        SparseWeight = 1.0f,
        Bm25Weight = 1.0f,
    };
}
```

重みの調整例:
- **セマンティック重視**: `DenseWeight = 2.0, SparseWeight = 1.0, Bm25Weight = 0.5`
- **キーワードマッチ重視**: `DenseWeight = 0.5, SparseWeight = 1.0, Bm25Weight = 2.0`
- **均等**: `DenseWeight = 1.0, SparseWeight = 1.0, Bm25Weight = 1.0`（デフォルト）

### 重み付き RRF 計算例

**設定**: `k = 60, DenseWeight = 2.0, SparseWeight = 1.0, Bm25Weight = 0.5`

```
Dense 結果:  [docA(rank1), docB(rank2), docC(rank3)]
Sparse 結果: [docB(rank1), docC(rank2), docD(rank3)]
BM25 結果:   [docC(rank1), docA(rank2), docD(rank3)]

RRF(docA) = 2.0/(60+1) + 0.0       + 0.5/(60+2)
          = 0.03279    + 0.0       + 0.00806
          = 0.04085

RRF(docB) = 2.0/(60+2) + 1.0/(60+1) + 0.0
          = 0.03226    + 0.01639    + 0.0
          = 0.04865

RRF(docC) = 2.0/(60+3) + 1.0/(60+2) + 0.5/(60+1)
          = 0.03175    + 0.01613    + 0.00820
          = 0.05608

RRF(docD) = 0.0        + 1.0/(60+3) + 0.5/(60+3)
          = 0.0        + 0.01587    + 0.00794
          = 0.02381

期待順位: docC (0.05608) > docB (0.04865) > docA (0.04085) > docD (0.02381)
```

### タイブレーク方針

RRF スコアが同値の場合 (浮動小数点の等値比較)、以下の優先度で順位を決定する:

1. **出現ソース数**: より多くのサブ検索に出現するドキュメントを優先する
2. **元のランク合計**: 各サブ検索での元ランクの合計が小さい（= 上位に出現している）ドキュメントを優先する
3. **InternalId 昇順**: 上記で決まらない場合は InternalId の昇順（安定ソート）

> **実装上の注意**: 浮動小数点の丸め誤差により完全一致は稀であるが、
> 安定した結果を保証するためタイブレーク規則を定義する。

### 3.2 HybridSearchParams

ハイブリッド検索のリクエストパラメータ。各サブ検索のクエリと設定を保持する。

```csharp
public struct HybridSearchParams
{
    /// <summary>Dense 検索用クエリベクトル。使わない場合は default。</summary>
    public NativeArray<float> DenseQuery;

    /// <summary>Sparse 検索用クエリベクトル。使わない場合は default。</summary>
    public NativeArray<SparseElement> SparseQuery;

    /// <summary>BM25 用テキストクエリ (UTF-8 バイト列)。使わない場合は default。</summary>
    public NativeArray<byte> TextQuery;

    /// <summary>最終返却件数。</summary>
    public int K;

    /// <summary>各サブ検索の取得件数。K より大きくすることで RRF の精度が向上する。</summary>
    public int SubSearchK;

    /// <summary>RRF マージ設定。</summary>
    public RrfConfig RrfConfig;

    /// <summary>Dense 検索パラメータ (efSearch 等)。</summary>
    public SearchParams DenseParams;
}
```

### 3.3 サブ検索の選択的実行

クエリが `default` (未設定) のサブ検索はスキップされる。
これにより、2手法のみの組み合わせ（例: Dense + BM25）も柔軟に実行できる。

```
DenseQuery が有効 かつ SparseQuery が default かつ TextQuery が有効
→ Dense + BM25 の 2手法ハイブリッド検索
```

---

## 4. HybridSearcher オーケストレータ

### 4.1 構造

```csharp
public struct HybridSearcher : IDisposable
{
    // 内部に各インデックスへの参照を保持（ポインタベース）
    // HnswIndex, SparseIndex, Bm25Engine への unsafe 参照

    /// <summary>
    /// ハイブリッド検索を実行する。
    /// 各サブ検索を並列実行し、RRF で結果をマージして Top-K を返す。
    /// </summary>
    public NativeArray<SearchResult> Execute(HybridSearchParams param) { ... }

    public void Dispose() { ... }
}
```

### 4.2 実行フロー

```
Execute(params)
│
├── 1. パラメータ検証
│   └── K > 0, SubSearchK >= K, 少なくとも1つのクエリが有効
│
├── 2. 有効なサブ検索を判定
│   ├── DenseQuery.IsCreated → Dense 検索を実行
│   ├── SparseQuery.IsCreated → Sparse 検索を実行
│   └── TextQuery.IsCreated → BM25 検索を実行
│
├── 3. サブ検索を並列実行 (Jobs System)
│   ├── DenseSearchJob.Schedule()
│   ├── SparseSearchJob.Schedule()
│   └── Bm25SearchJob.Schedule()
│
├── 4. 全 Job の完了を待機
│   └── JobHandle.CombineDependencies(...).Complete()
│
├── 5. RRF マージ
│   └── RrfMergeJob で全結果を統合・Top-K 選択
│
└── 6. 結果を返却
    └── NativeArray<SearchResult> (スコア降順)
```

---

## 5. サブ検索の並列実行

### 5.1 Jobs System による並列化

Unity Jobs System を使い、各サブ検索を独立した IJob として並列実行する。

```csharp
// 結果バッファの確保 (Allocator.TempJob)
var denseResults = new NativeArray<SearchResult>(subSearchK, Allocator.TempJob);
var sparseResults = new NativeArray<SearchResult>(subSearchK, Allocator.TempJob);
var bm25Results = new NativeArray<SearchResult>(subSearchK, Allocator.TempJob);

// 各サブ検索 Job をスケジュール
var denseHandle = new DenseSearchJob
{
    Query = param.DenseQuery,
    Results = denseResults,
    // ...
}.Schedule();

var sparseHandle = new SparseSearchJob
{
    Query = param.SparseQuery,
    Results = sparseResults,
    // ...
}.Schedule();

var bm25Handle = new Bm25SearchJob
{
    Query = param.TextQuery,
    Results = bm25Results,
    // ...
}.Schedule();

// 全 Job の完了を待機
var combined = JobHandle.CombineDependencies(denseHandle, sparseHandle, bm25Handle);

// RRF マージ Job をスケジュール
var mergedResults = new NativeArray<SearchResult>(param.K, Allocator.TempJob);
new RrfMergeJob
{
    DenseResults = denseResults,
    SparseResults = sparseResults,
    Bm25Results = bm25Results,
    MergedResults = mergedResults,
    Config = param.RrfConfig,
    K = param.K,
}.Schedule(combined).Complete();
```

### 5.2 WebGL 環境での動作

WebGL ではシングルスレッドで動作するため、Jobs System による並列実行は行われない。
各サブ検索は逐次実行にフォールバックするが、API は同一であり呼び出し側のコード変更は不要。

```
マルチスレッド環境:
  Dense ──┐
  Sparse ─┤ → 並列実行 → RRF マージ
  BM25 ───┘

WebGL (シングルスレッド):
  Dense → Sparse → BM25 → RRF マージ (逐次実行)
```

### WebGL パフォーマンス影響

WebGL 環境では以下の要因により、デスクトップ比で約 **3 倍** のレイテンシ増加を見込む:

| 要因 | 影響 |
|---|---|
| シングルスレッド実行 | サブ検索が逐次実行されるため、並列化による高速化が得られない |
| WASM オーバーヘッド | ネイティブコードと比較して一般的に 1.5-2x のオーバーヘッド |
| Burst WASM SIMD | SSE/AVX ほどの幅広い SIMD サポートなし |

**推奨**: WebGL 環境では `SubSearchK` を小さめに設定し（例: `K * 2` 程度）、
レイテンシを抑制することを推奨する。

プラットフォーム別の詳細な制約は [10-technical-constraints.md](./10-technical-constraints.md) を参照。

### 5.3 Allocator 戦略

| バッファ | Allocator | ライフサイクル |
|---|---|---|
| サブ検索結果 (`denseResults` 等) | `TempJob` | Job スケジュール ~ マージ完了後に Dispose |
| マージ結果 (`mergedResults`) | `TempJob` | マージ完了 ~ 呼び出し元で Dispose |
| RRF 内部作業バッファ (HashMap 等) | `TempJob` | RrfMergeJob 内で確保・解放 |

---

## 6. RRF マージ Job

### 6.1 構造体定義

```csharp
[BurstCompile]
public struct RrfMergeJob : IJob
{
    [ReadOnly] public NativeArray<SearchResult> DenseResults;
    [ReadOnly] public NativeArray<SearchResult> SparseResults;
    [ReadOnly] public NativeArray<SearchResult> Bm25Results;
    public NativeArray<SearchResult> MergedResults;
    public RrfConfig Config;
    public int K;

    // 各サブ検索の有効結果数（SubSearchK 以下の場合がある）
    public int DenseCount;
    public int SparseCount;
    public int Bm25Count;

    public void Execute()
    {
        // 1. ドキュメント ID → RRF スコアの HashMap を構築
        // 2. 各結果リストを走査し、rank から RRF スコアを加算
        // 3. Top-K を選択して MergedResults に格納
    }
}
```

### 6.2 マージアルゴリズム詳細

```
Execute():

Step 1: NativeParallelHashMap<int, float> を Allocator.Temp で確保
        初期容量 = DenseCount + SparseCount + Bm25Count

Step 2: Dense 結果を走査
  for rank = 0 .. DenseCount-1:
    docId = DenseResults[rank].InternalId
    rrfScore = Config.DenseWeight / (Config.RankConstant + (rank + 1))
    // Burst 互換: TryGetValue + Remove + Add パターン
    if hashMap.TryGetValue(docId, out float existing):
        hashMap.Remove(docId)
        hashMap.Add(docId, existing + rrfScore)
    else:
        hashMap.Add(docId, rrfScore)

Step 3: Sparse 結果を走査
  for rank = 0 .. SparseCount-1:
    docId = SparseResults[rank].InternalId
    rrfScore = Config.SparseWeight / (Config.RankConstant + (rank + 1))
    // Burst 互換: TryGetValue + Remove + Add パターン
    if hashMap.TryGetValue(docId, out float existing):
        hashMap.Remove(docId)
        hashMap.Add(docId, existing + rrfScore)
    else:
        hashMap.Add(docId, rrfScore)

Step 4: BM25 結果を走査
  for rank = 0 .. Bm25Count-1:
    docId = Bm25Results[rank].InternalId
    rrfScore = Config.Bm25Weight / (Config.RankConstant + (rank + 1))
    // Burst 互換: TryGetValue + Remove + Add パターン
    if hashMap.TryGetValue(docId, out float existing):
        hashMap.Remove(docId)
        hashMap.Add(docId, existing + rrfScore)
    else:
        hashMap.Add(docId, rrfScore)

Step 5: Top-K 選択
  MinHeap (サイズ K) を使って上位 K 件を効率的に選択
  → MergedResults に格納（スコア降順でソート）
```

### 6.3 Top-K 選択の実装方針

Top-K 選択には **MinHeap** (最小ヒープ、サイズ K) を使用する。
全ドキュメントを走査し、ヒープの最小要素より大きいスコアのドキュメントのみをヒープに挿入する。

```
計算量:
- HashMap 構築: O(SubSearchK * NumActiveSources)
- Top-K 選択: O(N * log K)  (N = ユニークドキュメント数)
- 合計: O(SubSearchK * 3 + N * log K)

SubSearchK = 50, K = 10, N <= 150 (最大) の場合:
  O(150 + 150 * log 10) ≈ O(650) → 極めて高速
```

### 6.4 Burst 互換性

RrfMergeJob は `[BurstCompile]` 属性を付与し、以下の制約を遵守する:

- `NativeParallelHashMap<int, float>` を使用（マネージド Dictionary 不可）
- ヒープ操作は `NativeArray` ベースの手動実装
- 例外処理不可 → `ErrorCode` で結果を返す
- string 操作不可 → ドキュメント ID は `int` 型

---

## 7. スコア正規化（オプション）

### 7.1 基本方針

RRF はランクベースの統合であるため、スコア正規化は **不要** が基本である。
ただし、最終スコアを 0-1 の範囲に収めたい場合のオプションとして Min-Max 正規化を提供する。

### 7.2 Min-Max 正規化

```
normalized_score = (score - min_score) / (max_score - min_score)
```

- `max_score`: マージ結果中の最大 RRF スコア
- `min_score`: マージ結果中の最小 RRF スコア
- すべてのスコアが同一の場合は全件 1.0 とする

### 7.3 正規化の適用タイミング

```
RRF マージ → Top-K 選択 → [オプション] Min-Max 正規化 → 結果返却
```

正規化は Top-K 選択の **後** に適用する。
選択前に正規化すると相対順位は変わらないが、不要な計算が増える。

---

## 8. SubSearchK の設計指針

### 8.1 SubSearchK と検索精度の関係

`SubSearchK` は各サブ検索が返す結果件数であり、最終返却件数 `K` より大きくする必要がある。
SubSearchK が大きいほど RRF マージの対象候補が増え、精度が向上するが、計算コストも増加する。

```
SubSearchK と精度のトレードオフ:

SubSearchK = K    → 最低限の候補。ある手法でのみ上位のドキュメントを見逃す可能性あり
SubSearchK = 3*K  → 実用的なバランス。推奨値
SubSearchK = 10*K → 高精度だが計算コスト増
```

### 8.2 推奨値

| ユースケース | K | SubSearchK | 説明 |
|---|---|---|---|
| リアルタイム検索 | 10 | 30 | 低レイテンシ重視 |
| 高精度検索 | 10 | 100 | 精度重視 |
| バッチ処理 | 50 | 200 | 大量結果取得 |

---

## 9. エラーハンドリング

ハイブリッド検索で発生しうるエラーと対処方針:

| エラー | 条件 | 対処 |
|---|---|---|
| `InvalidParameter` | K <= 0, SubSearchK < K | `Result<T>.Fail()` を返す |
| `InvalidParameter` | 全クエリが未設定 (default) | `Result<T>.Fail()` を返す |
| `IndexNotBuilt` | サブ検索のインデックスが未構築 | 該当サブ検索をスキップし、有効なサブ検索のみで RRF 実行 |
| `DimensionMismatch` | Dense クエリの次元数不一致 | `Result<T>.Fail()` を返す |

サブ検索が1つでも成功すれば、ハイブリッド検索は結果を返す（Graceful Degradation）。
全サブ検索が失敗した場合のみエラーを返す。

エラーハンドリングの基本設計は [01-architecture.md](./01-architecture.md) のセクション 6 を参照。

### Graceful Degradation の詳細フロー

サブ検索の一部が失敗した場合でも、成功したサブ検索の結果のみで RRF マージを実行する。

```
Execute(params):

  // 1. 各サブ検索を実行し、成功/失敗を記録
  denseOk = false
  sparseOk = false
  bm25Ok = false

  if DenseQuery.IsCreated:
      denseResult = ExecuteDenseSearch(...)
      denseOk = denseResult.IsSuccess
      if !denseOk: Log.Warning("Dense search failed: {denseResult.Error}")

  if SparseQuery.IsCreated:
      sparseResult = ExecuteSparseSearch(...)
      sparseOk = sparseResult.IsSuccess
      if !sparseOk: Log.Warning("Sparse search failed: {sparseResult.Error}")

  if TextQuery.IsCreated:
      bm25Result = ExecuteBm25Search(...)
      bm25Ok = bm25Result.IsSuccess
      if !bm25Ok: Log.Warning("BM25 search failed: {bm25Result.Error}")

  // 2. 全サブ検索が失敗した場合のみエラーを返す
  if !denseOk AND !sparseOk AND !bm25Ok:
      return Result<NativeArray<SearchResult>>.Fail(ErrorCode.IndexNotBuilt)

  // 3. 成功したサブ検索のみで RRF マージ
  RrfMerge(
      denseResults: denseOk ? denseResults : empty,
      sparseResults: sparseOk ? sparseResults : empty,
      bm25Results: bm25Ok ? bm25Results : empty,
      denseCount: denseOk ? denseResults.Length : 0,
      sparseCount: sparseOk ? sparseResults.Length : 0,
      bm25Count: bm25Ok ? bm25Results.Length : 0
  )
```

この設計により、たとえば Dense インデックスのみが構築済みの初期段階でも
ハイブリッド検索 API を呼び出すことが可能となる。

---

## 10. 使用例

### 10.1 基本的なハイブリッド検索

```csharp
var db = new UniCortexDatabase(config);

// ドキュメント追加
db.Add(id: 1, denseVector: vec1, text: "火属性の伝説の剣");
db.Add(id: 2, denseVector: vec2, text: "氷属性の魔法の杖");
// ...

// ハイブリッド検索 (Dense + BM25)
var results = db.SearchHybrid(new HybridSearchParams
{
    DenseQuery = queryVector,
    TextQuery = Encoding.UTF8.GetBytes("伝説の武器"),
    K = 10,
    SubSearchK = 50,
    RrfConfig = RrfConfig.Default,
});

if (results.IsSuccess)
{
    foreach (var result in results.Value)
    {
        Debug.Log($"ID: {result.Id}, Score: {result.Score}");
    }
}
```

### 10.2 重み付きハイブリッド検索

```csharp
// セマンティック検索を重視する設定
var results = db.SearchHybrid(new HybridSearchParams
{
    DenseQuery = queryVector,
    SparseQuery = sparseQueryVector,
    TextQuery = Encoding.UTF8.GetBytes("伝説の武器"),
    K = 10,
    SubSearchK = 50,
    RrfConfig = new RrfConfig
    {
        RankConstant = 60f,
        DenseWeight = 2.0f,    // Dense を重視
        SparseWeight = 1.0f,
        Bm25Weight = 0.5f,     // BM25 の影響を抑える
    },
});
```

### 10.3 2手法のみの組み合わせ

```csharp
// Dense + BM25 のみ (Sparse は未指定 = スキップ)
var results = db.SearchHybrid(new HybridSearchParams
{
    DenseQuery = queryVector,
    // SparseQuery は未設定 → Sparse 検索はスキップされる
    TextQuery = Encoding.UTF8.GetBytes("伝説の武器"),
    K = 10,
    SubSearchK = 50,
    RrfConfig = RrfConfig.Default,
});
```

---

## 関連ドキュメント

| ドキュメント | 内容 |
|---|---|
| [01-architecture.md](./01-architecture.md) | アーキテクチャ概要・エラーハンドリング方針 |
| [03-hnsw-design.md](./03-hnsw-design.md) | Dense ベクトル検索 (HNSW) の詳細設計 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse ベクトル検索の詳細設計 |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 全文検索の詳細設計 |
| [07-filter-design.md](./07-filter-design.md) | スカラーフィルタ設計 |
| [10-technical-constraints.md](./10-technical-constraints.md) | 技術制約・プラットフォーム対応 |
| [11-security-guidelines.md](./11-security-guidelines.md) | セキュリティガイドライン |
| [12-test-plan.md](./12-test-plan.md) | テスト計画 |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット |
