using NUnit.Framework;
using Unity.Collections;
using UniCortex.Sparse;

namespace UniCortex.Tests.Editor.Sparse
{
    public class SparseSearcherTests
    {
        [Test]
        public void Search_ManualScoreVerification()
        {
            var index = new SparseIndex(100, Allocator.Temp);

            // doc0: dim10=1.0, dim20=2.0
            var vec0 = new NativeArray<SparseElement>(2, Allocator.Temp);
            vec0[0] = new SparseElement { Index = 10, Value = 1.0f };
            vec0[1] = new SparseElement { Index = 20, Value = 2.0f };
            index.Add(0, vec0);

            // doc1: dim10=3.0
            var vec1 = new NativeArray<SparseElement>(1, Allocator.Temp);
            vec1[0] = new SparseElement { Index = 10, Value = 3.0f };
            index.Add(1, vec1);

            // query: dim10=1.0, dim20=1.0
            var query = new NativeArray<SparseElement>(2, Allocator.Temp);
            query[0] = new SparseElement { Index = 10, Value = 1.0f };
            query[1] = new SparseElement { Index = 20, Value = 1.0f };

            var results = SparseSearcher.Search(ref index, query, 10, Allocator.Temp);

            // doc0: 1.0*1.0 + 2.0*1.0 = 3.0 → negScore = -3.0
            // doc1: 3.0*1.0 = 3.0 → negScore = -3.0
            Assert.AreEqual(2, results.Length);

            // 両方とも -3.0 のスコア
            Assert.AreEqual(-3.0f, results[0].Score, 1e-6f);
            Assert.AreEqual(-3.0f, results[1].Score, 1e-6f);

            results.Dispose();
            query.Dispose();
            vec0.Dispose();
            vec1.Dispose();
            index.Dispose();
        }

        [Test]
        public void Search_NegativeScoreConversion()
        {
            var index = new SparseIndex(100, Allocator.Temp);

            var vec = new NativeArray<SparseElement>(1, Allocator.Temp);
            vec[0] = new SparseElement { Index = 5, Value = 2.0f };
            index.Add(0, vec);

            var query = new NativeArray<SparseElement>(1, Allocator.Temp);
            query[0] = new SparseElement { Index = 5, Value = 3.0f };

            var results = SparseSearcher.Search(ref index, query, 10, Allocator.Temp);

            // dot = 2.0 * 3.0 = 6.0, negScore = -6.0
            Assert.AreEqual(1, results.Length);
            Assert.AreEqual(-6.0f, results[0].Score, 1e-6f);

            results.Dispose();
            query.Dispose();
            vec.Dispose();
            index.Dispose();
        }

        [Test]
        public void Search_DeletedExcluded()
        {
            var index = new SparseIndex(100, Allocator.Temp);

            var vec0 = new NativeArray<SparseElement>(1, Allocator.Temp);
            vec0[0] = new SparseElement { Index = 1, Value = 1.0f };
            index.Add(0, vec0);

            var vec1 = new NativeArray<SparseElement>(1, Allocator.Temp);
            vec1[0] = new SparseElement { Index = 1, Value = 2.0f };
            index.Add(1, vec1);

            index.Remove(0); // ソフト削除

            var query = new NativeArray<SparseElement>(1, Allocator.Temp);
            query[0] = new SparseElement { Index = 1, Value = 1.0f };

            var results = SparseSearcher.Search(ref index, query, 10, Allocator.Temp);

            Assert.AreEqual(1, results.Length);
            Assert.AreEqual(1, results[0].InternalId);

            results.Dispose();
            query.Dispose();
            vec0.Dispose();
            vec1.Dispose();
            index.Dispose();
        }

        [Test]
        public void Search_EmptyQuery_ReturnsEmpty()
        {
            var index = new SparseIndex(100, Allocator.Temp);
            var query = new NativeArray<SparseElement>(0, Allocator.Temp);

            var results = SparseSearcher.Search(ref index, query, 10, Allocator.Temp);
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            query.Dispose();
            index.Dispose();
        }

        [Test]
        public void Search_TopK_LimitsResults()
        {
            var index = new SparseIndex(100, Allocator.Temp);

            for (int i = 0; i < 10; i++)
            {
                var vec = new NativeArray<SparseElement>(1, Allocator.Temp);
                vec[0] = new SparseElement { Index = 1, Value = (float)(i + 1) };
                index.Add(i, vec);
                vec.Dispose();
            }

            var query = new NativeArray<SparseElement>(1, Allocator.Temp);
            query[0] = new SparseElement { Index = 1, Value = 1.0f };

            var results = SparseSearcher.Search(ref index, query, 3, Allocator.Temp);
            Assert.AreEqual(3, results.Length);

            // 最もスコアが高い (内積が大きい → 負値が小さい) 3件
            Assert.AreEqual(-10.0f, results[0].Score, 1e-6f);
            Assert.AreEqual(-9.0f, results[1].Score, 1e-6f);
            Assert.AreEqual(-8.0f, results[2].Score, 1e-6f);

            results.Dispose();
            query.Dispose();
            index.Dispose();
        }
    }
}
