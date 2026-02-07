namespace UniCortex.Sparse
{
    /// <summary>
    /// スパースベクトルの非ゼロ要素を表す。
    /// </summary>
    public struct SparseElement
    {
        /// <summary>次元インデックス（トークン ID 等）。</summary>
        public int Index;

        /// <summary>その次元の重み（SPLADE 等のモデル出力値）。</summary>
        public float Value;
    }

    /// <summary>
    /// 転置インデックスのポスティングエントリ。
    /// </summary>
    public struct SparsePosting
    {
        /// <summary>ドキュメントの内部 ID。</summary>
        public int InternalId;

        /// <summary>この次元におけるドキュメントの重み値。</summary>
        public float Value;
    }
}
