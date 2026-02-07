# UniCortex: プロジェクト概要

## ビジョンと目的

UniCortex は Unity 向けのオンデバイス統合検索エンジンライブラリである。

既存の Unity 向け検索ソリューションは Dense ベクトル検索 (ANN) のみを提供するものが大半であり、
キーワード検索やハイブリッド検索を必要とするユースケースでは不十分だった。
UniCortex は以下の 4 種類の検索を単一ライブラリで統合し、
Pure C# (Burst/Jobs 最適化) により WebGL を含む全プラットフォームで動作する:

- **Dense ベクトル検索** -- HNSW アルゴリズムによる近似最近傍探索
- **Sparse ベクトル検索** -- キーワード特徴量ベースのスパースベクトル検索
- **BM25 全文検索** -- 転置インデックス + トークナイザによる古典的全文検索
- **ハイブリッド検索** -- Reciprocal Rank Fusion (RRF) で複数検索結果を統合

これにスカラーフィルタと MemoryMappedFile 永続化を加え、
ゲームやインタラクティブアプリケーションに必要な検索機能をワンストップで提供する。

## ターゲット

| 項目 | 値 |
|---|---|
| Unity バージョン | Unity 6 LTS (6000.3.6f1) |
| ターゲット規模 | ~50,000 ベクトル |
| 対応プラットフォーム | Windows / macOS / Linux / Android / iOS / WebGL |
| 配布形態 | UPM (Unity Package Manager) パッケージ |

## 技術スタック

| 要素 | 詳細 |
|---|---|
| 言語 | C# |
| コンパイラ最適化 | Burst Compiler (SSE/AVX on x86, NEON on ARM を自動生成) |
| 並列処理 | Unity Jobs System |
| 数学ライブラリ | Unity.Mathematics (`float4` による SIMD auto-vectorize) |
| メモリ管理 | NativeArray / NativeContainer (`Allocator.Persistent`) |
| 永続化 | `System.IO.MemoryMappedFiles` |
| テストフレームワーク | NUnit (Unity Test Framework) |

技術的制約の詳細は [01-architecture.md](./01-architecture.md) を参照。

## 競合比較

既存の Unity 向け検索ライブラリ (RAGSearchUnity/USearch, hnsw-sharp 等) は Dense ベクトル検索のみを提供する。
UniCortex は以下の機能で差別化する:

| 機能 | RAGSearchUnity | hnsw-sharp | **UniCortex** |
|---|---|---|---|
| Dense ベクトル検索 | ✅ | ✅ | ✅ |
| Sparse ベクトル検索 | ❌ | ❌ | **✅** |
| BM25 全文検索 | ❌ | ❌ | **✅** |
| ハイブリッド検索 (RRF) | ❌ | ❌ | **✅** |
| スカラーフィルタ | ❌ | ❌ | **✅** |
| WebGL 対応 | △ | ✅ | **✅** |
| Pure C# (Burst) | ❌ (USearch C++) | ✅ | **✅** |

- **RAGSearchUnity** は内部で USearch (C++ ネイティブプラグイン) を使用するため、WebGL での動作が制限される。
- **hnsw-sharp** は Pure C# だが、Dense ベクトル検索のみの単機能ライブラリである。
- **UniCortex** は Dense / Sparse / BM25 / ハイブリッドの全検索方式を Pure C# で統合提供する。

## ユースケース

### ゲーム内アイテム・NPC のセマンティック検索
プレイヤーの自然言語クエリや行動履歴の埋め込みベクトルを使い、
類似アイテムや関連 NPC をリアルタイムで検索する。

### ダイアログ・クエスト検索
大量のダイアログテキストやクエスト説明文から、
BM25 キーワード検索と Dense ベクトル検索をハイブリッドで組み合わせ、
文脈的に最も関連性の高い結果を返す。

### AI エージェントの知識ベース (RAG)
LLM ベースの AI エージェントに対し、ゲーム内データを検索して文脈として注入する
Retrieval-Augmented Generation (RAG) パイプラインのローカル検索エンジンとして機能する。

### ユーザー生成コンテンツの類似検索
UGC (ユーザー生成コンテンツ) のテキストや特徴量ベクトルを索引化し、
類似コンテンツの推薦やフィルタリングを行う。

## コンポーネント概要

UniCortex は 6 つの主要コンポーネントで構成される。

### HNSW Index
Hierarchical Navigable Small World グラフによる近似最近傍探索 (ANN) エンジン。
Burst/SIMD 最適化により高速な Dense ベクトル検索を実現する。
ベクトルデータは SoA (Structure of Arrays) レイアウトで格納し、SIMD 効率を最大化する。

詳細は [03-hnsw-design.md](./03-hnsw-design.md) を参照。

### Sparse Index
キーワード特徴量ベースのスパースベクトル検索エンジン。
高次元・疎なベクトル空間での効率的な類似度計算を提供する。

詳細は [04-sparse-design.md](./04-sparse-design.md) を参照。

### BM25 Engine
転置インデックスとトークナイザによる古典的な全文検索エンジン。
BM25 スコアリングにより、キーワードベースの関連性ランキングを行う。

詳細は [05-bm25-design.md](./05-bm25-design.md) を参照。

### RRF ReRanker
Reciprocal Rank Fusion (RRF) アルゴリズムで複数の検索結果を統合するハイブリッド検索エンジン。
Dense, Sparse, BM25 の各検索結果を統一スコアで再ランキングする。

詳細は [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) を参照。

### Scalar Filter
`price >= 1000 AND category == "weapon"` のような条件式による事前・事後フィルタリング機能。
検索結果に対してメタデータベースの絞り込みを適用する。

詳細は [07-filter-design.md](./07-filter-design.md) を参照。

### Persistence
MemoryMappedFile による永続化・遅延読み込み機能。
インデックスデータをディスクに保存し、必要に応じてページ単位で読み込むことで、
大規模データでもメモリ効率の良い運用を可能にする。

詳細は [08-persistence-design.md](./08-persistence-design.md) を参照。

## アーキテクチャ概観

```
UniCortex
├── Dense ベクトル検索 (HNSW アルゴリズム, Burst/SIMD 最適化)
├── Sparse ベクトル検索
├── BM25 全文検索 (転置インデックス + トークナイザ)
├── ハイブリッド検索 (RRF ReRanker で結果統合)
├── スカラーフィルタ
├── 永続化 (MemoryMappedFile)
└── API: Add / Search / Update / Delete / Filter
```

アーキテクチャの詳細は [01-architecture.md](./01-architecture.md) を参照。

## プロジェクト構成

```
Assets/
├── UniCortex/
│   ├── Runtime/           # ランタイムコード
│   │   ├── UniCortex.Runtime.asmdef
│   │   ├── Core/          # 共通データ構造・メモリ管理
│   │   ├── Hnsw/          # HNSW Dense ベクトル検索
│   │   ├── Sparse/        # Sparse ベクトル検索
│   │   ├── FullText/      # BM25 全文検索・転置インデックス
│   │   ├── Hybrid/        # RRF ReRanker・ハイブリッド検索
│   │   ├── Filter/        # スカラーフィルタ
│   │   └── Persistence/   # MemoryMappedFile 永続化
│   ├── Editor/            # エディタ拡張
│   │   └── UniCortex.Editor.asmdef
│   └── Tests/
│       ├── Runtime/       # ランタイムテスト
│       │   └── UniCortex.Tests.Runtime.asmdef
│       └── Editor/        # エディタテスト
│           └── UniCortex.Tests.Editor.asmdef
```

## ドキュメント一覧

| ファイル | 内容 |
|---|---|
| [00-project-overview.md](./00-project-overview.md) | プロジェクト概要 (本ドキュメント) |
| [01-architecture.md](./01-architecture.md) | アーキテクチャ詳細・技術的制約 |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW Dense ベクトル検索 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse ベクトル検索 |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 全文検索 |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | ハイブリッド検索 (RRF) |
| [07-filter-design.md](./07-filter-design.md) | スカラーフィルタ |
| [08-persistence-design.md](./08-persistence-design.md) | 永続化 (MemoryMappedFile) |
