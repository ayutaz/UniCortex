using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace UniCortex
{
    /// <summary>
    /// Burst 互換の距離計算関数群。float4 による SIMD auto-vectorize。
    /// </summary>
    [BurstCompile]
    public static class DistanceFunctions
    {
        /// <summary>
        /// ユークリッド距離の二乗を計算する。
        /// </summary>
        [BurstCompile]
        public static float EuclideanSq(ref NativeSlice<float> a, ref NativeSlice<float> b, int dim)
        {
            float4 sum = float4.zero;
            int i = 0;
            for (; i + 3 < dim; i += 4)
            {
                float4 va = new float4(a[i], a[i + 1], a[i + 2], a[i + 3]);
                float4 vb = new float4(b[i], b[i + 1], b[i + 2], b[i + 3]);
                float4 diff = va - vb;
                sum += diff * diff;
            }
            float result = math.csum(sum);
            for (; i < dim; i++)
            {
                float d = a[i] - b[i];
                result += d * d;
            }
            return result;
        }

        /// <summary>
        /// コサイン距離 (1 - cosine_similarity) を計算する。正規化済みベクトル前提。
        /// </summary>
        [BurstCompile]
        public static float Cosine(ref NativeSlice<float> a, ref NativeSlice<float> b, int dim)
        {
            float4 sum = float4.zero;
            int i = 0;
            for (; i + 3 < dim; i += 4)
            {
                float4 va = new float4(a[i], a[i + 1], a[i + 2], a[i + 3]);
                float4 vb = new float4(b[i], b[i + 1], b[i + 2], b[i + 3]);
                sum += va * vb;
            }
            float dot = math.csum(sum);
            for (; i < dim; i++)
            {
                dot += a[i] * b[i];
            }
            return 1.0f - dot;
        }

        /// <summary>
        /// 負の内積を計算する。内積が大きいほど類似 → 負値で統一規約維持。
        /// </summary>
        [BurstCompile]
        public static float DotProduct(ref NativeSlice<float> a, ref NativeSlice<float> b, int dim)
        {
            float4 sum = float4.zero;
            int i = 0;
            for (; i + 3 < dim; i += 4)
            {
                float4 va = new float4(a[i], a[i + 1], a[i + 2], a[i + 3]);
                float4 vb = new float4(b[i], b[i + 1], b[i + 2], b[i + 3]);
                sum += va * vb;
            }
            float dot = math.csum(sum);
            for (; i < dim; i++)
            {
                dot += a[i] * b[i];
            }
            return -dot;
        }

        /// <summary>
        /// DistanceType に応じた距離を計算する。
        /// </summary>
        [BurstCompile]
        public static float ComputeDistance(
            ref NativeSlice<float> a, ref NativeSlice<float> b, int dim, DistanceType type)
        {
            switch (type)
            {
                case DistanceType.EuclideanSq: return EuclideanSq(ref a, ref b, dim);
                case DistanceType.Cosine:      return Cosine(ref a, ref b, dim);
                case DistanceType.DotProduct:   return DotProduct(ref a, ref b, dim);
                default:                        return EuclideanSq(ref a, ref b, dim);
            }
        }

        /// <summary>
        /// NaN または Inf を含むかチェックする。入力バリデーション用。
        /// </summary>
        [BurstCompile]
        public static bool ContainsNanOrInf(ref NativeArray<float> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (math.isnan(data[i]) || math.isinf(data[i]))
                    return true;
            }
            return false;
        }
    }
}
