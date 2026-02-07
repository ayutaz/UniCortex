using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UniCortex.Hybrid;

namespace UniCortex.Tests.Editor.Hybrid
{
    public class RrfTests
    {
        [Test]
        public void RrfMergeJob_BasicMerge_CorrectScores()
        {
            // Dense: [docA(rank1), docB(rank2)]
            // Sparse: [docB(rank1), docC(rank2)]
            // k=60, all weights=1.0
            var dense = new NativeArray<SearchResult>(2, Allocator.Temp);
            dense[0] = new SearchResult { InternalId = 0, Score = 0.1f };
            dense[1] = new SearchResult { InternalId = 1, Score = 0.2f };

            var sparse = new NativeArray<SearchResult>(2, Allocator.Temp);
            sparse[0] = new SearchResult { InternalId = 1, Score = -0.9f };
            sparse[1] = new SearchResult { InternalId = 2, Score = -0.5f };

            var bm25 = new NativeArray<SearchResult>(0, Allocator.Temp);
            var merged = new NativeArray<SearchResult>(3, Allocator.Temp);

            var job = new RrfMergeJob
            {
                DenseResults = dense,
                SparseResults = sparse,
                Bm25Results = bm25,
                MergedResults = merged,
                Config = RrfConfig.Default,
                K = 3,
                DenseCount = 2,
                SparseCount = 2,
                Bm25Count = 0,
            };
            job.Execute();

            // docB: 出現2回 → 1/(60+1) + 1/(60+2) = Dense rank2 + Sparse rank1
            //   Dense: rank2 → 1/(60+2) = 0.01613
            //   Sparse: rank1 → 1/(60+1) = 0.01639
            //   合計: 0.03252
            // docA: Dense rank1 → 1/(60+1) = 0.01639
            // docC: Sparse rank2 → 1/(60+2) = 0.01613

            // Top結果はdocB (最高RRFスコア)
            Assert.AreEqual(1, merged[0].InternalId); // docB

            // RRFスコア検証 (docB)
            float expectedDocB = 1.0f / (60f + 2f) + 1.0f / (60f + 1f);
            Assert.AreEqual(expectedDocB, merged[0].Score, 1e-6f);

            dense.Dispose();
            sparse.Dispose();
            bm25.Dispose();
            merged.Dispose();
        }

        [Test]
        public void RrfMergeJob_WeightedMerge_RespectsWeights()
        {
            // Dense weight=2.0, Sparse weight=0.5
            var dense = new NativeArray<SearchResult>(2, Allocator.Temp);
            dense[0] = new SearchResult { InternalId = 0, Score = 0.1f }; // rank 1
            dense[1] = new SearchResult { InternalId = 1, Score = 0.2f }; // rank 2

            var sparse = new NativeArray<SearchResult>(1, Allocator.Temp);
            sparse[0] = new SearchResult { InternalId = 1, Score = -0.9f }; // rank 1

            var bm25 = new NativeArray<SearchResult>(0, Allocator.Temp);
            var merged = new NativeArray<SearchResult>(2, Allocator.Temp);

            var config = new RrfConfig
            {
                RankConstant = 60f,
                DenseWeight = 2.0f,
                SparseWeight = 0.5f,
                Bm25Weight = 1.0f,
            };

            var job = new RrfMergeJob
            {
                DenseResults = dense,
                SparseResults = sparse,
                Bm25Results = bm25,
                MergedResults = merged,
                Config = config,
                K = 2,
                DenseCount = 2,
                SparseCount = 1,
                Bm25Count = 0,
            };
            job.Execute();

            // docA: Dense rank1 → 2.0/(60+1) = 0.03279
            // docB: Dense rank2 → 2.0/(60+2) = 0.03226, Sparse rank1 → 0.5/(60+1) = 0.00820
            //   合計: 0.04046
            // docB > docA
            Assert.AreEqual(1, merged[0].InternalId); // docB

            float expectedDocB = 2.0f / (60f + 2f) + 0.5f / (60f + 1f);
            Assert.AreEqual(expectedDocB, merged[0].Score, 1e-6f);

            float expectedDocA = 2.0f / (60f + 1f);
            Assert.AreEqual(expectedDocA, merged[1].Score, 1e-6f);

            dense.Dispose();
            sparse.Dispose();
            bm25.Dispose();
            merged.Dispose();
        }

        [Test]
        public void RrfMergeJob_EmptyInputs_NoResults()
        {
            var dense = new NativeArray<SearchResult>(0, Allocator.Temp);
            var sparse = new NativeArray<SearchResult>(0, Allocator.Temp);
            var bm25 = new NativeArray<SearchResult>(0, Allocator.Temp);
            var merged = new NativeArray<SearchResult>(0, Allocator.Temp);

            var job = new RrfMergeJob
            {
                DenseResults = dense,
                SparseResults = sparse,
                Bm25Results = bm25,
                MergedResults = merged,
                Config = RrfConfig.Default,
                K = 0,
                DenseCount = 0,
                SparseCount = 0,
                Bm25Count = 0,
            };
            job.Execute();

            Assert.AreEqual(0, merged.Length);

            dense.Dispose();
            sparse.Dispose();
            bm25.Dispose();
            merged.Dispose();
        }

        [Test]
        public void RrfMergeJob_ThreeSourcesMerge()
        {
            // 設計ドキュメントの計算例を検証
            // Dense: [docA(rank1), docB(rank2), docC(rank3)]
            // Sparse: [docB(rank1), docC(rank2), docD(rank3)]
            // BM25: [docC(rank1), docA(rank2), docD(rank3)]
            // 重み: Dense=2.0, Sparse=1.0, BM25=0.5, k=60
            var dense = new NativeArray<SearchResult>(3, Allocator.Temp);
            dense[0] = new SearchResult { InternalId = 0, Score = 0.1f }; // docA
            dense[1] = new SearchResult { InternalId = 1, Score = 0.2f }; // docB
            dense[2] = new SearchResult { InternalId = 2, Score = 0.3f }; // docC

            var sparse = new NativeArray<SearchResult>(3, Allocator.Temp);
            sparse[0] = new SearchResult { InternalId = 1, Score = -0.9f }; // docB
            sparse[1] = new SearchResult { InternalId = 2, Score = -0.5f }; // docC
            sparse[2] = new SearchResult { InternalId = 3, Score = -0.3f }; // docD

            var bm25 = new NativeArray<SearchResult>(3, Allocator.Temp);
            bm25[0] = new SearchResult { InternalId = 2, Score = -5.0f }; // docC
            bm25[1] = new SearchResult { InternalId = 0, Score = -3.0f }; // docA
            bm25[2] = new SearchResult { InternalId = 3, Score = -2.0f }; // docD

            var merged = new NativeArray<SearchResult>(4, Allocator.Temp);

            var config = new RrfConfig
            {
                RankConstant = 60f,
                DenseWeight = 2.0f,
                SparseWeight = 1.0f,
                Bm25Weight = 0.5f,
            };

            var job = new RrfMergeJob
            {
                DenseResults = dense,
                SparseResults = sparse,
                Bm25Results = bm25,
                MergedResults = merged,
                Config = config,
                K = 4,
                DenseCount = 3,
                SparseCount = 3,
                Bm25Count = 3,
            };
            job.Execute();

            // 期待される RRF スコア:
            // docA: 2.0/(60+1) + 0.5/(60+2) = 0.03279 + 0.00806 = 0.04085
            // docB: 2.0/(60+2) + 1.0/(60+1) = 0.03226 + 0.01639 = 0.04865
            // docC: 2.0/(60+3) + 1.0/(60+2) + 0.5/(60+1) = 0.03175 + 0.01613 + 0.00820 = 0.05608
            // docD: 1.0/(60+3) + 0.5/(60+3) = 0.01587 + 0.00794 = 0.02381
            // 順位: docC > docB > docA > docD

            Assert.AreEqual(2, merged[0].InternalId); // docC
            Assert.AreEqual(1, merged[1].InternalId); // docB
            Assert.AreEqual(0, merged[2].InternalId); // docA
            Assert.AreEqual(3, merged[3].InternalId); // docD

            float expectedDocC = 2.0f / 63f + 1.0f / 62f + 0.5f / 61f;
            Assert.AreEqual(expectedDocC, merged[0].Score, 1e-5f);

            dense.Dispose();
            sparse.Dispose();
            bm25.Dispose();
            merged.Dispose();
        }

        [Test]
        public void RrfMergeJob_TopKSelection_ClampsResults()
        {
            // 5件あるが K=2 で上位2件のみ
            var dense = new NativeArray<SearchResult>(3, Allocator.Temp);
            dense[0] = new SearchResult { InternalId = 0, Score = 0.1f };
            dense[1] = new SearchResult { InternalId = 1, Score = 0.2f };
            dense[2] = new SearchResult { InternalId = 2, Score = 0.3f };

            var sparse = new NativeArray<SearchResult>(2, Allocator.Temp);
            sparse[0] = new SearchResult { InternalId = 3, Score = -0.9f };
            sparse[1] = new SearchResult { InternalId = 4, Score = -0.5f };

            var bm25 = new NativeArray<SearchResult>(0, Allocator.Temp);
            var merged = new NativeArray<SearchResult>(2, Allocator.Temp);

            var job = new RrfMergeJob
            {
                DenseResults = dense,
                SparseResults = sparse,
                Bm25Results = bm25,
                MergedResults = merged,
                Config = RrfConfig.Default,
                K = 2,
                DenseCount = 3,
                SparseCount = 2,
                Bm25Count = 0,
            };
            job.Execute();

            // 2件のみ返されること
            Assert.AreEqual(2, merged.Length);
            // スコアが正であること（RRFスコア > 0）
            Assert.Greater(merged[0].Score, 0f);
            Assert.Greater(merged[1].Score, 0f);
            // 降順であること
            Assert.GreaterOrEqual(merged[0].Score, merged[1].Score);

            dense.Dispose();
            sparse.Dispose();
            bm25.Dispose();
            merged.Dispose();
        }

        [Test]
        public void RrfConfig_Default_HasExpectedValues()
        {
            var config = RrfConfig.Default;
            Assert.AreEqual(60f, config.RankConstant);
            Assert.AreEqual(1.0f, config.DenseWeight);
            Assert.AreEqual(1.0f, config.SparseWeight);
            Assert.AreEqual(1.0f, config.Bm25Weight);
        }

        [Test]
        public void RrfMergeJob_ZeroWeight_IgnoresSource()
        {
            // Dense weight=0 → Dense 結果は無視される
            var dense = new NativeArray<SearchResult>(1, Allocator.Temp);
            dense[0] = new SearchResult { InternalId = 0, Score = 0.1f };

            var sparse = new NativeArray<SearchResult>(1, Allocator.Temp);
            sparse[0] = new SearchResult { InternalId = 1, Score = -0.9f };

            var bm25 = new NativeArray<SearchResult>(0, Allocator.Temp);
            var merged = new NativeArray<SearchResult>(2, Allocator.Temp);

            var config = new RrfConfig
            {
                RankConstant = 60f,
                DenseWeight = 0f,
                SparseWeight = 1.0f,
                Bm25Weight = 1.0f,
            };

            var job = new RrfMergeJob
            {
                DenseResults = dense,
                SparseResults = sparse,
                Bm25Results = bm25,
                MergedResults = merged,
                Config = config,
                K = 2,
                DenseCount = 1,
                SparseCount = 1,
                Bm25Count = 0,
            };
            job.Execute();

            // docA(0) は Dense のみで weight=0 → RRFスコア=0 → 結果に含まれない
            // docB(1) は Sparse rank1 → 1.0/(60+1) > 0
            Assert.AreEqual(1, merged[0].InternalId);
            float expectedDocB = 1.0f / (60f + 1f);
            Assert.AreEqual(expectedDocB, merged[0].Score, 1e-6f);

            dense.Dispose();
            sparse.Dispose();
            bm25.Dispose();
            merged.Dispose();
        }
    }
}
