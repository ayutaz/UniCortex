using Unity.Collections;
using Unity.Mathematics;

namespace UniCortex.FullText
{
    /// <summary>
    /// BM25 スコアリングによる全文検索。
    /// </summary>
    public static class BM25Searcher
    {
        /// <summary>BM25 パラメータ k1 デフォルト値。</summary>
        public const float DefaultK1 = 1.2f;

        /// <summary>BM25 パラメータ b デフォルト値。</summary>
        public const float DefaultB = 0.75f;

        /// <summary>
        /// BM25 検索を実行する。
        /// </summary>
        public static NativeArray<SearchResult> Search(
            ref BM25Index index,
            NativeList<uint> queryTokenHashes,
            int k,
            float k1,
            float b,
            Allocator resultAllocator)
        {
            if (k <= 0 || queryTokenHashes.Length == 0 || index.TotalDocuments == 0)
                return new NativeArray<SearchResult>(0, resultAllocator);

            float avgdl = math.max(index.AverageDocumentLength, 1.0f);
            int n = index.TotalDocuments;

            // クエリトークンのユニーク化
            var uniqueTokens = new NativeParallelHashSet<uint>(queryTokenHashes.Length, Allocator.Temp);
            var queryTokenList = new NativeList<uint>(queryTokenHashes.Length, Allocator.Temp);
            for (int i = 0; i < queryTokenHashes.Length; i++)
            {
                if (!uniqueTokens.Contains(queryTokenHashes[i]))
                {
                    uniqueTokens.Add(queryTokenHashes[i]);
                    queryTokenList.Add(queryTokenHashes[i]);
                }
            }

            // スコア累積
            var scores = new NativeParallelHashMap<int, float>(
                index.TotalDocuments > 0 ? index.TotalDocuments : 64, Allocator.Temp);

            for (int qi = 0; qi < queryTokenList.Length; qi++)
            {
                uint tokenHash = queryTokenList[qi];

                // DF を取得
                if (!index.DocumentFrequency.TryGetValue(tokenHash, out int df))
                    continue; // このトークンはインデックスにない

                // IDF 計算
                float idf = math.log((float)(n - df + 0.5f) / (df + 0.5f) + 1.0f);

                // ポスティングリスト走査
                if (!index.InvertedIndex.TryGetFirstValue(tokenHash, out BM25Posting posting, out var iterator))
                    continue;

                do
                {
                    if (index.DeletedIds.Contains(posting.InternalId))
                        continue;

                    float tf = posting.TermFrequency;
                    float dl = index.DocumentLengths[posting.InternalId];

                    // BM25 スコア
                    float tfComponent = (tf * (k1 + 1.0f)) / (tf + k1 * (1.0f - b + b * dl / avgdl));
                    float termScore = idf * tfComponent;

                    // 累積
                    if (scores.TryGetValue(posting.InternalId, out float existing))
                    {
                        scores.Remove(posting.InternalId);
                        scores.Add(posting.InternalId, existing + termScore);
                    }
                    else
                    {
                        scores.Add(posting.InternalId, termScore);
                    }
                } while (index.InvertedIndex.TryGetNextValue(out posting, ref iterator));
            }

            // Top-K 選択 (負値変換)
            var heap = new NativeMaxHeap(k, Allocator.Temp);
            var enumerator = scores.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var kv = enumerator.Current;
                float negScore = -kv.Value;

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

            // 結果をスコア昇順で返す
            int resultCount = heap.Count;
            var results = new NativeArray<SearchResult>(resultCount, resultAllocator);
            for (int i = resultCount - 1; i >= 0; i--)
            {
                results[i] = heap.Pop();
            }

            heap.Dispose();
            scores.Dispose();
            uniqueTokens.Dispose();
            queryTokenList.Dispose();

            return results;
        }
    }
}
