using Unity.Collections;
using UniCortex.Sparse;

namespace UniCortex.Hybrid
{
    /// <summary>
    /// ハイブリッド検索のリクエストパラメータ。
    /// </summary>
    public struct HybridSearchParams
    {
        /// <summary>Dense 検索用クエリベクトル。使わない場合は default。</summary>
        public NativeArray<float> DenseQuery;

        /// <summary>Sparse 検索用クエリベクトル。使わない場合は default。</summary>
        public NativeArray<SparseElement> SparseQuery;

        /// <summary>BM25 用テキストクエリ (UTF-8 バイト列)。使わない場合は default。</summary>
        public NativeArray<byte> TextQuery;

        /// <summary>最終返却件数。</summary>
        public int K;

        /// <summary>各サブ検索の取得件数。K より大きくすることで RRF の精度が向上する。</summary>
        public int SubSearchK;

        /// <summary>RRF マージ設定。</summary>
        public RrfConfig RrfConfig;

        /// <summary>Dense 検索パラメータ (efSearch 等)。</summary>
        public SearchParams DenseParams;
    }
}
