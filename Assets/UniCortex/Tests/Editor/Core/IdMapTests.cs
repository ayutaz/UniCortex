using NUnit.Framework;
using Unity.Collections;

namespace UniCortex.Tests.Editor.Core
{
    public class IdMapTests
    {
        [Test]
        public void Add_GetInternal_Basic()
        {
            var map = new IdMap(10, Allocator.Temp);
            var result = map.Add(42);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, result.Value);

            var getResult = map.GetInternal(42);
            Assert.IsTrue(getResult.IsSuccess);
            Assert.AreEqual(0, getResult.Value);

            map.Dispose();
        }

        [Test]
        public void Add_Duplicate_Fails()
        {
            var map = new IdMap(10, Allocator.Temp);
            map.Add(42);

            var result = map.Add(42);
            Assert.AreEqual(ErrorCode.DuplicateId, result.Error);

            map.Dispose();
        }

        [Test]
        public void Add_CapacityExceeded_Fails()
        {
            var map = new IdMap(2, Allocator.Temp);
            map.Add(1);
            map.Add(2);

            var result = map.Add(3);
            Assert.AreEqual(ErrorCode.CapacityExceeded, result.Error);

            map.Dispose();
        }

        [Test]
        public void GetExternal_Basic()
        {
            var map = new IdMap(10, Allocator.Temp);
            map.Add(42);

            var result = map.GetExternal(0);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(42UL, result.Value);

            map.Dispose();
        }

        [Test]
        public void GetExternal_AfterRemove_ReturnsNotFound()
        {
            var map = new IdMap(10, Allocator.Temp);
            map.Add(42);
            map.Remove(42);

            var result = map.GetExternal(0);
            Assert.AreEqual(ErrorCode.NotFound, result.Error);

            map.Dispose();
        }

        [Test]
        public void Remove_FreeListReuse()
        {
            var map = new IdMap(10, Allocator.Temp);
            map.Add(1); // internalId = 0
            map.Add(2); // internalId = 1
            map.Remove(1); // frees internalId 0

            var result = map.Add(3); // should reuse internalId 0
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, result.Value);

            map.Dispose();
        }

        [Test]
        public void Remove_NotFound_Fails()
        {
            var map = new IdMap(10, Allocator.Temp);
            var result = map.Remove(999);
            Assert.AreEqual(ErrorCode.NotFound, result.Error);
            map.Dispose();
        }

        [Test]
        public void ExternalId_Zero_Works()
        {
            var map = new IdMap(10, Allocator.Temp);
            var result = map.Add(0);
            Assert.IsTrue(result.IsSuccess);

            var ext = map.GetExternal(result.Value);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(0UL, ext.Value);

            map.Dispose();
        }

        [Test]
        public void GetExternal_OutOfRange_Fails()
        {
            var map = new IdMap(10, Allocator.Temp);
            var result = map.GetExternal(-1);
            Assert.AreEqual(ErrorCode.NotFound, result.Error);

            result = map.GetExternal(10);
            Assert.AreEqual(ErrorCode.NotFound, result.Error);

            map.Dispose();
        }
    }
}
