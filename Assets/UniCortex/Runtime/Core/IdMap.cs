using System;
using Unity.Collections;

namespace UniCortex
{
    /// <summary>
    /// 外部 ID (ulong) と内部 ID (int) の双方向マッピング + FreeList。
    /// 削除された内部 ID は sentinel (ulong.MaxValue) でマークし、FreeList で再利用する。
    /// </summary>
    public struct IdMap : IDisposable
    {
        /// <summary>外部 ID → 内部 ID のハッシュマップ。</summary>
        public NativeParallelHashMap<ulong, int> ExternalToInternal;

        /// <summary>内部 ID → 外部 ID の配列。削除済みは ulong.MaxValue。</summary>
        public NativeArray<ulong> InternalToExternal;

        /// <summary>再利用可能な内部 ID のリスト。</summary>
        public NativeList<int> FreeList;

        /// <summary>次に割り当てる内部 ID (FreeList が空の場合)。</summary>
        public int Count;

        /// <summary>最大容量。</summary>
        public int Capacity;

        /// <summary>削除済みエントリの sentinel 値。</summary>
        public const ulong Sentinel = ulong.MaxValue;

        public IdMap(int capacity, Allocator allocator)
        {
            ExternalToInternal = new NativeParallelHashMap<ulong, int>(capacity, allocator);
            InternalToExternal = new NativeArray<ulong>(capacity, allocator);
            FreeList = new NativeList<int>(allocator);
            Count = 0;
            Capacity = capacity;

            // sentinel で初期化
            for (int i = 0; i < capacity; i++)
            {
                InternalToExternal[i] = Sentinel;
            }
        }

        /// <summary>
        /// 外部 ID を登録し、内部 ID を割り当てる。
        /// </summary>
        public Result<int> Add(ulong externalId)
        {
            if (ExternalToInternal.ContainsKey(externalId))
                return Result<int>.Fail(ErrorCode.DuplicateId);

            int internalId;
            if (FreeList.Length > 0)
            {
                internalId = FreeList[FreeList.Length - 1];
                FreeList.RemoveAt(FreeList.Length - 1);
            }
            else
            {
                if (Count >= Capacity)
                    return Result<int>.Fail(ErrorCode.CapacityExceeded);

                internalId = Count;
                Count++;
            }

            ExternalToInternal.Add(externalId, internalId);
            InternalToExternal[internalId] = externalId;

            return Result<int>.Success(internalId);
        }

        /// <summary>
        /// 外部 ID から内部 ID を取得する。
        /// </summary>
        public Result<int> GetInternal(ulong externalId)
        {
            if (ExternalToInternal.TryGetValue(externalId, out int internalId))
                return Result<int>.Success(internalId);

            return Result<int>.Fail(ErrorCode.NotFound);
        }

        /// <summary>
        /// 内部 ID から外部 ID を取得する。
        /// </summary>
        public Result<ulong> GetExternal(int internalId)
        {
            if (internalId < 0 || internalId >= Capacity)
                return Result<ulong>.Fail(ErrorCode.NotFound);

            ulong externalId = InternalToExternal[internalId];
            if (externalId == Sentinel)
                return Result<ulong>.Fail(ErrorCode.NotFound);

            return Result<ulong>.Success(externalId);
        }

        /// <summary>
        /// 外部 ID を削除する。内部 ID は FreeList に追加される。
        /// </summary>
        public Result<int> Remove(ulong externalId)
        {
            if (!ExternalToInternal.TryGetValue(externalId, out int internalId))
                return Result<int>.Fail(ErrorCode.NotFound);

            ExternalToInternal.Remove(externalId);
            InternalToExternal[internalId] = Sentinel;
            FreeList.Add(internalId);

            return Result<int>.Success(internalId);
        }

        public void Dispose()
        {
            if (ExternalToInternal.IsCreated) ExternalToInternal.Dispose();
            if (InternalToExternal.IsCreated) InternalToExternal.Dispose();
            if (FreeList.IsCreated) FreeList.Dispose();
        }
    }
}
