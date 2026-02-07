# 実装ロードマップ・マイルストーン

## 概要

UniCortex は 12 フェーズ（Phase 0 -- Phase 10 + Phase 9b）で段階的に実装する。
各フェーズは前フェーズの成果に依存するが、一部のフェーズは並列開発が可能である。

プロジェクト全体の目的・スコープについては [00-project-overview.md](./00-project-overview.md) を、
アーキテクチャ全体像については [01-architecture.md](./01-architecture.md) を参照。

### MVP 定義

**MVP (Minimum Viable Product)** は Phase 0-7 の完了時点で達成される。

MVP に含まれる機能:
- Dense ベクトル検索 (HNSW)
- Sparse ベクトル検索
- BM25 全文検索
- ハイブリッド検索 (RRF)
- スカラーフィルタ
- UniCortexDatabase ファサード API

MVP に含まれない機能（Phase 8 以降）:
- 永続化 (MemoryMappedFile / IndexedDB)
- パフォーマンス最適化
- UPM パッケージ化
- セキュリティテスト

MVP 完了をもって、ライブラリの基本機能が利用可能となる。

---

## 目次

1. [Phase 0: プロジェクトセットアップ](#phase-0-プロジェクトセットアップ)
2. [Phase 1: Core データ構造](#phase-1-core-データ構造)
3. [Phase 2: HNSW Dense ベクトル検索](#phase-2-hnsw-dense-ベクトル検索)
4. [Phase 3: Sparse ベクトル検索](#phase-3-sparse-ベクトル検索)
5. [Phase 4: BM25 全文検索](#phase-4-bm25-全文検索)
6. [Phase 5: メタデータ & スカラーフィルタ](#phase-5-メタデータ--スカラーフィルタ)
7. [Phase 6: ハイブリッド検索 (RRF)](#phase-6-ハイブリッド検索-rrf)
8. [Phase 7: UniCortexDatabase ファサード](#phase-7-unicortexdatabase-ファサード)
9. [Phase 8: 永続化](#phase-8-永続化)
10. [Phase 9: パフォーマンス最適化](#phase-9-パフォーマンス最適化)
11. [Phase 9b: セキュリティテスト](#phase-9b-セキュリティテスト)
12. [Phase 10: ドキュメント & パッケージ化](#phase-10-ドキュメント--パッケージ化)
13. [フェーズ依存関係図](#フェーズ依存関係図)
14. [フェーズ別見積もり工数](#フェーズ別見積もり工数)
15. [リスク登録簿](#リスク登録簿)
16. [並列開発可能なフェーズ](#並列開発可能なフェーズ)
17. [パフォーマンス目標](#パフォーマンス目標)
18. [テスト実施計画](#テスト実施計画)
19. [品質基準](#品質基準)

---

## Phase 0: プロジェクトセットアップ

**目標**: ディレクトリ構造、Assembly Definition (asmdef) 作成、基本的なビルド確認

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Assets/UniCortex/Runtime/UniCortex.Runtime.asmdef` | Runtime 用 Assembly Definition |
| `Assets/UniCortex/Editor/UniCortex.Editor.asmdef` | Editor 拡張用 Assembly Definition |
| `Assets/UniCortex/Tests/Runtime/UniCortex.Tests.Runtime.asmdef` | Runtime テスト用 Assembly Definition |
| `Assets/UniCortex/Tests/Editor/UniCortex.Tests.Editor.asmdef` | Editor テスト用 Assembly Definition |

### テスト基準

- asmdef が正しく参照解決されること
- 空のテストが Unity Test Runner で通ること

### 依存

- なし

### リスク

- `com.unity.jobs` パッケージが未インストールの可能性がある。`manifest.json` で明示的に追加して対応する。

---

## Phase 1: Core データ構造

**目標**: 全コンポーネントが共通利用するデータ構造の実装

設計詳細は [02-core-design.md](./02-core-design.md) を参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Core/ErrorCode.cs` | ErrorCode enum, Result\<T\> 構造体 |
| `Core/SearchResult.cs` | SearchResult 構造体 |
| `Core/DistanceFunctions.cs` | EuclideanSq, Cosine, DotProduct (float4 SIMD) |
| `Core/VectorStorage.cs` | SoA ベクトルストレージ |
| `Core/IdMap.cs` | 双方向 ID マッピング + FreeList |
| `Core/NativeMinHeap.cs` | バイナリヒープ（最小） |
| `Core/NativeMaxHeap.cs` | バイナリヒープ（最大） |

### テスト基準

- 各データ構造のユニットテスト
- 距離関数の正確性（既知ベクトル対での期待値検証）
- ヒープの Push/Pop/Peek 操作の正確性

### 依存

- Phase 0

### リスク

- Burst 互換性の確認が必要。マネージド型の混入を防ぐため、早期に `[BurstCompile]` 属性でコンパイル検証を行う。

---

## Phase 2: HNSW Dense ベクトル検索

**目標**: HNSW グラフの構築 (INSERT) と K-NN 検索 (SEARCH) の実装

設計詳細は [03-hnsw-design.md](./03-hnsw-design.md) を参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Hnsw/HnswGraph.cs` | HNSW グラフデータ構造 |
| `Hnsw/HnswBuilder.cs` | INSERT アルゴリズム |
| `Hnsw/HnswSearcher.cs` | SEARCH アルゴリズム |
| `Hnsw/HnswConfig.cs` | パラメータ設定 (M, efConstruction, efSearch 等) |
| `Hnsw/HnswSearchJob.cs` | Burst Job によるマルチスレッド検索 |

### テスト基準

- K-NN 精度テスト: Recall@10 >= 0.95
- 構築の正確性: ノード挿入後にグラフ不変条件が維持されること
- 検索の正確性: Brute-force 結果との比較

### 依存

- Phase 1

### リスク

- パフォーマンス目標未達の可能性。M, efConstruction, efSearch パラメータの調整で対応する。

---

## Phase 3: Sparse ベクトル検索

**目標**: スパースベクトルの転置インデックス構築と DAAT (Document-At-A-Time) 検索の実装

設計詳細は [04-sparse-design.md](./04-sparse-design.md) を参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Sparse/SparseElement.cs` | スパースベクトル要素構造体 |
| `Sparse/SparseIndex.cs` | 転置インデックス |
| `Sparse/SparseSearcher.cs` | DAAT 検索アルゴリズム |
| `Sparse/SparseSearchJob.cs` | Burst Job |

### テスト基準

- 検索精度テスト: 既知データセットでの Top-K 一致
- 転置インデックスの正確性: 挿入・削除後のインデックス整合性

### 依存

- Phase 1

### リスク

- 低。比較的シンプルなアルゴリズムであり、転置インデックスの実装は確立された手法に従う。

---

## Phase 4: BM25 全文検索

**目標**: Burst 互換のトークナイザ + 転置インデックス + BM25 スコアリングの実装

設計詳細は [05-bm25-design.md](./05-bm25-design.md) を参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `FullText/Tokenizer.cs` | UTF-8 トークナイザ (ASCII, CJK, ひらがな対応) |
| `FullText/TokenHash.cs` | xxHash3 ベースのトークンハッシュ |
| `FullText/BM25Index.cs` | 転置インデックス |
| `FullText/BM25Searcher.cs` | BM25 検索アルゴリズム |
| `FullText/BM25SearchJob.cs` | Burst Job |

### テスト基準

- トークナイザの正確性: ASCII 英単語、CJK 文字、ひらがな・カタカナのトークン分割
- BM25 スコアの正確性: 既知パラメータ (k1=1.2, b=0.75) での期待スコア検証
- 転置インデックスの正確性: ドキュメント追加・削除後の整合性

### 依存

- Phase 1

### リスク

- Burst 互換の UTF-8 処理が複雑。`FixedString` や `NativeText` ではなくバイト列レベルで Unicode カテゴリを判定する必要がある。

---

## Phase 5: メタデータ & スカラーフィルタ

**目標**: カラムナメタデータストレージの実装とフィルタ式の評価エンジン

設計詳細は [07-filter-design.md](./07-filter-design.md) を参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Filter/MetadataStorage.cs` | カラムナ形式のメタデータストレージ |
| `Filter/FilterExpression.cs` | フィルタ式の構文木表現 |
| `Filter/FilterEvaluator.cs` | フィルタ式の評価 |
| `Filter/FilterEvaluateJob.cs` | Burst Job によるバッチ評価 |

### テスト基準

- フィルタ条件の正確性: 等値比較、範囲比較、文字列一致
- 複合条件テスト: AND / OR / NOT の組み合わせ
- 空集合・全集合の境界条件

### 依存

- Phase 1

### リスク

- 低。フィルタ式の評価は値型ベースで実装可能であり、Burst 互換性の問題は少ない。

---

## Phase 6: ハイブリッド検索 (RRF)

**目標**: Dense, Sparse, BM25 の検索結果を Reciprocal Rank Fusion で統合するハイブリッド検索の実装

設計詳細は [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) を参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Hybrid/RrfConfig.cs` | RRF パラメータ設定 (k 定数等) |
| `Hybrid/RrfReRanker.cs` | RRF スコア計算・順位統合 |
| `Hybrid/HybridSearcher.cs` | 複数検索バックエンドの呼び出し・結果統合 |
| `Hybrid/RrfMergeJob.cs` | Burst Job による統合処理 |

### テスト基準

- RRF スコア計算の正確性: `1 / (k + rank)` の数値検証
- 統合結果の順序テスト: 既知ランキングの統合結果が期待通りであること
- サブ検索の結果が空の場合でも正常動作すること

### 依存

- Phase 2 (HNSW), Phase 3 (Sparse), Phase 4 (BM25)

### リスク

- サブ検索の結果が空の場合やスコアが同点の場合のハンドリング。エッジケースを網羅するテストで対応する。

---

## Phase 7: UniCortexDatabase ファサード

**目標**: 各検索コンポーネントを統一する公開 API の実装

設計詳細は [02-core-design.md](./02-core-design.md) の「UniCortexDatabase ファサード API」セクションを参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Core/UniCortexDatabase.cs` | 統一 API ファサード |
| `Core/DatabaseConfig.cs` | データベース全体の設定 |

### テスト基準

- E2E テスト: Add -> Search -> Update -> Delete のライフサイクル
- 各検索タイプ (Dense / Sparse / BM25 / Hybrid) が API 経由で正しく動作すること
- フィルタ付き検索の E2E テスト

### 依存

- Phase 2 (HNSW), Phase 3 (Sparse), Phase 4 (BM25), Phase 5 (Filter), Phase 6 (Hybrid/RRF)

### リスク

- API 設計の使いやすさ。ユーザ視点でのレビューを行い、直感的なインターフェースを目指す。

---

## Phase 8: 永続化

**目標**: MemoryMappedFile による永続化と WebGL 向けフォールバック (IndexedDB) の実装

設計詳細は [08-persistence-design.md](./08-persistence-design.md) を参照。

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `Persistence/FileHeader.cs` | ファイルヘッダ (マジックナンバー、バージョン、チェックサム) |
| `Persistence/MmapPersistence.cs` | MemoryMappedFile 永続化 |
| `Persistence/WebGlPersistence.cs` | WebGL 用 IndexedDB 永続化 |
| `Persistence/PersistenceFactory.cs` | プラットフォーム別永続化バックエンドのファクトリ |
| `Plugins/WebGL/IndexedDBPlugin.jslib` | WebGL 用 JavaScript プラグイン |

### テスト基準

- Save/Load の往復テスト: 保存前後でデータが一致すること
- チェックサム検証: 破損データの検知
- バージョン互換性: ヘッダバージョン不一致時の適切なエラー

### 依存

- Phase 7

### リスク

- WebGL の IndexedDB 連携が複雑。非同期 API のラッピングと jslib プラグイン間のデータ受け渡しに注意が必要。

---

## Phase 9: パフォーマンス最適化

**目標**: ベンチマーク作成、プロファイリング、ボトルネック最適化

### 作成ファイル

| ファイル | 説明 |
|---|---|
| ベンチマークテストスクリプト | 各検索タイプの自動ベンチマーク |
| パフォーマンスレポート | 計測結果と最適化記録 |

### テスト基準

レイテンシ目標は [パフォーマンス目標](#パフォーマンス目標) セクションを参照。

### 依存

- Phase 7

### リスク

- Burst 最適化の限界。Inspector や Profile Analyzer で Burst の生成コードを確認し、auto-vectorize が効いていない箇所を特定・修正する。

---

## Phase 9b: セキュリティテスト

**目標**: セキュリティガイドライン ([11-security-guidelines.md](./11-security-guidelines.md)) に基づくテスト実施

### テスト内容

| テスト種別 | 対象 | 説明 |
|---|---|---|
| 境界値テスト | 全 API | K=0, dim=0, 空ベクトル、最大容量等の境界値 |
| 整数オーバーフローテスト | VectorStorage, HNSW, MetadataStorage | インデックス計算のオーバーフロー検証 |
| デシリアライズ検証テスト | Persistence | 改ざんファイル、不正オフセット、不正サイズでの Load |
| DoS 耐性テスト | 全検索 API | 極端に大きな K, efSearch, トークン数での安定性 |
| メモリ安全性テスト | 全コンポーネント | NativeLeakDetection + Safety Check 有効下でのテスト |

### 依存

- Phase 9

### テスト基準

- 全境界値テストケースが正常にエラーハンドリングされること
- 不正ファイルの Load が適切なエラーコードを返すこと
- 極端なパラメータでクラッシュ・ハングしないこと

---

## Phase 10: ドキュメント & パッケージ化

**目標**: UPM パッケージ化、API ドキュメント整備、サンプルシーンの作成

### 作成ファイル

| ファイル | 説明 |
|---|---|
| `package.json` | UPM パッケージマニフェスト |
| `CHANGELOG.md` | 変更履歴 |
| `Documentation~/` | API リファレンス・ガイド |
| `Samples~/` | サンプルシーン・スクリプト |

### テスト基準

- `package.json` が UPM の仕様に準拠していること
- サンプルシーンが正常に動作すること
- API ドキュメントが全パブリック型をカバーしていること

### 依存

- Phase 9

### リスク

- 低。ドキュメントとパッケージングは技術的リスクが小さい。

---

## フェーズ依存関係図

```
Phase 0 (Setup)
    └── Phase 1 (Core)
        ├── Phase 2 (HNSW)     ─┐
        ├── Phase 3 (Sparse)    ├── Phase 6 (Hybrid/RRF)
        ├── Phase 4 (BM25)     ─┘        │
        └── Phase 5 (Filter)              │
                                          ▼
                                Phase 7 (Facade)
                                    │
                            ┌───────┴───────┐
                            ▼               ▼
                    Phase 8 (Persist)  Phase 9 (Perf)
                            │               │
                            │         Phase 9b (Security)
                            │               │
                            └───────┬───────┘
                                    ▼
                            Phase 10 (Package)
```

---

## フェーズ別見積もり工数

| フェーズ | 工数 (T-shirt) | 推定日数 | 主要リスク |
|---|---|---|---|
| Phase 0: セットアップ | S | 1日 | なし |
| Phase 1: Core | M | 3-5日 | Burst 互換性検証 |
| Phase 2: HNSW | XL | 7-10日 | アルゴリズム複雑度、パフォーマンス目標 |
| Phase 3: Sparse | M | 3-5日 | 低リスク |
| Phase 4: BM25 | L | 5-7日 | UTF-8 トークナイザの Burst 互換実装 |
| Phase 5: Filter | M | 3-5日 | 低リスク |
| Phase 6: Hybrid/RRF | M | 3-5日 | エッジケースハンドリング |
| Phase 7: Facade | L | 5-7日 | API 設計の整合性 |
| Phase 8: Persistence | L | 5-7日 | WebGL IndexedDB 連携 |
| Phase 9: Performance | L | 5-7日 | ボトルネック特定の不確実性 |
| Phase 9b: Security | M | 3-5日 | テストケース網羅性 |
| Phase 10: Package | S | 2-3日 | 低リスク |
| **合計** | | **45-66日** | |

> T-shirt サイズ: S (1-2日), M (3-5日), L (5-7日), XL (7-10日)

---

## リスク登録簿

| ID | リスク | 確率 | 影響度 | 緩和策 |
|---|---|---|---|---|
| R1 | Burst 互換性の問題でコア機能が実装不可 | 低 | 致命的 | Phase 1 で早期に Burst コンパイル検証を実施 |
| R2 | HNSW のパフォーマンス目標未達 | 中 | 高 | M, efConstruction, efSearch パラメータ調整。Burst Inspector でボトルネック特定 |
| R3 | WebGL での IndexedDB 連携が複雑 | 中 | 中 | 非同期 API ラッピングの早期プロトタイプ。Phase 8 で集中対応 |
| R4 | NativeParallelMultiHashMap の制約で設計変更が必要 | 高 | 中 | ソフト削除 + バッチ再構築戦略で対処（C1 対策済み） |
| R5 | UTF-8 トークナイザの Burst 互換実装が複雑 | 中 | 中 | バイト列レベルの Unicode 判定で対応。Phase 4 で集中実装 |
| R6 | WebGL シングルスレッドでのパフォーマンス不足 | 中 | 中 | プラットフォーム別パフォーマンス目標を設定（デスクトップの 3x） |
| R7 | デシリアライズ時のセキュリティ脆弱性 | 低 | 高 | 全 Offset/Size の境界検証、DocumentCount/Dimension 上限チェック |
| R8 | メモリ使用量がモバイル / WebGL の制約を超過 | 中 | 高 | メモリバジェット ([13-memory-budget.md](./13-memory-budget.md)) で事前計画。プラットフォーム別推奨設定を提供 |

---

## 並列開発可能なフェーズ

| グループ | 並列開発可能なフェーズ | 前提 |
|---|---|---|
| 検索エンジン群 | Phase 2 (HNSW), Phase 3 (Sparse), Phase 4 (BM25), Phase 5 (Filter) | Phase 1 完了後 |
| 後工程 | Phase 8 (Persist), Phase 9 (Perf) | Phase 7 完了後 |

---

## パフォーマンス目標

条件: 50,000 件、dim=128 (Dense/Sparse)、K=10

### デスクトップ (Windows / macOS / Linux)

| 操作 | レイテンシ目標 | 備考 |
|---|---|---|
| HNSW Search | < 5 ms | efSearch=64, Burst + SIMD + Jobs |
| Sparse Search | < 5 ms | DAAT, Burst |
| BM25 Search | < 10 ms | 50K docs, 転置インデックス |
| Hybrid Search (RRF) | < 20 ms | Dense + Sparse + BM25 統合 |
| Scalar Filter | < 2 ms | 単一条件、50K 件走査 |
| Insert (single) | < 1 ms | HNSW グラフ更新含む |
| Delete (single) | < 1 ms | 論理削除 |
| Save (全体) | < 500 ms | 50K 件、MemoryMappedFile |
| Load (全体) | < 200 ms | 遅延読み込み有効時 |

### WebGL

WebGL ではシングルスレッド実行 + WASM オーバーヘッドにより、デスクトップの約 **3 倍** のレイテンシを見込む。

| 操作 | レイテンシ目標 | 備考 |
|---|---|---|
| HNSW Search | < 15 ms | efSearch=64, Burst (WASM SIMD) |
| Sparse Search | < 15 ms | DAAT, Burst |
| BM25 Search | < 30 ms | 50K docs |
| Hybrid Search (RRF) | < 60 ms | 逐次実行 |
| Scalar Filter | < 6 ms | 単一条件 |
| Save (IndexedDB) | < 2000 ms | byte[] コピー + IndexedDB 書き込み |
| Load (IndexedDB) | < 1000 ms | IndexedDB 読み出し + byte[] 復元 |

### モバイル (Android / iOS)

モバイルではデスクトップの約 **1.5 倍** のレイテンシを見込む。

| 操作 | レイテンシ目標 | 備考 |
|---|---|---|
| HNSW Search | < 8 ms | efSearch=50, Burst + NEON |
| Sparse Search | < 8 ms | DAAT, Burst |
| BM25 Search | < 15 ms | 50K docs |
| Hybrid Search (RRF) | < 30 ms | Jobs による並列化は有効 |
| Scalar Filter | < 3 ms | 単一条件 |
| Save | < 750 ms | persistentDataPath |
| Load | < 300 ms | 遅延読み込み有効時 |

---

## テスト実施計画

| フェーズ | ユニット | 統合 | E2E | パフォーマンス | 回帰 |
|---|---|---|---|---|---|
| Phase 1 (Core) | ✅ | - | - | - | - |
| Phase 2 (HNSW) | ✅ | - | - | ✅ (Recall) | - |
| Phase 3 (Sparse) | ✅ | - | - | ✅ (レイテンシ) | - |
| Phase 4 (BM25) | ✅ | - | - | ✅ (レイテンシ) | - |
| Phase 5 (Filter) | ✅ | - | - | - | - |
| Phase 6 (Hybrid) | ✅ | ✅ | - | ✅ (統合レイテンシ) | - |
| Phase 7 (Facade) | - | ✅ | ✅ | - | - |
| Phase 8 (Persist) | ✅ | ✅ | ✅ | ✅ (Save/Load) | - |
| Phase 9 (Perf) | - | - | - | ✅ (全指標) | ✅ |
| Phase 9b (Security) | ✅ | ✅ | - | ✅ (DoS 耐性) | - |

詳細なテスト計画は [12-test-plan.md](./12-test-plan.md) を参照。

---

## 品質基準

| 基準 | 要件 |
|---|---|
| テストカバレッジ | 各フェーズで関連するユニットテスト必須 |
| 検索精度 | HNSW Recall@10 >= 0.95 |
| Burst 互換 | `[BurstCompile]` 属性でコンパイルエラーなし |
| メモリリーク | NativeContainer の Dispose 漏れなし（全テストで検証） |
| コードスタイル | XML ドキュメントコメントを全パブリック API に付与 |

---

## 関連ドキュメント

| ドキュメント | 説明 |
|---|---|
| [00-project-overview.md](./00-project-overview.md) | プロジェクト概要・ビジョン |
| [01-architecture.md](./01-architecture.md) | アーキテクチャ全体像 |
| [02-core-design.md](./02-core-design.md) | Core データ構造・API 設計 |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW Dense ベクトル検索設計 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse ベクトル検索設計 |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 全文検索設計 |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | ハイブリッド検索 (RRF) 設計 |
| [07-filter-design.md](./07-filter-design.md) | スカラーフィルタ設計 |
| [08-persistence-design.md](./08-persistence-design.md) | 永続化設計 |
| [11-security-guidelines.md](./11-security-guidelines.md) | セキュリティガイドライン |
| [12-test-plan.md](./12-test-plan.md) | テスト計画 |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット |
