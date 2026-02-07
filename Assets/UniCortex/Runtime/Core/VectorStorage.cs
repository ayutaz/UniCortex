using System;
using Unity.Collections;

namespace UniCortex
{
    /// <summary>
    /// SoA (flat NativeArray) ベースのベクトルストレージ。
    /// index = internalId * dimension + dimIndex でアクセスする。
    /// </summary>
    public struct VectorStorage : IDisposable
    {
        /// <summary>SoA flat array。全ベクトルを連続格納。</summary>
        public NativeArray<float> Data;

        /// <summary>ベクトルの次元数。</summary>
        public int Dimension;

        /// <summary>格納済みベクトル数（IdMap の Count と同期）。</summary>
        public int Count;

        /// <summary>最大格納可能数。</summary>
        public int Capacity;

        public VectorStorage(int capacity, int dimension, Allocator allocator)
        {
            Data = new NativeArray<float>(capacity * dimension, allocator);
            Dimension = dimension;
            Count = 0;
            Capacity = capacity;
        }

        /// <summary>
        /// ベクトルを追加する。NaN/Inf 検証必須。
        /// </summary>
        public Result<bool> Add(int internalId, NativeArray<float> vector)
        {
            if (vector.Length != Dimension)
                return Result<bool>.Fail(ErrorCode.DimensionMismatch);

            if (internalId < 0 || internalId >= Capacity)
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            if (DistanceFunctions.ContainsNanOrInf(ref vector))
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            long offset = (long)internalId * Dimension;
            for (int i = 0; i < Dimension; i++)
            {
                Data[(int)(offset + i)] = vector[i];
            }

            return Result<bool>.Success(true);
        }

        /// <summary>
        /// ベクトルをゼロコピー参照する。
        /// internalId が範囲外なら default(NativeSlice)。
        /// </summary>
        public NativeSlice<float> Get(int internalId)
        {
            if (internalId < 0 || internalId >= Capacity)
                return default;

            long offset = (long)internalId * Dimension;
            return Data.Slice((int)offset, Dimension);
        }

        /// <summary>
        /// ベクトルを上書き更新する。
        /// </summary>
        public Result<bool> Update(int internalId, NativeArray<float> vector)
        {
            if (vector.Length != Dimension)
                return Result<bool>.Fail(ErrorCode.DimensionMismatch);

            if (internalId < 0 || internalId >= Capacity)
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            if (DistanceFunctions.ContainsNanOrInf(ref vector))
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            long offset = (long)internalId * Dimension;
            for (int i = 0; i < Dimension; i++)
            {
                Data[(int)(offset + i)] = vector[i];
            }

            return Result<bool>.Success(true);
        }

        public void Dispose()
        {
            if (Data.IsCreated) Data.Dispose();
        }
    }
}
