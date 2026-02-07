using System.Runtime.InteropServices;

namespace UniCortex.Persistence
{
    /// <summary>
    /// UniCortex インデックスファイルのヘッダ。
    /// 固定長 128 bytes。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FileHeader
    {
        /// <summary>マジックナンバー。0x554E4358 ("UNCX")。</summary>
        public uint MagicNumber;

        /// <summary>メジャーバージョン。</summary>
        public ushort VersionMajor;

        /// <summary>マイナーバージョン。</summary>
        public ushort VersionMinor;

        /// <summary>ベクトル次元数。</summary>
        public int Dimension;

        /// <summary>格納ドキュメント数。</summary>
        public int DocumentCount;

        /// <summary>VectorData セクションのオフセット。</summary>
        public long VectorDataOffset;

        /// <summary>VectorData セクションのサイズ。</summary>
        public long VectorDataSize;

        /// <summary>HnswGraph セクションのオフセット。</summary>
        public long HnswGraphOffset;

        /// <summary>HnswGraph セクションのサイズ。</summary>
        public long HnswGraphSize;

        /// <summary>SparseIndex セクションのオフセット。</summary>
        public long SparseIndexOffset;

        /// <summary>SparseIndex セクションのサイズ。</summary>
        public long SparseIndexSize;

        /// <summary>BM25 インデックスセクションのオフセット。</summary>
        public long Bm25IndexOffset;

        /// <summary>BM25 インデックスセクションのサイズ。</summary>
        public long Bm25IndexSize;

        /// <summary>IdMap セクションのオフセット。</summary>
        public long IdMapOffset;

        /// <summary>IdMap セクションのサイズ。</summary>
        public long IdMapSize;

        /// <summary>CRC32 チェックサム。</summary>
        public uint Checksum;

        /// <summary>パディング (128 bytes に合わせる)。</summary>
        public unsafe fixed byte Reserved[128 - 4 - 2 - 2 - 4 - 4 - 6 * 16 - 4];

        /// <summary>マジックナンバー定数。</summary>
        public const uint ExpectedMagic = 0x554E4358;

        /// <summary>現在のメジャーバージョン。</summary>
        public const ushort CurrentVersionMajor = 1;

        /// <summary>現在のマイナーバージョン。</summary>
        public const ushort CurrentVersionMinor = 0;

        /// <summary>ヘッダサイズ。</summary>
        public const int HeaderSize = 128;
    }
}
