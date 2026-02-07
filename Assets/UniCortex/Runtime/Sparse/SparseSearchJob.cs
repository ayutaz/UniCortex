using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace UniCortex.Sparse
{
    /// <summary>
    /// Sparse ベクトル検索を実行する Burst Job。
    /// DAAT アルゴリズムにより転置インデックスを走査し、Top-K を返す。
    /// </summary>
    [BurstCompile]
    public struct SparseSearchJob : IJob
    {
        /// <summary>クエリのスパースベクトル（非ゼロ要素）。</summary>
        [ReadOnly] public NativeArray<SparseElement> Query;

        /// <summary>転置インデックス（読み取り専用）。</summary>
        [ReadOnly] public NativeParallelMultiHashMap<int, SparsePosting> InvertedIndex;

        /// <summary>ソフト削除済み ID セット（読み取り専用）。</summary>
        [ReadOnly] public NativeParallelHashSet<int> DeletedIds;

        /// <summary>検索結果の出力先。サイズは K。</summary>
        public NativeArray<SearchResult> Results;

        /// <summary>返却する上位件数。</summary>
        public int K;

        public void Execute()
        {
            // スコア累積
            var scores = new NativeParallelHashMap<int, float>(128, Allocator.Temp);

            for (int qi = 0; qi < Query.Length; qi++)
            {
                int queryDim = Query[qi].Index;
                float queryValue = Query[qi].Value;

                if (!InvertedIndex.TryGetFirstValue(queryDim, out SparsePosting posting, out var iterator))
                    continue;

                do
                {
                    if (DeletedIds.Contains(posting.InternalId))
                        continue;

                    float newScore = queryValue * posting.Value;

                    if (scores.TryGetValue(posting.InternalId, out float existing))
                    {
                        scores.Remove(posting.InternalId);
                        scores.Add(posting.InternalId, existing + newScore);
                    }
                    else
                    {
                        scores.Add(posting.InternalId, newScore);
                    }
                } while (InvertedIndex.TryGetNextValue(out posting, ref iterator));
            }

            // Top-K 選択
            var heap = new NativeMaxHeap(K, Allocator.Temp);
            var enumerator = scores.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var kv = enumerator.Current;
                float negScore = -kv.Value;

                if (heap.Count < K)
                {
                    heap.Push(new SearchResult { InternalId = kv.Key, Score = negScore });
                }
                else if (negScore < heap.Peek().Score)
                {
                    heap.Pop();
                    heap.Push(new SearchResult { InternalId = kv.Key, Score = negScore });
                }
            }
            enumerator.Dispose();

            // 結果をスコア昇順で書き込み
            int resultCount = heap.Count;
            for (int i = resultCount - 1; i >= 0; i--)
            {
                Results[i] = heap.Pop();
            }
            // 残りをクリア
            for (int i = resultCount; i < Results.Length; i++)
            {
                Results[i] = default;
            }

            heap.Dispose();
            scores.Dispose();
        }
    }
}
