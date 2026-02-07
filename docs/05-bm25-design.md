# 05 - BM25 全文検索 設計書

BM25 (Best Matching 25) アルゴリズムによる全文検索エンジンの詳細設計。
Burst 互換のトークナイザと転置インデックスで実装し、日本語・英語・多言語テキストに対応する。

対応するランタイムコードは `Assets/UniCortex/Runtime/FullText/` に配置する。

---

## 1. 概要

BM25 は情報検索における代表的なランキング関数であり、TF-IDF の発展形として
Term Frequency (TF) の飽和と文書長の正規化を導入したモデルである。
UniCortex では以下の方針で BM25 全文検索を実装する:

- **Burst 互換**: managed string を使わず UTF-8 バイト列を直接処理するトークナイザ
- **転置インデックス**: `NativeParallelMultiHashMap` によるトークンハッシュ → Posting リストの管理
- **多言語対応**: ASCII テキストは空白分割、CJK / ひらがな / カタカナはユニグラム + バイグラム
- **Job System 統合**: `BM25SearchJob` による Burst 最適化された検索処理

BM25 エンジンは単体で全文検索として使用できるほか、
ハイブリッド検索 ([06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md)) において
Dense ベクトル検索 ([03-hnsw-design.md](./03-hnsw-design.md)) や
Sparse ベクトル検索 ([04-sparse-design.md](./04-sparse-design.md)) と
RRF ReRanker で統合される。

---

## 2. トークナイザ設計 (Burst 互換 UTF-8 処理)

### 2.1 設計方針

Burst Compiler はマネージドメモリの動的割り当てや `System.String` の操作をサポートしない
([01-architecture.md](./01-architecture.md) セクション 6 参照)。
そのため、トークナイザは UTF-8 エンコードされた `NativeArray<byte>` を直接入力とし、
マネージドヒープを一切使用しない設計とする。

形態素解析 (MeCab 等) は辞書ファイルへの依存と managed API の使用が避けられないため採用しない。
代わりに、文字種判定に基づくルールベースのトークン分割を行う。

### 2.2 ASCII テキスト

ASCII 範囲 (U+0000 ~ U+007F) のテキストは以下のルールでトークン化する:

1. **空白・句読点で分割**: スペース、タブ、改行、およびアルファベット・数字以外の文字を区切りとする
2. **小文字化**: 大文字 A-Z (0x41-0x5A) を小文字 a-z (0x61-0x7A) に変換する
3. **ストップワード除外なし**: シンプルさを優先し、ストップワードの除外は行わない

```
入力: "The Dragon Sword deals 150 damage"
トークン: ["the", "dragon", "sword", "deals", "150", "damage"]
```

### 2.3 CJK / ひらがな / カタカナ

CJK 統合漢字、ひらがな、カタカナはスペース区切りではないため、
N-gram ベースのトークン化を行う:

- **ユニグラム (1文字)**: 各文字を単独のトークンとして生成
- **バイグラム (2文字連続)**: 隣接する2文字を結合したトークンも同時に生成

この方式により、辞書なしで部分一致検索を実現する。

```
入力: "東京都"
トークン: ["東", "京", "都", "東京", "京都"]
```

ユニグラムのみでは検索精度が低く (「京」で「北京」も「東京」もヒットする)、
バイグラムを併用することで文脈を捉えた検索が可能になる。

### 2.4 混合テキストの処理

ASCII とCJK が混在するテキストでは、文字種の切り替わりをトークン境界とする:

```
入力: "HP回復potion"
トークン: ["hp", "回", "復", "回復", "potion"]
```

### 2.5 コードポイント分類ロジック

UTF-8 デコード後のコードポイントに対し、以下の関数で文字種を判定する:

```csharp
public static bool IsAsciiAlpha(byte b) => (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z');
public static bool IsAsciiDigit(byte b) => b >= '0' && b <= '9';
public static bool IsWhitespace(byte b) => b == ' ' || b == '\t' || b == '\n' || b == '\r';

// UTF-8 デコードでコードポイントを取得し、Unicode ブロックで分類
public static bool IsCjk(int codepoint) =>
    (codepoint >= 0x4E00 && codepoint <= 0x9FFF) ||    // CJK Unified Ideographs
    (codepoint >= 0x3400 && codepoint <= 0x4DBF) ||    // CJK Extension A
    (codepoint >= 0x20000 && codepoint <= 0x2A6DF);    // CJK Extension B

public static bool IsHiragana(int codepoint) =>
    codepoint >= 0x3040 && codepoint <= 0x309F;

public static bool IsKatakana(int codepoint) =>
    codepoint >= 0x30A0 && codepoint <= 0x30FF;
```

CJK / ひらがな / カタカナはすべて「CJK 系文字」として同一のトークン化ルール (ユニグラム + バイグラム) を適用する。

### 2.6 UTF-8 デコード

Burst 互換の UTF-8 バイト列 → Unicode コードポイントのデコーダ:

```csharp
/// <summary>
/// UTF-8 バイト列から1文字分のコードポイントをデコードする。
/// </summary>
/// <param name="data">UTF-8 エンコードされたバイト列</param>
/// <param name="offset">読み取り開始位置</param>
/// <param name="bytesRead">消費したバイト数 (出力)</param>
/// <returns>デコードされた Unicode コードポイント</returns>
public static int DecodeUtf8(NativeArray<byte> data, int offset, out int bytesRead)
{
    byte b0 = data[offset];
    if (b0 < 0x80)
    {
        bytesRead = 1;
        return b0;
    }
    if ((b0 & 0xE0) == 0xC0)
    {
        bytesRead = 2;
        return ((b0 & 0x1F) << 6) | (data[offset + 1] & 0x3F);
    }
    if ((b0 & 0xF0) == 0xE0)
    {
        bytesRead = 3;
        return ((b0 & 0x0F) << 12)
             | ((data[offset + 1] & 0x3F) << 6)
             | (data[offset + 2] & 0x3F);
    }
    bytesRead = 4;
    return ((b0 & 0x07) << 18)
         | ((data[offset + 1] & 0x3F) << 12)
         | ((data[offset + 2] & 0x3F) << 6)
         | (data[offset + 3] & 0x3F);
}
```

不正な UTF-8 シーケンス (先頭バイトが 0xFE/0xFF 等) に対しては、
`bytesRead = 1` を返してスキップする (replacement character は生成しない)。

---

## 3. トークンハッシュ

### 3.1 方針

転置インデックスのキーとしてトークン文字列を直接使うと、
可変長データの管理が必要になり Burst 互換の NativeContainer では扱いにくい。
そこで、トークンの UTF-8 バイト列を **xxHash3** でハッシュ化し、`uint` 値として管理する。

### 3.2 ハッシュ関数

Unity.Collections が提供する `xxHash3` を使用する:

```csharp
uint tokenHash = xxHash3.Hash64(tokenBytes, tokenByteLength, seed: 0).x;
```

### 3.3 衝突確率

xxHash3 の衝突確率は理論上 `1 / 2^32 ≈ 2.3 x 10^-10` (ペアあたり) である。
50,000 ドキュメントで平均 100 ユニークトークン/ドキュメントと仮定すると、
ユニークトークン総数は最大でも数十万程度であり、Birthday Problem による衝突期待値は:

```
n = 500,000 トークン
P(collision) ≈ 1 - e^(-n^2 / (2 * 2^32)) ≈ 2.8%
```

少数のトークン衝突は検索精度にわずかな影響を与えるが、
ターゲット規模 (~50,000 ドキュメント) では実用上問題ない。

---

## 4. 転置インデックス設計

### 4.1 データ構造

```csharp
/// <summary>
/// 転置インデックスの1エントリ (Posting)。
/// あるトークンが特定のドキュメントに出現したことを記録する。
/// </summary>
public struct BM25Posting
{
    /// <summary>内部ドキュメント ID</summary>
    public int InternalId;

    /// <summary>このドキュメント内でのトークン出現頻度 (Term Frequency)</summary>
    public int TermFrequency;
}
```

```csharp
/// <summary>
/// BM25 全文検索インデックス。
/// 転置インデックスとドキュメント統計情報を保持する。
/// </summary>
public struct BM25Index : IDisposable
{
    /// <summary>転置インデックス: tokenHash → Posting リスト</summary>
    public NativeParallelMultiHashMap<uint, BM25Posting> InvertedIndex;

    /// <summary>Document Frequency: tokenHash → そのトークンを含むドキュメント数</summary>
    public NativeParallelHashMap<uint, int> DocumentFrequency;

    /// <summary>各ドキュメントのトークン数 (文書長)</summary>
    public NativeArray<int> DocumentLengths;

    /// <summary>登録済みドキュメント総数</summary>
    public int TotalDocuments;

    /// <summary>平均文書長 (全ドキュメントのトークン数合計 / ドキュメント数)</summary>
    public float AverageDocumentLength;
}
```

### 4.2 NativeContainer の選択理由

| コンテナ | 用途 | 選択理由 |
|---|---|---|
| `NativeParallelMultiHashMap<uint, BM25Posting>` | 転置インデックス | 1つのトークンハッシュに対し複数の Posting を格納する必要がある。Burst 互換で並列読み取りが可能。 |
| `NativeParallelHashMap<uint, int>` | Document Frequency | トークンハッシュ → DF の 1:1 マッピング。 |
| `NativeArray<int>` | DocumentLengths | ドキュメント ID でインデックスアクセス。固定長配列で十分。 |

すべて `Allocator.Persistent` で確保し、`BM25Index.Dispose()` で一括解放する
([01-architecture.md](./01-architecture.md) セクション 5 参照)。

### 4.3 メモリ見積もり

50,000 ドキュメント、平均 100 トークン/ドキュメントの場合:

| データ | サイズ見積もり |
|---|---|
| InvertedIndex (Posting) | ~5,000,000 エントリ x 8 bytes = ~40 MB |
| DocumentFrequency | ~500,000 エントリ x 8 bytes = ~4 MB |
| DocumentLengths | 50,000 x 4 bytes = ~0.2 MB |
| **合計** | **~44 MB** |

実際にはハッシュマップのオーバーヘッド (ロードファクター、バケット管理) により、
1.5~2 倍程度のメモリを消費する見込み。

---

## 5. BM25 スコア計算式

### 5.1 基本式

クエリ `q` とドキュメント `d` の BM25 スコア:

```
score(q, d) = Σ IDF(qi) * (tf(qi, d) * (k1 + 1)) / (tf(qi, d) + k1 * (1 - b + b * |d| / avgdl))
```

各記号の意味:

| 記号 | 意味 |
|---|---|
| `qi` | クエリ中の i 番目のトークン |
| `tf(qi, d)` | ドキュメント `d` におけるトークン `qi` の出現頻度 (Term Frequency) |
| `|d|` | ドキュメント `d` のトークン数 (文書長) |
| `avgdl` | 全ドキュメントの平均文書長 |
| `N` | 総ドキュメント数 |
| `df(qi)` | トークン `qi` を含むドキュメント数 (Document Frequency) |

### 5.2 IDF (Inverse Document Frequency)

```
IDF(qi) = ln((N - df(qi) + 0.5) / (df(qi) + 0.5) + 1)
```

- 多くのドキュメントに出現するトークン (高 DF) は低い IDF 値となり、識別力が低いことを反映する
- `+ 1` は IDF が負にならないことを保証する (Robertson-Walker の変形)
- `+ 0.5` は Laplace smoothing の一種で、DF = 0 や DF = N の極端なケースを安定化させる

### 5.3 パラメータ

| パラメータ | デフォルト値 | 説明 | 調整指針 |
|---|---|---|---|
| `k1` | 1.2 | TF 飽和パラメータ | 値を大きくするとTFの影響が線形に近づき、同じトークンの繰り返しが高スコアに寄与する。小さくすると TF の飽和が早く、1回でも出現すれば十分なスコアを得る。一般的には 1.2~2.0 の範囲。 |
| `b` | 0.75 | 文書長正規化パラメータ | `b = 1.0` で完全正規化 (長いドキュメントを強くペナルティ)、`b = 0.0` で正規化なし。短い定型テキスト (アイテム名等) が多い場合は小さめに、長文テキスト (クエスト説明等) が混在する場合はデフォルト付近が適切。 |

### 5.4 計算例

```
N = 10,000 ドキュメント
avgdl = 50 トークン
クエリ: "dragon sword"

ドキュメント d:
  |d| = 40 トークン
  tf("dragon", d) = 3
  tf("sword", d) = 1

df("dragon") = 200
df("sword") = 500

IDF("dragon") = ln((10000 - 200 + 0.5) / (200 + 0.5) + 1) = ln(49.88) ≈ 3.91
IDF("sword")  = ln((10000 - 500 + 0.5) / (500 + 0.5) + 1) = ln(19.99) ≈ 3.00

score_dragon = 3.91 * (3 * 2.2) / (3 + 1.2 * (1 - 0.75 + 0.75 * 40/50))
             = 3.91 * 6.6 / (3 + 1.2 * 0.85)
             = 3.91 * 6.6 / 4.02
             ≈ 6.42

score_sword  = 3.00 * (1 * 2.2) / (1 + 1.2 * 0.85)
             = 3.00 * 2.2 / 2.02
             ≈ 3.27

total_score  = 6.42 + 3.27 = 9.69
```

### 5.5 SearchResult への格納 — スコア符号規約

BM25 スコアは「大きいほど関連度が高い」特性を持つが、
UniCortex の統一スコア符号規約 ([02-core-design.md](./02-core-design.md)「統一スコア符号規約」参照) では
**「Score が小さいほど関連度が高い」** を全検索方式で維持する。

そのため、BM25 スコアは **負値変換 (`-bm25Score`)** して `SearchResult.Score` に格納する。

```
SearchResult.Score = -total_score
                   = -9.69
```

Top-K 選択では NativeMaxHeap を使用し、Score が最大 (= 関連度最低) の要素を除去する。
最終結果は Score 昇順 (= 関連度降順) でソートされる。

### 5.6 エッジケース動作表

| 入力条件 | 動作 | 理由 |
|---|---|---|
| 空クエリ (`""`) | 空の結果配列を返す (エラーなし) | トークンが0個 → スコア計算対象なし |
| 空白のみのクエリ (`"   "`) | 空の結果配列を返す (エラーなし) | トークナイザが空白をスキップし、トークン0個 |
| 単一 CJK 文字クエリ (`"剣"`) | ユニグラム1個で検索実行 | バイグラム生成には2文字以上が必要 |
| 不正 UTF-8 バイト列 | 不正バイトをスキップ (bytesRead=1) | セクション 2.6 の DecodeUtf8 仕様に従う |
| N = 0 (ドキュメント未登録) | 空の結果配列を返す (エラーなし) | 転置インデックスが空 → ポスティングなし |
| df(qi) = 0 (クエリトークンがどのドキュメントにも出現しない) | 該当トークンの IDF 項をスキップ | ポスティングリストが空なのでスコア加算なし |
| df(qi) = N (全ドキュメントに出現するトークン) | IDF = ln(0.5/N+0.5 + 1) ≈ ln(2) ≈ 0.693 | IDF が正値を保ちスコアに寄与するが影響は小さい |
| tf が極端に大きい (tf = 1000) | k1 パラメータにより飽和 | TF 飽和: `tf*(k1+1)/(tf+k1*...) → k1+1` に漸近 |
| avgdl = 0 (全ドキュメントのトークン数が 0) | ゼロ除算を回避: avgdl = max(avgdl, 1.0) とする | 安全な下限値を設定 |
| テキスト長 > 65,536 bytes | `InvalidParameter` エラー | セクション 7.4 の入力制限に従う |
| トークン数 > 1,000 / ドキュメント | 上限超過分を切り捨て (警告ログ) | セクション 7.4 の入力制限に従う |

---

## 6. Burst Job 構造

### 6.1 BM25SearchJob

```csharp
/// <summary>
/// BM25 スコアリングによる全文検索 Job。
/// クエリトークンのハッシュ列を入力とし、全ドキュメントの BM25 スコアを計算して
/// Top-K の検索結果を返す。
/// </summary>
[BurstCompile]
public struct BM25SearchJob : IJob
{
    /// <summary>クエリをトークナイズしハッシュ化した配列</summary>
    [ReadOnly] public NativeArray<uint> QueryTokenHashes;

    /// <summary>転置インデックス (tokenHash → Posting リスト)</summary>
    [ReadOnly] public NativeParallelMultiHashMap<uint, BM25Posting> InvertedIndex;

    /// <summary>Document Frequency (tokenHash → DF)</summary>
    [ReadOnly] public NativeParallelHashMap<uint, int> DocumentFrequency;

    /// <summary>各ドキュメントのトークン数</summary>
    [ReadOnly] public NativeArray<int> DocumentLengths;

    /// <summary>検索結果の出力先 (Top-K)</summary>
    public NativeArray<SearchResult> Results;

    /// <summary>返却する上位結果数</summary>
    public int K;

    /// <summary>総ドキュメント数</summary>
    public int TotalDocuments;

    /// <summary>平均文書長</summary>
    public float AverageDocumentLength;

    /// <summary>TF 飽和パラメータ (デフォルト: 1.2)</summary>
    public float K1;

    /// <summary>文書長正規化パラメータ (デフォルト: 0.75)</summary>
    public float B;
}
```

### 6.2 実行フロー

```
1. クエリトークンごとに IDF を計算
2. 各クエリトークンの Posting リストを走査し、ドキュメントごとにスコアを累積
3. 全ドキュメントのスコアを NativeArray<float> に格納
4. Top-K を選択して Results に書き込み
```

### 6.3 Top-K 選択

K が小さい場合 (一般的なユースケースでは K <= 100)、
線形スキャンで最小スコアを追跡しながら Top-K を維持するアプローチを採用する。
ヒープベースの選択は K が大きい場合に有効だが、
NativeContainer でのヒープ実装は複雑になるため初期実装では線形スキャンとする。

### 6.4 SearchResult 構造体

検索結果は Core 層で定義される共通の `SearchResult` を使用する
([02-core-design.md](./02-core-design.md) 参照):

```csharp
public struct SearchResult
{
    public int InternalId;
    public float Score;
}
```

---

## 7. インデクシングフロー

### 7.1 ドキュメント追加

```
1. テキストを NativeArray<byte> (UTF-8) として受け取る
2. トークナイザで UTF-8 バイト列をトークンに分割
   - ASCII: 空白分割 + 小文字化
   - CJK / ひらがな / カタカナ: ユニグラム + バイグラム
3. 各トークンを xxHash3 でハッシュ化 → uint
4. 転置インデックスに Posting (InternalId, TermFrequency) を追加
5. DocumentFrequency を更新 (新規トークンは +1、既存トークンも +1)
6. DocumentLengths にトークン数を記録
7. TotalDocuments をインクリメント
8. AverageDocumentLength を再計算
```

### 7.2 ドキュメント削除

```
1. 削除対象ドキュメントの InternalId を取得
2. 転置インデックスから該当 InternalId の Posting を全削除
3. DocumentFrequency を減算 (DF が 0 になったトークンはエントリ削除)
4. DocumentLengths を 0 にリセット (配列の再配置は行わない)
5. TotalDocuments をデクリメント
6. AverageDocumentLength を再計算
```

> **重要: NativeParallelMultiHashMap の部分削除制約**
>
> `NativeParallelMultiHashMap` は特定の Key-Value ペアの直接削除をサポートしていない。
> この制約に対応するため、BM25 のドキュメント削除は **ソフト削除 + バッチ再構築** 方式を採用する。
>
> 1. **ソフト削除**: 削除対象の InternalId を削除済みセット (`NativeParallelHashSet<int>`) に記録
> 2. **検索時フィルタ**: BM25 スコア計算時に削除済みドキュメントをスキップ
> 3. **統計値の遅延更新**: `TotalDocuments` と `AverageDocumentLength` は再構築時に再計算
> 4. **バッチ再構築**: 削除率が閾値を超えた場合、有効ドキュメントのみでインデックスを完全再構築
>
> この方式は Sparse ベクトル検索 ([04-sparse-design.md](./04-sparse-design.md)) および
> HNSW ([03-hnsw-design.md](./03-hnsw-design.md)) のソフト削除方針と統一される。

### 7.3 ドキュメント更新

更新はドキュメント削除 + 再追加として実装する (in-place 更新は転置インデックスの
整合性維持が複雑なため)。

### 7.4 入力制限

インデックス肥大化と DoS 攻撃を防止するため、以下の入力制限を設ける:

| パラメータ | 上限 | 超過時の動作 |
|---|---|---|
| テキスト長 (UTF-8 bytes) | 65,536 (64 KB) | `InvalidParameter` エラー |
| トークン数 / ドキュメント | 1,000 | 上限超過分は切り捨て（警告ログ出力） |
| ユニークトークン数 / ドキュメント | 500 | 上限超過分は切り捨て |

これらの上限はターゲット規模（~50,000 ドキュメント）における合理的な値であり、
`BM25Config` で変更可能とする。

セキュリティ面での考慮については [11-security-guidelines.md](./11-security-guidelines.md) を参照。

---

## 8. クエリ処理フロー

```
1. クエリテキストを NativeArray<byte> (UTF-8) として受け取る
2. トークナイザでクエリをトークンに分割 (ドキュメント登録時と同一のルール)
3. 各トークンを xxHash3 でハッシュ化
4. BM25SearchJob をスケジュール
5. Job 完了後、Results から Top-K を取得
6. (オプション) Scalar Filter を適用 → フィルタ後の結果を返却
```

スカラーフィルタとの統合については [07-filter-design.md](./07-filter-design.md) を参照。

---

## 9. パフォーマンス考慮事項

### 9.1 メモリアクセスパターン

転置インデックスの走査ではハッシュマップのランダムアクセスが発生するため、
Dense ベクトル検索の SoA レイアウトほどのキャッシュ効率は期待できない。
ただし、クエリトークン数は通常少数 (1~10 程度) であり、
Posting リストの走査がボトルネックとなるケースは限定的である。

### 9.2 転置インデックスのサイズ制御

`NativeParallelMultiHashMap` は内部でバケット配列を管理しており、
エントリ数の増加に応じて自動的にリサイズされる。
初期容量は予想されるトータル Posting 数の 1.5 倍程度に設定し、
頻繁なリサイズを避ける。

### 9.3 CJK バイグラムによるインデックス膨張

CJK テキストではユニグラム + バイグラムにより、
1文字あたり最大2つのトークンが生成される。
これにより ASCII テキストと比較してインデックスサイズが約 2 倍になる。
ターゲット規模 (~50,000 ドキュメント) では許容範囲内だが、
メモリ見積もり (セクション 4.3) では CJK 比率を考慮する必要がある。

#### CJK バイグラムの重複カウント問題

CJK バイグラム生成では、隣接する文字が複数のバイグラムに含まれるため、
1文字が複数のトークンに寄与する。

```
例: "東京都"
ユニグラム: ["東", "京", "都"]
バイグラム: ["東京", "京都"]
→ "京" はユニグラム "京" とバイグラム "東京", "京都" の計3トークンに出現
```

この重複により、CJK テキストのドキュメント長 (`|d|`) が実際の文字数より大きくなり、
BM25 の文書長正規化パラメータ `b` の影響が強く出る可能性がある。

**対策**: CJK 主体のコーパスでは `b` を小さめ (例: 0.5) に設定することで、
文書長ペナルティの過大評価を緩和できる。

---

## 10. 関連ドキュメント

| ドキュメント | 関連内容 |
|---|---|
| [00-project-overview.md](./00-project-overview.md) | プロジェクト全体像・BM25 の位置づけ |
| [01-architecture.md](./01-architecture.md) | Burst/IL2CPP 制約、Allocator 戦略、エラーハンドリング |
| [02-core-design.md](./02-core-design.md) | `SearchResult` 構造体、`Result<T>` エラーハンドリング |
| [03-hnsw-design.md](./03-hnsw-design.md) | Dense ベクトル検索 (ハイブリッド検索での併用先) |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse ベクトル検索 (ハイブリッド検索での併用先) |
| [06-hybrid-rrf-design.md](./06-hybrid-rrf-design.md) | RRF ReRanker による BM25 + Dense + Sparse 結果の統合 |
| [08-persistence-design.md](./08-persistence-design.md) | BM25Index の永続化・復元 |
| [11-security-guidelines.md](./11-security-guidelines.md) | 入力バリデーション・セキュリティ指針 |
| [12-test-plan.md](./12-test-plan.md) | テスト計画 |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット |
