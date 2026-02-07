using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace UniCortex.Tests.Editor
{
    /// <summary>
    /// Phase 0 スモークテスト: Burst/Collections/Mathematics の参照解決を検証。
    /// </summary>
    public class SetupSmokeTest
    {
        [Test]
        public void BurstCollectionsMathematics_ReferencesResolved()
        {
            // Unity.Collections
            var array = new NativeArray<float>(4, Allocator.Temp);
            Assert.AreEqual(4, array.Length);

            // Unity.Mathematics
            float4 v = new float4(1, 2, 3, 4);
            Assert.AreEqual(10f, math.csum(v));

            array.Dispose();
        }
    }
}
