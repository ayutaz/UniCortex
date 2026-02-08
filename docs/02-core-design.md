# Core データ構造・API 設計

本ドキュメントでは UniCortex の共通基盤となるデータ構造、エラーハンドリング戦略、
メモリレイアウト、距離関数、およびファサード API の設計を記述する。

ここで定義する型はすべて `Assets/UniCortex/Runtime/Core/` ディレクトリに配置され、
HNSW ([03-hnsw-design.md](./03-hnsw-design.md))、Sparse ([04-sparse-design.md](./04-sparse-design.md))、
BM25 ([05-bm25-design.md](./05-bm25-design.md))、Hybrid ([06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md))、
Filter ([07-filter-design.md](./07-filter-design.md))、
Persistence ([08-persistence-design.md](./08-persistence-design.md)) の各コンポーネントから共通参照される。

全体のアーキテクチャについては [01-architecture.md](./01-architecture.md) を参照。

---

## 目次

1. [エラーハンドリング](#1-エラーハンドリング)
2. [検索結果型](#2-検索結果型)
3. [VectorStorage (SoA レイアウト)](#3-vectorstorage-soa-レイアウト)
4. [IdMap (双方向マッピング + FreeList)](#4-idmap-双方向マッピング--freelist)
5. [MetadataStorage (カラムナストレージ)](#5-metadatastorage-カラムナストレージ)
6. [NativeMinHeap / NativeMaxHeap](#6-nativeminheap--nativemaxheap)
7. [距離関数](#7-距離関数)
8. [UniCortexDatabase ファサード API](#8-unicortexdatabase-ファサード-api)

---

## 1. エラーハンドリング

### 設計判断

Burst Compiler 環境ではマネージド例外 (`throw` / `try-catch`) を使用できない。
そのため、UniCortex ではすべての失敗可能な操作に対して **Result パターン** を採用する。

- `ErrorCode` enum で失敗原因を明示する
- `Result<T>` 構造体で成功値またはエラーコードを返す
- `where T : unmanaged` 制約により Burst 互換を保証する
- `IsSuccess` プロパティによるシンプルなチェックを提供する

この方式は Go 言語のエラー返却パターンに近く、
Burst/IL2CPP 環境で安全かつゼロアロケーションで動作する。

### 定義

```csharp
public enum ErrorCode : byte
{
    None = 0,
    NotFound,
    DuplicateId,
    CapacityExceeded,
    DimensionMismatch,
    InvalidParameter,
    IndexNotBuilt,
    FileNotFound,           // 指定されたファイルが存在しない
    InvalidFileFormat,      // マジックナンバー不一致
    IncompatibleVersion,    // メジャーバージョン不一致
    DataCorrupted,          // CRC32 チェックサム不一致
    IoError,                // ファイル I/O エラー
    StorageQuotaExceeded,   // WebGL IndexedDB クォータ超過
}

public struct Result<T> where T : unmanaged
{
    public ErrorCode Error;
    public T Value;
    public bool IsSuccess => Error == ErrorCode.None;

    public static Result<T> Success(T value) => new Result<T> { Error = ErrorCode.None, Value = value };
    public static Result<T> Fail(ErrorCode error) => new Result<T> { Error = error };
}
```

### ErrorCode の意味

| コード | 意味 | 発生場面の例 |
|---|---|---|
| `None` | 成功 | -- |
| `NotFound` | 指定 ID が存在しない | `Delete` / `Update` / `Get` 時 |
| `DuplicateId` | 同一 ID の重複追加 | `Add` 時 |
| `CapacityExceeded` | 容量上限に到達 | `Add` 時 (VectorStorage / IdMap の容量超過) |
| `DimensionMismatch` | ベクトル次元数の不一致 | `Add` / `Update` / `Search` 時 |
| `InvalidParameter` | パラメータが不正 | K <= 0, EfSearch <= 0 等 |
| `IndexNotBuilt` | インデックス未構築 | `Build()` 前の `Search` 呼び出し |
| `FileNotFound` | 指定されたファイルが存在しない | `Load` 時にファイルが見つからない |
| `InvalidFileFormat` | マジックナンバー不一致 | `Load` 時にファイル形式が不正 |
| `IncompatibleVersion` | メジャーバージョン不一致 | `Load` 時にバージョン非互換 |
| `DataCorrupted` | CRC32 チェックサム不一致 | `Load` 時にデータ破損を検出 |
| `IoError` | ファイル I/O エラー | `Save` / `Load` 時の I/O 失敗 |
| `StorageQuotaExceeded` | WebGL IndexedDB クォータ超過 | WebGL 環境での `Save` 時にストレージ不足 |

### 使用例

```csharp
Result<int> result = idMap.Add(externalId);
if (!result.IsSuccess)
{
    // result.Error で原因を判別して対処
    Debug.LogWarning($"Add failed: {result.Error}");
    return;
}
int internalId = result.Value;
```

---

## 2. 検索結果型

### 設計判断

検索結果はすべてのインデックス種別 (Dense / Sparse / BM25 / Hybrid) で共通の型を使用する。
これにより RRF ReRanker ([06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md)) での結果統合が容易になる。

- `SearchResult` は内部 ID とスコアのペア。`IComparable<SearchResult>` を実装し、ヒープやソートで直接利用可能
- `SearchParams` は検索パラメータを集約する。Dense 固有の `EfSearch` も含むが、他の検索方式では無視される
- `DistanceType` で距離関数の種別を指定する。`DatabaseConfig.DistanceType` で HNSW グラフ構築時の距離関数を決定し、`SearchParams.DistanceType` で検索時の距離関数を指定する。グラフ構築と検索で同一の距離関数を使用することを推奨する

### 定義

```csharp
public struct SearchResult : IComparable<SearchResult>
{
    public int InternalId;
    public float Score;

    public int CompareTo(SearchResult other) => Score.CompareTo(other.Score);
}

public struct SearchParams
{
    public int K;              // 返却件数
    public int EfSearch;       // HNSW 探索幅 (Dense 用)
    public DistanceType DistanceType;
}
```

### SearchResult のソート順 — 統一スコア符号規約

`Score` の昇順ソートとなる。**全検索方式で「Score が小さいほど関連度が高い」** 規約を維持する。
各検索方式は以下のようにスコアを格納する:

#### Dense ベクトル検索

| DistanceType | Score の意味 | 小さいほど類似 |
|---|---|---|
| `EuclideanSq` | ユークリッド距離の二乗 | Yes |
| `Cosine` | 1 - cosine similarity | Yes |
| `DotProduct` | 負の内積 (`-dot`) | Yes |

#### Sparse ベクトル検索 / BM25 全文検索

Sparse 検索の内積スコアと BM25 スコアは「大きいほど関連度が高い」特性を持つため、
**SearchResult.Score には負値 (`-score`) を格納する**。

| 検索方式 | 元のスコア特性 | Score への格納方法 | 格納後の特性 |
|---|---|---|---|
| Sparse (内積) | 大きいほど類似 | `-dotProduct` | 小さいほど類似 |
| BM25 | 大きいほど関連 | `-bm25Score` | 小さいほど関連 |

この負値変換により、全検索方式で以下が成立する:
- `SearchResult.CompareTo` の昇順ソートで上位結果が先頭に来る
- NativeMaxHeap で Top-K 選択時、最も関連度の低い結果 (Score が最大) を Pop で除去できる
- RRF ReRanker ([06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md)) でランク付けする際、Score 昇順でランクを割り当てられる

> **注意**: RRF ReRanker はランクベースの統合であるため、Score の絶対値は使用しない。
> 各サブ検索結果のソート順（Score 昇順 = 関連度降順）のみが重要である。

---

## 3. VectorStorage (SoA レイアウト)

### 設計判断

ベクトルデータの格納には **SoA (Structure of Arrays) flat レイアウト** を採用する。

- 1 つの連続した `NativeArray<float>` にすべてのベクトルを格納
- アクセスパターン: `index = internalId * dimension + dimIndex`
- Capacity ベースの事前確保 (例: 50,000 ベクトル x 128 次元 = 25.6 MB)

**SoA を選択した理由:**

1. **SIMD 効率**: 距離計算時にベクトルの各次元を `float4` 単位で連続読み出しできる。同一ベクトルの全次元がメモリ上で連続するため、キャッシュラインを最大限に活用できる
2. **メモリアライメント**: NativeArray はアライメントが保証されており、Burst が SIMD 命令を安全に生成できる
3. **MemoryMappedFile との親和性**: 単一の連続バッファは mmap によるファイルマッピングと相性が良く、永続化レイヤー ([08-persistence-design.md](./08-persistence-design.md)) での実装が容易になる

**注意**: ここでの「SoA」は「各ベクトルの全次元が連続する flat array」を指す。ベクトル単位で見れば AoS (各ベクトルが連続) だが、VectorStorage 全体としては単一の float 配列に flatten されるため SoA flat と呼称する。

### 定義

```csharp
public struct VectorStorage : IDisposable
{
    public NativeArray<float> Data;     // SoA flat array
    public int Dimension;
    public int Count;
    public int Capacity;

    public VectorStorage(int capacity, int dimension, Allocator allocator)
    {
        Data = new NativeArray<float>(capacity * dimension, allocator);
        Dimension = dimension;
        Count = 0;
        Capacity = capacity;
    }

    /// <summary>
    /// ベクトルを追加する。
    /// </summary>
    /// <returns>
    /// 成功時: Result&lt;bool&gt;.Success(true)
    /// 失敗時: DimensionMismatch (vector.Length != Dimension),
    ///         InvalidParameter (internalId が範囲外),
    ///         CapacityExceeded (internalId >= Capacity)
    /// </returns>
    public Result<bool> Add(int internalId, NativeArray<float> vector) { ... }

    /// <summary>
    /// ベクトルを参照する（ゼロコピー）。
    /// </summary>
    /// <returns>
    /// 成功時: 該当ベクトルの NativeSlice。
    /// 失敗時（internalId が範囲外）: default(NativeSlice&lt;float&gt;)。
    /// 呼び出し元は internalId の有効性を事前確認すること。
    /// </returns>
    public NativeSlice<float> Get(int internalId) { ... }

    /// <summary>
    /// ベクトルを上書き更新する。
    /// </summary>
    /// <returns>
    /// 成功時: Result&lt;bool&gt;.Success(true)
    /// 失敗時: DimensionMismatch (vector.Length != Dimension),
    ///         InvalidParameter (internalId が範囲外)
    /// </returns>
    public Result<bool> Update(int internalId, NativeArray<float> vector) { ... }

    public void Dispose()
    {
        if (Data.IsCreated) Data.Dispose();
    }
}
```

### メモリレイアウト図

```
Data (NativeArray<float>):
┌─────────────────────┬─────────────────────┬─────────────────────┬───
│  Vector 0 (dim=128) │  Vector 1 (dim=128) │  Vector 2 (dim=128) │ ...
│ [0..127]            │ [128..255]          │ [256..383]          │
└─────────────────────┴─────────────────────┴─────────────────────┴───
```

### 容量見積もり

| ベクトル数 | 次元数 | メモリ使用量 |
|---|---|---|
| 10,000 | 128 | 4.88 MB |
| 50,000 | 128 | 24.4 MB |
| 50,000 | 256 | 48.8 MB |
| 50,000 | 768 | 146.5 MB |

ターゲット規模 (~50,000 ベクトル, 128 次元) では約 25 MB となり、
モバイルデバイスでも十分に収まる。

### 操作の計算量

| 操作 | 計算量 | 備考 |
|---|---|---|
| `Add` | O(d) | d = 次元数。ベクトルコピー |
| `Get` | O(1) | NativeSlice によるゼロコピー参照 |
| `Update` | O(d) | ベクトル上書きコピー |

### インデックス計算の安全性

VectorStorage のインデックス計算 `internalId * dimension + dimIndex` は、
大きな `internalId` や `dimension` の値に対して整数オーバーフローを起こす可能性がある。

**対策:**

1. **境界チェック**: `Add` / `Get` / `Update` の各操作で `internalId >= 0 && internalId < Capacity` を検証する
2. **オーバーフロー防止**: 中間計算に `long` を使用するか、`checked` 算術を適用する

```csharp
// 安全なインデックス計算の例
public NativeSlice<float> Get(int internalId)
{
    // 境界チェック
    if (internalId < 0 || internalId >= Capacity)
        return default; // エラーハンドリング

    // long による中間計算でオーバーフローを回避
    long offset = (long)internalId * Dimension;
    return Data.Slice((int)offset, Dimension);
}
```

セキュリティ上の詳細な方針については [11-security-guidelines.md](./11-security-guidelines.md) を参照。

---

## 4. IdMap (双方向マッピング + FreeList)

### 設計判断

UniCortex は外部 ID (`ulong`) と内部 ID (`int`) を分離する。

- **外部 ID (ulong)**: ユーザーが指定する任意の識別子。ゲーム内アイテム ID、データベースの主キー等
- **内部 ID (int)**: VectorStorage や HNSW グラフの配列インデックスとして使用。連続した整数値

この分離には以下の利点がある:

1. **配列インデックスの直接利用**: 内部 ID を配列インデックスとしてそのまま使えるため、ハッシュテーブルのルックアップが不要。VectorStorage や HNSW の隣接リストへのアクセスが O(1) になる
2. **メモリ効率**: 内部 ID は 0 から連番で振られるため、配列に空きが生じにくい
3. **FreeList による再利用**: 削除された内部 ID は FreeList に追加され、次回の `Add` で再利用される。これにより配列の断片化を最小限に抑える

### 定義

```csharp
public struct IdMap : IDisposable
{
    public NativeParallelHashMap<ulong, int> ExternalToInternal;
    public NativeArray<ulong> InternalToExternal;
    public NativeList<int> FreeList;
    public int Count;

    public IdMap(int capacity, Allocator allocator)
    {
        ExternalToInternal = new NativeParallelHashMap<ulong, int>(capacity, allocator);
        InternalToExternal = new NativeArray<ulong>(capacity, allocator);
        FreeList = new NativeList<int>(allocator);
        Count = 0;
    }

    public Result<int> Add(ulong externalId) { ... }
    public Result<int> GetInternal(ulong externalId) { ... }
    public Result<ulong> GetExternal(int internalId) { ... }
    public Result<int> Remove(ulong externalId) { ... }

    public void Dispose()
    {
        if (ExternalToInternal.IsCreated) ExternalToInternal.Dispose();
        if (InternalToExternal.IsCreated) InternalToExternal.Dispose();
        if (FreeList.IsCreated) FreeList.Dispose();
    }
}
```

### Add のフロー

```
Add(externalId=42):
  1. ExternalToInternal に 42 が存在するか確認 → 存在すれば DuplicateId エラー
  2. FreeList が空でなければ Pop して internalId を取得
     FreeList が空なら:
       Count >= Capacity の場合 → CapacityExceeded エラー
       そうでなければ Count を internalId として使用し Count++
  3. ExternalToInternal[42] = internalId
  4. InternalToExternal[internalId] = 42
  5. Result<int>.Success(internalId) を返す
```

### Remove のフロー

```
Remove(externalId=42):
  1. ExternalToInternal から internalId を取得 → 存在しなければ NotFound エラー
  2. ExternalToInternal から 42 を削除
  3. InternalToExternal[internalId] を sentinel 値 (ulong.MaxValue) に設定
     ※ ゼロクリア (0) では externalId = 0 の正当な ID と区別できないため、
       sentinel 値として ulong.MaxValue を使用する
  4. FreeList に internalId を Push
  5. Result<int>.Success(internalId) を返す (呼び出し元が関連データを削除するため)
```

### GetExternal の Remove 後挙動

```
GetExternal(internalId):
  1. internalId が範囲外 (< 0 || >= Capacity) → NotFound エラー
  2. InternalToExternal[internalId] が sentinel 値 (ulong.MaxValue) → NotFound エラー
     ※ FreeList に含まれる削除済み internalId はこの sentinel チェックで検出される
  3. Result<ulong>.Success(InternalToExternal[internalId]) を返す
```

### 操作の計算量

| 操作 | 計算量 | 備考 |
|---|---|---|
| `Add` | O(1) 平均 | HashMap の挿入 |
| `GetInternal` | O(1) 平均 | HashMap のルックアップ |
| `GetExternal` | O(1) | 配列インデックスアクセス |
| `Remove` | O(1) 平均 | HashMap の削除 + FreeList Push |

---

## 5. MetadataStorage (カラムナストレージ)

### 設計判断

スカラーフィルタ ([07-filter-design.md](./07-filter-design.md)) で使用するメタデータを格納する。
**カラムナ (列指向) ストレージ** を採用し、フィルタリング時の効率を最大化する。

- フィールド名は `int` ハッシュ値に変換して使用する (文字列比較を回避)
- 各データ型ごとに独立した `NativeParallelHashMap` を保持する
- 対応型: `Int32`, `Float32`, `Bool`

**カラムナストレージを選択した理由:**

1. **フィルタの効率**: `price >= 1000` のようなフィルタでは、`price` カラムのみをスキャンすれば良い。行指向だと無関係なカラムのデータもキャッシュラインに載ってしまう
2. **型安全性**: 型ごとに HashMap を分離することで、Burst 互換のまま型安全なアクセスを実現する。マネージドな `object` 型や `Any` パターンを回避できる
3. **スパースデータ対応**: HashMap ベースなので、全ドキュメントが同じフィールドを持つ必要がない。フィールドを持たないドキュメントはエントリが存在しないだけで、メモリを消費しない

### 定義

```csharp
public struct MetadataStorage : IDisposable
{
    public NativeParallelHashMap<int, NativeParallelHashMap<int, int>> IntColumns;
    public NativeParallelHashMap<int, NativeParallelHashMap<int, float>> FloatColumns;
    public NativeParallelHashMap<int, NativeParallelHashMap<int, bool>> BoolColumns;

    /// <summary>int 値を設定する。既存値がある場合は上書きする。</summary>
    /// <returns>成功時: true。失敗時: InvalidParameter (internalId が範囲外)。</returns>
    public Result<bool> SetInt(int internalId, int fieldHash, int value) { ... }

    /// <summary>int 値を取得する。</summary>
    /// <returns>
    /// 成功時: 格納値。
    /// 失敗時: NotFound (該当フィールドが存在しない — フィールド未設定の場合)。
    /// </returns>
    public Result<int> GetInt(int internalId, int fieldHash) { ... }

    /// <summary>float 値を設定する。既存値がある場合は上書きする。</summary>
    public Result<bool> SetFloat(int internalId, int fieldHash, float value) { ... }

    /// <summary>float 値を取得する。</summary>
    /// <returns>成功時: 格納値。失敗時: NotFound。</returns>
    public Result<float> GetFloat(int internalId, int fieldHash) { ... }

    /// <summary>bool 値を設定する。既存値がある場合は上書きする。</summary>
    public Result<bool> SetBool(int internalId, int fieldHash, bool value) { ... }

    /// <summary>bool 値を取得する。</summary>
    /// <returns>成功時: 格納値。失敗時: NotFound。</returns>
    public Result<bool> GetBool(int internalId, int fieldHash) { ... }

    /// <summary>
    /// 指定 internalId の全メタデータを削除する。
    /// 全型 (Int/Float/Bool) の全カラムから該当 internalId のエントリを除去する。
    /// 存在しないフィールドの削除は無視する（エラーにしない）。
    /// </summary>
    /// <returns>成功時: true。失敗時: InvalidParameter (internalId が範囲外)。</returns>
    public Result<bool> Remove(int internalId) { ... }

    public void Dispose() { ... }
}

### MetadataStorage のエラー条件まとめ

| 操作 | エラー条件 | ErrorCode |
|---|---|---|
| `Set*` | `internalId < 0` | `InvalidParameter` |
| `Get*` | 該当フィールドが未設定 | `NotFound` |
| `Remove` | `internalId < 0` | `InvalidParameter` |
| `Get*` / `Set*` | ハッシュ衝突で異なるフィールド名が同一 fieldHash に | — (衝突は許容。50K 規模で実用上問題なし) |
```

### フィールド名のハッシュ化

```csharp
// 文字列フィールド名を int ハッシュに変換するユーティリティ
public static int HashFieldName(string fieldName)
{
    return fieldName.GetHashCode();  // マネージド API 層でのみ使用
}
```

フィールド名のハッシュ化はマネージド API 層 (UniCortexDatabase ファサード) で行い、
Core 層では `int` ハッシュ値のみを扱う。これにより Core 層は Burst 互換を維持する。

### ストレージ構造図

```
IntColumns:
  fieldHash("price") → { internalId:0 → 500, internalId:1 → 1200, ... }
  fieldHash("level") → { internalId:0 → 5,   internalId:2 → 10,   ... }

FloatColumns:
  fieldHash("weight") → { internalId:0 → 2.5, internalId:1 → 0.8, ... }

BoolColumns:
  fieldHash("is_rare") → { internalId:1 → true, internalId:3 → true, ... }
```

---

## 6. NativeMinHeap / NativeMaxHeap

### 設計判断

検索アルゴリズムでは上位 K 件の候補を効率的に管理する必要がある。
固定容量の **binary heap** を `NativeArray` ベースで実装する。

- **NativeMinHeap**: Score が最小の要素を先頭に保持。最終結果の取得に使用
- **NativeMaxHeap**: Score が最大の要素を先頭に保持。HNSW 探索中の候補管理に使用 (最悪スコアの候補を効率的に除去)

**NativeArray ベースの binary heap を選択した理由:**

1. **固定メモリ**: 容量を事前確保するため、検索中の動的アロケーションが一切発生しない
2. **キャッシュ効率**: 配列ベースの heap はメモリが連続しており、キャッシュフレンドリー
3. **Burst 互換**: マネージド型を一切使用しないため、Burst コンパイル可能
4. **シンプルさ**: binary heap は実装が単純で、バグの混入リスクが低い

### 定義

```csharp
public struct NativeMinHeap : IDisposable
{
    public NativeArray<SearchResult> Data;
    public int Count;
    public int Capacity;

    public NativeMinHeap(int capacity, Allocator allocator)
    {
        Data = new NativeArray<SearchResult>(capacity, allocator);
        Count = 0;
        Capacity = capacity;
    }

    /// <summary>
    /// 要素を追加する。
    /// 容量超過時: Count == Capacity の場合、item.Score が Peek().Score より小さければ
    /// 最大要素を Pop して item を Push する。そうでなければ何もしない（item を破棄）。
    /// </summary>
    public void Push(SearchResult item) { ... }

    /// <summary>
    /// 最小スコアの要素を取り出す。
    /// 空ヒープ時: Count == 0 の場合、default(SearchResult) を返す。
    /// 呼び出し元は Count > 0 を事前確認すること。
    /// </summary>
    public SearchResult Pop() { ... }

    /// <summary>
    /// 最小スコアの要素を参照する（削除しない）。
    /// 空ヒープ時: Count == 0 の場合、default(SearchResult) を返す。
    /// </summary>
    public SearchResult Peek() { ... }

    public void Clear() { Count = 0; }

    public void Dispose()
    {
        if (Data.IsCreated) Data.Dispose();
    }
}

public struct NativeMaxHeap : IDisposable
{
    public NativeArray<SearchResult> Data;
    public int Count;
    public int Capacity;

    public NativeMaxHeap(int capacity, Allocator allocator)
    {
        Data = new NativeArray<SearchResult>(capacity, allocator);
        Count = 0;
        Capacity = capacity;
    }

    /// <summary>
    /// 要素を追加する。
    /// 容量超過時: Count == Capacity の場合、item.Score が Peek().Score より大きければ
    /// 最小要素を Pop して item を Push する。そうでなければ何もしない（item を破棄）。
    /// </summary>
    public void Push(SearchResult item) { ... }

    /// <summary>
    /// 最大スコアの要素を取り出す。
    /// 空ヒープ時: Count == 0 の場合、default(SearchResult) を返す。
    /// 呼び出し元は Count > 0 を事前確認すること。
    /// </summary>
    public SearchResult Pop() { ... }

    /// <summary>
    /// 最大スコアの要素を参照する（削除しない）。
    /// 空ヒープ時: Count == 0 の場合、default(SearchResult) を返す。
    /// </summary>
    public SearchResult Peek() { ... }

    public void Clear() { Count = 0; }

    public void Dispose()
    {
        if (Data.IsCreated) Data.Dispose();
    }
}
```

### 使い分け

| 構造 | 用途 | 典型的な使用場面 |
|---|---|---|
| NativeMinHeap | 最小スコアを先頭に | 最終結果の収集 (上位 K 件を Score 昇順で取得) |
| NativeMaxHeap | 最大スコアを先頭に | HNSW 探索の候補プール (最悪候補を効率的に Pop) |

### 操作の計算量

| 操作 | 計算量 |
|---|---|
| `Push` | O(log n) |
| `Pop` | O(log n) |
| `Peek` | O(1) |

### HNSW 探索での活用例

HNSW の greedy search ([03-hnsw-design.md](./03-hnsw-design.md)) では、
候補セット (candidates) に NativeMinHeap、結果セット (results) に NativeMaxHeap を使用する:

```
candidates (NativeMinHeap): 最も近い候補を Pop して探索を進める
results (NativeMaxHeap):    結果が K 件を超えたら最も遠い候補を Pop して除去
```

---

## 7. 距離関数

### 設計判断

UniCortex は 3 種類の距離関数をサポートする。
すべての距離関数は `Unity.Mathematics.float4` を用いた SIMD auto-vectorize で実装し、
Burst Compiler がプラットフォームに応じて SSE/AVX (x86) または NEON (ARM) 命令を自動生成する。

**float4 による手動ベクトル化を選択した理由:**

1. **Burst の auto-vectorize 保証**: `float4` 演算は Burst が確実に SIMD 命令に変換する。スカラー `float` のループでは auto-vectorize が効かない場合がある
2. **ポータビリティ**: 同一のコードが x86 (SSE/AVX) と ARM (NEON) の両方で最適化される
3. **WebGL 対応**: Pure C# であるため、ネイティブ intrinsics に依存しない

### DistanceType 定義

```csharp
public enum DistanceType : byte
{
    EuclideanSq,    // ユークリッド距離の二乗
    Cosine,         // コサイン距離 (1 - cosine similarity)
    DotProduct,     // 負の内積 (-dot product)
}
```

### Euclidean Squared Distance

二点間のユークリッド距離の二乗を返す。平方根を省略することで計算コストを削減する。
順序関係は保存されるため、最近傍探索では二乗距離で十分である。

```csharp
[BurstCompile]
public static float EuclideanSq(NativeSlice<float> a, NativeSlice<float> b, int dim)
{
    float4 sum = float4.zero;
    int i = 0;
    for (; i + 3 < dim; i += 4)
    {
        float4 va = new float4(a[i], a[i + 1], a[i + 2], a[i + 3]);
        float4 vb = new float4(b[i], b[i + 1], b[i + 2], b[i + 3]);
        float4 diff = va - vb;
        sum += diff * diff;
    }
    float result = math.csum(sum);
    for (; i < dim; i++)
    {
        float d = a[i] - b[i];
        result += d * d;
    }
    return result;
}
```

### Cosine Distance

コサイン距離 = `1 - cosine_similarity` を返す。
**最適化**: 格納ベクトルを正規化済みとする前提を置くことで、コサイン距離を内積に帰着できる。

```
cosine_similarity(a, b) = dot(a, b) / (|a| * |b|)
```

`|a| = |b| = 1` (正規化済み) であれば:

```
cosine_similarity(a, b) = dot(a, b)
cosine_distance(a, b) = 1 - dot(a, b)
```

```csharp
[BurstCompile]
public static float Cosine(NativeSlice<float> a, NativeSlice<float> b, int dim)
{
    float4 sum = float4.zero;
    int i = 0;
    for (; i + 3 < dim; i += 4)
    {
        float4 va = new float4(a[i], a[i + 1], a[i + 2], a[i + 3]);
        float4 vb = new float4(b[i], b[i + 1], b[i + 2], b[i + 3]);
        sum += va * vb;
    }
    float dot = math.csum(sum);
    for (; i < dim; i++)
    {
        dot += a[i] * b[i];
    }
    return 1.0f - dot;
}
```

**前提条件**: `Add` / `Update` 時にベクトルを L2 正規化して格納する。
クエリベクトルも検索時に正規化する。この正規化は UniCortexDatabase ファサード層で自動的に行われる。

### Dot Product Distance

内積の負値を返す。内積が大きいほど類似度が高いため、負値にすることで
「Score が小さいほど類似」という統一規約を維持する。

```csharp
[BurstCompile]
public static float DotProduct(NativeSlice<float> a, NativeSlice<float> b, int dim)
{
    float4 sum = float4.zero;
    int i = 0;
    for (; i + 3 < dim; i += 4)
    {
        float4 va = new float4(a[i], a[i + 1], a[i + 2], a[i + 3]);
        float4 vb = new float4(b[i], b[i + 1], b[i + 2], b[i + 3]);
        sum += va * vb;
    }
    float dot = math.csum(sum);
    for (; i < dim; i++)
    {
        dot += a[i] * b[i];
    }
    return -dot;
}
```

### 距離関数ディスパッチ

Burst 互換のため、関数ポインタによるディスパッチを使用する:

```csharp
public static float ComputeDistance(
    NativeSlice<float> a, NativeSlice<float> b, int dim, DistanceType type)
{
    switch (type)
    {
        case DistanceType.EuclideanSq: return EuclideanSq(a, b, dim);
        case DistanceType.Cosine:      return Cosine(a, b, dim);
        case DistanceType.DotProduct:  return DotProduct(a, b, dim);
        default:                       return EuclideanSq(a, b, dim);
    }
}
```

> **注**: Burst Compiler は `switch` 文を最適化するため、仮想メソッドディスパッチよりも高効率である。
> Jobs System 内で使用する場合は `FunctionPointer<T>` によるディスパッチも検討する。
> 詳細は [03-hnsw-design.md](./03-hnsw-design.md) を参照。

### パフォーマンス特性

| 距離関数 | 計算コスト | 備考 |
|---|---|---|
| EuclideanSq | 2 FLOP/dim (差分 + 二乗) | 平方根を省略しているため最軽量 |
| Cosine | 1 FLOP/dim (乗算のみ) + 1 減算 | 正規化済み前提。実質 DotProduct と同等 |
| DotProduct | 1 FLOP/dim (乗算のみ) + 1 符号反転 | 最も軽量 |

いずれも `float4` で 4 次元ずつ処理するため、128 次元のベクトルでは 32 回のループ反復で完了する。

---

## 8. UniCortexDatabase ファサード API

### 設計判断

UniCortexDatabase はライブラリの **ファサード (Facade)** であり、
ユーザーに対して統一された高レベル API を提供する。

- 内部コンポーネント (VectorStorage, IdMap, HNSW, Sparse, BM25, Filter) を集約し、単一のエントリポイントを提供する
- マネージド API 層として動作し、Burst 互換の内部コンポーネントをラップする
- ベクトルの正規化、フィールド名のハッシュ化等の前処理を自動的に行う
- `IDisposable` パターンにより、全 NativeContainer の確実な解放を保証する

### 定義

```csharp
public class UniCortexDatabase : IDisposable
{
    // --- ドキュメント操作 ---

    /// <summary>
    /// ドキュメントを追加する。
    /// Dense ベクトル、Sparse ベクトル、BM25 テキスト、メタデータを同時に登録できる。
    /// </summary>
    public Result<int> Add(ulong id, NativeArray<float> denseVector, ...);

    /// <summary>
    /// ドキュメントを削除する。
    /// 全インデックスおよびメタデータから該当エントリを除去する。
    /// </summary>
    public Result<int> Delete(ulong id);

    /// <summary>
    /// ドキュメントを更新する。
    /// 内部的には Delete + Add として処理される。
    /// </summary>
    public Result<int> Update(ulong id, NativeArray<float> denseVector, ...);

    // --- 検索 ---

    /// <summary>
    /// Dense ベクトル検索 (HNSW)。
    /// 戻り値は Allocator.TempJob で確保される。
    /// 呼び出し元が Dispose() を呼ぶ責任を持つ (4 フレーム以内)。
    /// </summary>
    public NativeArray<SearchResult> SearchDense(NativeArray<float> query, SearchParams param);

    /// <summary>
    /// Sparse ベクトル検索。
    /// 戻り値は Allocator.TempJob で確保される。
    /// 呼び出し元が Dispose() を呼ぶ責任を持つ (4 フレーム以内)。
    /// </summary>
    public NativeArray<SearchResult> SearchSparse(NativeArray<SparseElement> query, int k);

    /// <summary>
    /// BM25 全文検索。
    /// 戻り値は Allocator.TempJob で確保される。
    /// 呼び出し元が Dispose() を呼ぶ責任を持つ (4 フレーム以内)。
    /// </summary>
    public NativeArray<SearchResult> SearchBM25(NativeText query, int k);

    /// <summary>
    /// ハイブリッド検索 (RRF)。
    /// 複数の検索方式を組み合わせて結果を統合する。
    /// 戻り値は Allocator.TempJob で確保される。
    /// 呼び出し元が Dispose() を呼ぶ責任を持つ (4 フレーム以内)。
    /// </summary>
    public NativeArray<SearchResult> SearchHybrid(HybridSearchParams param);

    // --- ライフサイクル ---

    /// <summary>
    /// インデックスを構築する。
    /// Add でデータを投入した後、検索前に呼び出す必要がある。
    /// Build() 未実行の状態で Search を呼ぶと空結果または IndexNotBuilt エラーを返す。
    /// </summary>
    public void Build();

    /// <summary>
    /// インデックスをファイルに保存する。
    /// </summary>
    public void Save(string path);

    /// <summary>
    /// ファイルからインデックスを読み込む。
    /// </summary>
    public static UniCortexDatabase Load(string path);

    /// <summary>
    /// 全リソースを解放する。
    /// </summary>
    public void Dispose();
}
```

### ライフサイクル

```
var db = new UniCortexDatabase(config);

// 1. データ投入
db.Add(1, denseVec1, ...);
db.Add(2, denseVec2, ...);
db.Add(3, denseVec3, ...);

// 2. インデックス構築
db.Build();

// 3. 検索
var results = db.SearchDense(queryVec, new SearchParams { K = 10, EfSearch = 64 });

// 4. 結果利用
for (int i = 0; i < results.Length; i++)
{
    ulong externalId = db.GetExternalId(results[i].InternalId);
    float score = results[i].Score;
    // ...
}

// 5. 解放
results.Dispose();
db.Dispose();
```

### 検索結果の NativeArray 管理

`Search*` メソッドが返す `NativeArray<SearchResult>` は **呼び出し元が Dispose する責任** を持つ。
内部で `Allocator.TempJob` を使用して確保されるため、**4 フレーム以内に Dispose** する必要がある。
`Allocator.TempJob` は `Allocator.Temp` (1 フレーム制限) より寿命が長く、
Job 完了待ちを含むワークフローでも安全に使用できる。

```csharp
// Good: フレーム内で Dispose
var results = db.SearchDense(query, param);
ProcessResults(results);
results.Dispose();

// Bad: Dispose を忘れるとメモリリーク
var results = db.SearchDense(query, param);
ProcessResults(results);
// results.Dispose() が呼ばれない → リーク
```

### 8.1 Burst 互換パターン注記

#### NativeParallelHashMap でのスコア累積パターン

Burst Compiler 環境では `NativeParallelHashMap` の `ContainsKey` が使用できない場合がある。
ハイブリッド検索 ([06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md)) 等でスコアを累積する際は、
以下の `TryGetValue` + `Remove` + `Add` パターンを使用する:

```csharp
// Burst 互換のスコア累積パターン
// NativeParallelHashMap では ContainsKey が使用不可のため、
// TryGetValue + Remove + Add パターンを使用する
if (scores.TryGetValue(docId, out float existing))
{
    scores.Remove(docId);
    scores.Add(docId, existing + newScore);
}
else
{
    scores.Add(docId, newScore);
}
```

このパターンは以下の場面で使用される:

- RRF ReRanker での複数検索結果のスコア統合
- Sparse ベクトル検索でのドキュメントスコア累積
- BM25 検索での TF-IDF スコア累積

### コンポーネント間の関係

```
UniCortexDatabase (Facade)
├── IdMap                    ← 全コンポーネント共通の ID マッピング
├── VectorStorage            ← Dense / Sparse ベクトルの格納
├── MetadataStorage          ← スカラーフィルタ用メタデータ
├── HnswIndex                ← Dense ベクトル検索 [03-hnsw-design.md]
├── SparseIndex              ← Sparse ベクトル検索 [04-sparse-design.md]
├── Bm25Engine               ← BM25 全文検索 [05-bm25-design.md]
├── RrfReRanker              ← ハイブリッド検索 [06-hybrid-rrf-design.md]
├── ScalarFilter             ← スカラーフィルタ [07-filter-design.md]
└── PersistenceManager       ← 永続化 [08-persistence-design.md]
```

---

## 関連ドキュメント

| ドキュメント | 内容 |
|---|---|
| [00-project-overview.md](./00-project-overview.md) | プロジェクト概要 |
| [01-architecture.md](./01-architecture.md) | アーキテクチャ詳細・技術的制約 |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW Dense ベクトル検索の詳細設計 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse ベクトル検索の詳細設計 |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 全文検索の詳細設計 |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | ハイブリッド検索 (RRF) の詳細設計 |
| [07-filter-design.md](./07-filter-design.md) | スカラーフィルタの詳細設計 |
| [08-persistence-design.md](./08-persistence-design.md) | 永続化 (MemoryMappedFile) の詳細設計 |
| [11-security-guidelines.md](./11-security-guidelines.md) | セキュリティガイドライン |
| [12-test-plan.md](./12-test-plan.md) | テスト計画 |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット |
