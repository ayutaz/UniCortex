# セキュリティガイドライン

UniCortex のメモリ安全性、入力バリデーション、永続化セキュリティ、DoS 耐性に関するガイドライン。
本ドキュメントはすべての実装フェーズで参照される横断的なセキュリティ要件を定義する。

---

## 目次

1. [メモリ安全性](#1-メモリ安全性)
2. [入力バリデーション](#2-入力バリデーション)
3. [永続化のセキュリティ](#3-永続化のセキュリティ)
4. [WebGL 固有のセキュリティ](#4-webgl-固有のセキュリティ)
5. [DoS 耐性](#5-dos-耐性)
6. [スレッドセーフティポリシー](#6-スレッドセーフティポリシー)

---

## 1. メモリ安全性

### 1.1 境界チェック

NativeArray / NativeContainer へのすべてのアクセスで境界チェックを実施する。

| チェック対象 | 条件 | 失敗時のエラー |
|---|---|---|
| VectorStorage インデックス | `0 <= internalId < Capacity` | `InvalidParameter` |
| VectorStorage 次元 | `dimIndex < Dimension` | `DimensionMismatch` |
| HNSW NeighborOffset | `offset + count <= Neighbors.Length` | `CapacityExceeded` |
| IdMap InternalToExternal | `0 <= internalId < Capacity` | `NotFound` |
| MetadataStorage キー | `key >= 0` (long 化により保証) | - |
| DocumentLengths | `0 <= internalId < DocumentLengths.Length` | `InvalidParameter` |

> **重要**: Unity の Safety Check は Release ビルドで無効化されるため、
> パブリック API では独自の境界チェックを必ず実装する。
> Safety Check に依存した安全性保証は行わない。
> 詳細は [10-technical-constraints.md](./10-technical-constraints.md) を参照。

### 1.2 整数オーバーフロー防止

インデックス計算におけるオーバーフローを防止する。

| 計算箇所 | 式 | 対策 |
|---|---|---|
| VectorStorage | `internalId * dimension + dimIndex` | `internalId < Capacity` を事前検証 |
| HNSW NeighborOffset | `M0 + M * maxLayer` (累積) | `long` で計算し `int` 範囲を検証 |
| MetadataStorage キー | `fieldHash * maxDocs + internalId` | `long` 型を使用 |
| Neighbors 配列サイズ | `totalSlots * sizeof(int)` | 容量計算時にオーバーフロー検証 |

```csharp
// 推奨パターン: long による中間計算
long offset = (long)internalId * dimension + dimIndex;
if (offset < 0 || offset >= Data.Length)
    return Result<float>.Fail(ErrorCode.InvalidParameter);
```

### 1.3 Use-After-Free 防止

NativeContainer の Dispose 後のアクセスを防止する。

- すべての NativeContainer は `IsCreated` プロパティで有効性を確認してからアクセスする
- `Dispose()` 実装では `IsCreated` チェック後に Dispose する（二重 Dispose 防止）
- 検索結果の `NativeArray<SearchResult>` は `Allocator.TempJob` で確保され、
  4 フレーム以内の Dispose が必要。Dispose 忘れは Editor でリーク警告として検出される

---

## 2. 入力バリデーション

### 2.1 ベクトル入力

| パラメータ | 検証条件 | エラーコード |
|---|---|---|
| Dense ベクトル次元 | `vector.Length == database.Dimension` | `DimensionMismatch` |
| Dense ベクトル値 | NaN / Infinity チェック **(必須)** | `InvalidParameter` |
| Sparse ベクトル要素数 | `0 < elements.Length <= 10,000` | `InvalidParameter` |
| SparseElement.Index | `Index >= 0` | `InvalidParameter` |
| SparseElement.Value | `Value != 0` (ゼロ要素は無意味) | 警告ログのみ |

### 2.1.1 NaN / Infinity バリデーション（必須）

すべての float 入力に対して NaN / Infinity チェックを必須とする。
NaN / Inf が距離計算やスコアリングに混入すると、結果が未定義となり検索品質が壊滅するため。

| 入力箇所 | チェック内容 | 失敗時の動作 |
|---|---|---|
| Dense ベクトル (`Add` / `Update`) | 全要素に `math.isnan(v) \|\| math.isinf(v)` | `InvalidParameter` エラーを返す |
| Sparse ベクトル (`SparseElement.Value`) | 各要素の Value に NaN/Inf チェック | `InvalidParameter` エラーを返す |
| Filter float 値 (`SetFloat`) | 格納値に NaN/Inf チェック | `InvalidParameter` エラーを返す |
| Filter 条件値 (`FilterCondition.FloatValue`) | 条件値に NaN/Inf チェック | `InvalidParameter` エラーを返す |
| BM25 パラメータ (`k1`, `b`) | NaN/Inf チェック | `InvalidParameter` エラーを返す |
| RRF パラメータ (`RankConstant`, `*Weight`) | NaN/Inf チェック | `InvalidParameter` エラーを返す |

```csharp
// 推奨パターン: Burst 互換の NaN/Inf チェック
[BurstCompile]
public static bool ContainsNanOrInf(NativeArray<float> vector)
{
    for (int i = 0; i < vector.Length; i++)
    {
        if (math.isnan(vector[i]) || math.isinf(vector[i]))
            return true;
    }
    return false;
}
```

> **注意**: `math.isnan` と `math.isinf` は `Unity.Mathematics` の Burst 互換関数である。
> `float.IsNaN` / `float.IsInfinity` はマネージド API であるが、Burst でも使用可能。

### 2.2 テキスト入力 (BM25)

| パラメータ | 上限 | 超過時の動作 |
|---|---|---|
| テキスト長 (UTF-8 bytes) | 65,536 (64 KB) | `InvalidParameter` エラー |
| トークン数 / ドキュメント | 1,000 | 上限超過分を切り捨て（警告） |
| ユニークトークン数 / ドキュメント | 500 | 上限超過分を切り捨て |

トークナイザの詳細は [05-bm25-design.md](./05-bm25-design.md) を参照。

### 2.3 検索パラメータ

| パラメータ | 検証条件 | デフォルト値 |
|---|---|---|
| K (返却件数) | `1 <= K <= 10,000` | 10 |
| efSearch | `K <= efSearch <= 10,000` | 50 |
| SubSearchK | `K <= SubSearchK <= 10,000` | K * 3 |
| RankConstant (k) | `k > 0` | 60 |
| OversamplingFactor | `1.0 <= factor <= 100.0` | 3.0 |
| BM25 k1 | `k1 > 0` | 1.2 |
| BM25 b | `0 <= b <= 1` | 0.75 |

### 2.4 ID バリデーション

| パラメータ | 検証条件 | エラーコード |
|---|---|---|
| External ID (ulong) | 重複チェック (Add 時) | `DuplicateId` |
| External ID (ulong) | 存在チェック (Delete/Update 時) | `NotFound` |

---

## 3. 永続化のセキュリティ

### 3.1 CRC32 の限界

CRC32 は**誤り検出**のみを提供し、**認証（改ざん検知）は提供しない**。

- CRC32 は線形関数であり、攻撃者がデータを改ざんし CRC32 を調整可能
- UniCortex のインデックスファイルは**信頼されたストレージ**から読み込むことを前提とする
- ネットワーク経由で受信したファイルの場合、アプリケーション側で HMAC-SHA256 等の暗号学的ハッシュを使用すべき
- 将来のファイルフォーマット v2 でオプショナルな HMAC フィールドの追加を検討

### 3.2 デシリアライズ検証

ファイルからのロード時に以下の全検証を実施する:

```
1. ファイルサイズ >= sizeof(FileHeader) (128 bytes)
2. MagicNumber == 0x554E4358 ("UNCX")
3. VersionMajor == CurrentVersionMajor
4. 0 <= DocumentCount <= 1,000,000
5. 1 <= Dimension <= 4,096
6. 全セクション: Offset >= sizeof(FileHeader)
7. 全セクション: Offset + Size <= fileSize
8. 全セクション間: 重複なし
9. VectorDataSize == DocumentCount * Dimension * sizeof(float)
10. CRC32 検証
```

いずれかの検証に失敗した場合、適切なエラーコードを返してロードを中止する。
不正なファイルからのデータ読み出しは一切行わない。

詳細は [08-persistence-design.md](./08-persistence-design.md) を参照。

### 3.3 メモリ制限

デシリアライズ時のメモリ確保は DocumentCount と Dimension から算出される上限値を超えないことを検証する。

```csharp
// 推奨: ロード前にメモリ要件を計算し、利用可能メモリと比較
long estimatedMemory = (long)documentCount * dimension * sizeof(float)  // VectorData
                     + (long)documentCount * 40  // HNSW Graph 概算
                     + sparseIndexSize + bm25IndexSize + metadataSize + idMapSize;

if (estimatedMemory > maxAllowedMemory)
    return Result<UniCortexDatabase>.Fail(ErrorCode.CapacityExceeded);
```

---

## 4. WebGL 固有のセキュリティ

### 4.1 IndexedDB オリジン分離

IndexedDB はブラウザの Same-Origin Policy に従う。

- 同一オリジン (protocol + host + port) のページのみがデータベースにアクセス可能
- 異なるオリジンからのインデックスデータの読み書きは不可
- iframe 内の UniCortex アプリケーションは親ページのオリジンに依存する

### 4.2 JavaScript Interop の安全性

`.jslib` プラグイン経由のデータ受け渡し:

- C# → JavaScript: `HEAPU8.buffer` からの `Uint8Array.slice()` でコピーを作成（元のバッファへの参照を保持しない）
- JavaScript → C#: コールバック経由でデータサイズを通知し、C# 側で NativeArray を確保してからコピー
- バッファサイズの検証: JavaScript 側で受信データのサイズが期待値と一致することを確認

### 4.3 WASM メモリ境界

- WASM ヒープサイズの上限を超えるデータのロードを防止する
- `navigator.deviceMemory` (利用可能な場合) でデバイスメモリを確認し、適切な容量制限を設定する

---

## 5. DoS 耐性

大量の計算リソースを消費する入力パターンに対する防御。

### 5.1 パラメータ上限

| パラメータ | 上限 | 理由 |
|---|---|---|
| K | 10,000 | 結果バッファサイズの制限 |
| efSearch | 10,000 | HNSW 探索の計算量制限 |
| SubSearchK | 10,000 | サブ検索の結果バッファ制限 |
| トークン数/ドキュメント | 1,000 | インデックス肥大化防止 |
| Sparse 非ゼロ要素数/クエリ | 10,000 | 走査次元数の制限 |
| DocumentCount | 1,000,000 | メモリ使用量の制限 |
| Dimension | 4,096 | ベクトルサイズの制限 |

### 5.2 計算量制限

各操作の最大計算量を制限する:

| 操作 | 最大計算量 | 制御パラメータ |
|---|---|---|
| HNSW Search | O(efSearch × M0 + log N) | efSearch 上限 |
| Sparse Search | O(Q × P_avg + N_hit × log K) | クエリ要素数上限 |
| BM25 Search | O(T × P_avg + N_hit × log K) | トークン数上限 |
| Hybrid Search | 上記の合計 | 各パラメータ上限の複合 |
| Filter Evaluation | O(N × C) | 条件数 C に実用的な上限なし（最大 100 程度を推奨） |

---

## 6. スレッドセーフティポリシー

### 6.1 基本方針

UniCortex は Unity Jobs System のスケジューリングモデルに従う。

| アクセスパターン | 安全性 | 備考 |
|---|---|---|
| 複数スレッドからの読み取り | ✅ 安全 | `[ReadOnly]` 属性で明示 |
| 単一スレッドからの書き込み | ✅ 安全 | Job の `Execute()` 内 |
| 複数スレッドからの書き込み | ❌ 非安全 | `[NativeDisableParallelForRestriction]` が必要な場合は領域分割で対応 |
| 読み取り + 書き込みの同時実行 | ❌ 非安全 | `JobHandle` の依存チェーンで順序を保証 |

### 6.2 ソフト削除のアトミック性

`bool` 型のソフト削除フラグ (`Deleted[nodeId]`) は、x86/ARM アーキテクチャ上でアトミックに読み書き可能。

- 検索 Job 実行中にメインスレッドで削除フラグが設定された場合、
  その検索結果に削除済みノードが含まれる可能性がある（許容される動作）
- 推奨: 削除操作は検索 Job の `Complete()` 後に実行し、次回検索で反映させる

### 6.3 インデックス再構築時の排他制御

バッチ再構築（ソフト削除されたエントリの物理削除）中は、対象インデックスへの検索・追加・削除を停止する必要がある。

- 再構築は `Build()` メソッドとして提供し、呼び出し元が排他を管理する
- 再構築中に検索 Job をスケジュールした場合の動作は未定義

---

## 関連ドキュメント

| ドキュメント | 関連するセキュリティ考慮 |
|---|---|
| [02-core-design.md](./02-core-design.md) | ErrorCode 定義、VectorStorage 境界チェック |
| [03-hnsw-design.md](./03-hnsw-design.md) | NeighborOffset オーバーフロー、ソフト削除スレッドセーフティ |
| [04-sparse-design.md](./04-sparse-design.md) | SparseElement.Index バリデーション |
| [05-bm25-design.md](./05-bm25-design.md) | テキスト長・トークン数制限 |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | SubSearchK 上限、Graceful Degradation |
| [07-filter-design.md](./07-filter-design.md) | MetadataStorage キーオーバーフロー |
| [08-persistence-design.md](./08-persistence-design.md) | デシリアライズ検証、CRC32 限界 |
| [09-roadmap.md](./09-roadmap.md) | Phase 9b セキュリティテスト |
| [10-technical-constraints.md](./10-technical-constraints.md) | Safety Check 無効化の影響 |
| [12-test-plan.md](./12-test-plan.md) | セキュリティテストケース |
| [13-memory-budget.md](./13-memory-budget.md) | メモリ制限・DoS 耐性 |
