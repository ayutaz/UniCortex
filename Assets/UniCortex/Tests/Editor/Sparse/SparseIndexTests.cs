using NUnit.Framework;
using Unity.Collections;
using UniCortex.Sparse;

namespace UniCortex.Tests.Editor.Sparse
{
    public class SparseIndexTests
    {
        [Test]
        public void Add_Basic()
        {
            var index = new SparseIndex(100, Allocator.Temp);
            var vec = new NativeArray<SparseElement>(2, Allocator.Temp);
            vec[0] = new SparseElement { Index = 10, Value = 1.5f };
            vec[1] = new SparseElement { Index = 20, Value = 2.0f };

            var result = index.Add(0, vec);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, index.DocumentCount);

            vec.Dispose();
            index.Dispose();
        }

        [Test]
        public void Add_NegativeIndex_Fails()
        {
            var index = new SparseIndex(100, Allocator.Temp);
            var vec = new NativeArray<SparseElement>(1, Allocator.Temp);
            vec[0] = new SparseElement { Index = -1, Value = 1.0f };

            var result = index.Add(0, vec);
            Assert.AreEqual(ErrorCode.InvalidParameter, result.Error);

            vec.Dispose();
            index.Dispose();
        }

        [Test]
        public void Remove_SoftDelete()
        {
            var index = new SparseIndex(100, Allocator.Temp);
            var vec = new NativeArray<SparseElement>(1, Allocator.Temp);
            vec[0] = new SparseElement { Index = 10, Value = 1.0f };
            index.Add(0, vec);

            index.Remove(0);
            Assert.IsTrue(index.DeletedIds.Contains(0));

            vec.Dispose();
            index.Dispose();
        }
    }
}
