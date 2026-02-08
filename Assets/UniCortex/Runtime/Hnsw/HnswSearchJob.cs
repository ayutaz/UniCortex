using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace UniCortex.Hnsw
{
    /// <summary>
    /// HNSW 検索の Burst 互換 Job ラッパー。
    /// </summary>
    [BurstCompile]
    public struct HnswSearchJob : IJob
    {
        [ReadOnly] public NativeArray<float> Query;
        public NativeArray<SearchResult> Results;
        public int K;
        public int EfSearch;
        public DistanceType Distance;

        public NativeArray<HnswNodeMeta> Nodes;
        public NativeArray<int> Neighbors;
        public NativeArray<int> NeighborCounts;
        [ReadOnly] public NativeArray<bool> Deleted;

        [ReadOnly] public NativeArray<float> VectorData;
        public int Dimension;
        public int VectorCapacity;

        public int EntryPoint;
        public int MaxLayer;
        public int M;
        public int M0;
        public int GraphCount;
        public int DeletedCount;

        /// <summary>出力結果数。</summary>
        public NativeArray<int> ResultCount;

        public void Execute()
        {
            if (GraphCount == 0 || EntryPoint < 0)
            {
                ResultCount[0] = 0;
                return;
            }

            // HnswGraph を再構築して Search を呼び出す
            var graph = new HnswGraph
            {
                Nodes = Nodes,
                Neighbors = Neighbors,
                NeighborCounts = NeighborCounts,
                Deleted = Deleted,
                EntryPoint = EntryPoint,
                MaxLayer = MaxLayer,
                M = M,
                M0 = M0,
                Count = GraphCount,
                DeletedCount = DeletedCount,
            };

            var storage = new VectorStorage
            {
                Data = VectorData,
                Dimension = Dimension,
                Count = VectorCapacity,
                Capacity = VectorCapacity,
            };

            var searchResults = HnswSearcher.Search(
                ref graph, ref storage, Query, K, EfSearch, Distance, Allocator.TempJob);

            int count = searchResults.Length < Results.Length ? searchResults.Length : Results.Length;
            for (int i = 0; i < count; i++)
                Results[i] = searchResults[i];

            ResultCount[0] = count;
            searchResults.Dispose();
        }
    }
}
