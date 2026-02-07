# 01 - アーキテクチャ概要

UniCortex は Unity 向けオンデバイス統合検索エンジンライブラリである。
Dense ベクトル検索 (HNSW)、Sparse ベクトル検索、BM25 全文検索、ハイブリッド検索 (RRF) を
Pure C# (Burst/Jobs 最適化) で提供し、WebGL を含む全プラットフォームで動作する。

---

## 1. コンポーネント構成図

```
┌─────────────────────────────────────────────────────────────────┐
│                        UniCortex API                            │
│              Add / Search / Update / Delete / Filter            │
└──────┬──────────┬──────────┬──────────┬──────────┬──────────────┘
       │          │          │          │          │
       ▼          ▼          ▼          ▼          ▼
┌──────────┐┌──────────┐┌──────────┐┌──────────┐┌──────────────┐
│  Dense   ││ Sparse   ││  BM25    ││ Scalar   ││ Persistence  │
│ ベクトル ││ ベクトル ││ 全文検索 ││ フィルタ ││ 永続化       │
│  検索    ││  検索    ││          ││          ││              │
│ (HNSW)   ││          ││(転置Index││          ││(MemoryMapped │
│          ││          ││+Tokenizer││          ││   File)      │
└────┬─────┘└────┬─────┘└────┬─────┘└────┬─────┘└──────────────┘
     │           │           │           │
     ▼           ▼           ▼           ▼
┌─────────────────────────────────────────────┐
│         ハイブリッド検索 (RRF ReRanker)      │
│     Reciprocal Rank Fusion で結果統合        │
└──────────────────┬──────────────────────────┘
                   │
                   ▼
          ┌────────────────┐
          │   検索結果      │
          │ (スコア付き)    │
          └────────────────┘
```

### コンポーネント間の依存関係

| コンポーネント | 依存先 | 説明 |
|---|---|---|
| **HNSW Index** | Core (ベクトルストレージ, 距離計算) | Dense ベクトルの近似最近傍探索 |
| **Sparse Index** | Core (スパースベクトル表現) | キーワード特徴量ベースの検索 |
| **BM25 Engine** | Core (転置インデックス, トークナイザ) | 全文検索エンジン |
| **RRF ReRanker** | HNSW, Sparse, BM25 の検索結果 | 複数結果の統合ランキング |
| **Scalar Filter** | Core (フィルタ式評価) | メタデータベースのフィルタリング |
| **Persistence** | 全インデックス | MemoryMappedFile による永続化・遅延読み込み |

各コンポーネントの詳細設計は以下を参照:
- Dense ベクトル検索: [03-hnsw-design.md](./03-hnsw-design.md)
- Sparse ベクトル検索: [04-sparse-design.md](./04-sparse-design.md)
- BM25 全文検索: [05-bm25-design.md](./05-bm25-design.md)
- ハイブリッド検索: [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md)

---

## 2. ディレクトリ構造

```
Assets/
├── UniCortex/
│   ├── Runtime/                       # ランタイムコード
│   │   ├── UniCortex.Runtime.asmdef
│   │   ├── Core/                      # 共通データ構造・メモリ管理
│   │   │   ├── VectorStorage.cs       #   SoA ベクトルストレージ
│   │   │   ├── DistanceFunctions.cs   #   距離計算 (Cosine, L2, DotProduct)
│   │   │   ├── Result.cs              #   Result<T> エラーハンドリング
│   │   │   ├── ErrorCode.cs           #   エラーコード定義
│   │   │   └── NativeCollections.cs   #   カスタム NativeContainer
│   │   ├── Hnsw/                      # HNSW Dense ベクトル検索
│   │   │   ├── HnswIndex.cs           #   HNSW グラフ構築・検索
│   │   │   ├── HnswNode.cs            #   ノード構造体
│   │   │   ├── HnswGraph.cs           #   グラフ構造管理
│   │   │   └── HnswSearchJob.cs       #   Burst Job による並列検索
│   │   ├── Sparse/                    # Sparse ベクトル検索
│   │   │   ├── SparseIndex.cs         #   スパースベクトルインデックス
│   │   │   └── SparseVector.cs        #   スパースベクトル表現
│   │   ├── FullText/                  # BM25 全文検索・転置インデックス
│   │   │   ├── Bm25Engine.cs          #   BM25 スコアリング
│   │   │   ├── InvertedIndex.cs       #   転置インデックス
│   │   │   └── Tokenizer.cs           #   トークナイザ
│   │   ├── Hybrid/                    # RRF ReRanker・ハイブリッド検索
│   │   │   ├── HybridSearcher.cs      #   ハイブリッド検索オーケストレータ
│   │   │   └── RrfReRanker.cs         #   Reciprocal Rank Fusion
│   │   ├── Filter/                    # スカラーフィルタ
│   │   │   ├── ScalarFilter.cs        #   フィルタ式評価
│   │   │   └── FilterExpression.cs    #   フィルタ式定義
│   │   └── Persistence/              # MemoryMappedFile 永続化
│   │       ├── IndexSerializer.cs     #   インデックスシリアライズ
│   │       └── MmapStorage.cs         #   MemoryMappedFile ラッパー
│   ├── Editor/                        # エディタ拡張
│   │   └── UniCortex.Editor.asmdef
│   └── Tests/
│       ├── Runtime/                   # ランタイムテスト (PlayMode)
│       │   └── UniCortex.Tests.Runtime.asmdef
│       └── Editor/                    # エディタテスト (EditMode)
│           └── UniCortex.Tests.Editor.asmdef
```

---

## 3. Assembly Definition 設定詳細

### UniCortex.Runtime.asmdef

ランタイムコアの asmdef。Burst/Jobs/Mathematics/Collections を参照する。

```json
{
    "name": "UniCortex.Runtime",
    "rootNamespace": "UniCortex",
    "references": [
        "Unity.Burst",
        "Unity.Collections",
        "Unity.Mathematics",
        "Unity.Jobs"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

### UniCortex.Editor.asmdef

エディタ拡張用。Runtime asmdef を参照し、Editor プラットフォームのみで動作する。

```json
{
    "name": "UniCortex.Editor",
    "rootNamespace": "UniCortex.Editor",
    "references": [
        "UniCortex.Runtime"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

### UniCortex.Tests.Runtime.asmdef

ランタイムテスト用。Runtime asmdef + テストフレームワークを参照する。

```json
{
    "name": "UniCortex.Tests.Runtime",
    "rootNamespace": "UniCortex.Tests.Runtime",
    "references": [
        "UniCortex.Runtime",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

### UniCortex.Tests.Editor.asmdef

エディタテスト用。Editor asmdef + Runtime asmdef + テストフレームワークを参照する。

```json
{
    "name": "UniCortex.Tests.Editor",
    "rootNamespace": "UniCortex.Tests.Editor",
    "references": [
        "UniCortex.Runtime",
        "UniCortex.Editor",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": true,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

---

## 4. メモリレイアウト戦略

UniCortex では検索性能を最大化するため、データの性質に応じて2つのメモリレイアウトを使い分ける。

### 4.1 ベクトルデータ — SoA (Structure of Arrays)

Dense ベクトルの距離計算では、各次元を連続メモリに配置する SoA レイアウトを採用する。
SIMD 命令 (`float4`) による一括処理に最適であり、Burst Compiler が SSE/AVX (x86) や NEON (ARM) を自動生成する。

```
ベクトル数: N = 50,000
次元数:    D = 128

┌─── AoS (不採用) ─────────────────────────────────────────────┐
│ Vec0: [d0, d1, d2, ..., d127] │ Vec1: [d0, d1, ..., d127]  │
│ → 異なるベクトル間の同一次元がメモリ上で離散                    │
│ → SIMD 効率が悪い                                            │
└──────────────────────────────────────────────────────────────┘

┌─── SoA (採用) ───────────────────────────────────────────────┐
│ Dim0: [v0_d0, v1_d0, v2_d0, ..., vN_d0]  ← NativeArray<float> │
│ Dim1: [v0_d1, v1_d1, v2_d1, ..., vN_d1]  ← NativeArray<float> │
│ ...                                                           │
│ Dim127: [v0_d127, v1_d127, ..., vN_d127]  ← NativeArray<float> │
│ → 同一次元のデータが連続 → float4 で4要素同時処理              │
└──────────────────────────────────────────────────────────────┘
```

**メモリ見積もり** (50,000 ベクトル x 128 次元):
- `50,000 x 128 x 4 bytes = 25.6 MB`

### 4.2 グラフ構造 — AoS (Array of Structures)

HNSW のグラフ探索では、1ノードのメタデータと隣接リストを一括で読み出す。
ノード単位のアクセスパターンに最適な AoS レイアウトを採用し、キャッシュ局所性を優先する。

```
┌─── AoS (採用) ───────────────────────────────────────────────┐
│ Node0: { Level, NeighborCount, [Neighbor0, Neighbor1, ...] } │
│ Node1: { Level, NeighborCount, [Neighbor0, Neighbor1, ...] } │
│ Node2: { Level, NeighborCount, [Neighbor0, Neighbor1, ...] } │
│ ...                                                          │
│ → ノード探索時に必要な情報が連続メモリに配置                   │
│ → キャッシュラインに収まりやすい                               │
└──────────────────────────────────────────────────────────────┘
```

### 4.3 レイアウト選択の判断基準

| 観点 | SoA | AoS |
|---|---|---|
| **アクセスパターン** | 同一フィールドを多数要素で参照 | 1要素の全フィールドを参照 |
| **SIMD 親和性** | 高い (連続データを float4 で処理) | 低い |
| **キャッシュ局所性** | フィールド単位で高い | 要素単位で高い |
| **適用箇所** | ベクトルストレージ, 距離計算 | HNSW グラフ, ノード探索 |

詳細は [02-core-design.md](./02-core-design.md) を参照。

---

## 5. Allocator 戦略

Unity の NativeContainer は用途に応じて Allocator を選択する必要がある。
UniCortex では以下の3段階の戦略を採用する。

### 5.1 Allocator.Persistent — 長期保持データ

インデックス構築後に長期間保持するデータに使用する。
明示的な `Dispose()` が必須であり、IDisposable パターンで管理する。

| 用途 | データ構造 | ライフサイクル |
|---|---|---|
| ベクトルストレージ | `NativeArray<float>` (SoA) | インデックス生成 ~ 破棄 |
| HNSW グラフ | `NativeArray<HnswNode>` | インデックス生成 ~ 破棄 |
| 転置インデックス | `NativeHashMap<int, NativeList<int>>` | インデックス生成 ~ 破棄 |
| Sparse ベクトル | `NativeArray<SparseEntry>` | インデックス生成 ~ 破棄 |

### 5.2 Allocator.TempJob — Job 実行中の一時バッファ

IJob / IJobParallelFor で使用する一時バッファ。Job 完了後に解放する。
最大4フレームまで有効。

| 用途 | データ構造 | ライフサイクル |
|---|---|---|
| 検索クエリベクトル | `NativeArray<float>` | Job スケジュール ~ Complete |
| 候補リスト | `NativeList<SearchCandidate>` | Job スケジュール ~ Complete |
| 距離計算結果 | `NativeArray<float>` | Job スケジュール ~ Complete |

### 5.3 Allocator.Temp — フレーム内超短期バッファ

1フレーム内で完結する処理の一時バッファ。フレーム終了時に自動解放される。
Job System では使用不可。

| 用途 | データ構造 | ライフサイクル |
|---|---|---|
| フィルタ評価結果 | `NativeArray<bool>` | フレーム内 |
| 一時ソート用バッファ | `NativeArray<int>` | フレーム内 |

### 5.4 Allocator 選択フローチャート

```
データの保持期間は？
├── 複数フレームにまたがる長期保持
│   └── Allocator.Persistent  (手動 Dispose 必須)
├── Job 実行中のみ (1~4フレーム)
│   └── Allocator.TempJob     (Job 完了後に Dispose)
└── 1フレーム内で完結
    └── Allocator.Temp        (自動解放、Job 不可)
```

---

## 6. エラーハンドリング方針

### 6.1 制約

Burst Compiler は managed 例外 (`try-catch`, `throw`) をサポートしない。
そのため、UniCortex では **Result パターン** を採用し、エラーを値として返す。

### 6.2 ErrorCode 定義

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
}
```

| コード | 説明 | 発生場面 |
|---|---|---|
| `None` | 成功 | - |
| `NotFound` | 指定 ID のエントリが存在しない | Update, Delete, Get |
| `DuplicateId` | 同一 ID が既に登録済み | Add |
| `CapacityExceeded` | インデックスの容量上限に到達 | Add |
| `DimensionMismatch` | ベクトル次元数が不一致 | Add, Search |
| `InvalidParameter` | 無効なパラメータ (k <= 0 等) | Search, Filter |
| `IndexNotBuilt` | インデックスが未構築 | Search |

### 6.3 Result\<T\> 構造体

```csharp
public struct Result<T> where T : unmanaged
{
    public ErrorCode Error;
    public T Value;

    public bool IsSuccess => Error == ErrorCode.None;

    public static Result<T> Ok(T value) => new Result<T>
    {
        Error = ErrorCode.None,
        Value = value
    };

    public static Result<T> Fail(ErrorCode error) => new Result<T>
    {
        Error = error,
        Value = default
    };
}
```

### 6.4 使用例

```csharp
// 検索の呼び出し側
Result<SearchResults> result = index.Search(queryVector, k: 10);

if (!result.IsSuccess)
{
    // エラー処理 (ログ出力、UI 通知等)
    Debug.LogError($"Search failed: {result.Error}");
    return;
}

SearchResults results = result.Value;
```

Burst Job 内部ではログ出力は行わず、`ErrorCode` を `NativeReference<ErrorCode>` 等で呼び出し元に返す。

詳細は [02-core-design.md](./02-core-design.md) を参照。

---

## 7. データフロー図

### 7.1 ハイブリッド検索のデータフロー

```
                    ┌─────────────────┐
                    │  検索リクエスト   │
                    │ (Query + Filter) │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │  HybridSearcher  │
                    │ (オーケストレータ)│
                    └──┬─────┬─────┬──┘
                       │     │     │
          ┌────────────┤     │     ├────────────┐
          │            │     │     │            │
          ▼            ▼     │     ▼            │
   ┌────────────┐┌─────────┐│┌─────────┐       │
   │   HNSW     ││ Sparse  │││  BM25   │       │
   │  Search    ││ Search  │││ Search  │       │
   │(Dense Vec) ││         │││(FullText│       │
   └─────┬──────┘└────┬────┘│└────┬────┘       │
         │            │     │     │             │
         ▼            ▼     │     ▼             │
   ┌──────────────────────┐ │                   │
   │ 各エンジンの検索結果  │ │                   │
   │ (ID + スコア) のリスト│ │                   │
   └──────────┬───────────┘ │                   │
              │             │                   │
              ▼             │                   │
   ┌──────────────────────┐ │                   │
   │   RRF ReRanker        │ │                   │
   │ ──────────────────── │ │                   │
   │ score = Σ 1/(k + rank)│ │                   │
   │ k = 60 (定数)         │ │                   │
   └──────────┬───────────┘ │                   │
              │             │                   │
              ▼             ▼                   │
   ┌──────────────────────────────┐             │
   │      Scalar Filter           │◄────────────┘
   │ (price >= 1000 AND           │
   │  category == "weapon" 等)    │
   └──────────┬───────────────────┘
              │
              ▼
   ┌──────────────────────┐
   │   最終検索結果        │
   │ (Top-K, スコア降順)   │
   └──────────────────────┘
```

### 7.2 データ追加のフロー

```
┌──────────────────┐
│  Add リクエスト   │
│ (ID, Vector,     │
│  Metadata, Text) │
└────────┬─────────┘
         │
         ▼
┌──────────────────┐     ┌──────────────────┐
│ VectorStorage    │────▶│ HNSW Graph       │
│ (SoA 格納)       │     │ (ノード挿入 +     │
└──────────────────┘     │  近傍接続)        │
         │               └──────────────────┘
         │
         ├──────────────────────┐
         │                     │
         ▼                     ▼
┌──────────────────┐  ┌──────────────────┐
│ Sparse Index     │  │ BM25 Engine      │
│ (スパースベクトル  │  │ (トークナイズ +   │
│  登録)           │  │  転置インデックス  │
└──────────────────┘  │  更新)           │
         │            └──────────────────┘
         │                     │
         ▼                     ▼
┌──────────────────────────────────────────┐
│ Scalar Filter Store                      │
│ (メタデータフィールド登録)                 │
└──────────────────────────────────────────┘
```

### 7.3 永続化のフロー

```
┌──────────────┐    Save    ┌───────────────────┐
│ In-Memory    │ ─────────▶ │ IndexSerializer   │
│ Index        │            │ (バイナリ変換)     │
└──────────────┘            └─────────┬─────────┘
                                      │
                                      ▼
                            ┌───────────────────┐
                            │ MemoryMappedFile  │
                            │ (.ucx ファイル)    │
                            └─────────┬─────────┘
                                      │
                              Load    │
                                      ▼
                            ┌───────────────────┐
                            │ MmapStorage       │
                            │ (遅延読み込み)     │
                            └─────────┬─────────┘
                                      │
                                      ▼
                            ┌───────────────────┐
                            │ In-Memory Index   │
                            │ (復元)            │
                            └───────────────────┘
```

詳細は [08-persistence-design.md](./08-persistence-design.md) を参照。

---

## 関連ドキュメント

| ドキュメント | 内容 |
|---|---|
| [02-core-design.md](./02-core-design.md) | Core 層の詳細設計 (VectorStorage, DistanceFunctions, Result\<T\>) |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW インデックスの詳細設計 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse ベクトル検索の詳細設計 |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 全文検索の詳細設計 |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | ハイブリッド検索・RRF ReRanker の詳細設計 |
| [08-persistence-design.md](./08-persistence-design.md) | 永続化の詳細設計 |
