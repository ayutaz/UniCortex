# UniCortex

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![Unity](https://img.shields.io/badge/Unity-6000.0+-black.svg)](https://unity.com)

Unity 向けオンデバイス統合検索エンジンライブラリ。
Dense ベクトル検索 (HNSW) + Sparse ベクトル検索 + BM25 全文検索 + ハイブリッド検索 (RRF) を Pure C# (Burst/Jobs 最適化) で提供し、WebGL を含む全プラットフォームで動作します。

> [English version below](#english)

---

## 特徴

| 機能 | 説明 |
|---|---|
| **Dense ベクトル検索** | HNSW アルゴリズム + SIMD 最適化距離計算 |
| **Sparse ベクトル検索** | 転置インデックスベースのスパースベクトル類似度検索 |
| **BM25 全文検索** | 転置インデックス + トークナイザ (ASCII, CJK, ひらがな, カタカナ) |
| **ハイブリッド検索 (RRF)** | Reciprocal Rank Fusion で複数検索結果を統合 |
| **スカラーフィルタ** | メタデータフィルタリング (int, float, bool) |
| **永続化** | CRC32 整合性チェック付きバイナリシリアライズ |
| **WebGL 対応** | Pure C# 実装でネイティブプラグイン不要 |

### 競合との比較

| 機能 | RAGSearchUnity | hnsw-sharp | **UniCortex** |
|---|---|---|---|
| Dense ベクトル検索 | ✅ | ✅ | ✅ |
| Sparse ベクトル検索 | - | - | **✅** |
| BM25 全文検索 | - | - | **✅** |
| ハイブリッド検索 (RRF) | - | - | **✅** |
| スカラーフィルタ | - | - | **✅** |
| WebGL 対応 | △ | ✅ | **✅** |
| Pure C# (Burst) | - (USearch C++) | ✅ | **✅** |

## 動作要件

- Unity 6000.0 以降
- Burst 1.8.0+
- Collections 2.1.0+
- Mathematics 1.3.0+

## インストール

Unity Package Manager から Git URL で追加します。

**方法 1: Package Manager ウィンドウから**

1. `Window` > `Package Manager` を開く
2. 左上の `+` ボタンをクリック
3. `Add package from git URL...` を選択
4. 以下の URL を入力:

```
https://github.com/ayutaz/UniCortex.git?path=Assets/UniCortex
```

**方法 2: manifest.json を直接編集**

`Packages/manifest.json` に以下を追加:

```json
{
  "dependencies": {
    "com.unicortex.runtime": "https://github.com/ayutaz/UniCortex.git?path=Assets/UniCortex"
  }
}
```

## クイックスタート

```csharp
using UniCortex;
using Unity.Collections;

// データベースを作成
var config = DatabaseConfig.Default;
config.Dimension = 128;
config.DistanceType = DistanceType.Cosine;
var db = new UniCortexDatabase(config);

// ドキュメントを追加
var vector = new NativeArray<float>(128, Allocator.Temp);
// ... ベクトルデータを設定 ...
db.Add(1, denseVector: vector);
vector.Dispose();

// インデックスを構築 (検索前に必須)
db.Build();

// Dense ベクトル検索
var query = new NativeArray<float>(128, Allocator.Temp);
// ... クエリベクトルを設定 ...
var results = db.SearchDense(query, new SearchParams { K = 10, EfSearch = 50 });

// 結果を処理 (Score が小さいほど類似度が高い)
for (int i = 0; i < results.Length; i++)
{
    var externalId = db.GetExternalId(results[i].InternalId);
    UnityEngine.Debug.Log($"ID: {externalId}, Score: {results[i].Score}");
}

results.Dispose();
query.Dispose();
db.Dispose();
```

### BM25 全文検索

```csharp
// テキストを UTF-8 バイト配列として渡す (内部でトークナイズ)
var textBytes = System.Text.Encoding.UTF8.GetBytes("search query");
var text = new NativeArray<byte>(textBytes, Allocator.Temp);

var results = db.SearchBM25(text, 10);
text.Dispose();

// 結果を処理
for (int i = 0; i < results.Length; i++)
    UnityEngine.Debug.Log($"Score: {results[i].Score}");

results.Dispose();
```

### ハイブリッド検索

```csharp
using UniCortex.Hybrid;

var param = new HybridSearchParams
{
    DenseQuery = denseQueryVector,
    SparseQuery = sparseQueryVector,
    TextQuery = textQueryBytes,  // UTF-8 byte array
    K = 10,
    SubSearchK = 20,
    RrfConfig = RrfConfig.Default,
    DenseParams = new SearchParams { K = 20, EfSearch = 50, DistanceType = DistanceType.Cosine },
};
var result = db.SearchHybrid(param);
if (result.IsSuccess)
{
    var results = result.Value;
    // 結果を処理...
    results.Dispose();
}
```

### メタデータフィルタ

```csharp
using UniCortex.Filter;

// メタデータを設定
db.SetMetadataInt(docId: 1, fieldHash: 100, value: 1500);  // Price
db.SetMetadataFloat(docId: 1, fieldHash: 102, value: 3.2f); // Weight
db.SetMetadataBool(docId: 1, fieldHash: 103, value: true);  // IsEquipable

// 検索結果に対してメタデータでポストフィルタリング
var results = db.SearchDense(query, new SearchParams { K = 20, EfSearch = 100 });
for (int i = 0; i < results.Length; i++)
{
    var extId = db.GetExternalId(results[i].InternalId);
    if (!extId.IsSuccess) continue;
    var price = db.GetMetadataInt(extId.Value, 100);
    if (price.IsSuccess && price.Value >= 1000)
    {
        // フィルタ通過
    }
}
results.Dispose();
```

### 永続化 (Save/Load)

```csharp
using UniCortex.Persistence;

// 保存
var saveResult = IndexSerializer.Save("path/to/index.ucx", db);
if (saveResult.IsSuccess) Debug.Log("Saved!");

// 読み込み
var loadResult = IndexSerializer.Load("path/to/index.ucx");
if (loadResult.IsSuccess)
{
    var loadedDb = loadResult.Value;
    // loadedDb は Build() 済み状態で復元される
    loadedDb.Dispose(); // 使い終わったら Dispose
}
```

## サンプルシーン

6つのインタラクティブなデモシーンが含まれています。Package Manager の Samples タブからインポートできます。

| シーン | 説明 |
|---|---|
| **01_DenseSearch** | HNSW ベクトル類似度検索 (プリセットクエリ + パラメータ調整) |
| **02_BM25Search** | BM25 全文検索 (テキスト入力 + プリセット) |
| **03_SparseSearch** | スパースベクトル検索 (キーワード + 重み) |
| **04_HybridSearch** | RRF ハイブリッド検索 (Dense + Sparse + BM25 統合) |
| **05_FilterDemo** | メタデータフィルタリング (価格・レアリティ・装備可否) |
| **06_Persistence** | Save/Load ワークフロー (ステップバイステップ) |

各デモは RPG アイテムデータベース (20件) を使用し、コード生成 UGUI でプレハブ不要です。

> **Note**: サンプルシーンには `com.unity.ugui` パッケージ (2.0.0+) が必要です。`Packages/manifest.json` に含まれていない場合は追加してください。

## 距離関数

| タイプ | 説明 |
|---|---|
| `EuclideanSq` | ユークリッド距離の二乗 (デフォルト) |
| `Cosine` | コサイン距離 (1 - コサイン類似度)。正規化済みベクトルを想定 |
| `DotProduct` | 内積の負値 (-dot)。類似度が高いほどスコアが低い |

`DatabaseConfig.DistanceType` でグラフ構築時の距離関数を設定します。

## アーキテクチャ

```
UniCortex
├── Core         - 共通データ構造・メモリ管理
├── Hnsw         - HNSW Dense ベクトル検索
├── Sparse       - Sparse ベクトル検索
├── FullText     - BM25 全文検索 + トークナイザ
├── Hybrid       - RRF ハイブリッド検索オーケストレータ
├── Filter       - スカラーメタデータフィルタ
└── Persistence  - CRC32 付きバイナリ Save/Load
```

## 設計上のポイント

- **スコア符号規約**: 全検索メソッドで「Score が小さいほど関連性が高い」を統一
- **Soft Delete**: HNSW, Sparse, BM25 すべてソフト削除 + バッチ再構築パターン
- **Build() 必須**: 検索前に `Build()` を呼び出す必要あり。未構築時は空結果を返却
- **NaN/Inf バリデーション**: すべての float 入力で NaN/Inf を検証
- **Burst 互換**: マネージドメモリ不使用、NativeContainer ベース

## ライセンス

[Apache License 2.0](Assets/UniCortex/LICENSE.md)

---

<a id="english"></a>

## English

### Overview

UniCortex is an on-device unified search engine library for Unity.
It provides Dense vector search (HNSW) + Sparse vector search + BM25 full-text search + Hybrid search (RRF) in Pure C# with Burst/Jobs optimization, running on all platforms including WebGL.

### Features

| Feature | Description |
|---|---|
| **Dense Vector Search** | HNSW algorithm with SIMD-optimized distance functions |
| **Sparse Vector Search** | Inverted index based sparse vector similarity |
| **BM25 Full-Text Search** | Inverted index + tokenizer (ASCII, CJK, Hiragana, Katakana) |
| **Hybrid Search (RRF)** | Reciprocal Rank Fusion merging multiple search results |
| **Scalar Filter** | Metadata filtering (int, float, bool) |
| **Persistence** | Binary serialization with CRC32 integrity check |
| **WebGL Support** | Pure C# implementation, no native plugins required |

### Comparison with Alternatives

| Feature | RAGSearchUnity | hnsw-sharp | **UniCortex** |
|---|---|---|---|
| Dense Vector Search | ✅ | ✅ | ✅ |
| Sparse Vector Search | - | - | **✅** |
| BM25 Full-Text Search | - | - | **✅** |
| Hybrid Search (RRF) | - | - | **✅** |
| Scalar Filter | - | - | **✅** |
| WebGL Support | △ | ✅ | **✅** |
| Pure C# (Burst) | - (USearch C++) | ✅ | **✅** |

### Requirements

- Unity 6000.0 or later
- Burst 1.8.0+
- Collections 2.1.0+
- Mathematics 1.3.0+

### Installation

Add via Unity Package Manager using git URL:

**Option 1: Via Package Manager window**

1. Open `Window` > `Package Manager`
2. Click the `+` button in the top-left corner
3. Select `Add package from git URL...`
4. Enter the following URL:

```
https://github.com/ayutaz/UniCortex.git?path=Assets/UniCortex
```

**Option 2: Edit manifest.json directly**

Add to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.unicortex.runtime": "https://github.com/ayutaz/UniCortex.git?path=Assets/UniCortex"
  }
}
```

### Quick Start

```csharp
using UniCortex;
using Unity.Collections;

// Create database
var config = DatabaseConfig.Default;
config.Dimension = 128;
config.DistanceType = DistanceType.Cosine;
var db = new UniCortexDatabase(config);

// Add documents
var vector = new NativeArray<float>(128, Allocator.Temp);
// ... fill vector data ...
db.Add(1, denseVector: vector);
vector.Dispose();

// Build index (required before search)
db.Build();

// Dense vector search
var query = new NativeArray<float>(128, Allocator.Temp);
// ... fill query data ...
var results = db.SearchDense(query, new SearchParams { K = 10, EfSearch = 50 });

// Process results (lower Score = more relevant)
for (int i = 0; i < results.Length; i++)
{
    var externalId = db.GetExternalId(results[i].InternalId);
    UnityEngine.Debug.Log($"ID: {externalId}, Score: {results[i].Score}");
}

results.Dispose();
query.Dispose();
db.Dispose();
```

### Sample Scenes

Six interactive demo scenes are included. Import them from the Samples tab in Package Manager.

| Scene | Description |
|---|---|
| **01_DenseSearch** | HNSW vector similarity search with preset queries |
| **02_BM25Search** | BM25 full-text search with text input |
| **03_SparseSearch** | Sparse vector search with keyword + weight pairs |
| **04_HybridSearch** | RRF hybrid search combining Dense + Sparse + BM25 |
| **05_FilterDemo** | Metadata filtering (price, rarity, equipable) |
| **06_Persistence** | Step-by-step Save/Load workflow |

Each demo uses an RPG item database (20 items) with code-generated UGUI (no prefab dependencies).

> **Note**: Sample scenes require the `com.unity.ugui` package (2.0.0+). Add it to your `Packages/manifest.json` if not already included.

### Distance Functions

| Type | Description |
|---|---|
| `EuclideanSq` | Squared Euclidean distance (default) |
| `Cosine` | Cosine distance (1 - cosine similarity), assumes normalized vectors |
| `DotProduct` | Negative dot product (-dot), higher similarity = lower score |

Configure via `DatabaseConfig.DistanceType`.

### Architecture

```
UniCortex
├── Core         - Shared data structures, memory management
├── Hnsw         - HNSW dense vector search
├── Sparse       - Sparse vector search
├── FullText     - BM25 full-text search with tokenizer
├── Hybrid       - RRF hybrid search orchestrator
├── Filter       - Scalar metadata filter
└── Persistence  - Binary save/load with CRC32
```

### Design Principles

- **Score sign convention**: All search methods use "lower Score = more relevant"
- **Soft Delete**: HNSW, Sparse, and BM25 all use soft delete + batch rebuild pattern
- **Build() required**: `Build()` must be called before searching. Returns empty results if not built
- **NaN/Inf validation**: Mandatory validation on all float inputs
- **Burst compatible**: No managed memory, NativeContainer-based throughout

### License

[Apache License 2.0](Assets/UniCortex/LICENSE.md)
