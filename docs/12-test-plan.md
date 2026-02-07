# テスト計画

UniCortex の全フェーズにわたるテスト戦略を定義する。
ユニットテスト、統合テスト、E2E テスト、パフォーマンステスト、回帰テストの計画と、
テストデータ生成方法、精度検証方法、異常系テストケースを網羅する。

---

## 目次

1. [フェーズ別テスト計画](#1-フェーズ別テスト計画)
2. [テストデータ生成](#2-テストデータ生成)
3. [精度検証方法](#3-精度検証方法)
4. [異常系テストケース](#4-異常系テストケース)
5. [境界値テストケース](#5-境界値テストケース)
6. [メモリリーク検出](#6-メモリリーク検出)
7. [プラットフォーム別テストマトリクス](#7-プラットフォーム別テストマトリクス)
8. [CI/CD パフォーマンス回帰テスト](#8-cicd-パフォーマンス回帰テスト)

---

## 1. フェーズ別テスト計画

| フェーズ | ユニット | 統合 | E2E | パフォーマンス | 回帰 |
|---|---|---|---|---|---|
| Phase 1 (Core) | ✅ | - | - | - | - |
| Phase 2 (HNSW) | ✅ | - | - | ✅ | - |
| Phase 3 (Sparse) | ✅ | - | - | ✅ | - |
| Phase 4 (BM25) | ✅ | - | - | ✅ | - |
| Phase 5 (Filter) | ✅ | - | - | - | - |
| Phase 6 (Hybrid) | ✅ | ✅ | - | ✅ | - |
| Phase 7 (Facade) | - | ✅ | ✅ | - | - |
| Phase 8 (Persist) | ✅ | ✅ | ✅ | ✅ | - |
| Phase 9 (Perf) | - | - | - | ✅ | ✅ |
| Phase 9b (Security) | ✅ | ✅ | - | ✅ | - |

### Phase 1: Core データ構造

| テスト対象 | テスト内容 |
|---|---|
| ErrorCode / Result\<T\> | Success/Fail 生成、IsSuccess 判定 |
| VectorStorage | Add/Get/Update 操作、容量超過エラー、次元数不一致エラー |
| IdMap | Add/Remove/GetInternal/GetExternal、FreeList 再利用、重複 ID エラー |
| NativeMinHeap / NativeMaxHeap | Push/Pop/Peek 操作、容量制限、空ヒープからの Pop |
| DistanceFunctions | EuclideanSq/Cosine/DotProduct の既知ベクトルペアでの期待値検証 |

### Phase 2: HNSW

| テスト対象 | テスト内容 |
|---|---|
| HnswBuilder | INSERT 後のグラフ不変条件検証（接続数 <= M/M0、双方向エッジ） |
| HnswSearcher | Recall@10 >= 0.95 (brute-force 比較) |
| レイヤー割り当て | 確率分布の検証（大半のノードが Layer 0 のみ） |
| ソフト削除 | 削除後の検索結果に削除ノードが含まれないこと |
| HnswSearchJob | Burst コンパイルエラーなし、マルチスレッド正確性 |

### Phase 3: Sparse

| テスト対象 | テスト内容 |
|---|---|
| SparseIndex.Add | ポスティング登録の正確性 |
| SparseSearchJob | 既知データセットでの Top-K 一致（内積計算の手動検証） |
| ソフト削除 | 削除済みドキュメントが検索結果に含まれないこと |

### Phase 4: BM25

| テスト対象 | テスト内容 |
|---|---|
| Tokenizer (ASCII) | 空白分割、小文字化の正確性 |
| Tokenizer (CJK) | ユニグラム + バイグラム生成の正確性 |
| Tokenizer (混合) | ASCII + CJK 混在テキストのトークン分割 |
| TokenHash | xxHash3 の決定性（同一入力 → 同一出力） |
| BM25 スコア | 手動計算結果との一致（k1=1.2, b=0.75 での期待スコア） |
| ソフト削除 | 削除済みドキュメントのスコア計算除外 |

### Phase 5: Filter

| テスト対象 | テスト内容 |
|---|---|
| MetadataStorage | SetInt/GetInt/SetFloat/GetFloat/SetBool/GetBool の正確性 |
| FilterEvaluateJob | 各演算子 (==, !=, <, <=, >, >=) の正確性 |
| 論理演算 | AND/OR の組み合わせ評価 |
| 存在しないフィールド | TryGetValue false → 条件 false |

### Phase 6: Hybrid/RRF

| テスト対象 | テスト内容 |
|---|---|
| RRF スコア計算 | `weight / (k + rank)` の数値検証 |
| 結果マージ | 既知ランキング入力での期待マージ結果 |
| Graceful Degradation | サブ検索 1-2 個が失敗した場合の正常動作 |
| 全サブ検索失敗 | エラーが正しく返されること |

### Phase 7: Facade

| テスト対象 | テスト内容 |
|---|---|
| ライフサイクル | Add → Build → Search → Update → Delete → Search |
| 各検索タイプ | Dense/Sparse/BM25/Hybrid が API 経由で動作すること |
| フィルタ付き検索 | フィルタ条件による結果絞り込み |

### Phase 8: Persistence

| テスト対象 | テスト内容 |
|---|---|
| Save/Load 往復 | 保存前後でデータ一致 |
| MagicNumber 検証 | 不正ファイルの検出 |
| CRC32 検証 | 破損データの検出 |
| バージョン互換性 | メジャーバージョン不一致エラー、マイナーバージョン互換 |

---

## 2. テストデータ生成

### 2.1 合成ベクトルデータ

```csharp
// ランダムベクトル生成 (テスト用)
public static NativeArray<float> GenerateRandomVector(int dim, uint seed, Allocator allocator)
{
    var rng = new Unity.Mathematics.Random(seed);
    var vector = new NativeArray<float>(dim, allocator);
    for (int i = 0; i < dim; i++)
        vector[i] = rng.NextFloat(-1f, 1f);
    return vector;
}
```

### 2.2 クラスタ化ベクトルデータ

HNSW の Recall テスト用にクラスタ構造を持つデータセット:
- 10 個のクラスタ中心をランダム生成
- 各クラスタに 5,000 ベクトルを正規分布で配置
- brute-force での真の最近傍を事前計算し、Recall の ground truth とする

### 2.3 多言語テキストデータ

| テキスト種別 | 例 | 用途 |
|---|---|---|
| ASCII 英語 | "The Dragon Sword deals 150 damage" | トークナイザ基本テスト |
| CJK (漢字) | "東京都港区六本木" | CJK バイグラムテスト |
| ひらがな | "ありがとうございます" | ひらがなトークナイザテスト |
| カタカナ | "ドラゴンソード" | カタカナトークナイザテスト |
| 混合 | "HP回復potion" | 文字種切り替えテスト |
| 空文字列 | "" | 境界値テスト |
| 長文 | 64KB のテキスト | 上限テスト |

---

## 3. 精度検証方法

### 3.1 HNSW Recall@10

```
Recall@K = |HNSW の Top-K ∩ Brute-force の Top-K| / K
```

- テストデータ: 50,000 ベクトル (dim=128)
- クエリ: 1,000 個のランダムクエリ
- K = 10, efSearch = 50 (デフォルト)
- **目標**: Recall@10 >= 0.95 (1,000 クエリの平均)
- brute-force は全ベクトル総当たりで真の最近傍を計算

### 3.2 BM25 スコア検証

既知のドキュメントセットとクエリに対して、手動計算した期待スコアとの一致を検証:

```
テストケース:
  N = 5 ドキュメント
  avgdl = 10 トークン
  k1 = 1.2, b = 0.75
  クエリ: "dragon sword"
  期待スコア: 手動で BM25 公式に値を代入して算出
  許容誤差: 1e-4
```

### 3.3 Sparse 内積検証

```
テストケース:
  query = {(0, 0.5), (3, 1.0), (7, 0.3)}
  doc1 = {(0, 0.8), (3, 0.6), (5, 0.2)}
  期待スコア = 0.5*0.8 + 1.0*0.6 + 0 = 1.0
```

### 3.4 RRF マージ検証

```
テストケース:
  Dense: [docA(rank1), docB(rank2), docC(rank3)]
  BM25:  [docB(rank1), docC(rank2), docA(rank3)]
  k = 60

  RRF(docA) = 1/(60+1) + 1/(60+3) = 0.01639 + 0.01587 = 0.03226
  RRF(docB) = 1/(60+2) + 1/(60+1) = 0.01613 + 0.01639 = 0.03252
  RRF(docC) = 1/(60+3) + 1/(60+2) = 0.01587 + 0.01613 = 0.03200

  期待順位: docB > docA > docC
```

### 3.5 浮動小数点比較の許容誤差テーブル

テストにおける浮動小数点スコアのアサーションには、以下の許容誤差 (epsilon) を使用する。

| テスト対象 | 許容誤差 (epsilon) | 根拠 |
|---|---|---|
| 距離関数 (EuclideanSq, Cosine, DotProduct) | `1e-6` | float32 の精度限界。SIMD 演算順序による微小な差異を許容 |
| BM25 スコア | `1e-4` | IDF の対数計算、TF 飽和の除算で丸め誤差が累積する |
| Sparse 内積スコア | `1e-6` | 単純な積和のみ。距離関数と同等の精度 |
| RRF マージスコア | `1e-6` | 除算のみの単純計算。float32 精度で十分 |
| Filter float 比較 (Equal) | exact (`==`) | フィルタは厳密一致。ユーザーが float の `==` を使う場合は仕様として厳密比較 |
| Filter float 比較 (順序) | exact | `<`, `<=`, `>`, `>=` は IEEE 754 の順序比較に従う |
| Persistence Save/Load 往復 | `0` (bit-exact) | バイト列のシリアライズ/デシリアライズは丸め誤差なし |

```csharp
// テストヘルパー例
public static void AssertApprox(float actual, float expected, float epsilon, string message = "")
{
    float diff = math.abs(actual - expected);
    Assert.IsTrue(diff <= epsilon,
        $"Expected {expected} ± {epsilon}, got {actual} (diff={diff}). {message}");
}
```

---

## 4. 異常系テストケース

| カテゴリ | テストケース | 期待動作 |
|---|---|---|
| **不正入力** | null/default NativeArray を渡す | `InvalidParameter` エラー |
| | 次元数 0 のベクトルで Add | `DimensionMismatch` エラー |
| | 負の K で検索 | `InvalidParameter` エラー |
| | 存在しない ID で Delete | `NotFound` エラー |
| | 同一 ID で二重 Add | `DuplicateId` エラー |
| **容量超過** | Capacity を超えて Add | `CapacityExceeded` エラー |
| | DocumentCount > 1,000,000 のファイルをロード | `InvalidParameter` エラー |
| **ファイル破損** | 切り詰めたファイルを Load | `DataCorrupted` エラー |
| | マジックナンバーが異なるファイル | `InvalidFileFormat` エラー |
| | CRC32 不一致のファイル | `DataCorrupted` エラー |
| | オフセットがファイルサイズ超過 | `DataCorrupted` エラー |
| **空状態** | 空インデックスで検索 | 空の結果 (エラーなし) or `IndexNotBuilt` |
| | 全ドキュメント削除後に検索 | 空の結果 |
| | Build 前に検索 | `IndexNotBuilt` エラー |

---

## 5. 境界値テストケース

| テストケース | 入力 | 期待動作 |
|---|---|---|
| K = 0 | SearchDense(query, K=0) | `InvalidParameter` エラー |
| K = 1 | SearchDense(query, K=1) | 正常: 1 件返却 |
| K = N (全件) | SearchDense(query, K=50000) | 正常: N 件返却 |
| K > N | SearchDense(query, K=100000) | 正常: N 件返却 (K を N にクランプ) |
| dim = 1 | 1 次元ベクトルでの検索 | 正常動作 |
| 1 ドキュメント | 1 件のみ登録して検索 | 正常: 1 件返却 |
| ゼロノルムクエリ | 全要素が 0.0 のクエリ | Cosine: 距離 1.0、Euclidean: 正常 |
| 同一ベクトル | 全ドキュメントが同一ベクトル | 正常: 距離 0.0 で全件返却 |
| efSearch = K | efSearch を K と同値に設定 | 正常だが Recall が低下する可能性 |
| Sparse 空クエリ | 非ゼロ要素が 0 個のクエリ | 空の結果 |
| BM25 空クエリ | 空文字列でクエリ | 空の結果 or `InvalidParameter` |
| フィルタ全除外 | 全ドキュメントがフィルタ条件を満たさない | 空の結果 |
| フィルタ全通過 | 全ドキュメントがフィルタ条件を満たす | オーバーサンプリングなしと同等の結果 |

---

## 6. メモリリーク検出

### 6.1 NativeLeakDetection

テスト環境では `NativeLeakDetection.Mode = EnableWithCallStacks` を設定し、
NativeContainer のリーク発生時にコールスタック付きの詳細ログを出力する。

```csharp
[SetUp]
public void Setup()
{
    NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace;
}
```

### 6.2 テスト TearDown

すべてのテストケースの `[TearDown]` で以下を確認:
- テスト中に作成した NativeContainer がすべて Dispose 済みであること
- `UniCortexDatabase.Dispose()` が正しく全サブコンポーネントを解放すること

### 6.3 ストレステスト

| テスト | 内容 | 検証項目 |
|---|---|---|
| 繰り返し Add/Delete | 10,000 回の Add + Delete サイクル | メモリリークなし、FreeList 正常動作 |
| 繰り返し Search | 10,000 回の Search + Dispose サイクル | 一時バッファのリークなし |
| Save/Load サイクル | 100 回の Save + Load サイクル | メモリ増加なし |

---

## 7. プラットフォーム別テストマトリクス

| テスト | Windows Editor | macOS Editor | Windows Standalone | Android | iOS | WebGL |
|---|---|---|---|---|---|---|
| ユニットテスト | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| パフォーマンステスト | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 永続化テスト (MMap) | ✅ | ✅ | ✅ | ✅ | ✅ | - |
| 永続化テスト (IndexedDB) | - | - | - | - | - | ✅ |
| Burst コンパイルテスト | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| シングルスレッドテスト | - | - | - | - | - | ✅ |

### WebGL 固有のテスト

- Jobs がシングルスレッドで正常動作すること
- IndexedDB の Save/Load が非同期で正しく完了すること
- パフォーマンスがデスクトップの 3x 以内であること

### 7.1 WebGL テスト戦略

WebGL 環境では以下の制約があるため、専用のテスト戦略を定義する。

#### シングルスレッド制約

WebGL では Jobs System がシングルスレッドにフォールバックする。
これにより、マルチスレッド環境では検出されない不具合が顕在化する可能性がある。

| テスト項目 | 内容 | アサーション |
|---|---|---|
| Jobs の逐次実行 | 全 Job (HnswSearch, Sparse, BM25, RrfMerge, FilterEvaluate) がシングルスレッドで正常完了 | 結果がマルチスレッド実行と同一 |
| Hybrid 逐次実行 | 3つのサブ検索が逐次実行され、RRF マージ結果が同一 | 結果件数 == K、スコア順が正しい |
| IJobParallelFor | FilterEvaluateJob, HnswBatchSearchJob がシングルスレッドで動作 | 結果がマルチスレッド実行と同一 |

#### IndexedDB テスト

IndexedDB は非同期 API のため、テストにはコルーチンベースの待機が必要。

| テスト項目 | 内容 | アサーション |
|---|---|---|
| Save + Load 往復 | IndexedDB に Save → Load → データ一致検証 | Save 前後でベクトルデータ・メタデータが bit-exact |
| クォータ超過 | 大量データの Save でクォータ超過を模擬 | `StorageQuotaExceeded` エラーが返る |
| 非同期コールバック | Save/Load のコールバックが正しく呼ばれる | callback 引数 == 0 (成功) |
| キー衝突 | 同一キーでの上書き Save | Load 結果が最新の Save データと一致 |
| 存在しないキー | 未保存のキーで Load | `FileNotFound` エラー |

#### IndexedDB モック方針

Editor テスト環境では IndexedDB が利用できないため、以下のモック戦略を使用する:

1. **IPersistence インターフェース経由のモック**: テスト時は `InMemoryPersistence` を注入し、
   `byte[]` をメモリ上に保持するモック実装でシリアライズ/デシリアライズのロジックを検証する
2. **WebGL 実機テスト**: IndexedDB の実際の非同期動作は WebGL ビルドで実機テストする
3. **JavaScript interop テスト**: `.jslib` の動作は手動テストまたは Playwright 等のブラウザ自動化で検証

#### パフォーマンス基準

WebGL 環境のパフォーマンスは以下の基準で検証する:

| 操作 | デスクトップ目標 | WebGL 許容上限 | 倍率 |
|---|---|---|---|
| HNSW Search (50K, K=10, ef=50) | < 1 ms | < 3 ms | 3x |
| Sparse Search (50K, K=10) | < 5 ms | < 15 ms | 3x |
| BM25 Search (50K, K=10) | < 5 ms | < 15 ms | 3x |
| Hybrid Search | < 10 ms | < 30 ms | 3x |
| Save (50K, dim=128) | < 500 ms | < 1500 ms | 3x |
| Load (50K, dim=128) | < 500 ms | < 1500 ms | 3x |

> **注意**: WebGL のパフォーマンスはブラウザ (Chrome, Firefox, Safari) ごとに異なる。
> 上記は Chrome での目標値であり、他ブラウザでは 1.5-2x の差異が発生する可能性がある。

---

## 8. CI/CD パフォーマンス回帰テスト

### 8.1 ベンチマークスイート

| ベンチマーク | 条件 | 計測指標 |
|---|---|---|
| HNSW Search | 50K, dim=128, K=10, ef=50 | レイテンシ (ms), Recall@10 |
| Sparse Search | 50K, avg 100 elements, K=10 | レイテンシ (ms) |
| BM25 Search | 50K docs, クエリ 3 tokens, K=10 | レイテンシ (ms) |
| Hybrid Search | 上記の統合, K=10 | レイテンシ (ms) |
| Insert | 単一ドキュメント追加 | レイテンシ (ms) |
| Save/Load | 50K, dim=128 | 時間 (ms), ファイルサイズ |

### 8.2 回帰検出

- 各コミットでベンチマークを自動実行
- 前回のベースラインと比較し、**10% 以上の劣化**でアラート
- メモリ使用量も計測し、**5% 以上の増加**でアラート
- ベースラインは main ブランチの最新コミットの計測値

### 8.3 実行環境

- CI サーバー: 固定スペック（CPU / メモリ / OS を固定）
- Burst コンパイル有効化
- Safety Check 無効 (Release 相当) でパフォーマンスを計測
- 計測は 5 回実行の中央値を使用

---

## 関連ドキュメント

| ドキュメント | 関連内容 |
|---|---|
| [02-core-design.md](./02-core-design.md) | ErrorCode 定義、データ構造仕様 |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW パラメータ、Recall 目標 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse 検索アルゴリズム |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 スコア計算、トークナイザ仕様 |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | RRF マージ、Graceful Degradation |
| [07-filter-design.md](./07-filter-design.md) | フィルタ評価ロジック |
| [08-persistence-design.md](./08-persistence-design.md) | Save/Load テスト仕様 |
| [09-roadmap.md](./09-roadmap.md) | フェーズ別テスト計画、パフォーマンス目標 |
| [10-technical-constraints.md](./10-technical-constraints.md) | Safety Check、Burst 制約 |
| [11-security-guidelines.md](./11-security-guidelines.md) | セキュリティテスト要件 |
| [13-memory-budget.md](./13-memory-budget.md) | メモリ使用量の期待値 |
