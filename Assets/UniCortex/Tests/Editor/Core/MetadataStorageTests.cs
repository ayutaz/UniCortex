using NUnit.Framework;
using Unity.Collections;

namespace UniCortex.Tests.Editor.Core
{
    public class MetadataStorageTests
    {
        [Test]
        public void SetGet_Int_Basic()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            var setResult = storage.SetInt(42, 0, 1000);
            Assert.IsTrue(setResult.IsSuccess);

            var getResult = storage.GetInt(42, 0);
            Assert.IsTrue(getResult.IsSuccess);
            Assert.AreEqual(1000, getResult.Value);

            storage.Dispose();
        }

        [Test]
        public void SetGet_Float_Basic()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            storage.SetFloat(10, 0, 3.14f);

            var result = storage.GetFloat(10, 0);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(3.14f, result.Value, 1e-6f);

            storage.Dispose();
        }

        [Test]
        public void SetGet_Bool_Basic()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            storage.SetBool(5, 0, true);

            var result = storage.GetBool(5, 0);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.Value);

            storage.Dispose();
        }

        [Test]
        public void Get_NotFound()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            var result = storage.GetInt(42, 0);
            Assert.AreEqual(ErrorCode.NotFound, result.Error);
            storage.Dispose();
        }

        [Test]
        public void Set_NegativeInternalId_Fails()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            var result = storage.SetInt(42, -1, 100);
            Assert.AreEqual(ErrorCode.InvalidParameter, result.Error);
            storage.Dispose();
        }

        [Test]
        public void Set_Overwrite()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            storage.SetInt(42, 0, 100);
            storage.SetInt(42, 0, 200);

            var result = storage.GetInt(42, 0);
            Assert.AreEqual(200, result.Value);

            storage.Dispose();
        }

        [Test]
        public void Remove_ClearsAllTypes()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            storage.SetInt(1, 5, 100);
            storage.SetFloat(2, 5, 1.5f);
            storage.SetBool(3, 5, true);

            var result = storage.Remove(5);
            Assert.IsTrue(result.IsSuccess);

            Assert.AreEqual(ErrorCode.NotFound, storage.GetInt(1, 5).Error);
            Assert.AreEqual(ErrorCode.NotFound, storage.GetFloat(2, 5).Error);
            Assert.AreEqual(ErrorCode.NotFound, storage.GetBool(3, 5).Error);

            storage.Dispose();
        }

        [Test]
        public void Remove_NegativeId_Fails()
        {
            var storage = new MetadataStorage(100, Allocator.Temp);
            var result = storage.Remove(-1);
            Assert.AreEqual(ErrorCode.InvalidParameter, result.Error);
            storage.Dispose();
        }

        [Test]
        public void LongKey_NoOverflow()
        {
            // fieldHash=100000, maxDocs=50000 → key > int.MaxValue
            var storage = new MetadataStorage(50000, Allocator.Temp);
            var result = storage.SetInt(100000, 123, 42);
            Assert.IsTrue(result.IsSuccess);

            var getResult = storage.GetInt(100000, 123);
            Assert.IsTrue(getResult.IsSuccess);
            Assert.AreEqual(42, getResult.Value);

            storage.Dispose();
        }
    }
}
