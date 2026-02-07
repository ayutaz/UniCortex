namespace UniCortex.Hybrid
{
    /// <summary>
    /// RRF (Reciprocal Rank Fusion) マージの設定パラメータ。
    /// </summary>
    public struct RrfConfig
    {
        /// <summary>Rank constant k。上位と下位の順位差の影響度を調整する。</summary>
        public float RankConstant;

        /// <summary>Dense ベクトル検索の重み。</summary>
        public float DenseWeight;

        /// <summary>Sparse ベクトル検索の重み。</summary>
        public float SparseWeight;

        /// <summary>BM25 全文検索の重み。</summary>
        public float Bm25Weight;

        /// <summary>デフォルト設定を返す (k=60, 各重み=1.0)。</summary>
        public static RrfConfig Default => new RrfConfig
        {
            RankConstant = 60f,
            DenseWeight = 1.0f,
            SparseWeight = 1.0f,
            Bm25Weight = 1.0f,
        };
    }
}
