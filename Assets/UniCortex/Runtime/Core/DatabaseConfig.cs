using UniCortex.Hnsw;

namespace UniCortex
{
    /// <summary>
    /// UniCortexDatabase の設定パラメータ。
    /// </summary>
    public struct DatabaseConfig
    {
        /// <summary>最大ドキュメント数。</summary>
        public int Capacity;

        /// <summary>Dense ベクトルの次元数。0 の場合は Dense 検索を無効化。</summary>
        public int Dimension;

        /// <summary>HNSW パラメータ。</summary>
        public HnswConfig HnswConfig;

        /// <summary>Dense ベクトルの距離関数。</summary>
        public DistanceType DistanceType;

        /// <summary>BM25 パラメータ k1。</summary>
        public float BM25K1;

        /// <summary>BM25 パラメータ b。</summary>
        public float BM25B;

        /// <summary>デフォルト設定を返す。</summary>
        public static DatabaseConfig Default => new DatabaseConfig
        {
            Capacity = 10000,
            Dimension = 128,
            HnswConfig = HnswConfig.Default,
            DistanceType = DistanceType.EuclideanSq,
            BM25K1 = 1.2f,
            BM25B = 0.75f,
        };
    }
}
