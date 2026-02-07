# 技術制約・プラットフォーム対応

UniCortex の開発において遵守すべき技術制約をまとめる。
Burst Compiler、IL2CPP (AOT)、WebGL の各制約と、NativeContainer のライフサイクル管理、
コーディング規約、パッケージバージョン情報を記述する。

全体のアーキテクチャについては [01-architecture.md](./01-architecture.md) を参照。
Core データ構造・API 設計については [02-core-design.md](./02-core-design.md) を参照。

---

## 目次

1. [Burst Compiler 制約](#1-burst-compiler-制約)
2. [IL2CPP (AOT) 制約](#2-il2cpp-aot-制約)
3. [WebGL 制約](#3-webgl-制約)
4. [NativeContainer ライフサイクル管理](#4-nativecontainer-ライフサイクル管理)
5. [コーディング規約](#5-コーディング規約)
6. [パッケージバージョン情報](#6-パッケージバージョン情報)

---

## 1. Burst Compiler 制約

Burst Compiler は C# の Job コードを高度に最適化されたネイティブコードに変換するが、
使用可能な C# 機能に厳しい制限がある。UniCortex のすべての高パフォーマンスパスは
これらの制約内で実装する必要がある。

### 1.1 マネージド型不可

`string`, `class`, `List<T>`, `Dictionary<K,V>` 等のマネージド型は Burst Job 内で使用できない。
GC によるヒープ割り当てが発生するため、Burst のコンパイル対象外となる。

**代替手段:**

| マネージド型 (NG) | Burst 互換型 (OK) |
|---|---|
| `string` | `FixedString32Bytes` / `FixedString64Bytes` / `FixedString128Bytes` / `NativeText` |
| `List<T>` | `NativeList<T>` |
| `Dictionary<K,V>` | `NativeParallelHashMap<K,V>` |
| `HashSet<T>` | `NativeParallelHashSet<T>` |
| `Queue<T>` | `NativeQueue<T>` |
| `class` | `struct` (`where T : unmanaged`) |

```csharp
// NG: マネージド型
string label = "item_001";
var neighbors = new List<int>();

// OK: Burst 互換型
FixedString64Bytes label = "item_001";
var neighbors = new NativeList<int>(64, Allocator.TempJob);
```

### 1.2 仮想メソッド不可

`virtual`, `abstract`, `interface` によるメソッドディスパッチは Burst Job 内で不可。
vtable ルックアップはマネージドランタイムに依存するため、Burst が最適化できない。

**代替手段:**
- 関数ポインタ (`FunctionPointer<T>`)
- enum + switch パターン

UniCortex の距離計算では enum + static method パターンを採用する
（詳細は [02-core-design.md](./02-core-design.md) の距離関数セクションを参照）。

```csharp
// NG: interface dispatch
IDistanceFunction func = new CosineDistance();
float d = func.Calculate(a, b);

// OK: enum + static method
public static float Calculate(DistanceType type, NativeSlice<float> a, NativeSlice<float> b, int dim)
{
    switch (type)
    {
        case DistanceType.EuclideanSq: return EuclideanSq(a, b, dim);
        case DistanceType.Cosine:      return Cosine(a, b, dim);
        case DistanceType.DotProduct:  return DotProduct(a, b, dim);
        default:                       return 0f;
    }
}
```

### 1.3 マネージド例外処理不可

`try` / `catch` / `throw` は Burst Job 内で使用できない。
マネージド例外はランタイムの例外処理機構に依存するため、AOT コンパイルと相性が悪い。

**代替手段:** `Result<T>` パターン（ErrorCode + 値の組み合わせ）

UniCortex では `ErrorCode` enum と `Result<T>` 構造体によるエラーハンドリングを採用する。
詳細は [02-core-design.md](./02-core-design.md) のエラーハンドリングセクションを参照。

```csharp
// NG: 例外スロー
if (!found) throw new KeyNotFoundException("ID not found");

// OK: Result パターン
if (!found) return new Result<SearchHit>(ErrorCode.NotFound);
```

### 1.4 動的メモリ割り当て不可

`new object()`, `new List<T>()` 等の GC 対象のヒープ割り当ては不可。
NativeContainer によるアンマネージドメモリ割り当ては OK。

**Allocator の使い分け:**

| Allocator | 寿命 | 用途 |
|---|---|---|
| `Allocator.Temp` | 1 フレーム | 関数内の一時バッファ |
| `Allocator.TempJob` | 4 フレーム | Job 完了までの一時バッファ |
| `Allocator.Persistent` | 無制限 | インデックス、ストレージ等の長期保持 |

UniCortex のインデックス構造（HNSW グラフ、転置インデックス等）は
`Allocator.Persistent` で確保し、検索クエリの一時バッファは `Allocator.TempJob` を使用する。

### 1.5 Burst でサポートされる型

Burst Job 内で使用可能な型の一覧:

- **プリミティブ型**: `bool`, `byte`, `short`, `int`, `long`, `float`, `double`
- **Unity.Mathematics**: `float2`, `float3`, `float4`, `int2`, `int3`, `int4`, `math.*`
- **NativeContainer**: `NativeArray<T>`, `NativeList<T>`, `NativeParallelHashMap<K,V>`, `NativeParallelHashSet<T>`, `NativeQueue<T>` 等
- **FixedString**: `FixedString32Bytes`, `FixedString64Bytes`, `FixedString128Bytes`, `FixedString512Bytes`, `FixedString4096Bytes`
- **ユーザー定義構造体**: `where T : unmanaged` 制約を満たす構造体

UniCortex の距離計算は `Unity.Mathematics.float4` を使用し、
Burst の auto-vectorize による SIMD 最適化に委ねる
（[03-hnsw-design.md](./03-hnsw-design.md) を参照）。

### 1.6 NativeParallelMultiHashMap の部分削除制約

`NativeParallelMultiHashMap<K, V>` は同一 Key に対して複数の Value を格納できるが、
**特定の Key-Value ペアのみを削除する API は提供されていない**。

| 操作 | API | 動作 |
|---|---|---|
| 全 Value 削除 | `Remove(key)` | Key に関連する全 Value を削除 |
| 個別 Value 削除 | (直接 API なし) | イテレータで走査し、一致する Value を見つけて `Remove` する必要がある |

この制約は Sparse Index ([04-sparse-design.md](./04-sparse-design.md)) および
BM25 Index ([05-bm25-design.md](./05-bm25-design.md)) の Remove 操作に影響する。

**対策**: ソフト削除 + バッチ再構築方式を採用する。
詳細は各設計書を参照。

---

## 2. IL2CPP (AOT) 制約

Unity の IL2CPP は C# の IL (Intermediate Language) を C++ に変換して AOT コンパイルする。
JIT コンパイルが利用できないため、動的コード生成に関する制約がある。

### 2.1 Reflection.Emit 不可

動的コード生成が不可能であるため、以下の API は使用できない:

- `System.Reflection.Emit` (動的アセンブリ・型・メソッド生成)
- `System.Linq.Expressions.Expression.Compile()` (式木のランタイムコンパイル)

**代替:** コンパイル時にすべてのコードパスを静的に生成する。
フィルタ式の評価（[07-filter-design.md](./07-filter-design.md) 参照）も
動的コード生成ではなく、静的なインタプリタ方式で実装する。

### 2.2 ジェネリクスの制限

IL2CPP では値型ジェネリクスに対して AOT 時に特殊化（specialization）が必要となる。
使用される型パラメータの組み合わせごとにコードが生成されるため、
複雑なジェネリクスはコード膨張を招く。

**対策:**
- `where T : struct` / `where T : unmanaged` で型パラメータを制約する
- 使用する具象型を限定し、無制限の型パラメータ展開を避ける
- UniCortex では距離関数の型パラメータ化を避け、enum + switch パターンを採用

### 2.3 コードストリッピング

IL2CPP はビルド時に未使用と判断したコードを削除する（コードストリッピング）。
リフレクション経由でのみ使用される型やメソッドが誤って削除される可能性がある。

**対策:**

- `link.xml` でアセンブリ・型を保護する
- `[Preserve]` 属性を付与する

```xml
<!-- link.xml -->
<linker>
  <assembly fullname="UniCortex.Runtime" preserve="all"/>
</linker>
```

UniCortex ではシリアライズ/デシリアライズ時にリフレクションを使用しない設計とするため、
コードストリッピングの影響は最小限に抑えられる。
ただし、安全策として `link.xml` による保護を設定する。

---

## 3. WebGL 制約

WebGL プラットフォームにはブラウザ環境固有の制約がある。
UniCortex は WebGL を含む全プラットフォームでの動作を目標とするため、
これらの制約を設計全体で考慮する必要がある。

### 3.1 MemoryMappedFile 不可

WebGL にはネイティブファイルシステムがないため、
`System.IO.MemoryMappedFiles` は使用できない。

**代替:**
- IndexedDB + JavaScript interop による永続化
- Persistence レイヤーでプラットフォーム別の抽象化を提供

詳細は [08-persistence-design.md](./08-persistence-design.md) を参照。

### 3.2 シングルスレッド

WebGL は SharedArrayBuffer の制限により、実質的にシングルスレッドで動作する。

- Unity Jobs は WebGL でもスケジュール可能だが、**実質シングルスレッドで逐次実行**される
- `IJobParallelFor` も並列化されず逐次実行される
- `Job.Schedule()` と `Job.Complete()` の間に他の処理を挟むことによる非同期的なメリットは得られない

**設計上の影響:**
- 並列処理に依存しないアルゴリズム設計にする
- Job 分割粒度は WebGL でのオーバーヘッドを考慮して調整する
- HNSW の挿入・検索は Job 化するが、WebGL では逐次実行でも許容できる性能を確保する
  （[03-hnsw-design.md](./03-hnsw-design.md) を参照）

### 3.2.1 WebGL パフォーマンス劣化

WebGL 環境ではデスクトップ比で約 **3 倍** のレイテンシ増加を見込む。

| 要因 | 影響度 | 説明 |
|---|---|---|
| シングルスレッド | 高 | Jobs の並列実行が不可。ハイブリッド検索で顕著 |
| WASM オーバーヘッド | 中 | ネイティブコード比で 1.5-2x のオーバーヘッド |
| WASM SIMD 制限 | 低 | SSE/AVX/NEON の一部命令が未対応 |
| IndexedDB I/O | 中 | MemoryMappedFile の遅延読み込みが使えず、全データを byte[] 経由でロード |

プラットフォーム別パフォーマンス目標は [09-roadmap.md](./09-roadmap.md) を参照。

**推奨事項**:
- WebGL ターゲットでは efSearch / SubSearchK を小さめに設定する
- 50,000 ベクトル × 128 次元が WebGL での推奨上限
- dim=384 以上の場合、30,000 ベクトル以下を推奨

### 3.3 メモリ制限

WebGL のメモリはブラウザの WASM ヒープサイズに制限される（デフォルト 256MB～2GB 程度）。

- NativeContainer の初期容量を適切に設定し、過剰な確保を避ける
- UniCortex のターゲット規模 (~50,000 ベクトル) は WebGL でも許容範囲内
  - 例: 50,000 × 384 次元 × 4 bytes = 約 73MB（ベクトルデータのみ）
- 大規模データセットでは HNSW グラフ + 転置インデックスのメモリ消費も考慮が必要

### 3.4 Burst の WebGL 対応

Burst は WebGL 向けに WASM (WebAssembly) コードを生成する。

- SSE/AVX (x86) および NEON (ARM) の SIMD 命令は **WASM SIMD** にマップされる
- `Unity.Mathematics.float4` による距離計算は WASM SIMD で最適化される
- ただし、一部の高度な最適化（AVX-512 等）は WASM では利用不可
- Burst の `[BurstCompile]` 属性は WebGL でも有効

### 3.5 ネイティブプラグイン不可

WebGL ではネイティブプラグイン (.dll / .so / .dylib) を使用できない。
そのため UniCortex は **Pure C#** で実装する必要がある。
C/C++ ライブラリ（USearch 等）への依存は不可。

これは UniCortex が競合（RAGSearchUnity/USearch）に対して持つ重要な差別化ポイントでもある
（[00-project-overview.md](./00-project-overview.md) を参照）。

---

## 4. NativeContainer ライフサイクル管理

NativeContainer はアンマネージドメモリを使用するため、
GC による自動回収が行われない。明示的な解放が必須である。

### 4.1 IDisposable パターン

- すべての NativeContainer は `IDisposable` を実装する
- `using` ブロックまたは明示的な `Dispose()` で解放する
- Dispose 漏れは Unity Editor のリーク検出により警告される

```csharp
// パターン 1: using ブロック（短寿命）
using (var tempBuffer = new NativeArray<float>(dim, Allocator.TempJob))
{
    // 一時バッファを使用
}

// パターン 2: 明示的 Dispose（長寿命のインデックス構造）
public struct HnswIndex : IDisposable
{
    NativeArray<float> _vectors;
    NativeList<int> _neighbors;

    public void Dispose()
    {
        if (_vectors.IsCreated) _vectors.Dispose();
        if (_neighbors.IsCreated) _neighbors.Dispose();
    }
}
```

### 4.2 Allocator 選択ガイドライン

| Allocator | 寿命 | 用途 | UniCortex での使用例 |
|---|---|---|---|
| `Temp` | 1 フレーム | フレーム内の一時バッファ | - |
| `TempJob` | 4 フレーム | Job 完了までの一時バッファ | 検索クエリの候補リスト、距離計算の中間バッファ |
| `Persistent` | 無制限 | インデックス、ストレージ等の長期保持 | HNSW グラフ、VectorStorage、転置インデックス、IdMap |

### 4.3 Safety Check と Release ビルドの影響

- **Unity Editor**: メモリアクセスの安全性チェック（境界チェック、二重 Dispose 検出等）が有効
- **Development ビルド**: `ENABLE_UNITY_COLLECTIONS_CHECKS` 定義で制御。デフォルトで有効
- **Release ビルド**: チェック無効化によりパフォーマンスが向上する

#### Release ビルドでの Safety Check 無効化の影響

| 検出機能 | Editor/Development | Release |
|---|---|---|
| NativeArray 境界チェック | ✅ 有効 | ❌ 無効 |
| 二重 Dispose 検出 | ✅ 有効 | ❌ 無効 |
| NativeContainer リーク検出 | ✅ 有効 | ❌ 無効 |
| Job データ競合検出 | ✅ 有効 | ❌ 無効 |

> **重要**: Release ビルドでは NativeArray の境界外アクセスが**サイレントに成功**し、
> メモリ破壊を引き起こす可能性がある。そのため:
>
> 1. 全パブリック API のパラメータを明示的にバリデーションする（Safety Check に依存しない）
> 2. Development ビルドで十分なテストを実施してからRelease ビルドを行う
> 3. VectorStorage のインデックス計算等、重要な箇所は独自の境界チェックを実装する
>
> 詳細は [11-security-guidelines.md](./11-security-guidelines.md) を参照。

### 4.4 コンポーネント別 Allocator チェックリスト

| コンポーネント | データ構造 | Allocator | Dispose 責任 |
|---|---|---|---|
| **VectorStorage** | `Data (NativeArray<float>)` | Persistent | VectorStorage.Dispose() |
| **IdMap** | `ExternalToInternal (HashMap)` | Persistent | IdMap.Dispose() |
| | `InternalToExternal (NativeArray)` | Persistent | IdMap.Dispose() |
| | `FreeList (NativeList)` | Persistent | IdMap.Dispose() |
| **HnswGraph** | `Nodes, Neighbors, NeighborCounts` | Persistent | HnswGraph.Dispose() |
| | `Deleted (NativeArray<bool>)` | Persistent | HnswGraph.Dispose() |
| **SparseIndex** | `InvertedIndex (MultiHashMap)` | Persistent | SparseIndex.Dispose() |
| | `DeletedIds (HashSet)` | Persistent | SparseIndex.Dispose() |
| **BM25Index** | `InvertedIndex, DocumentFrequency` | Persistent | BM25Index.Dispose() |
| | `DocumentLengths (NativeArray)` | Persistent | BM25Index.Dispose() |
| **MetadataStorage** | `intValues, floatValues, boolValues` | Persistent | MetadataStorage.Dispose() |
| **検索結果** | `NativeArray<SearchResult>` | TempJob | **呼び出し元** |
| **HNSW 探索バッファ** | `Visited (NativeBitArray)` | TempJob | Job 完了後に Dispose |
| | `Candidates, Results (Heap)` | TempJob | Job 完了後に Dispose |
| **RRF マージバッファ** | `HashMap<int, float>` | Temp | Job 内で自動解放 |
| **フィルタ結果** | `PassFilter (NativeArray<bool>)` | TempJob | フィルタ処理後に Dispose |
| **FilterExpression** | `Conditions, LogicalOps` | TempJob | **呼び出し元** |

---

## 5. コーディング規約

### 5.1 Burst 関連

- Burst 互換コードには `[BurstCompile]` 属性を付与する
- Job 構造体内ではマネージド型を使用しない
- グローバル状態が必要な場合は `SharedStatic<T>` を使用する（ただし最小限に留める）
- Burst 非互換のコード（API ファサード等）は明確に分離する

### 5.2 距離計算

- `Unity.Mathematics.float4` で実装し、Burst の auto-vectorize に委ねる
- 手動 intrinsics (`X86.Sse`, `Arm.Neon` 等) は使用しない
- Burst がプラットフォーム別に最適な SIMD 命令を自動生成する:
  - x86: SSE / AVX
  - ARM: NEON
  - WebGL: WASM SIMD

距離関数の実装詳細は [02-core-design.md](./02-core-design.md) を参照。

### 5.3 API 設計

- パブリック API には XML ドキュメントコメントを付与する
- テストは NUnit (Unity Test Framework) で記述する
- NativeContainer を返す API はドキュメントに Dispose 責任を明記する
- エラーハンドリングは `Result<T>` パターンを使用する

### 5.4 命名規約

| 対象 | 規約 | 例 |
|---|---|---|
| 型名 | PascalCase | `VectorStorage`, `HnswGraph` |
| メソッド名 | PascalCase | `SearchDense`, `AddDocument` |
| パブリックフィールド | PascalCase | `Capacity`, `Dimension` |
| プライベートフィールド | `_camelCase` | `_vectors`, `_entryPoint` |
| ローカル変数 | camelCase | `candidates`, `bestDistance` |
| 定数 | PascalCase | `DefaultM`, `MaxLayers` |
| enum 値 | PascalCase | `DistanceType.Cosine` |

### 5.5 メモリレイアウト

データ構造のメモリレイアウトは用途に応じて使い分ける:

- **ベクトルデータ**: SoA (Structure of Arrays) — SIMD 効率最大化
- **グラフ構造**: AoS (Array of Structures) — キャッシュ局所性優先

詳細は [02-core-design.md](./02-core-design.md) の VectorStorage セクションおよび
[03-hnsw-design.md](./03-hnsw-design.md) のグラフ構造セクションを参照。

---

## 6. パッケージバージョン情報

### 6.1 使用パッケージ一覧

| パッケージ | バージョン | 用途 |
|---|---|---|
| `com.unity.burst` | 1.8.27 | Burst Compiler |
| `com.unity.collections` | 2.6.2 | NativeContainer |
| `com.unity.mathematics` | 1.3.3 | SIMD 数学ライブラリ |
| `com.unity.test-framework` | 1.6.0 | テストフレームワーク |
| Unity Editor | 6000.3.6f1 | Unity 6 LTS |

### 6.2 パッケージ依存関係の注意

- `com.unity.collections` 2.x には Jobs 関連の型（`IJobParallelFor` 等）が統合されている
- `com.unity.jobs` パッケージが manifest.json にない場合、`com.unity.collections` 2.x が代替している可能性があるため要確認
- パッケージのアップグレード時は Burst Compiler との互換性を確認すること

### 6.3 Assembly Definition 構成

```
Assets/UniCortex/
├── Runtime/
│   └── UniCortex.Runtime.asmdef
│       → 参照: Unity.Burst, Unity.Collections, Unity.Mathematics, Unity.Jobs
├── Editor/
│   └── UniCortex.Editor.asmdef
│       → 参照: UniCortex.Runtime + UnityEditor
└── Tests/
    ├── Runtime/
    │   └── UniCortex.Tests.Runtime.asmdef
    │       → 参照: UniCortex.Runtime + UnityEngine.TestRunner + UnityEditor.TestRunner
    └── Editor/
        └── UniCortex.Tests.Editor.asmdef
            → 参照: UniCortex.Runtime + UniCortex.Editor + UnityEngine.TestRunner + UnityEditor.TestRunner
```

詳細は [01-architecture.md](./01-architecture.md) のプロジェクト構成セクションを参照。

---

## 関連ドキュメント

| ドキュメント | 関連する制約 |
|---|---|
| [00-project-overview.md](./00-project-overview.md) | プロジェクト概要、競合比較 |
| [01-architecture.md](./01-architecture.md) | アーキテクチャ、プロジェクト構成 |
| [02-core-design.md](./02-core-design.md) | エラーハンドリング (Result パターン)、距離関数、VectorStorage |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW グラフ構造、SIMD 距離計算 |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse ベクトル検索 |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 全文検索、転置インデックス |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | ハイブリッド検索、RRF ReRanker |
| [07-filter-design.md](./07-filter-design.md) | スカラーフィルタ、フィルタ式評価 |
| [08-persistence-design.md](./08-persistence-design.md) | 永続化、WebGL 対応 |
| [09-roadmap.md](./09-roadmap.md) | 実装ロードマップ、パフォーマンス目標 |
| [11-security-guidelines.md](./11-security-guidelines.md) | セキュリティガイドライン |
| [12-test-plan.md](./12-test-plan.md) | テスト計画 |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット |
