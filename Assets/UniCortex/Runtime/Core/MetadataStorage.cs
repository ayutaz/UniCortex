using System;
using Unity.Collections;

namespace UniCortex
{
    /// <summary>
    /// カラムナ方式のメタデータストレージ。
    /// flat long キー (fieldHash * maxDocs + internalId) で Burst 互換。
    /// 07-filter-design.md セクション2 準拠。
    /// </summary>
    public struct MetadataStorage : IDisposable
    {
        NativeParallelHashMap<long, int> intValues;
        NativeParallelHashMap<long, float> floatValues;
        NativeParallelHashMap<long, bool> boolValues;
        int maxDocs;

        public MetadataStorage(int maxDocs, Allocator allocator)
        {
            this.maxDocs = maxDocs;
            intValues = new NativeParallelHashMap<long, int>(maxDocs, allocator);
            floatValues = new NativeParallelHashMap<long, float>(maxDocs, allocator);
            boolValues = new NativeParallelHashMap<long, bool>(maxDocs, allocator);
        }

        /// <summary>最大ドキュメント数を返す。</summary>
        public int MaxDocs => maxDocs;

        long ComputeKey(int fieldHash, int internalId)
        {
            return (long)fieldHash * maxDocs + internalId;
        }

        /// <summary>int 値を設定する。既存値がある場合は上書き。</summary>
        public Result<bool> SetInt(int fieldHash, int internalId, int value)
        {
            if (internalId < 0)
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            long key = ComputeKey(fieldHash, internalId);
            if (intValues.ContainsKey(key))
                intValues.Remove(key);
            intValues.Add(key, value);
            return Result<bool>.Success(true);
        }

        /// <summary>int 値を取得する。</summary>
        public Result<int> GetInt(int fieldHash, int internalId)
        {
            long key = ComputeKey(fieldHash, internalId);
            if (intValues.TryGetValue(key, out int value))
                return Result<int>.Success(value);
            return Result<int>.Fail(ErrorCode.NotFound);
        }

        /// <summary>float 値を設定する。既存値がある場合は上書き。</summary>
        public Result<bool> SetFloat(int fieldHash, int internalId, float value)
        {
            if (internalId < 0)
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            long key = ComputeKey(fieldHash, internalId);
            if (floatValues.ContainsKey(key))
                floatValues.Remove(key);
            floatValues.Add(key, value);
            return Result<bool>.Success(true);
        }

        /// <summary>float 値を取得する。</summary>
        public Result<float> GetFloat(int fieldHash, int internalId)
        {
            long key = ComputeKey(fieldHash, internalId);
            if (floatValues.TryGetValue(key, out float value))
                return Result<float>.Success(value);
            return Result<float>.Fail(ErrorCode.NotFound);
        }

        /// <summary>bool 値を設定する。既存値がある場合は上書き。</summary>
        public Result<bool> SetBool(int fieldHash, int internalId, bool value)
        {
            if (internalId < 0)
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            long key = ComputeKey(fieldHash, internalId);
            if (boolValues.ContainsKey(key))
                boolValues.Remove(key);
            boolValues.Add(key, value);
            return Result<bool>.Success(true);
        }

        /// <summary>bool 値を取得する。</summary>
        public Result<bool> GetBool(int fieldHash, int internalId)
        {
            long key = ComputeKey(fieldHash, internalId);
            if (boolValues.TryGetValue(key, out bool value))
                return Result<bool>.Success(value);
            return Result<bool>.Fail(ErrorCode.NotFound);
        }

        /// <summary>
        /// 指定 internalId の全メタデータを削除する。
        /// 全型 (Int/Float/Bool) の全フィールドから該当 internalId のエントリを除去する。
        /// </summary>
        public Result<bool> Remove(int internalId)
        {
            if (internalId < 0)
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            // NativeParallelHashMap のイテレータで全エントリをスキャンし、
            // internalId に一致するキーを削除する。
            // key % maxDocs == internalId のエントリを除去。
            RemoveFromMap(ref intValues, internalId);
            RemoveFromMapFloat(ref floatValues, internalId);
            RemoveFromMapBool(ref boolValues, internalId);

            return Result<bool>.Success(true);
        }

        void RemoveFromMap(ref NativeParallelHashMap<long, int> map, int internalId)
        {
            var keys = map.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] % maxDocs == internalId)
                    map.Remove(keys[i]);
            }
            keys.Dispose();
        }

        void RemoveFromMapFloat(ref NativeParallelHashMap<long, float> map, int internalId)
        {
            var keys = map.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] % maxDocs == internalId)
                    map.Remove(keys[i]);
            }
            keys.Dispose();
        }

        void RemoveFromMapBool(ref NativeParallelHashMap<long, bool> map, int internalId)
        {
            var keys = map.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] % maxDocs == internalId)
                    map.Remove(keys[i]);
            }
            keys.Dispose();
        }

        /// <summary>int 値の TryGet。Burst Job 内で使用可能。</summary>
        public bool TryGetInt(int fieldHash, int internalId, out int value)
        {
            long key = ComputeKey(fieldHash, internalId);
            return intValues.TryGetValue(key, out value);
        }

        /// <summary>float 値の TryGet。Burst Job 内で使用可能。</summary>
        public bool TryGetFloat(int fieldHash, int internalId, out float value)
        {
            long key = ComputeKey(fieldHash, internalId);
            return floatValues.TryGetValue(key, out value);
        }

        /// <summary>bool 値の TryGet。Burst Job 内で使用可能。</summary>
        public bool TryGetBool(int fieldHash, int internalId, out bool value)
        {
            long key = ComputeKey(fieldHash, internalId);
            return boolValues.TryGetValue(key, out value);
        }

        public void Dispose()
        {
            if (intValues.IsCreated) intValues.Dispose();
            if (floatValues.IsCreated) floatValues.Dispose();
            if (boolValues.IsCreated) boolValues.Dispose();
        }
    }
}
