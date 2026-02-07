# メモリバジェット

UniCortex の各コンポーネントのメモリ使用量を詳細に分析し、
プラットフォーム別の推奨設定とメモリ最適化戦略を定義する。

---

## 目次

1. [メモリ使用量サマリー](#1-メモリ使用量サマリー)
2. [コンポーネント別メモリ使用量](#2-コンポーネント別メモリ使用量)
3. [Allocator チェックリスト](#3-allocator-チェックリスト)
4. [検索時ピーク一時メモリ](#4-検索時ピーク一時メモリ)
5. [Save/Load ピークメモリ](#5-saveload-ピークメモリ)
6. [プラットフォーム別メモリ制約と推奨設定](#6-プラットフォーム別メモリ制約と推奨設定)
7. [メモリ断片化対策](#7-メモリ断片化対策)
8. [メモリプーリング戦略](#8-メモリプーリング戦略)

---

## 1. メモリ使用量サマリー

### 総メモリ使用量テーブル（全コンポーネント合計）

| ドキュメント数 | dim=128 | dim=384 | dim=768 |
|---|---|---|---|
| 10,000 | ~30 MB | ~55 MB | ~95 MB |
| 30,000 | ~85 MB | ~155 MB | ~275 MB |
| 50,000 | ~140 MB | ~255 MB | ~450 MB |

> 上記は Dense ベクトル + HNSW グラフ + Sparse Index (avg 100 elements) + BM25 Index (avg 100 tokens) + Metadata (5 fields) + IdMap の合計概算。
> 実際のメモリ使用量はデータの特性（Sparse 要素数、テキスト長、メタデータ数）により変動する。

---

## 2. コンポーネント別メモリ使用量

### 2.1 VectorStorage

Dense ベクトルデータの SoA flat array。

```
サイズ = N × dim × sizeof(float) = N × dim × 4 bytes
```

| N | dim=128 | dim=384 | dim=768 |
|---|---|---|---|
| 10,000 | 4.88 MB | 14.6 MB | 29.3 MB |
| 30,000 | 14.6 MB | 43.9 MB | 87.9 MB |
| 50,000 | 24.4 MB | 73.2 MB | 146.5 MB |

参照: [02-core-design.md](./02-core-design.md) セクション 3

### 2.2 HnswGraph

HNSW グラフ構造。M=16, M0=32 の場合。

| コンポーネント | 計算式 | 50K ノードでのサイズ |
|---|---|---|
| Nodes (HnswNodeMeta) | N × 8 bytes | 0.4 MB |
| Neighbors (flat) | N × 平均スロット数 × 4 bytes | 7.56 MB |
| NeighborCounts | Neighbors と同サイズ | 7.56 MB |
| Deleted | N × 1 byte | 0.05 MB |
| **小計** | | **~15.6 MB** |

```
平均スロット数/ノード ≈ M0 + M × mL = 32 + 16 × 0.36 ≈ 37.8
```

| N | グラフサイズ |
|---|---|
| 10,000 | ~3.1 MB |
| 30,000 | ~9.4 MB |
| 50,000 | ~15.6 MB |

> グラフサイズはベクトル次元数に依存しない（ベクトルデータは VectorStorage が管理するため）。

参照: [03-hnsw-design.md](./03-hnsw-design.md) セクション 11

### 2.3 SparseIndex

転置インデックス。NativeParallelMultiHashMap ベース。

```
ポスティングデータ = N × 平均非ゼロ要素数 × sizeof(SparsePosting)
                   = N × S_avg × 8 bytes
```

| N | S_avg=50 | S_avg=100 | S_avg=200 |
|---|---|---|---|
| 10,000 | 4 MB | 8 MB | 16 MB |
| 30,000 | 12 MB | 24 MB | 48 MB |
| 50,000 | 20 MB | 40 MB | 80 MB |

NativeParallelMultiHashMap のオーバーヘッド（バケット配列、next ポインタ等）により、
実際のメモリ使用量は上記の **1.5〜2 倍** となる。

| N (S_avg=100) | ポスティング | オーバーヘッド込み |
|---|---|---|
| 10,000 | 8 MB | ~12-16 MB |
| 30,000 | 24 MB | ~36-48 MB |
| 50,000 | 40 MB | ~60-80 MB |

参照: [04-sparse-design.md](./04-sparse-design.md) セクション 6

### 2.4 BM25Index

転置インデックス + Document Frequency + DocumentLengths。

| コンポーネント | 計算式 (N=50K, avg 100 tokens) | サイズ |
|---|---|---|
| InvertedIndex (Posting) | N × 100 × 8 bytes | ~40 MB |
| DocumentFrequency | ユニークトークン数 × 8 bytes | ~4 MB |
| DocumentLengths | N × 4 bytes | ~0.2 MB |
| **小計 (raw)** | | **~44 MB** |
| **オーバーヘッド込み** | × 1.5-2 | **~66-88 MB** |

> CJK テキスト主体のコーパスでは、バイグラムによりトークン数が約 2 倍になるため、
> メモリ使用量も約 2 倍に増加する可能性がある。

参照: [05-bm25-design.md](./05-bm25-design.md) セクション 4.3

### 2.5 MetadataStorage

カラムナストレージ。フィールド数とドキュメント数に依存。

```
サイズ ≈ フィールド数 × N × (キーサイズ + 値サイズ)
```

| フィールド数 | N=50K (Int32 フィールド) | N=50K (混合: 3 Int + 1 Float + 1 Bool) |
|---|---|---|
| 1 | ~0.6 MB | - |
| 3 | ~1.8 MB | - |
| 5 | ~3.0 MB | ~2.6 MB |
| 10 | ~6.0 MB | - |

NativeParallelHashMap のオーバーヘッドにより、実際は 1.5 倍程度。

参照: [07-filter-design.md](./07-filter-design.md) セクション 9

### 2.6 IdMap

外部 ID ↔ 内部 ID マッピング。

| コンポーネント | 計算式 (N=50K) | サイズ |
|---|---|---|
| ExternalToInternal (HashMap) | N × (8 + 4) bytes + overhead | ~1.2 MB |
| InternalToExternal (NativeArray) | N × 8 bytes | 0.4 MB |
| FreeList (NativeList) | 最大 N × 4 bytes | 0.2 MB |
| **小計** | | **~1.8 MB** |

---

## 3. Allocator チェックリスト

| コンポーネント | データ構造 | Allocator | Dispose 責任 |
|---|---|---|---|
| **VectorStorage** | Data (NativeArray\<float\>) | Persistent | VectorStorage.Dispose() |
| **IdMap** | ExternalToInternal (HashMap) | Persistent | IdMap.Dispose() |
| | InternalToExternal (NativeArray) | Persistent | IdMap.Dispose() |
| | FreeList (NativeList) | Persistent | IdMap.Dispose() |
| **HnswGraph** | Nodes, Neighbors, NeighborCounts | Persistent | HnswGraph.Dispose() |
| | Deleted (NativeArray\<bool\>) | Persistent | HnswGraph.Dispose() |
| **SparseIndex** | InvertedIndex (MultiHashMap) | Persistent | SparseIndex.Dispose() |
| | DeletedIds (HashSet) | Persistent | SparseIndex.Dispose() |
| **BM25Index** | InvertedIndex, DocumentFrequency | Persistent | BM25Index.Dispose() |
| | DocumentLengths (NativeArray) | Persistent | BM25Index.Dispose() |
| **MetadataStorage** | intValues, floatValues, boolValues | Persistent | MetadataStorage.Dispose() |
| **検索結果** | NativeArray\<SearchResult\> | TempJob | **呼び出し元** |
| **HNSW 探索バッファ** | Visited (NativeBitArray) | TempJob | Job 完了後 |
| | Candidates/Results (Heap) | TempJob | Job 完了後 |
| **Sparse スコア** | NativeParallelHashMap\<int, float\> | Temp | Job 内で自動解放 |
| **RRF マージ** | NativeParallelHashMap\<int, float\> | Temp | Job 内で自動解放 |
| **フィルタ結果** | PassFilter (NativeArray\<bool\>) | TempJob | フィルタ処理後 |
| **FilterExpression** | Conditions, LogicalOps | TempJob | **呼び出し元** |

参照: [10-technical-constraints.md](./10-technical-constraints.md) セクション 4

---

## 4. 検索時ピーク一時メモリ

検索操作中に追加で確保される一時メモリの見積もり。

### 4.1 HNSW Search

| バッファ | サイズ (N=50K) |
|---|---|
| Visited ビットセット | ceil(N / 8) = ~6.25 KB |
| Candidates ヒープ (efSearch=50) | 50 × 12 bytes = 600 bytes |
| Results ヒープ (K=10) | 10 × 12 bytes = 120 bytes |
| **合計** | **~7 KB** |

### 4.2 Sparse Search

| バッファ | サイズ (最大) |
|---|---|
| スコア累積 HashMap (N_hit ドキュメント) | 最大 N × 8 bytes = ~400 KB |
| Results ヒープ (K=10) | 120 bytes |
| **合計** | **最大 ~400 KB** |

> 実際の N_hit はクエリの非ゼロ要素とドキュメントの重なりに依存し、通常は N の一部。

### 4.3 BM25 Search

| バッファ | サイズ (最大) |
|---|---|
| スコア累積 (N_hit ドキュメント) | 最大 N × 8 bytes = ~400 KB |
| IDF キャッシュ (クエリトークン数) | 典型 10 × 4 bytes = 40 bytes |
| Results ヒープ (K=10) | 120 bytes |
| **合計** | **最大 ~400 KB** |

### 4.4 Hybrid Search (RRF)

| バッファ | サイズ |
|---|---|
| Dense サブ検索結果 (SubSearchK=50) | 50 × 12 = 600 bytes |
| Sparse サブ検索結果 (SubSearchK=50) | 50 × 12 = 600 bytes |
| BM25 サブ検索結果 (SubSearchK=50) | 50 × 12 = 600 bytes |
| RRF マージ HashMap (最大 150 entries) | ~2.4 KB |
| 各サブ検索の一時バッファ | HNSW + Sparse + BM25 の合計 |
| **合計 (一時バッファのみ)** | **~810 KB** |

### 4.5 Filter Evaluation

| バッファ | サイズ |
|---|---|
| PassFilter 配列 (候補数) | candidates × 1 byte |
| 例: K=10, OversamplingFactor=3.0 | 30 bytes |
| **合計** | **< 1 KB** |

---

## 5. Save/Load ピークメモリ

### 5.1 Save 時のピークメモリ

Save 処理では、インメモリデータ + シリアライズバッファの両方が必要。

```
ピークメモリ ≈ 通常メモリ × 2
```

| N | dim=128 | dim=384 |
|---|---|---|
| 30,000 | ~170 MB | ~310 MB |
| 50,000 | ~280 MB | ~510 MB |

> **注意**: モバイルや WebGL ではこのピークメモリが制約となる可能性がある。

### 5.2 Load 時のピークメモリ

- **MmapPersistence (デスクトップ)**: MemoryMappedFile はページキャッシュを使用するため、
  追加メモリは OS が管理する。物理メモリを超えるファイルも扱える
- **WebGlPersistence**: IndexedDB → byte[] → NativeArray のコピーチェーンにより、
  ピークメモリは約 **2.5x**（byte[] + NativeArray の両方がメモリ上に存在する期間がある）

### 5.3 インクリメンタル Save（将来構想）

変更されたセクションのみを書き出すことで、ピークメモリを削減可能:

```
現行: 全セクション書き出し → ピーク 2x
将来: 差分セクションのみ → ピーク 1.0x + 変更セクションサイズ
```

参照: [08-persistence-design.md](./08-persistence-design.md) セクション 9

---

## 6. プラットフォーム別メモリ制約と推奨設定

### 6.1 プラットフォーム別制約

| プラットフォーム | 利用可能メモリ | 推奨上限 |
|---|---|---|
| デスクトップ (Windows/macOS/Linux) | 数 GB〜 | 500 MB |
| モバイル (Android/iOS) | 1-4 GB (アプリ制限) | 150 MB |
| WebGL | 256 MB-2 GB (WASM ヒープ) | 200 MB |

### 6.2 推奨最大構成

| プラットフォーム | 最大ドキュメント数 | 最大次元数 | 推定メモリ |
|---|---|---|---|
| デスクトップ | 50,000 | 768 | ~450 MB |
| モバイル (ハイエンド) | 50,000 | 128 | ~140 MB |
| モバイル (ローエンド) | 20,000 | 128 | ~55 MB |
| WebGL | 30,000 | 128 | ~85 MB |
| WebGL (ミニマム) | 10,000 | 128 | ~30 MB |

### 6.3 メモリバジェット配分ガイドライン

50,000 ドキュメント、dim=128 の場合の配分:

| コンポーネント | メモリ | 割合 |
|---|---|---|
| VectorStorage | ~24 MB | 17% |
| HnswGraph | ~16 MB | 11% |
| SparseIndex | ~60 MB | 43% |
| BM25Index | ~33 MB | 24% |
| MetadataStorage | ~4 MB | 3% |
| IdMap | ~2 MB | 1% |
| 検索時一時バッファ | ~1 MB | 1% |
| **合計** | **~140 MB** | **100%** |

> Sparse Index と BM25 Index が全体の 67% を占める。Sparse 検索や BM25 検索を使用しない場合、
> メモリ使用量は大幅に削減される（Dense のみ: ~42 MB）。

---

## 7. メモリ断片化対策

### 7.1 IdMap FreeList

削除されたドキュメントの内部 ID は FreeList に追加され、次回の Add で再利用される。
これにより VectorStorage と HNSW グラフの配列に空きスロットが蓄積するのを防ぐ。

参照: [02-core-design.md](./02-core-design.md) セクション 4

### 7.2 インデックス再構築 (Compact)

ソフト削除が蓄積した場合、有効なエントリのみでインデックスを再構築する:

| コンポーネント | 再構築タイミング | 再構築方法 |
|---|---|---|
| HnswGraph | 削除率 > 20% | 有効ノードで新規グラフを構築 |
| SparseIndex | 削除率 > 20% | 有効ドキュメントで転置インデックスを再構築 |
| BM25Index | 削除率 > 20% | 有効ドキュメントで転置インデックスを再構築 |

再構築中のピークメモリ:
```
ピーク = 旧インデックス + 新インデックス ≈ 2x (対象コンポーネントのみ)
```

### 7.3 NativeParallelHashMap の内部断片化

NativeParallelHashMap はオープンアドレッシング方式であり、
削除エントリはトゥームストーンとして残る。多数の削除後にリハッシュが発生すると、
一時的にメモリ使用量が増加する可能性がある。

対策: `Capacity` の設定を適切に行い、ロードファクタを 0.75 以下に維持する。

---

## 8. メモリプーリング戦略

### 8.1 検索バッファの再利用

HNSW 検索で使用する Visited ビットセットとヒープバッファは、
検索のたびに確保・解放するとオーバーヘッドが生じる。

```csharp
/// <summary>
/// HNSW 検索用のバッファプール。
/// 検索バッファを事前確保し、複数回の検索で再利用する。
/// </summary>
public struct HnswSearchBufferPool : IDisposable
{
    NativeBitArray visited;
    NativeArray<SearchResult> candidateBuffer;
    NativeArray<SearchResult> resultBuffer;

    public HnswSearchBufferPool(int nodeCapacity, int efSearch, int k, Allocator allocator)
    {
        visited = new NativeBitArray(nodeCapacity, allocator);
        candidateBuffer = new NativeArray<SearchResult>(efSearch, allocator);
        resultBuffer = new NativeArray<SearchResult>(k, allocator);
    }

    public void Reset()
    {
        visited.Clear();
        // バッファは再利用可能（上書きされる）
    }

    public void Dispose()
    {
        if (visited.IsCreated) visited.Dispose();
        if (candidateBuffer.IsCreated) candidateBuffer.Dispose();
        if (resultBuffer.IsCreated) resultBuffer.Dispose();
    }
}
```

### 8.2 プーリングの効果

| 方式 | 検索あたりのアロケーション | メモリオーバーヘッド |
|---|---|---|
| 毎回確保 (Allocator.TempJob) | 3 回 | なし（使用後解放） |
| プーリング (Allocator.Persistent) | 0 回 | バッファ分が常駐 (~7 KB) |

50,000 ノード × efSearch=50 × K=10 の場合、プール常駐メモリは約 7 KB であり、
毎フレーム検索を行うアプリケーションではプーリングを推奨する。

### 8.3 プーリングが不要なケース

- 検索頻度が低い（1 秒に 1 回以下）
- Allocator.TempJob のオーバーヘッドが計測上問題ない
- WebGL 環境（シングルスレッドのため並行バッファ管理が不要）

---

## 関連ドキュメント

| ドキュメント | 関連内容 |
|---|---|
| [02-core-design.md](./02-core-design.md) | VectorStorage、IdMap のメモリレイアウト |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW グラフのメモリ概算 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse 転置インデックスのメモリ概算 |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 転置インデックスのメモリ概算 |
| [07-filter-design.md](./07-filter-design.md) | MetadataStorage のメモリ使用量 |
| [08-persistence-design.md](./08-persistence-design.md) | Save/Load のメモリ概算、ファイルサイズ |
| [09-roadmap.md](./09-roadmap.md) | パフォーマンス目標 |
| [10-technical-constraints.md](./10-technical-constraints.md) | Allocator 選択ガイドライン、WebGL メモリ制約 |
| [11-security-guidelines.md](./11-security-guidelines.md) | メモリ安全性、DoS 耐性 |
| [12-test-plan.md](./12-test-plan.md) | メモリリーク検出テスト |
