using NUnit.Framework;
using Unity.Collections;

namespace UniCortex.Tests.Editor.Core
{
    public class VectorStorageTests
    {
        [Test]
        public void Add_Get_Basic()
        {
            var storage = new VectorStorage(10, 4, Allocator.Temp);
            var vec = new NativeArray<float>(4, Allocator.Temp);
            vec[0] = 1; vec[1] = 2; vec[2] = 3; vec[3] = 4;

            var result = storage.Add(0, vec);
            Assert.IsTrue(result.IsSuccess);

            var slice = storage.Get(0);
            Assert.AreEqual(4, slice.Length);
            Assert.AreEqual(1f, slice[0], 1e-6f);
            Assert.AreEqual(4f, slice[3], 1e-6f);

            vec.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Add_DimensionMismatch_Fails()
        {
            var storage = new VectorStorage(10, 4, Allocator.Temp);
            var vec = new NativeArray<float>(3, Allocator.Temp);

            var result = storage.Add(0, vec);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(ErrorCode.DimensionMismatch, result.Error);

            vec.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Add_InvalidId_Fails()
        {
            var storage = new VectorStorage(10, 4, Allocator.Temp);
            var vec = new NativeArray<float>(4, Allocator.Temp);

            var result = storage.Add(-1, vec);
            Assert.AreEqual(ErrorCode.InvalidParameter, result.Error);

            result = storage.Add(10, vec);
            Assert.AreEqual(ErrorCode.InvalidParameter, result.Error);

            vec.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Add_NaNVector_Fails()
        {
            var storage = new VectorStorage(10, 4, Allocator.Temp);
            var vec = new NativeArray<float>(4, Allocator.Temp);
            vec[0] = 1; vec[1] = float.NaN; vec[2] = 3; vec[3] = 4;

            var result = storage.Add(0, vec);
            Assert.AreEqual(ErrorCode.InvalidParameter, result.Error);

            vec.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Update_Basic()
        {
            var storage = new VectorStorage(10, 4, Allocator.Temp);
            var vec1 = new NativeArray<float>(4, Allocator.Temp);
            vec1[0] = 1; vec1[1] = 2; vec1[2] = 3; vec1[3] = 4;
            storage.Add(0, vec1);

            var vec2 = new NativeArray<float>(4, Allocator.Temp);
            vec2[0] = 10; vec2[1] = 20; vec2[2] = 30; vec2[3] = 40;
            var result = storage.Update(0, vec2);
            Assert.IsTrue(result.IsSuccess);

            var slice = storage.Get(0);
            Assert.AreEqual(10f, slice[0], 1e-6f);

            vec1.Dispose();
            vec2.Dispose();
            storage.Dispose();
        }

        [Test]
        public void Get_OutOfRange_ReturnsDefault()
        {
            var storage = new VectorStorage(10, 4, Allocator.Temp);
            var slice = storage.Get(-1);
            Assert.AreEqual(0, slice.Length);

            slice = storage.Get(10);
            Assert.AreEqual(0, slice.Length);

            storage.Dispose();
        }
    }
}
