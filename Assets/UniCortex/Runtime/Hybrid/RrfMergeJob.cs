using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UniCortex.Hybrid
{
    /// <summary>
    /// RRF (Reciprocal Rank Fusion) マージ Job。
    /// 複数のサブ検索結果をランクベースで統合し、Top-K を選出する。
    /// </summary>
    [BurstCompile]
    public struct RrfMergeJob : IJob
    {
        [ReadOnly] public NativeArray<SearchResult> DenseResults;
        [ReadOnly] public NativeArray<SearchResult> SparseResults;
        [ReadOnly] public NativeArray<SearchResult> Bm25Results;

        public NativeArray<SearchResult> MergedResults;
        public RrfConfig Config;
        public int K;

        /// <summary>Dense の有効結果数。</summary>
        public int DenseCount;

        /// <summary>Sparse の有効結果数。</summary>
        public int SparseCount;

        /// <summary>BM25 の有効結果数。</summary>
        public int Bm25Count;

        public void Execute()
        {
            int totalEstimate = DenseCount + SparseCount + Bm25Count;
            if (totalEstimate <= 0)
            {
                // 結果なし: MergedResults は長さ 0 のまま
                return;
            }

            var scoreMap = new NativeParallelHashMap<int, float>(
                math.max(totalEstimate, 4), Allocator.TempJob);

            // Dense 結果を走査
            AccumulateScores(ref scoreMap, DenseResults, DenseCount, Config.DenseWeight, Config.RankConstant);

            // Sparse 結果を走査
            AccumulateScores(ref scoreMap, SparseResults, SparseCount, Config.SparseWeight, Config.RankConstant);

            // BM25 結果を走査
            AccumulateScores(ref scoreMap, Bm25Results, Bm25Count, Config.Bm25Weight, Config.RankConstant);

            // Top-K 選択 (RRF スコアが大きいほど上位 → MinHeap で下位を押し出す)
            // NativeMinHeap は Score 昇順なので、RRF スコアを負値に変換して格納する
            int heapSize = math.min(K, scoreMap.Count());
            var heap = new NativeMinHeap(heapSize, Allocator.TempJob);

            var enumerator = scoreMap.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var kv = enumerator.Current;
                // 負値変換: RRF スコアが大きいほど Score が小さくなる → MinHeap の先頭が最上位
                heap.Push(new SearchResult
                {
                    InternalId = kv.Key,
                    Score = -kv.Value
                });
            }
            enumerator.Dispose();

            // ヒープからスコア昇順（= RRF 降順）で取り出し
            int resultCount = math.min(heap.Count, MergedResults.Length);
            for (int i = 0; i < resultCount; i++)
            {
                var item = heap.Pop();
                // 負値を戻す（RRF スコアのまま返す）
                MergedResults[i] = new SearchResult
                {
                    InternalId = item.InternalId,
                    Score = -item.Score
                };
            }

            heap.Dispose();
            scoreMap.Dispose();
        }

        static void AccumulateScores(
            ref NativeParallelHashMap<int, float> scoreMap,
            NativeArray<SearchResult> results,
            int count,
            float weight,
            float rankConstant)
        {
            if (weight <= 0f || count <= 0) return;

            for (int rank = 0; rank < count; rank++)
            {
                int docId = results[rank].InternalId;
                float rrfScore = weight / (rankConstant + (rank + 1));

                // Burst 互換: TryGetValue + Remove + Add パターン
                if (scoreMap.TryGetValue(docId, out float existing))
                {
                    scoreMap.Remove(docId);
                    scoreMap.Add(docId, existing + rrfScore);
                }
                else
                {
                    scoreMap.Add(docId, rrfScore);
                }
            }
        }
    }
}
