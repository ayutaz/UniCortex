using Unity.Mathematics;

namespace UniCortex.Hnsw
{
    /// <summary>
    /// HNSW パラメータ設定。
    /// </summary>
    public struct HnswConfig
    {
        /// <summary>Layer 1 以上の最大接続数。</summary>
        public int M;

        /// <summary>Layer 0 の最大接続数 (通常 2*M)。</summary>
        public int M0;

        /// <summary>構築時の探索幅。</summary>
        public int EfConstruction;

        /// <summary>レイヤー選択の確率パラメータ 1/ln(M)。</summary>
        public float ML;

        /// <summary>デフォルト設定を返す。</summary>
        public static HnswConfig Default => new HnswConfig
        {
            M = 16,
            M0 = 32,
            EfConstruction = 200,
            ML = 1.0f / math.log(16f)
        };

        /// <summary>
        /// ノード数から理論上の最大レイヤーを計算する。
        /// </summary>
        public static int CalculateMaxLevel(int nodeCount, float mL)
        {
            if (nodeCount <= 1) return 0;
            return (int)math.floor(math.log(nodeCount) * mL) + 2; // 安全マージン
        }
    }
}
