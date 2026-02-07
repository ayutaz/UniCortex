using NUnit.Framework;

namespace UniCortex.Tests.Editor.Core
{
    public class ErrorCodeTests
    {
        [Test]
        public void Result_Success_ReturnsValue()
        {
            var result = Result<int>.Success(42);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ErrorCode.None, result.Error);
            Assert.AreEqual(42, result.Value);
        }

        [Test]
        public void Result_Fail_ReturnsError()
        {
            var result = Result<int>.Fail(ErrorCode.NotFound);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(ErrorCode.NotFound, result.Error);
        }

        [Test]
        public void Result_Fail_DefaultValue()
        {
            var result = Result<int>.Fail(ErrorCode.DuplicateId);
            Assert.AreEqual(0, result.Value);
        }

        [Test]
        public void Result_Bool_Success()
        {
            var result = Result<bool>.Success(true);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.Value);
        }
    }
}
