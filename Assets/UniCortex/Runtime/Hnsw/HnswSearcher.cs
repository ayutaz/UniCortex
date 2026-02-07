using Unity.Collections;
using Unity.Mathematics;

namespace UniCortex.Hnsw
{
    /// <summary>
    /// HNSW 検索 + ソフト削除ロジック。
    /// </summary>
    public static class HnswSearcher
    {
        /// <summary>
        /// Top-K 近傍検索を実行する。
        /// </summary>
        public static NativeArray<SearchResult> Search(
            ref HnswGraph graph,
            ref VectorStorage vectorStorage,
            NativeArray<float> query,
            int k,
            int efSearch,
            DistanceType distanceType,
            Allocator resultAllocator)
        {
            if (graph.Count == 0 || graph.EntryPoint < 0)
                return new NativeArray<SearchResult>(0, resultAllocator);

            int effectiveCount = graph.Count - graph.DeletedCount;
            if (effectiveCount <= 0)
                return new NativeArray<SearchResult>(0, resultAllocator);

            int actualK = math.min(k, effectiveCount);
            int actualEf = math.max(efSearch, actualK);

            int ep = graph.EntryPoint;

            // 上位レイヤーを greedy で降りる
            for (int lc = graph.MaxLayer; lc >= 1; lc--)
            {
                ep = GreedyClosest(ref graph, ref vectorStorage, query, ep, lc, distanceType);
            }

            // Layer 0 で ef 個の候補を収集
            var candidates = SearchLayer(ref graph, ref vectorStorage, query, ep, actualEf, 0, distanceType);

            // Top-K 選択
            candidates.Sort();
            int resultCount = math.min(candidates.Length, actualK);
            var results = new NativeArray<SearchResult>(resultCount, resultAllocator);
            for (int i = 0; i < resultCount; i++)
            {
                results[i] = candidates[i];
            }

            candidates.Dispose();
            return results;
        }

        /// <summary>
        /// ノードをソフト削除する。
        /// </summary>
        public static void Delete(ref HnswGraph graph, int nodeId)
        {
            if (nodeId < 0 || nodeId >= graph.Count || graph.Deleted[nodeId])
                return;

            graph.Deleted[nodeId] = true;
            graph.DeletedCount++;

            // エントリポイントが削除された場合、代替を探す
            if (nodeId == graph.EntryPoint)
            {
                int newEntry = FindNonDeletedEntry(ref graph, nodeId);
                if (newEntry < 0)
                {
                    graph.EntryPoint = -1;
                    graph.MaxLayer = -1;
                }
                else
                {
                    graph.EntryPoint = newEntry;
                    graph.MaxLayer = graph.Nodes[newEntry].MaxLayer;
                }
            }
        }

        static int FindNonDeletedEntry(ref HnswGraph graph, int deletedEntryId)
        {
            // まず隣接ノードから探す
            for (int layer = graph.MaxLayer; layer >= 0; layer--)
            {
                int offset = graph.GetLayerOffset(deletedEntryId, layer);
                int count = graph.GetNeighborCount(deletedEntryId, layer);
                for (int i = 0; i < count; i++)
                {
                    int n = graph.Neighbors[offset + i];
                    if (n >= 0 && !graph.Deleted[n] && graph.Nodes[n].MaxLayer >= layer)
                        return n;
                }
            }

            // 全ノード線形スキャン
            int bestNode = -1;
            int bestLayer = -1;
            for (int i = 0; i < graph.Count; i++)
            {
                if (!graph.Deleted[i] && graph.Nodes[i].MaxLayer > bestLayer)
                {
                    bestLayer = graph.Nodes[i].MaxLayer;
                    bestNode = i;
                }
            }
            return bestNode;
        }

        static int GreedyClosest(
            ref HnswGraph graph,
            ref VectorStorage vectorStorage,
            NativeArray<float> query,
            int entryPoint,
            int layer,
            DistanceType distanceType)
        {
            int current = entryPoint;
            var querySlice = query.Slice();
            var currentVec = vectorStorage.Get(current);
            float currentDist = DistanceFunctions.ComputeDistance(ref querySlice, ref currentVec, query.Length, distanceType);

            bool improved = true;
            while (improved)
            {
                improved = false;
                int offset = graph.GetLayerOffset(current, layer);
                int count = graph.GetNeighborCount(current, layer);

                for (int i = 0; i < count; i++)
                {
                    int neighborId = graph.Neighbors[offset + i];
                    if (neighborId < 0 || graph.Deleted[neighborId]) continue;

                    var neighborVec = vectorStorage.Get(neighborId);
                    float dist = DistanceFunctions.ComputeDistance(ref querySlice, ref neighborVec, query.Length, distanceType);
                    if (dist < currentDist)
                    {
                        current = neighborId;
                        currentDist = dist;
                        improved = true;
                    }
                }
            }
            return current;
        }

        static NativeList<SearchResult> SearchLayer(
            ref HnswGraph graph,
            ref VectorStorage vectorStorage,
            NativeArray<float> query,
            int entryPoint,
            int ef,
            int layer,
            DistanceType distanceType)
        {
            var visited = new NativeParallelHashSet<int>(ef * 4, Allocator.Temp);
            var candidateHeap = new NativeMinHeap(ef * 2, Allocator.Temp);
            var resultHeap = new NativeMaxHeap(ef, Allocator.Temp);

            var querySlice = query.Slice();
            var epVec = vectorStorage.Get(entryPoint);
            float epDist = DistanceFunctions.ComputeDistance(ref querySlice, ref epVec, query.Length, distanceType);

            visited.Add(entryPoint);
            candidateHeap.Push(new SearchResult { InternalId = entryPoint, Score = epDist });
            if (!graph.Deleted[entryPoint])
                resultHeap.Push(new SearchResult { InternalId = entryPoint, Score = epDist });

            while (candidateHeap.Count > 0)
            {
                var c = candidateHeap.Pop();

                if (resultHeap.Count >= ef && c.Score > resultHeap.Peek().Score)
                    break;

                int offset = graph.GetLayerOffset(c.InternalId, layer);
                int count = graph.GetNeighborCount(c.InternalId, layer);

                for (int i = 0; i < count; i++)
                {
                    int neighborId = graph.Neighbors[offset + i];
                    if (neighborId < 0 || visited.Contains(neighborId)) continue;
                    visited.Add(neighborId);
                    if (graph.Deleted[neighborId]) continue;

                    var neighborVec = vectorStorage.Get(neighborId);
                    float dist = DistanceFunctions.ComputeDistance(ref querySlice, ref neighborVec, query.Length, distanceType);

                    if (resultHeap.Count < ef || dist < resultHeap.Peek().Score)
                    {
                        candidateHeap.Push(new SearchResult { InternalId = neighborId, Score = dist });
                        resultHeap.Push(new SearchResult { InternalId = neighborId, Score = dist });
                        if (resultHeap.Count > ef)
                            resultHeap.Pop();
                    }
                }
            }

            var resultList = new NativeList<SearchResult>(resultHeap.Count, Allocator.Temp);
            while (resultHeap.Count > 0)
                resultList.Add(resultHeap.Pop());

            visited.Dispose();
            candidateHeap.Dispose();
            resultHeap.Dispose();

            return resultList;
        }
    }
}
