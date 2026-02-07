using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UniCortex.Hnsw;

namespace UniCortex.Tests.Editor.Hnsw
{
    public class HnswTests
    {
        const int Dim = 4;
        HnswConfig config;
        Random rng;

        [SetUp]
        public void Setup()
        {
            config = HnswConfig.Default;
            rng = new Random(42);
        }

        NativeArray<float> MakeVector(params float[] values)
        {
            var v = new NativeArray<float>(values.Length, Allocator.Temp);
            for (int i = 0; i < values.Length; i++)
                v[i] = values[i];
            return v;
        }

        [Test]
        public void Insert_SingleNode()
        {
            var graph = new HnswGraph(100, config.M, config.M0, 6, Allocator.Temp);
            var storage = new VectorStorage(100, Dim, Allocator.Temp);

            var vec = MakeVector(1, 0, 0, 0);
            var result = HnswBuilder.Insert(ref graph, ref storage, 0, vec, config, DistanceType.EuclideanSq, ref rng);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, graph.Count);
            Assert.AreEqual(0, graph.EntryPoint);

            vec.Dispose();
            graph.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Insert_MultipleNodes_And_Search()
        {
            var graph = new HnswGraph(100, config.M, config.M0, 6, Allocator.Temp);
            var storage = new VectorStorage(100, Dim, Allocator.Temp);

            // 10個のベクトルを挿入
            for (int i = 0; i < 10; i++)
            {
                var vec = MakeVector(i, 0, 0, 0);
                HnswBuilder.Insert(ref graph, ref storage, i, vec, config, DistanceType.EuclideanSq, ref rng);
                vec.Dispose();
            }

            Assert.AreEqual(10, graph.Count);

            // クエリ: [5, 0, 0, 0] → 最近傍は id=5
            var query = MakeVector(5, 0, 0, 0);
            var results = HnswSearcher.Search(ref graph, ref storage, query, 3, 50, DistanceType.EuclideanSq, Allocator.Temp);

            Assert.GreaterOrEqual(results.Length, 1);
            Assert.AreEqual(5, results[0].InternalId);
            Assert.AreEqual(0f, results[0].Score, 1e-6f);

            results.Dispose();
            query.Dispose();
            graph.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Search_EmptyGraph_ReturnsEmpty()
        {
            var graph = new HnswGraph(100, config.M, config.M0, 6, Allocator.Temp);
            var storage = new VectorStorage(100, Dim, Allocator.Temp);

            var query = MakeVector(1, 0, 0, 0);
            var results = HnswSearcher.Search(ref graph, ref storage, query, 5, 50, DistanceType.EuclideanSq, Allocator.Temp);
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            query.Dispose();
            graph.Dispose();
            storage.Dispose();
        }

        [Test]
        public void SoftDelete_ExcludedFromSearch()
        {
            var graph = new HnswGraph(100, config.M, config.M0, 6, Allocator.Temp);
            var storage = new VectorStorage(100, Dim, Allocator.Temp);

            var vec0 = MakeVector(0, 0, 0, 0);
            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(2, 0, 0, 0);

            HnswBuilder.Insert(ref graph, ref storage, 0, vec0, config, DistanceType.EuclideanSq, ref rng);
            HnswBuilder.Insert(ref graph, ref storage, 1, vec1, config, DistanceType.EuclideanSq, ref rng);
            HnswBuilder.Insert(ref graph, ref storage, 2, vec2, config, DistanceType.EuclideanSq, ref rng);

            HnswSearcher.Delete(ref graph, 1);

            var query = MakeVector(1, 0, 0, 0);
            var results = HnswSearcher.Search(ref graph, ref storage, query, 10, 50, DistanceType.EuclideanSq, Allocator.Temp);

            // id=1 は削除済みなので結果に含まれない
            for (int i = 0; i < results.Length; i++)
            {
                Assert.AreNotEqual(1, results[i].InternalId);
            }

            results.Dispose();
            query.Dispose();
            vec0.Dispose();
            vec1.Dispose();
            vec2.Dispose();
            graph.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Search_KGreaterThanCount_ClampsResult()
        {
            var graph = new HnswGraph(100, config.M, config.M0, 6, Allocator.Temp);
            var storage = new VectorStorage(100, Dim, Allocator.Temp);

            var vec = MakeVector(1, 0, 0, 0);
            HnswBuilder.Insert(ref graph, ref storage, 0, vec, config, DistanceType.EuclideanSq, ref rng);

            var query = MakeVector(0, 0, 0, 0);
            var results = HnswSearcher.Search(ref graph, ref storage, query, 100, 50, DistanceType.EuclideanSq, Allocator.Temp);

            Assert.AreEqual(1, results.Length);

            results.Dispose();
            query.Dispose();
            vec.Dispose();
            graph.Dispose();
            storage.Dispose();
        }

        [Test]
        public void GraphInvariant_NeighborCountWithinBounds()
        {
            var graph = new HnswGraph(200, config.M, config.M0, 6, Allocator.Temp);
            var storage = new VectorStorage(200, Dim, Allocator.Temp);

            for (int i = 0; i < 50; i++)
            {
                var vec = MakeVector(rng.NextFloat() * 100, rng.NextFloat() * 100, rng.NextFloat() * 100, rng.NextFloat() * 100);
                HnswBuilder.Insert(ref graph, ref storage, i, vec, config, DistanceType.EuclideanSq, ref rng);
                vec.Dispose();
            }

            // 不変条件: 各ノードの各レイヤーの接続数 <= maxConn
            for (int nodeId = 0; nodeId < graph.Count; nodeId++)
            {
                int maxLayer = graph.Nodes[nodeId].MaxLayer;
                for (int layer = 0; layer <= maxLayer; layer++)
                {
                    int count = graph.GetNeighborCount(nodeId, layer);
                    int maxConn = graph.GetMaxConnections(layer);
                    Assert.LessOrEqual(count, maxConn,
                        $"Node {nodeId} layer {layer}: count={count} > maxConn={maxConn}");
                }
            }

            graph.Dispose();
            storage.Dispose();
        }
    }
}
