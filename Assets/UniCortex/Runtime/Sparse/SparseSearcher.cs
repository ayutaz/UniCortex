using Unity.Collections;

namespace UniCortex.Sparse
{
    /// <summary>
    /// DAAT (Document-At-A-Time) 方式のスパースベクトル検索。
    /// </summary>
    public static class SparseSearcher
    {
        /// <summary>
        /// スパースベクトル検索を実行し、Top-K を返す。
        /// スコアは負値変換済み (小さいほど関連度が高い)。
        /// 戻り値の NativeArray は呼び出し元が Dispose する。
        /// </summary>
        public static NativeArray<SearchResult> Search(
            ref SparseIndex index,
            NativeArray<SparseElement> query,
            int k,
            Allocator resultAllocator)
        {
            if (k <= 0 || query.Length == 0)
                return new NativeArray<SearchResult>(0, resultAllocator);

            // スコア累積用 HashMap
            var scores = new NativeParallelHashMap<int, float>(
                index.DocumentCount > 0 ? index.DocumentCount : 64, Allocator.TempJob);

            // DAAT: クエリの各非ゼロ次元について転置インデックスを走査
            for (int qi = 0; qi < query.Length; qi++)
            {
                int queryDim = query[qi].Index;
                float queryValue = query[qi].Value;

                if (!index.InvertedIndex.TryGetFirstValue(queryDim, out SparsePosting posting, out var iterator))
                    continue;

                do
                {
                    // ソフト削除チェック
                    if (index.DeletedIds.Contains(posting.InternalId))
                        continue;

                    float newScore = queryValue * posting.Value;

                    // Burst 互換パターン: TryGetValue + Remove + Add
                    if (scores.TryGetValue(posting.InternalId, out float existing))
                    {
                        scores.Remove(posting.InternalId);
                        scores.Add(posting.InternalId, existing + newScore);
                    }
                    else
                    {
                        scores.Add(posting.InternalId, newScore);
                    }
                } while (index.InvertedIndex.TryGetNextValue(out posting, ref iterator));
            }

            // Top-K 選択 (NativeMaxHeap で最悪候補を Pop)
            var heap = new NativeMaxHeap(k, Allocator.TempJob);
            var enumerator = scores.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var kv = enumerator.Current;
                float negScore = -kv.Value; // 負値変換

                if (heap.Count < k)
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

            // 結果をスコア昇順でソート
            int resultCount = heap.Count;
            var results = new NativeArray<SearchResult>(resultCount, resultAllocator);
            for (int i = resultCount - 1; i >= 0; i--)
            {
                results[i] = heap.Pop();
            }

            heap.Dispose();
            scores.Dispose();
            return results;
        }
    }
}
