using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace UniCortex.FullText
{
    /// <summary>
    /// BM25 検索を実行する Burst Job。
    /// </summary>
    [BurstCompile]
    public struct BM25SearchJob : IJob
    {
        [ReadOnly] public NativeArray<uint> QueryTokenHashes;
        [ReadOnly] public NativeParallelMultiHashMap<uint, BM25Posting> InvertedIndex;
        [ReadOnly] public NativeParallelHashMap<uint, int> DocumentFrequency;
        [ReadOnly] public NativeArray<int> DocumentLengths;
        [ReadOnly] public NativeParallelHashSet<int> DeletedIds;

        public NativeArray<SearchResult> Results;
        public int K;
        public int TotalDocuments;
        public float AverageDocumentLength;
        public float K1;
        public float B;

        public void Execute()
        {
            if (K <= 0 || QueryTokenHashes.Length == 0 || TotalDocuments == 0)
                return;

            float avgdl = math.max(AverageDocumentLength, 1.0f);
            int n = TotalDocuments;

            var scores = new NativeParallelHashMap<int, float>(128, Allocator.Temp);

            for (int qi = 0; qi < QueryTokenHashes.Length; qi++)
            {
                uint tokenHash = QueryTokenHashes[qi];

                if (!DocumentFrequency.TryGetValue(tokenHash, out int df))
                    continue;

                float idf = math.log((float)(n - df + 0.5f) / (df + 0.5f) + 1.0f);

                if (!InvertedIndex.TryGetFirstValue(tokenHash, out BM25Posting posting, out var iterator))
                    continue;

                do
                {
                    if (DeletedIds.Contains(posting.InternalId))
                        continue;

                    float tf = posting.TermFrequency;
                    float dl = DocumentLengths[posting.InternalId];

                    float tfComponent = (tf * (K1 + 1.0f)) / (tf + K1 * (1.0f - B + B * dl / avgdl));
                    float termScore = idf * tfComponent;

                    if (scores.TryGetValue(posting.InternalId, out float existing))
                    {
                        scores.Remove(posting.InternalId);
                        scores.Add(posting.InternalId, existing + termScore);
                    }
                    else
                    {
                        scores.Add(posting.InternalId, termScore);
                    }
                } while (InvertedIndex.TryGetNextValue(out posting, ref iterator));
            }

            // Top-K
            var heap = new NativeMaxHeap(K, Allocator.Temp);
            var enumerator = scores.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var kv = enumerator.Current;
                float negScore = -kv.Value;

                if (heap.Count < K)
                    heap.Push(new SearchResult { InternalId = kv.Key, Score = negScore });
                else if (negScore < heap.Peek().Score)
                {
                    heap.Pop();
                    heap.Push(new SearchResult { InternalId = kv.Key, Score = negScore });
                }
            }
            enumerator.Dispose();

            int resultCount = heap.Count;
            for (int i = resultCount - 1; i >= 0; i--)
            {
                Results[i] = heap.Pop();
            }
            for (int i = resultCount; i < Results.Length; i++)
            {
                Results[i] = default;
            }

            heap.Dispose();
            scores.Dispose();
        }
    }
}
