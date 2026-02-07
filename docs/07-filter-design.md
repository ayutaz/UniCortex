# 07 - スカラーフィルタ 設計書

## 1. 概要

スカラーフィルタは、検索結果に対してメタデータベースのフィルタリングを適用するコンポーネントである。
`price >= 1000 AND category == "weapon"` のような条件式を評価し、
条件に合致しない結果を除外することで、ユーザーが求める検索結果に絞り込む。

### 設計方針

- **ポストフィルタ方式**: 検索エンジン (HNSW, Sparse, BM25) が結果を返した後にフィルタを適用する
- **Burst 互換**: フィルタ評価は `[BurstCompile]` Job として実装し、マネージド型を使用しない
- **カラムナストレージ**: メタデータはフィールドごとの列指向ストレージで管理する

### ファイル構成

| ファイル | 説明 |
|---|---|
| `Filter/MetadataStorage.cs` | カラムナ方式のメタデータストレージ |
| `Filter/FilterExpression.cs` | フィルタ条件式の定義 (演算子、論理結合) |
| `Filter/ScalarFilter.cs` | フィルタ評価ロジック + Burst Job |

---

## 2. カラムナ MetadataStorage 設計

メタデータはフィールドごとに列 (カラム) を持つカラムナストレージで管理する。
各フィールドは `NativeParallelHashMap` で `internalId -> value` をマッピングし、
フィールド名はハッシュ化して `int` キーとして管理する。

### 2.1 ストレージ構造

```csharp
public struct MetadataStorage : IDisposable
{
    // フィールドハッシュ * maxDocs + internalId をキーとする flat map
    NativeParallelHashMap<int, int> intValues;      // fieldHash*maxDocs+internalId -> int
    NativeParallelHashMap<int, float> floatValues;   // fieldHash*maxDocs+internalId -> float
    NativeParallelHashMap<int, bool> boolValues;     // fieldHash*maxDocs+internalId -> bool

    int maxDocs;

    public void SetInt(int fieldHash, int internalId, int value)
    {
        intValues[fieldHash * maxDocs + internalId] = value;
    }

    public bool TryGetInt(int fieldHash, int internalId, out int value)
    {
        return intValues.TryGetValue(fieldHash * maxDocs + internalId, out value);
    }

    // float, bool も同様
}
```

### 2.2 キー計算方式

フィールド名は文字列のまま保持せず (Burst 非互換)、ハッシュ値に変換して `int` として扱う。
複合キー `fieldHash * maxDocs + internalId` により、単一の `NativeParallelHashMap` で
全フィールド・全ドキュメントの値を管理する。

```
キー = fieldHash * maxDocs + internalId

例: maxDocs = 50000, fieldHash("price") = 42, internalId = 123
    キー = 42 * 50000 + 123 = 2100123
```

### 2.4 キー計算のオーバーフロー対策

複合キー `fieldHash * maxDocs + internalId` は `int` の範囲を超える可能性がある。

```
例: fieldHash = 100,000, maxDocs = 50,000
    キー = 100,000 * 50,000 + internalId = 5,000,000,000 + internalId
    → int.MaxValue (2,147,483,647) を超過
```

**対策**: 複合キーの型を `int` から `long` に変更する。

```csharp
public struct MetadataStorage : IDisposable
{
    NativeParallelHashMap<long, int> intValues;      // long キー
    NativeParallelHashMap<long, float> floatValues;
    NativeParallelHashMap<long, bool> boolValues;

    int maxDocs;

    public void SetInt(int fieldHash, int internalId, int value)
    {
        long key = (long)fieldHash * maxDocs + internalId;
        intValues[key] = value;
    }

    public bool TryGetInt(int fieldHash, int internalId, out int value)
    {
        long key = (long)fieldHash * maxDocs + internalId;
        return intValues.TryGetValue(key, out value);
    }
}
```

これにより `fieldHash` と `maxDocs` が大きい場合でもオーバーフローを防止できる。
`long` (64-bit) の範囲は 9.2 × 10^18 であり、実用上オーバーフローは発生しない。

セキュリティ面の詳細は [11-security-guidelines.md](./11-security-guidelines.md) を参照。

### 2.3 カラムナストレージを選択した理由

| 観点 | カラムナ (採用) | 行指向 |
|---|---|---|
| フィルタ評価 | 1フィールドのデータが連続 -> キャッシュ効率が良い | 全フィールドを読み出す必要がある |
| Burst 互換性 | `NativeParallelHashMap` で実装可能 | 構造体の可変長フィールドが困難 |
| メモリ効率 | 値が存在するエントリのみ格納 (sparse) | 全エントリにスロットが必要 |
| スケーラビリティ | フィールド追加が容易 | スキーマ変更が困難 |

---

## 3. 対応フィールド型

| 型 | C# 型 | 説明 | 対応演算子 |
|---|---|---|---|
| Int32 | `int` | 整数値 (価格、レベル、数量等) | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Float32 | `float` | 浮動小数点 (スコア、距離、重み等) | `==`, `!=`, `<`, `<=`, `>`, `>=` |
| Bool | `bool` | 真偽値 (有効/無効、フラグ等) | `==`, `!=` |

### 演算子×型 互換マトリクス

| 演算子 (FilterOp) | Int32 | Float32 | Bool |
|---|---|---|---|
| `Equal` (`==`) | ✅ | ✅ | ✅ |
| `NotEqual` (`!=`) | ✅ | ✅ | ✅ |
| `LessThan` (`<`) | ✅ | ✅ | ❌ |
| `LessOrEqual` (`<=`) | ✅ | ✅ | ❌ |
| `GreaterThan` (`>`) | ✅ | ✅ | ❌ |
| `GreaterOrEqual` (`>=`) | ✅ | ✅ | ❌ |

- **Bool 型の順序演算 (`<`, `<=`, `>`, `>=`)**: Bool に対する順序比較は意味をなさないため、
  条件は常に `false` として評価する（エラーにはしない）。
- **型不一致**: FilterCondition の FieldType が MetadataStorage の実際の格納型と異なる場合、
  `TryGetValue` がキーを見つけられず `false` として評価される（セクション 4.6 参照）。

### Float32 の NaN 比較挙動

Float32 フィールドに NaN が格納されている場合、または FilterCondition の FloatValue が NaN の場合:

| 比較 | 結果 | 理由 |
|---|---|---|
| `NaN == x` | `false` | IEEE 754: NaN は自身を含む全値と不等 |
| `NaN != x` | `true` | IEEE 754 |
| `NaN < x` | `false` | IEEE 754: 順序比較は常に false |
| `NaN <= x` | `false` | IEEE 754 |
| `NaN > x` | `false` | IEEE 754 |
| `NaN >= x` | `false` | IEEE 754 |

> **推奨**: NaN をメタデータ値として格納することは推奨しない。
> 入力バリデーション ([11-security-guidelines.md](./11-security-guidelines.md)) で NaN/Inf を
> 拒否するか、「値なし」はフィールドを設定しないことで表現すべきである。

### string 型の取り扱い

string 型は Burst Compiler と互換性がないため、直接サポートしない。
代わりに、文字列をハッシュ化して `int` として格納する戦略を採用する。

```csharp
// 文字列をハッシュ化して int フィールドとして格納
int categoryHash = HashString("weapon");  // -> 安定したハッシュ値
storage.SetInt(FieldHash("category"), internalId, categoryHash);

// フィルタ時も同じハッシュ関数で比較
filter.AddCondition(FieldHash("category"), FilterOp.Equal, HashString("weapon"));
```

ハッシュ衝突のリスクは存在するが、ターゲット規模 (~50,000 ドキュメント) では
実用上問題にならない確率である。衝突が許容できないケースでは、
enum 値を `int` に変換して格納することを推奨する。

---

## 4. フィルタ演算子と条件式

### 4.1 演算子定義

```csharp
public enum FilterOp : byte
{
    Equal,           // ==
    NotEqual,        // !=
    LessThan,        // <
    LessOrEqual,     // <=
    GreaterThan,     // >
    GreaterOrEqual,  // >=
}
```

### 4.2 論理演算子

```csharp
public enum LogicalOp : byte
{
    And,  // 全条件が真のとき真
    Or,   // いずれかの条件が真のとき真
}
```

### 4.3 フィルタ条件構造体

```csharp
public enum FieldType : byte
{
    Int32,
    Float32,
    Bool,
}

public struct FilterCondition
{
    public int FieldHash;        // フィールド名のハッシュ値
    public FilterOp Op;          // 比較演算子
    public FieldType FieldType;  // フィールドの型

    // union 的に値を格納 (unmanaged struct のため明示的レイアウトは使用しない)
    public int IntValue;
    public float FloatValue;
    public bool BoolValue;
}
```

### 4.6 型安全性チェック

`FilterCondition` の `FieldType` は、MetadataStorage に実際に格納されている型と一致する必要がある。
型の不一致はサイレントな誤動作を引き起こすため、以下の検証を行う:

- **Add 時**: `FilterExpression.AddCondition()` で指定された `FieldType` を記録
- **評価時**: `EvaluateCondition` 内で `FieldType` に基づいて対応する HashMap を参照する（現行設計で対応済み）
- **ユーザー向けドキュメント**: フィルタ条件のフィールド型は、`SetInt` / `SetFloat` / `SetBool` で格納した型と一致させる必要があることを明記する

型の不一致例:
```csharp
// NG: float で格納したフィールドに int 条件を適用
storage.SetFloat(fieldHash, internalId, 99.5f);
filter.AddCondition(fieldHash, FilterOp.GreaterThan, 99);  // FieldType.Int32 → 常に false
```

> **注意**: 現行設計では型の不一致はランタイムエラーを生成せず、条件が `false` として評価される（`TryGetValue` がキーを見つけられないため）。将来的には型情報をメタデータとして保持し、不一致時に `InvalidParameter` エラーを返す拡張を検討する。

### 4.4 フィルタ式

```csharp
public struct FilterExpression : IDisposable
{
    public NativeArray<FilterCondition> Conditions;
    public NativeArray<LogicalOp> LogicalOps;  // Conditions[i] と Conditions[i+1] 間の論理演算子
    // LogicalOps.Length == Conditions.Length - 1

    public void AddCondition(int fieldHash, FilterOp op, int intValue) { ... }
    public void AddCondition(int fieldHash, FilterOp op, float floatValue) { ... }
    public void AddCondition(int fieldHash, FilterOp op, bool boolValue) { ... }
    public void AddLogicalOp(LogicalOp logicalOp) { ... }

    public void Dispose()
    {
        if (Conditions.IsCreated) Conditions.Dispose();
        if (LogicalOps.IsCreated) LogicalOps.Dispose();
    }
}
```

### 4.5 評価順序

条件式は左から右へ順次評価する。括弧によるグループ化は初期バージョンではサポートしない。

```
条件A AND 条件B OR 条件C
-> ((条件A AND 条件B) OR 条件C)  ← 左から右へ評価
```

将来的に括弧サポートが必要な場合は、条件式を木構造 (NativeArray ベースの
インデックス参照ツリー) に拡張する。

> **制限事項**: 左から右への順次評価は AND/OR の標準的な優先度（AND が OR より高い）を尊重しない。ユーザーは条件の順序を明示的に制御する必要がある。
>
> ```
> 意図: price >= 1000 AND (category == "weapon" OR category == "armor")
> 現行の記述方法では正しく表現できない
> ```
>
> **将来の括弧サポート**: NativeArray ベースのインデックス参照ツリーによる条件式の木構造化で実現する。ロードマップでは Phase 10 以降の拡張として位置づけている（[09-roadmap.md](./09-roadmap.md) 参照）。

---

## 5. ポストフィルタ方式の設計判断

### 5.1 選択: ポストフィルタ (検索後フィルタ)

検索エンジンが結果を返した後にフィルタを適用するポストフィルタ方式を採用する。

```
検索エンジン (HNSW / Sparse / BM25)
    ↓  Top-K * oversamplingFactor 件を取得
フィルタ評価 (FilterEvaluateJob)
    ↓  条件に合致する結果のみ残す
最終結果 (Top-K 件)
```

### 5.2 プレフィルタを不採用とした理由

プレフィルタ (フィルタ後に検索) は HNSW のグラフ探索と相性が悪い:

- HNSW はグラフ上の隣接ノードを辿って探索するため、フィルタで除外されたノードが
  探索経路を遮断し、到達可能なノード数が大幅に減少する
- フィルタ適合率が低い場合、探索が早期に行き詰まり、検索品質が著しく低下する
- グラフ探索ロジックにフィルタ判定を組み込む必要があり、実装の複雑度が大幅に増加する

### 5.3 トレードオフ

| 観点 | ポストフィルタ (採用) | プレフィルタ |
|---|---|---|
| 実装の複雑度 | 低い (検索とフィルタが独立) | 高い (検索ロジックに組み込み) |
| 検索品質 | HNSW の探索品質に影響なし | フィルタ率が高いと品質低下 |
| 結果件数の保証 | K 件未満になる可能性あり | K 件を保証しやすい |
| 計算コスト | オーバーサンプリングの分だけ多い | フィルタ済みデータのみ探索 |

ポストフィルタの「結果が K 件未満になる可能性」は、オーバーサンプリングで対策する。

---

## 6. オーバーサンプリング戦略

### 6.1 基本方針

ポストフィルタ方式では、フィルタにより結果が間引かれることを見越して、
検索時に K の倍数を取得する。

```csharp
int actualK = (int)(k * oversamplingFactor);
// actualK 件を検索 -> フィルタ適用 -> 上位 K 件を返す
```

### 6.2 パラメータ

```csharp
public struct FilteredSearchParams
{
    public SearchParams BaseParams;       // 基本検索パラメータ (K, EfSearch 等)
    public FilterExpression Filter;       // フィルタ条件式
    public float OversamplingFactor;      // オーバーサンプリング倍率 (default: 3.0)
}
```

| パラメータ | デフォルト値 | 説明 |
|---|---|---|
| `OversamplingFactor` | `3.0` | K の何倍を検索するか。フィルタ適合率が低い場合は増やす |

### 6.3 オーバーサンプリング倍率の選定根拠

- フィルタ適合率 33% の場合: 3.0x で期待的に K 件を確保
- フィルタ適合率 50% の場合: 2.0x で十分だが、安全マージンとして 3.0x
- フィルタ適合率 10% 未満の場合: 10.0x 以上が必要 -> ユーザーが明示的に指定

### 6.4 将来拡張: 動的オーバーサンプリング

初期バージョンでは固定倍率とするが、将来的にはフィルタ適合率の統計情報を蓄積し、
動的に倍率を調整する拡張を検討する。

```
// 将来構想 (初期バージョンでは実装しない)
actualOversamplingFactor = max(minFactor, 1.0 / estimatedSelectivity)
```

---

## 7. フィルタ評価 Job

### 7.1 FilterEvaluateJob

フィルタ評価は `IJobParallelFor` として実装し、Burst Compiler で最適化する。
各候補を独立に評価できるため、並列化が容易である。

```csharp
[BurstCompile]
public struct FilterEvaluateJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<SearchResult> Candidates;
    [ReadOnly] public NativeArray<FilterCondition> Conditions;
    [ReadOnly] public NativeArray<LogicalOp> LogicalOps;

    // MetadataStorage の各カラム (ReadOnly)
    [ReadOnly] public NativeParallelHashMap<int, int> IntValues;
    [ReadOnly] public NativeParallelHashMap<int, float> FloatValues;
    [ReadOnly] public NativeParallelHashMap<int, bool> BoolValues;

    public int MaxDocs;

    // 出力: 各候補がフィルタを通過したか
    public NativeArray<bool> PassFilter;

    public void Execute(int index)
    {
        int internalId = Candidates[index].InternalId;
        bool result = EvaluateCondition(Conditions[0], internalId);

        for (int i = 1; i < Conditions.Length; i++)
        {
            bool condResult = EvaluateCondition(Conditions[i], internalId);

            if (LogicalOps[i - 1] == LogicalOp.And)
                result = result && condResult;
            else // LogicalOp.Or
                result = result || condResult;
        }

        PassFilter[index] = result;
    }

    private bool EvaluateCondition(FilterCondition cond, int internalId)
    {
        int key = cond.FieldHash * MaxDocs + internalId;

        switch (cond.FieldType)
        {
            case FieldType.Int32:
                if (!IntValues.TryGetValue(key, out int intVal)) return false;
                return EvaluateInt(intVal, cond.Op, cond.IntValue);

            case FieldType.Float32:
                if (!FloatValues.TryGetValue(key, out float floatVal)) return false;
                return EvaluateFloat(floatVal, cond.Op, cond.FloatValue);

            case FieldType.Bool:
                if (!BoolValues.TryGetValue(key, out bool boolVal)) return false;
                return EvaluateBool(boolVal, cond.Op, cond.BoolValue);

            default:
                return false;
        }
    }

    private bool EvaluateInt(int actual, FilterOp op, int expected)
    {
        switch (op)
        {
            case FilterOp.Equal:          return actual == expected;
            case FilterOp.NotEqual:       return actual != expected;
            case FilterOp.LessThan:       return actual < expected;
            case FilterOp.LessOrEqual:    return actual <= expected;
            case FilterOp.GreaterThan:    return actual > expected;
            case FilterOp.GreaterOrEqual: return actual >= expected;
            default: return false;
        }
    }

    // EvaluateFloat, EvaluateBool も同様のパターン
}
```

### 7.2 Job の実行フロー

```
1. 検索エンジンが候補を返す (NativeArray<SearchResult>)
2. FilterEvaluateJob をスケジュール
3. Job 完了後、PassFilter[i] == true の候補のみを収集
4. スコア降順でソートし、上位 K 件を返す
```

### 7.3 メタデータが存在しないフィールドの扱い

`TryGetValue` が `false` を返した場合 (= 該当ドキュメントにそのフィールドが存在しない場合)、
条件は `false` として評価する。これにより、メタデータが不完全なドキュメントは
フィルタ条件にマッチしない (= 除外される) という直感的な挙動になる。

---

## 8. 使用例

### 8.1 基本的なフィルタ付き検索

```csharp
// フィルタ式を構築
var filter = new FilterExpression(Allocator.TempJob);
filter.AddCondition(FieldHash("price"), FilterOp.GreaterOrEqual, 1000);
filter.AddLogicalOp(LogicalOp.And);
filter.AddCondition(FieldHash("category"), FilterOp.Equal, HashString("weapon"));

// フィルタ付き検索を実行
var results = db.SearchDense(query, new FilteredSearchParams
{
    BaseParams = new SearchParams { K = 10, EfSearch = 50 },
    Filter = filter,
    OversamplingFactor = 3.0f,
});

// フィルタ式を破棄
filter.Dispose();
```

### 8.2 メタデータの登録

```csharp
// ドキュメント追加時にメタデータを設定
db.SetMetadata(internalId, FieldHash("price"), 2500);
db.SetMetadata(internalId, FieldHash("category"), HashString("weapon"));
db.SetMetadata(internalId, FieldHash("is_rare"), true);
```

### 8.3 複合条件 (OR を含む)

```csharp
var filter = new FilterExpression(Allocator.TempJob);
filter.AddCondition(FieldHash("price"), FilterOp.GreaterOrEqual, 1000);
filter.AddLogicalOp(LogicalOp.And);
filter.AddCondition(FieldHash("level"), FilterOp.LessOrEqual, 50);
filter.AddLogicalOp(LogicalOp.Or);
filter.AddCondition(FieldHash("is_rare"), FilterOp.Equal, true);
// 評価: ((price >= 1000 AND level <= 50) OR is_rare == true)
```

### 8.4 ハイブリッド検索との組み合わせ

スカラーフィルタはハイブリッド検索 (RRF ReRanker) と組み合わせて使用できる。
RRF による統合ランキングの後にポストフィルタを適用する。

```csharp
var results = db.SearchHybrid(denseQuery, sparseQuery, textQuery,
    new FilteredSearchParams
    {
        BaseParams = new SearchParams { K = 10 },
        Filter = filter,
        OversamplingFactor = 3.0f,
    });
```

検索フロー全体の詳細は [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) を参照。

---

## 9. 性能特性

### 9.1 計算量

| 操作 | 計算量 | 説明 |
|---|---|---|
| メタデータ登録 | O(1) | `NativeParallelHashMap` への挿入 |
| メタデータ取得 | O(1) | `NativeParallelHashMap` からの読み取り |
| フィルタ評価 (1候補) | O(C) | C = 条件数。各条件で O(1) のハッシュマップ参照 |
| フィルタ評価 (全候補) | O(N * C) | N = 候補数、C = 条件数。Job で並列化 |

### 9.2 メモリ使用量

| データ | 計算式 | 50,000 ドキュメント x 5 フィールドの場合 |
|---|---|---|
| IntValues | エントリ数 * (4 + 4) bytes | 250,000 * 8 = ~2.0 MB |
| FloatValues | エントリ数 * (4 + 4) bytes | 250,000 * 8 = ~2.0 MB |
| BoolValues | エントリ数 * (4 + 1) bytes | 250,000 * 5 = ~1.25 MB |

実際にはフィールドが全型を網羅するケースは稀であり、使用するフィールド型のみのメモリを消費する。

---

## 10. 制約と今後の拡張

### 10.1 初期バージョンの制約

- 括弧によるグループ化は未サポート (左から右へ順次評価)
- `NOT` 単項演算子は未サポート (`!=` で代替)
- `IN` 演算子 (複数値マッチ) は未サポート
- 動的オーバーサンプリングは未実装 (固定倍率)

### 10.2 将来の拡張候補

| 拡張 | 説明 | 優先度 |
|---|---|---|
| 括弧サポート | 条件式の木構造化 | 中 |
| `IN` 演算子 | `category IN ("weapon", "armor")` | 中 |
| `NOT` 演算子 | `NOT (price < 100)` | 低 |
| 動的オーバーサンプリング | フィルタ適合率に基づく自動調整 | 低 |
| Range クエリ最適化 | ソート済みカラムによる範囲検索の高速化 | 低 |

---

## 関連ドキュメント

| ドキュメント | 関連内容 |
|---|---|
| [01-architecture.md](./01-architecture.md) | アーキテクチャ全体図、スカラーフィルタの位置付け |
| [02-core-design.md](./02-core-design.md) | Core 層の共通データ構造 (Result\<T\>, ErrorCode) |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW 検索パラメータ (SearchParams, EfSearch) |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | ハイブリッド検索との統合、RRF ReRanker 後のフィルタ適用 |
| [08-persistence-design.md](./08-persistence-design.md) | MetadataStorage の永続化 |
| [11-security-guidelines.md](./11-security-guidelines.md) | セキュリティガイドライン（入力バリデーション） |
| [12-test-plan.md](./12-test-plan.md) | テスト計画（フィルタ評価テストケース） |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット（MetadataStorage メモリ見積もり） |
