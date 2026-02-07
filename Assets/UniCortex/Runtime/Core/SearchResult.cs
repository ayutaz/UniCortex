using System;

namespace UniCortex
{
    /// <summary>
    /// 距離関数の種類。
    /// </summary>
    public enum DistanceType : byte
    {
        /// <summary>ユークリッド距離の二乗。</summary>
        EuclideanSq,
        /// <summary>コサイン距離 (1 - cosine similarity)。正規化済みベクトル前提。</summary>
        Cosine,
        /// <summary>負の内積 (-dot product)。</summary>
        DotProduct
    }

    /// <summary>
    /// 検索結果の1エントリ。Score 昇順 = 関連度降順。
    /// </summary>
    public struct SearchResult : IComparable<SearchResult>
    {
        /// <summary>内部 ID (VectorStorage / HNSW グラフのインデックス)。</summary>
        public int InternalId;

        /// <summary>
        /// スコア。小さいほど関連度が高い。
        /// Dense: 距離値、Sparse/BM25: 負値変換済みスコア。
        /// </summary>
        public float Score;

        /// <summary>
        /// Score 昇順で比較する。
        /// </summary>
        public int CompareTo(SearchResult other)
        {
            return Score.CompareTo(other.Score);
        }
    }

    /// <summary>
    /// 検索パラメータ。
    /// </summary>
    public struct SearchParams
    {
        /// <summary>返却する上位 K 件数。</summary>
        public int K;

        /// <summary>HNSW 探索時の候補数 (ef_search)。</summary>
        public int EfSearch;

        /// <summary>距離関数の種類。</summary>
        public DistanceType DistanceType;
    }
}
