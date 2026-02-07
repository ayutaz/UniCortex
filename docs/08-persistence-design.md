# 永続化 設計書

## 1. 概要

UniCortex の永続化レイヤーは、インデックスデータをディスクに保存し、起動時に高速ロードを実現する。
プラットフォームごとの制約に応じて異なるバックエンドを使い分ける。

- **デスクトップ / モバイル**: `System.IO.MemoryMappedFiles` による遅延読み込み（ページキャッシュ活用）
- **WebGL**: `ByteArray` + IndexedDB によるフォールバック（ファイルシステム非対応のため）
- **PersistenceFactory**: プリプロセッサディレクティブでプラットフォームを判定し、適切なバックエンドを自動選択

アーキテクチャ全体における位置づけは [01-architecture.md](./01-architecture.md) のコンポーネント構成図を参照。
プラットフォーム固有の技術制約については [10-technical-constraints.md](./10-technical-constraints.md) を参照。

---

## 2. シリアライゼーションヘッダ

すべてのインデックスファイル (`.ucx`) は固定長の `FileHeader` から始まる。
ヘッダにはファイルの整合性検証情報と、各セクションへのオフセット・サイズが含まれる。

```csharp
/// <summary>
/// UniCortex インデックスファイルのヘッダ。
/// 固定長 128 bytes。先頭にマジックナンバーとバージョンを持ち、
/// 各セクションの位置とサイズを記録する。
/// </summary>
public struct FileHeader
{
    /// <summary>マジックナンバー。0x554E4358 ("UNCX")。</summary>
    public uint MagicNumber;       // 0x554E4358 ("UNCX")

    /// <summary>メジャーバージョン。後方互換性なしの変更時にインクリメント。</summary>
    public ushort VersionMajor;    // 1

    /// <summary>マイナーバージョン。後方互換ありの変更時にインクリメント。</summary>
    public ushort VersionMinor;    // 0

    /// <summary>ベクトル次元数。</summary>
    public int Dimension;

    /// <summary>格納ドキュメント数。</summary>
    public int DocumentCount;

    /// <summary>VectorData セクションのファイル先頭からのオフセット (bytes)。</summary>
    public long VectorDataOffset;

    /// <summary>VectorData セクションのサイズ (bytes)。</summary>
    public long VectorDataSize;

    /// <summary>HnswGraph セクションのファイル先頭からのオフセット (bytes)。</summary>
    public long HnswGraphOffset;

    /// <summary>HnswGraph セクションのサイズ (bytes)。</summary>
    public long HnswGraphSize;

    /// <summary>SparseIndex セクションのファイル先頭からのオフセット (bytes)。</summary>
    public long SparseIndexOffset;

    /// <summary>SparseIndex セクションのサイズ (bytes)。</summary>
    public long SparseIndexSize;

    /// <summary>BM25 インデックスセクションのファイル先頭からのオフセット (bytes)。</summary>
    public long Bm25IndexOffset;

    /// <summary>BM25 インデックスセクションのサイズ (bytes)。</summary>
    public long Bm25IndexSize;

    /// <summary>Metadata セクションのファイル先頭からのオフセット (bytes)。</summary>
    public long MetadataOffset;

    /// <summary>Metadata セクションのサイズ (bytes)。</summary>
    public long MetadataSize;

    /// <summary>IdMap セクションのファイル先頭からのオフセット (bytes)。</summary>
    public long IdMapOffset;

    /// <summary>IdMap セクションのサイズ (bytes)。</summary>
    public long IdMapSize;

    /// <summary>ヘッダ以降の全データに対する CRC32 チェックサム。</summary>
    public uint Checksum;          // CRC32
}
```

### ヘッダフィールドの設計判断

| フィールド | 型 | 説明 |
|---|---|---|
| `MagicNumber` | `uint` | ファイル識別子。誤ったファイルの読み込みを即座に検出 |
| `VersionMajor` / `VersionMinor` | `ushort` | バージョニング戦略に基づく互換性チェック |
| `*Offset` / `*Size` | `long` | 各セクションの位置。64-bit で大規模ファイルにも対応 |
| `Checksum` | `uint` | CRC32 によるデータ破損検出 |

ヘッダは 128 bytes に固定する。将来のフィールド追加に備えて末尾にパディングを確保する。

### バイナリヘッダのバイト列例（テストフィクスチャ用）

以下は `DocumentCount = 3, Dimension = 4` の最小インデックスファイルのヘッダ例（リトルエンディアン）。
テストではこのバイト列を基に破損検証・パース検証のフィクスチャを生成する。

```
Offset  Field               Hex bytes (little-endian)      Value
------  ------------------  ----------------------------   -----
0x00    MagicNumber         58 43 4E 55                    0x554E4358 ("UNCX")
0x04    VersionMajor        01 00                          1
0x06    VersionMinor        00 00                          0
0x08    Dimension           04 00 00 00                    4
0x0C    DocumentCount       03 00 00 00                    3
0x10    VectorDataOffset    80 00 00 00 00 00 00 00        128 (= sizeof(FileHeader))
0x18    VectorDataSize      30 00 00 00 00 00 00 00        48 (= 3 * 4 * sizeof(float))
0x20    HnswGraphOffset     B0 00 00 00 00 00 00 00        176 (= 128 + 48)
0x28    HnswGraphSize       xx xx xx xx xx xx xx xx        (可変)
...     (以下同様に各セクション)
0x7C    Checksum            xx xx xx xx                    CRC32 (ヘッダ以降の全データ)
```

テスト検証に使用する具体的なアサーション:

| テスト | バイト操作 | 期待結果 |
|---|---|---|
| 正常ヘッダ | 上記バイト列をそのまま Load | 成功 |
| 不正マジックナンバー | offset 0x00 を `00 00 00 00` に書き換え | `InvalidFileFormat` |
| バージョン不一致 | offset 0x04 を `02 00` に書き換え | `IncompatibleVersion` |
| CRC32 不一致 | offset 0x7C を `00 00 00 00` に書き換え | `DataCorrupted` |
| ファイル切り詰め | 先頭 64 bytes のみのファイル | `DataCorrupted` (ヘッダ不完全) |
| DocumentCount 超過 | offset 0x0C を `40 42 0F 00` (1,000,000) に書き換え | 成功 (上限内) |
| DocumentCount 超過 | offset 0x0C を `41 42 0F 00` (1,000,001) に書き換え | `InvalidParameter` |

### CRC32 の限界

> **重要**: CRC32 は**誤り検出**のみを提供し、**認証（改ざん検知）は提供しない**。
>
> - CRC32 は線形関数であるため、攻撃者がデータを意図的に改ざんし、CRC32 値を調整して一致させることが可能である
> - UniCortex のインデックスファイルは信頼されたストレージから読み込むことを前提とする
> - ネットワーク経由で受信したインデックスファイルを直接ロードする場合は、アプリケーション側で HMAC-SHA256 等の暗号学的ハッシュによる検証を追加すべきである
> - 将来のファイルフォーマット v2 で、オプショナルな HMAC フィールドの追加を検討する

---

## 3. ファイルフォーマット

インデックスファイル (`.ucx`) は以下のセクションが連続して配置される。

```
┌──────────────────────────────────┐
│  FileHeader (128 bytes)          │  固定長ヘッダ
├──────────────────────────────────┤
│  VectorData Section              │  SoA flat array の生バイト列
├──────────────────────────────────┤
│  HnswGraph Section               │  ノードメタデータ + 隣接リスト
├──────────────────────────────────┤
│  SparseIndex Section             │  転置インデックス (次元 → ポスティング)
├──────────────────────────────────┤
│  BM25Index Section               │  転置インデックス + DF + DocLengths
├──────────────────────────────────┤
│  Metadata Section                │  カラムナストレージ
├──────────────────────────────────┤
│  IdMap Section                   │  外部 ID ↔ 内部 ID マッピング
└──────────────────────────────────┘
```

### エンディアン制御

UniCortex のインデックスファイルは **リトルエンディアン** を前提とする。

- Unity がサポートする全プラットフォーム (Windows, macOS, Linux, Android, iOS, WebGL) はリトルエンディアンである
- `NativeArray<T>.Reinterpret<byte>()` によるバイト列変換はネイティブエンディアンを使用するため、リトルエンディアン環境間ではバイトスワップ不要
- 異なるエンディアンのプラットフォームをサポートする場合は、ファイルフォーマット v2 でエンディアンフィールドの追加を検討する

### 3.1 VectorData Section

Dense ベクトルを SoA (Structure of Arrays) レイアウトでフラットに格納する。
メモリ上の `NativeArray<float>` をそのままバイト列として書き出す。

```
[VectorData Section]
├── Dim0: float[DocumentCount]    // 第 0 次元の全ドキュメント値
├── Dim1: float[DocumentCount]    // 第 1 次元の全ドキュメント値
├── ...
└── DimN: float[DocumentCount]    // 第 N-1 次元の全ドキュメント値

サイズ = Dimension * DocumentCount * sizeof(float)
```

SoA レイアウトの詳細は [01-architecture.md](./01-architecture.md) の「4.1 ベクトルデータ - SoA」を参照。

### 3.2 HnswGraph Section

HNSW グラフのノードメタデータと隣接リストを格納する。
各ノードは固定長のメタデータ部分と可変長の隣接リスト部分で構成される。

```
[HnswGraph Section]
├── MaxLevel: int                     // グラフの最大レベル
├── EntryPointId: int                 // エントリポイントの内部 ID
├── M: int                            // 最大隣接数パラメータ
├── NodeCount: int                    // ノード数
├── NodeMeta[NodeCount]:              // ノードメタデータ配列
│   ├── Level: int                    //   このノードの最高レベル
│   └── NeighborOffsets[Level+1]: int //   各レベルの隣接リスト開始位置
└── NeighborData[]:                   // 隣接リスト (flat array)
    └── int[]                         //   隣接ノード ID の列
```

- ノードメタデータは AoS (Array of Structures) で格納し、ノード単位の読み出しに最適化
- 隣接リストは全レベル分を1つのフラット配列にまとめ、オフセットで参照

### 3.3 SparseIndex Section

Sparse ベクトル検索の転置インデックスをシリアライズする。
`NativeParallelMultiHashMap<int, SparsePosting>` をソート済みリストに変換して格納する。

```
[SparseIndex Section]
├── DimensionCount: int                       // 次元数（ユニークな次元 Index の数）
├── TotalPostings: int                        // 総ポスティング数
├── DimensionEntries[DimensionCount]:         // 次元エントリ配列
│   ├── DimensionIndex: int                   //   次元 Index
│   ├── PostingOffset: int                    //   PostingData 内の開始位置
│   └── PostingCount: int                     //   この次元のポスティング数
└── PostingData[TotalPostings]:               // ポスティングデータ (flat array)
    ├── InternalId: int                       //   ドキュメントの内部 ID
    └── Value: float                          //   この次元の重み値
```

Sparse ベクトル検索の詳細は [04-sparse-design.md](./04-sparse-design.md) を参照。

### 3.4 BM25Index Section

BM25 全文検索の転置インデックス、Document Frequency、ドキュメント長を格納する。

```
[BM25Index Section]
├── TermCount: int                            // ユニークターム数
├── TotalPostings: int                        // 総ポスティング数
├── AverageDocLength: float                   // 平均ドキュメント長
├── TermEntries[TermCount]:                   // タームエントリ配列
│   ├── TermHash: int                         //   ターム のハッシュ値
│   ├── DocumentFrequency: int                //   このタームを含むドキュメント数
│   ├── PostingOffset: int                    //   PostingData 内の開始位置
│   └── PostingCount: int                     //   このタームのポスティング数
├── PostingData[TotalPostings]:               // ポスティングデータ (flat array)
│   ├── InternalId: int                       //   ドキュメントの内部 ID
│   └── TermFrequency: int                    //   このドキュメント内の出現回数
└── DocLengths[DocumentCount]: int            // 各ドキュメントのトークン数
```

### 3.5 Metadata Section

スカラーフィルタ用のメタデータをカラムナ (列指向) ストレージで格納する。
列ごとに同一型のデータが連続するため、フィルタ評価時のキャッシュ効率が高い。

```
[Metadata Section]
├── ColumnCount: int                          // カラム数
├── ColumnHeaders[ColumnCount]:               // カラムヘッダ配列
│   ├── NameHash: int                         //   カラム名のハッシュ値
│   ├── DataType: byte                        //   型識別子 (0=int, 1=float, 2=bool)
│   ├── DataOffset: long                      //   データ本体の開始位置
│   └── DataSize: long                        //   データ本体のサイズ
└── ColumnData[]:                             // 各カラムのデータ本体
    └── T[DocumentCount]                      //   型 T の値が DocumentCount 分連続
```

### 3.6 IdMap Section

外部 ID (ユーザー指定) と内部 ID (0-indexed 連番) のマッピングを格納する。

```
[IdMap Section]
├── EntryCount: int                           // エントリ数
└── Entries[EntryCount]:                      // マッピングエントリ配列
    ├── ExternalId: long                      //   外部 ID
    └── InternalId: int                       //   内部 ID
```

---

## 4. MemoryMappedFile 永続化 (デスクトップ / モバイル)

デスクトップおよびモバイルプラットフォームでは `System.IO.MemoryMappedFiles` を使用し、
必要なセクションだけをメモリにマップする遅延読み込みを行う。

```csharp
/// <summary>
/// MemoryMappedFile を用いた永続化バックエンド。
/// デスクトップおよびモバイルプラットフォームで使用する。
/// </summary>
public struct MmapPersistence : IPersistence
{
    /// <summary>
    /// インデックスデータをファイルに保存する。
    /// 各コンポーネントから NativeArray を取得し、FileHeader と共に書き出す。
    /// </summary>
    public void Save(string path, UniCortexDatabase db)
    {
        // 1. 各コンポーネントのデータを NativeArray として取得
        // 2. 各セクションのサイズを算出し FileHeader を構築
        // 3. MemoryMappedFile を作成 (総ファイルサイズを指定)
        // 4. MemoryMappedViewAccessor で各セクションを書き込み
        //    accessor.Write<T>(offset, ref value) で構造体を直接書き込み
        //    accessor.WriteArray(offset, array, 0, length) で NativeArray を書き込み
        // 5. CRC32 チェックサムを計算してヘッダに書き込み
    }

    /// <summary>
    /// ファイルからインデックスデータを読み込む。
    /// MemoryMappedFile で遅延マッピングし、必要なセクションのみ読み出す。
    /// </summary>
    public UniCortexDatabase Load(string path)
    {
        // 1. MemoryMappedFile.CreateFromFile(path) でファイルをマップ
        // 2. MemoryMappedViewAccessor で FileHeader を読み取り
        // 3. MagicNumber (0x554E4358) を検証
        // 4. VersionMajor / VersionMinor を検証
        // 5. CRC32 チェックサムを検証
        // 6. 各セクションのオフセットから NativeArray を復元
        //    accessor.ReadArray(offset, array, 0, length) で読み出し
        // 7. 各コンポーネントを再構築して返却
    }
}
```

### 利点

- **OS ページキャッシュの活用**: MemoryMappedFile は OS のページキャッシュ機構を利用するため、
  頻繁にアクセスされるセクションは自動的にメモリに保持される
- **物理メモリ以上のデータ対応**: 仮想メモリマッピングにより、物理メモリを超えるファイルも扱える
  （ページフォルト時に OS がディスクから読み出し）
- **起動時の高速ロード**: ファイル全体をコピーせず、必要なページだけをオンデマンドで読み込む
- **NativeArray との相性**: `MemoryMappedViewAccessor.ReadArray` / `WriteArray` で
  NativeArray とのデータ転送が効率的に行える

### 注意点

- **WebGL では使用不可**: WebGL 環境にはファイルシステムが存在しないため、
  MemoryMappedFile は利用できない（セクション 5 のフォールバックを使用）
- **モバイルのパス**: モバイルでは `Application.persistentDataPath` をベースパスとして使用する。
  `Application.dataPath` は読み取り専用のため使用しない
- **ファイルロック**: MemoryMappedFile はファイルをロックするため、
  Save 中に別プロセスが同ファイルを開けない。書き込みは一時ファイルに行い、
  完了後にアトミックリネームする

### プラットフォーム別パス解決

```csharp
/// <summary>
/// プラットフォームに応じたインデックスファイルパスを返す。
/// </summary>
public static string GetDefaultPath(string fileName)
{
    // Application.persistentDataPath はプラットフォームごとに適切なパスを返す:
    // - Windows: %USERPROFILE%/AppData/LocalLow/<company>/<product>
    // - macOS: ~/Library/Application Support/<company>/<product>
    // - Android: /data/data/<package>/files
    // - iOS: /var/mobile/Containers/Data/Application/<guid>/Documents
    return Path.Combine(Application.persistentDataPath, fileName);
}
```

---

## 5. WebGL フォールバック (ByteArray + IndexedDB)

WebGL 環境ではファイルシステムが利用できないため、
バイト配列へのシリアライズと JavaScript interop による IndexedDB 保存でフォールバックする。

```csharp
/// <summary>
/// WebGL 向け永続化バックエンド。
/// NativeArray をバイト配列にシリアライズし、IndexedDB に保存する。
/// </summary>
public struct WebGlPersistence : IPersistence
{
    /// <summary>
    /// インデックスデータを IndexedDB に保存する。
    /// </summary>
    public void Save(string key, UniCortexDatabase db)
    {
        // 1. 各コンポーネントのデータを NativeArray として取得
        // 2. FileHeader を構築
        // 3. 全セクションを byte[] にシリアライズ
        //    - NativeArray<T>.Reinterpret<byte>() でバイト列に変換
        //    - byte[] にコピー
        // 4. JavaScript interop で IndexedDB に保存
    }

    /// <summary>
    /// IndexedDB からインデックスデータを読み込む。
    /// </summary>
    public UniCortexDatabase Load(string key)
    {
        // 1. JavaScript interop で IndexedDB からバイト配列を読み出し
        // 2. byte[] から FileHeader を復元
        // 3. MagicNumber / Version / Checksum を検証
        // 4. 各セクションのオフセットから NativeArray を復元
        // 5. 各コンポーネントを再構築して返却
    }
}
```

### JavaScript Interop (jslib プラグイン)

WebGL では `.jslib` ファイルを用いて JavaScript 関数を C# から呼び出す。
IndexedDB は非同期 API のため、コールバックで完了を通知する。

```csharp
// C# 側の extern 宣言
[DllImport("__Internal")]
private static extern void UniCortex_SaveToIndexedDB(
    string key, byte[] data, int length, Action<int> callback);

[DllImport("__Internal")]
private static extern void UniCortex_LoadFromIndexedDB(
    string key, byte[] buffer, int maxLength, Action<int> callback);

[DllImport("__Internal")]
private static extern void UniCortex_DeleteFromIndexedDB(
    string key, Action<int> callback);
```

```javascript
// jslib プラグイン (UniCortexPersistence.jslib)
mergeInto(LibraryManager.library, {
    UniCortex_SaveToIndexedDB: function(keyPtr, dataPtr, length, callbackPtr) {
        var key = UTF8ToString(keyPtr);
        var data = new Uint8Array(HEAPU8.buffer, dataPtr, length).slice();
        var request = indexedDB.open("UniCortex", 1);
        request.onupgradeneeded = function(e) {
            e.target.result.createObjectStore("indices");
        };
        request.onsuccess = function(e) {
            var db = e.target.result;
            var tx = db.transaction("indices", "readwrite");
            tx.objectStore("indices").put(data, key);
            tx.oncomplete = function() {
                dynCall_vi(callbackPtr, 0); // success
            };
        };
    },
    // LoadFromIndexedDB, DeleteFromIndexedDB も同様
});
```

### 注意事項

- **jslib は Burst 非互換**: 永続化レイヤーはマネージドコードで実装する。
  インデックスの内部処理（検索・構築）のみ Burst を使用する
  （[10-technical-constraints.md](./10-technical-constraints.md) 参照）
- **非同期処理**: IndexedDB は非同期のため、Save/Load はコールバックまたは
  Coroutine でラップして完了を待つ
- **データサイズ制限**: ブラウザごとに IndexedDB のストレージクォータが異なる。
  50,000 ベクトル x 128 次元のケースでは約 25 MB + インデックスデータとなり、
  一般的なクォータ内に収まるが、大規模データではクォータ超過に注意する
- **メモリコピーのオーバーヘッド**: `NativeArray` → `byte[]` → IndexedDB の間で
  メモリコピーが発生する。デスクトップの MemoryMappedFile と比較してオーバーヘッドが大きい

### IndexedDB クォータ超過ハンドリング

ブラウザの IndexedDB にはストレージクォータ制限がある。クォータ超過時の対処:

1. **エラー検出**: IndexedDB の `QuotaExceededError` を JavaScript 側でキャッチし、C# に `StorageQuotaExceeded` エラーコードで通知する
2. **ユーザー通知**: アプリケーション層でユーザーにストレージ不足を通知し、以下の対処を案内する:
   - 不要なインデックスの削除 (`UniCortex_DeleteFromIndexedDB`)
   - ドキュメント数の削減
   - ブラウザのストレージ設定の確認
3. **リトライ不要**: クォータ超過はリトライで解決しないため、即座にエラーを返す
4. **見積もり**: `navigator.storage.estimate()` でクォータの残量を事前確認可能（JavaScript interop 経由）

---

## 6. PersistenceFactory プラットフォーム切替

プリプロセッサディレクティブを用いて、ビルドターゲットに応じた永続化バックエンドを自動選択する。

```csharp
/// <summary>
/// プラットフォームに応じた IPersistence 実装を生成するファクトリ。
/// </summary>
public static class PersistenceFactory
{
    /// <summary>
    /// 現在のプラットフォームに最適な IPersistence インスタンスを返す。
    /// </summary>
    public static IPersistence Create()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new WebGlPersistence();
#else
        return new MmapPersistence();
#endif
    }
}
```

### IPersistence インターフェース

```csharp
/// <summary>
/// 永続化バックエンドの共通インターフェース。
/// Burst Job の外でのみ使用する（interface は Burst 非互換のため）。
/// </summary>
public interface IPersistence
{
    /// <summary>
    /// インデックスデータをストレージに保存する。
    /// </summary>
    /// <param name="path">保存先パス (WebGL の場合は IndexedDB のキー)。</param>
    /// <param name="db">シリアライズ対象のデータベース。</param>
    void Save(string path, UniCortexDatabase db);

    /// <summary>
    /// ストレージからインデックスデータを読み込む。
    /// </summary>
    /// <param name="path">読み込み元パス (WebGL の場合は IndexedDB のキー)。</param>
    /// <returns>復元されたデータベース。</returns>
    UniCortexDatabase Load(string path);
}
```

### Burst 互換性に関する注意

`IPersistence` は interface（マネージド型）であり、Burst Job 内では使用できない。
永続化の呼び出しはメインスレッドまたはマネージドコードから行い、
Burst Job 内では具体型 (`MmapPersistence`, `WebGlPersistence`) を直接使う場面はない。

```
┌─────────────────────────────┐
│  メインスレッド (マネージド)  │
│  IPersistence.Save/Load     │  ← interface 呼び出し OK
└──────────┬──────────────────┘
           │ NativeArray で受け渡し
           ▼
┌─────────────────────────────┐
│  Burst Job (アンマネージド)  │
│  HnswSearchJob, etc.        │  ← interface 使用不可
└─────────────────────────────┘
```

---

## 7. シリアライゼーション / デシリアライゼーション手順

### 7.1 Save フロー

```
1. FileHeader を構築
   ├── MagicNumber = 0x554E4358
   ├── VersionMajor = 1, VersionMinor = 0
   ├── Dimension, DocumentCount を設定
   └── 各セクションの Offset / Size は後述のステップで計算

2. 各コンポーネントのデータを NativeArray として取得
   ├── VectorStorage → NativeArray<float> (SoA flat array)
   ├── HnswGraph → ノードメタ + 隣接リスト
   ├── SparseIndex → 転置インデックスをソート済みリストに変換
   ├── BM25Engine → 転置インデックス + DF + DocLengths
   ├── ScalarFilter → カラムナデータ
   └── IdMap → 外部↔内部 ID マッピング

3. 各セクションの Offset / Size を計算
   ├── VectorDataOffset = sizeof(FileHeader) = 128
   ├── HnswGraphOffset = VectorDataOffset + VectorDataSize
   ├── SparseIndexOffset = HnswGraphOffset + HnswGraphSize
   ├── ... (以下同様に連続配置)
   └── FileHeader に書き戻し

4. ファイルに順番に書き出し
   ├── FileHeader (Checksum = 0 の状態で仮書き込み)
   ├── VectorData Section
   ├── HnswGraph Section
   ├── SparseIndex Section
   ├── BM25Index Section
   ├── Metadata Section
   └── IdMap Section

5. CRC32 チェックサムを計算
   ├── ヘッダ以降の全バイト列に対して CRC32 を計算
   └── FileHeader.Checksum にチェックサムを書き込み (先頭にシーク)
```

### 7.2 Load フロー

```
1. FileHeader を読み取り (先頭 128 bytes)

2. マジックナンバーを検証
   └── MagicNumber != 0x554E4358 → エラー (InvalidFileFormat)

3. バージョンを検証
   ├── VersionMajor が現行と不一致 → エラー (IncompatibleVersion)
   └── VersionMinor が現行より大きい → 警告 (新フィールド無視で続行)

4. CRC32 チェックサムを検証
   ├── ヘッダ以降の全バイト列に対して CRC32 を再計算
   └── FileHeader.Checksum と不一致 → エラー (DataCorrupted)

4.5. オフセット・サイズの境界検証
   ├── 各セクションの Offset がファイルサイズ以内であることを検証
   │   └── VectorDataOffset + VectorDataSize <= fileSize
   │   └── HnswGraphOffset + HnswGraphSize <= fileSize
   │   └── ... (全セクション)
   ├── 各セクションの Size が 0 以上であることを検証
   ├── セクション間の重複がないことを検証
   └── 検証失敗時 → エラー (DataCorrupted)

5. 各セクションのオフセットから NativeArray を復元
   ├── VectorData: NativeArray<float> (Allocator.Persistent)
   ├── HnswGraph: ノードメタ + 隣接リストを NativeArray に復元
   ├── SparseIndex: ソート済みリストから NativeParallelMultiHashMap を再構築
   ├── BM25Index: 転置インデックス + DF + DocLengths を復元
   ├── Metadata: カラムナデータを復元
   └── IdMap: 外部↔内部 ID マッピングを NativeHashMap に復元

6. 各コンポーネントを再構築
   ├── VectorStorage に NativeArray<float> をセット
   ├── HnswIndex にグラフ構造をセット
   ├── SparseIndex に転置インデックスをセット
   ├── BM25Engine に転置インデックス + 統計値をセット
   ├── ScalarFilter にカラムナデータをセット
   └── IdMap をセット

7. UniCortexDatabase として返却
```

### 7.4 デシリアライズ安全性ガイドライン

外部から提供されたインデックスファイルを安全にロードするため、以下の検証を実装する:

| 検証項目 | チェック内容 | 失敗時のエラー |
|---|---|---|
| マジックナンバー | `MagicNumber == 0x554E4358` | `InvalidFileFormat` |
| バージョン | `VersionMajor == CurrentVersionMajor` | `IncompatibleVersion` |
| ファイルサイズ | ファイルサイズ >= sizeof(FileHeader) | `DataCorrupted` |
| オフセット範囲 | 全 Offset + Size <= ファイルサイズ | `DataCorrupted` |
| DocumentCount | `0 <= DocumentCount <= 1,000,000` | `InvalidParameter` |
| Dimension | `1 <= Dimension <= 4,096` | `InvalidParameter` |
| VectorData 整合性 | `VectorDataSize == DocumentCount * Dimension * sizeof(float)` | `DataCorrupted` |
| CRC32 | 再計算した CRC32 がヘッダの値と一致 | `DataCorrupted` |

これらの検証は、改ざんまたは破損されたファイルによる OOM (Out of Memory) やメモリ破壊を防止する。
詳細は [11-security-guidelines.md](./11-security-guidelines.md) を参照。

### 7.3 エラーハンドリング

永続化レイヤーはメインスレッド（マネージドコード）で動作するため、
例外ベースのエラーハンドリングも使用可能だが、一貫性のため
[01-architecture.md](./01-architecture.md) の `Result<T>` パターンを優先する。

```csharp
public enum PersistenceErrorCode : byte
{
    None = 0,
    FileNotFound,
    InvalidFileFormat,      // マジックナンバー不一致
    IncompatibleVersion,    // メジャーバージョン不一致
    DataCorrupted,          // CRC32 チェックサム不一致
    IoError,                // ファイル I/O エラー
    StorageQuotaExceeded,   // WebGL IndexedDB クォータ超過
}
```

---

## 8. バージョニング戦略

### メジャーバージョン変更 (VersionMajor)

- **後方互換性なし**: ファイルフォーマットの根本的な変更時にインクリメント
- 旧バージョンのファイルはロード不可 → エラー (`IncompatibleVersion`) を返す
- ユーザーはインデックスを再構築する必要がある

**メジャーバージョン変更の例**:
- セクションの順序変更
- FileHeader の構造変更
- チェックサムアルゴリズムの変更

### マイナーバージョン変更 (VersionMinor)

- **後方互換性あり**: 新フィールドは末尾に追加し、旧バージョンのリーダーは未知フィールドを無視
- 新しいリーダーは古いファイルを問題なく読める（新フィールドにはデフォルト値を使用）
- 古いリーダーは新しいファイルを読めるが、新フィールドは無視される

**マイナーバージョン変更の例**:
- 新しいメタデータカラム型の追加
- セクション内への追加フィールドの末尾付与
- 新しいオプショナルセクションの追加

### バージョン検証の疑似コード

```csharp
Result<FileHeader> ValidateHeader(FileHeader header)
{
    if (header.MagicNumber != 0x554E4358)
        return Result<FileHeader>.Fail(PersistenceErrorCode.InvalidFileFormat);

    if (header.VersionMajor != CurrentVersionMajor)
        return Result<FileHeader>.Fail(PersistenceErrorCode.IncompatibleVersion);

    if (header.VersionMinor > CurrentVersionMinor)
        Debug.LogWarning("File was created with a newer minor version. " +
                         "Some features may be unavailable.");

    return Result<FileHeader>.Ok(header);
}
```

---

## 9. メモリ概算

50,000 ドキュメント、128 次元のケースにおけるファイルサイズの概算。

| セクション | 計算式 | 概算サイズ |
|---|---|---|
| FileHeader | 固定 | 128 bytes |
| VectorData | 128 * 50,000 * 4 | 25.6 MB |
| HnswGraph | 50,000 * (16 + 64 * 4) ※M=16 想定 | ~14.4 MB |
| SparseIndex | 5,000,000 * 8 + overhead | ~45 MB |
| BM25Index | ドキュメント・語彙サイズに依存 | ~20-40 MB |
| Metadata | カラム数・型に依存 | ~1-10 MB |
| IdMap | 50,000 * 12 | ~0.6 MB |
| **合計** | | **~107-136 MB** |

> HnswGraph のサイズは HNSW パラメータ M に依存する。上記は M=16 の想定。
> SparseIndex は平均非ゼロ要素数 100 を想定。
> 詳細は [04-sparse-design.md](./04-sparse-design.md) のメモリ概算を参照。

### Save 時のピークメモリ

Save 処理では、インメモリのインデックスデータに加えて、シリアライズ用のバッファが必要となる。

```
ピークメモリ ≈ 通常使用量 × 2
```

| フェーズ | メモリ使用 |
|---|---|
| 通常運用時 | インデックスデータ（~107-136 MB） |
| Save 実行中 | インデックスデータ + シリアライズバッファ ≈ **214-272 MB** |
| Save 完了後 | インデックスデータのみに戻る |

> **将来の最適化**: インクリメンタル Save（変更されたセクションのみ書き出し）により、
> ピークメモリを削減可能。初期バージョンでは全セクションの書き出しとする。

### モバイル / WebGL での考慮

- **モバイル**: メモリ制約が厳しい環境では、Save 中のピークメモリがデバイスの制限を超過する可能性がある。ユーザーに Save 前のメモリ使用量を確認する API を提供する
- **WebGL**: IndexedDB への書き込み時に `byte[]` へのコピーが追加で発生するため、ピークメモリは約 **2.5x** に増加する

---

## 10. 関連ドキュメント

| ドキュメント | 関連内容 |
|---|---|
| [00-project-overview.md](./00-project-overview.md) | プロジェクト全体概要 |
| [01-architecture.md](./01-architecture.md) | アーキテクチャ概要、メモリレイアウト戦略、永続化フロー図 |
| [02-core-design.md](./02-core-design.md) | 共通データ構造 (Result\<T\>, VectorStorage 等) |
| [03-hnsw-design.md](./03-hnsw-design.md) | HNSW グラフ構造（シリアライズ対象） |
| [04-sparse-design.md](./04-sparse-design.md) | Sparse 転置インデックス（シリアライズ対象） |
| [05-bm25-design.md](./05-bm25-design.md) | BM25 転置インデックス（シリアライズ対象） |
| [10-technical-constraints.md](./10-technical-constraints.md) | Burst/IL2CPP/WebGL の技術制約 |
| [11-security-guidelines.md](./11-security-guidelines.md) | セキュリティガイドライン（デシリアライズ検証） |
| [12-test-plan.md](./12-test-plan.md) | テスト計画（永続化テスト） |
| [13-memory-budget.md](./13-memory-budget.md) | メモリバジェット（Save/Load ピークメモリ） |
