using NUnit.Framework;
using Unity.Collections;

namespace UniCortex.Tests.Editor.Core
{
    public class DistanceFunctionsTests
    {
        [Test]
        public void EuclideanSq_KnownVectors_CorrectResult()
        {
            var a = new NativeArray<float>(4, Allocator.Temp);
            var b = new NativeArray<float>(4, Allocator.Temp);
            a[0] = 1; a[1] = 2; a[2] = 3; a[3] = 4;
            b[0] = 5; b[1] = 6; b[2] = 7; b[3] = 8;

            var sa = a.Slice();
            var sb = b.Slice();
            float dist = DistanceFunctions.EuclideanSq(ref sa, ref sb, 4);
            // (5-1)^2 + (6-2)^2 + (7-3)^2 + (8-4)^2 = 16+16+16+16 = 64
            Assert.AreEqual(64f, dist, 1e-6f);

            a.Dispose();
            b.Dispose();
        }

        [Test]
        public void EuclideanSq_NonMultipleOf4Dim()
        {
            var a = new NativeArray<float>(3, Allocator.Temp);
            var b = new NativeArray<float>(3, Allocator.Temp);
            a[0] = 1; a[1] = 0; a[2] = 0;
            b[0] = 0; b[1] = 1; b[2] = 0;

            var sa = a.Slice();
            var sb = b.Slice();
            float dist = DistanceFunctions.EuclideanSq(ref sa, ref sb, 3);
            // 1+1+0 = 2
            Assert.AreEqual(2f, dist, 1e-6f);

            a.Dispose();
            b.Dispose();
        }

        [Test]
        public void Cosine_OrthogonalVectors_ReturnsOne()
        {
            var a = new NativeArray<float>(4, Allocator.Temp);
            var b = new NativeArray<float>(4, Allocator.Temp);
            a[0] = 1; a[1] = 0; a[2] = 0; a[3] = 0;
            b[0] = 0; b[1] = 1; b[2] = 0; b[3] = 0;

            var sa = a.Slice();
            var sb = b.Slice();
            float dist = DistanceFunctions.Cosine(ref sa, ref sb, 4);
            // 1 - dot(a,b) = 1 - 0 = 1
            Assert.AreEqual(1f, dist, 1e-6f);

            a.Dispose();
            b.Dispose();
        }

        [Test]
        public void Cosine_IdenticalNormalized_ReturnsZero()
        {
            var a = new NativeArray<float>(4, Allocator.Temp);
            a[0] = 0.5f; a[1] = 0.5f; a[2] = 0.5f; a[3] = 0.5f;

            var sa = a.Slice();
            var sb = a.Slice();
            float dist = DistanceFunctions.Cosine(ref sa, ref sb, 4);
            // 1 - dot(a,a) = 1 - 1 = 0
            Assert.AreEqual(0f, dist, 1e-6f);

            a.Dispose();
        }

        [Test]
        public void DotProduct_KnownVectors_NegativeDot()
        {
            var a = new NativeArray<float>(4, Allocator.Temp);
            var b = new NativeArray<float>(4, Allocator.Temp);
            a[0] = 1; a[1] = 2; a[2] = 3; a[3] = 4;
            b[0] = 1; b[1] = 1; b[2] = 1; b[3] = 1;

            var sa = a.Slice();
            var sb = b.Slice();
            float dist = DistanceFunctions.DotProduct(ref sa, ref sb, 4);
            // -(1+2+3+4) = -10
            Assert.AreEqual(-10f, dist, 1e-6f);

            a.Dispose();
            b.Dispose();
        }

        [Test]
        public void ComputeDistance_Dispatches_Correctly()
        {
            var a = new NativeArray<float>(4, Allocator.Temp);
            var b = new NativeArray<float>(4, Allocator.Temp);
            a[0] = 1; a[1] = 0; a[2] = 0; a[3] = 0;
            b[0] = 0; b[1] = 1; b[2] = 0; b[3] = 0;

            var sa = a.Slice();
            var sb = b.Slice();

            float eucDist = DistanceFunctions.ComputeDistance(ref sa, ref sb, 4, DistanceType.EuclideanSq);
            Assert.AreEqual(2f, eucDist, 1e-6f);

            float cosDist = DistanceFunctions.ComputeDistance(ref sa, ref sb, 4, DistanceType.Cosine);
            Assert.AreEqual(1f, cosDist, 1e-6f);

            float dotDist = DistanceFunctions.ComputeDistance(ref sa, ref sb, 4, DistanceType.DotProduct);
            Assert.AreEqual(0f, dotDist, 1e-6f);

            a.Dispose();
            b.Dispose();
        }

        [Test]
        public void ContainsNanOrInf_DetectsNaN()
        {
            var data = new NativeArray<float>(4, Allocator.Temp);
            data[0] = 1; data[1] = float.NaN; data[2] = 3; data[3] = 4;
            Assert.IsTrue(DistanceFunctions.ContainsNanOrInf(ref data));
            data.Dispose();
        }

        [Test]
        public void ContainsNanOrInf_DetectsInf()
        {
            var data = new NativeArray<float>(4, Allocator.Temp);
            data[0] = 1; data[1] = 2; data[2] = float.PositiveInfinity; data[3] = 4;
            Assert.IsTrue(DistanceFunctions.ContainsNanOrInf(ref data));
            data.Dispose();
        }

        [Test]
        public void ContainsNanOrInf_ValidData_ReturnsFalse()
        {
            var data = new NativeArray<float>(4, Allocator.Temp);
            data[0] = 1; data[1] = 2; data[2] = 3; data[3] = 4;
            Assert.IsFalse(DistanceFunctions.ContainsNanOrInf(ref data));
            data.Dispose();
        }
    }
}
