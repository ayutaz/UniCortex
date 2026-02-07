using Unity.Collections;
using Unity.Mathematics;

namespace UniCortex.Hnsw
{
    /// <summary>
    /// HNSW グラフの構築 (INSERT) ロジック。
    /// </summary>
    public static class HnswBuilder
    {
        /// <summary>
        /// ノードを HNSW グラフに挿入する。
        /// </summary>
        public static Result<bool> Insert(
            ref HnswGraph graph,
            ref VectorStorage vectorStorage,
            int internalId,
            NativeArray<float> vector,
            HnswConfig config,
            DistanceType distanceType,
            ref Random rng)
        {
            // レイヤー選択
            int nodeLayer = (int)math.floor(-math.log(rng.NextFloat()) * config.ML);
            if (nodeLayer < 0) nodeLayer = 0;

            // ノード確保
            var allocResult = graph.AllocateNode(nodeLayer);
            if (!allocResult.IsSuccess)
                return Result<bool>.Fail(allocResult.Error);

            int nodeId = allocResult.Value;

            // ベクトル格納
            var addResult = vectorStorage.Add(internalId, vector);
            if (!addResult.IsSuccess)
                return Result<bool>.Fail(addResult.Error);

            // 最初のノード
            if (graph.Count == 1)
            {
                graph.EntryPoint = nodeId;
                graph.MaxLayer = nodeLayer;
                return Result<bool>.Success(true);
            }

            int ep = graph.EntryPoint;
            int currentMaxLayer = graph.MaxLayer;

            // 上位レイヤーを greedy search で降りる
            for (int lc = currentMaxLayer; lc > nodeLayer; lc--)
            {
                ep = GreedyClosest(ref graph, ref vectorStorage, vector, ep, lc, distanceType);
            }

            // 挿入レイヤーから Layer 0 まで
            for (int lc = math.min(currentMaxLayer, nodeLayer); lc >= 0; lc--)
            {
                // SEARCH_LAYER で候補収集
                var candidates = SearchLayer(
                    ref graph, ref vectorStorage, vector, ep, config.EfConstruction, lc, distanceType);

                int maxConn = graph.GetMaxConnections(lc);

                // 近い順にソート
                candidates.Sort();
                int selectCount = math.min(candidates.Length, maxConn);

                for (int i = 0; i < selectCount; i++)
                {
                    int neighborId = candidates[i].InternalId;

                    // 双方向エッジ
                    graph.AddNeighbor(nodeId, neighborId, lc);
                    bool added = graph.AddNeighbor(neighborId, nodeId, lc);

                    // 既存ノードが接続数超過ならプルーニング
                    if (!added)
                    {
                        PruneNeighbors(ref graph, ref vectorStorage, neighborId, nodeId, lc, maxConn, distanceType);
                    }
                }

                // ep を最近傍に更新
                if (candidates.Length > 0)
                    ep = candidates[0].InternalId;

                candidates.Dispose();
            }

            // エントリポイント更新
            if (nodeLayer > currentMaxLayer)
            {
                graph.EntryPoint = nodeId;
                graph.MaxLayer = nodeLayer;
            }

            return Result<bool>.Success(true);
        }

        /// <summary>
        /// Greedy search で最近傍1ノードを返す。
        /// </summary>
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
                    if (neighborId < 0) continue;
                    if (graph.Deleted[neighborId]) continue;

                    var neighborVec = vectorStorage.Get(neighborId);
                    float dist = DistanceFunctions.ComputeDistance(ref querySlice, ref neighborVec, query.Length, distanceType);

                    if (dist < currentDist)
                    {
                        current = neighborId;
                        currentDist = dist;
                        currentVec = neighborVec;
                        improved = true;
                    }
                }
            }

            return current;
        }

        /// <summary>
        /// SEARCH_LAYER: ef 個の候補を収集する。
        /// </summary>
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
            var candidates = new NativeMinHeap(ef * 2, Allocator.Temp);
            var results = new NativeMaxHeap(ef, Allocator.Temp);

            var querySlice = query.Slice();
            var epVec = vectorStorage.Get(entryPoint);
            float epDist = DistanceFunctions.ComputeDistance(ref querySlice, ref epVec, query.Length, distanceType);

            visited.Add(entryPoint);
            candidates.Push(new SearchResult { InternalId = entryPoint, Score = epDist });
            if (!graph.Deleted[entryPoint])
                results.Push(new SearchResult { InternalId = entryPoint, Score = epDist });

            while (candidates.Count > 0)
            {
                var c = candidates.Pop();

                // 打ち切り
                if (results.Count >= ef && c.Score > results.Peek().Score)
                    break;

                int offset = graph.GetLayerOffset(c.InternalId, layer);
                int count = graph.GetNeighborCount(c.InternalId, layer);

                for (int i = 0; i < count; i++)
                {
                    int neighborId = graph.Neighbors[offset + i];
                    if (neighborId < 0) continue;
                    if (visited.Contains(neighborId)) continue;
                    visited.Add(neighborId);

                    if (graph.Deleted[neighborId]) continue;

                    var neighborVec = vectorStorage.Get(neighborId);
                    float dist = DistanceFunctions.ComputeDistance(ref querySlice, ref neighborVec, query.Length, distanceType);

                    if (results.Count < ef || dist < results.Peek().Score)
                    {
                        candidates.Push(new SearchResult { InternalId = neighborId, Score = dist });
                        results.Push(new SearchResult { InternalId = neighborId, Score = dist });
                        if (results.Count > ef)
                            results.Pop();
                    }
                }
            }

            // 結果リストに変換
            var resultList = new NativeList<SearchResult>(results.Count, Allocator.Temp);
            while (results.Count > 0)
            {
                resultList.Add(results.Pop());
            }

            visited.Dispose();
            candidates.Dispose();
            results.Dispose();

            return resultList;
        }

        /// <summary>
        /// 接続数超過時のプルーニング。
        /// </summary>
        static void PruneNeighbors(
            ref HnswGraph graph,
            ref VectorStorage vectorStorage,
            int nodeId,
            int newNeighborId,
            int layer,
            int maxConn,
            DistanceType distanceType)
        {
            int offset = graph.GetLayerOffset(nodeId, layer);
            int count = graph.GetNeighborCount(nodeId, layer);

            var nodeVec = vectorStorage.Get(nodeId);
            var candidates = new NativeList<SearchResult>(count + 1, Allocator.Temp);

            // 既存の隣接ノード
            for (int i = 0; i < count; i++)
            {
                int nId = graph.Neighbors[offset + i];
                if (nId < 0) continue;
                var nVec = vectorStorage.Get(nId);
                float dist = DistanceFunctions.ComputeDistance(ref nodeVec, ref nVec, vectorStorage.Dimension, distanceType);
                candidates.Add(new SearchResult { InternalId = nId, Score = dist });
            }

            // 新しい隣接ノード
            var newVec = vectorStorage.Get(newNeighborId);
            float newDist = DistanceFunctions.ComputeDistance(ref nodeVec, ref newVec, vectorStorage.Dimension, distanceType);
            candidates.Add(new SearchResult { InternalId = newNeighborId, Score = newDist });

            // ソート
            candidates.Sort();

            // 上位 maxConn を保持
            var newNeighbors = new NativeList<int>(maxConn, Allocator.Temp);
            for (int i = 0; i < candidates.Length && newNeighbors.Length < maxConn; i++)
            {
                newNeighbors.Add(candidates[i].InternalId);
            }

            graph.SetNeighbors(nodeId, layer, newNeighbors);

            candidates.Dispose();
            newNeighbors.Dispose();
        }
    }
}
