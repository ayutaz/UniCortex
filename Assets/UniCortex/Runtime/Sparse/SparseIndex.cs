using System;
using Unity.Collections;

namespace UniCortex.Sparse
{
    /// <summary>
    /// スパースベクトルの転置インデックス。
    /// NativeParallelMultiHashMap でポスティングリストを管理する。
    /// </summary>
    public struct SparseIndex : IDisposable
    {
        /// <summary>
        /// 転置インデックス本体。Key: 次元 Index, Value: ポスティング。
        /// </summary>
        public NativeParallelMultiHashMap<int, SparsePosting> InvertedIndex;

        /// <summary>ソフト削除済みドキュメントの ID セット。</summary>
        public NativeParallelHashSet<int> DeletedIds;

        /// <summary>索引済みドキュメント数（ソフト削除含む）。</summary>
        public int DocumentCount;

        public SparseIndex(int initialCapacity, Allocator allocator)
        {
            InvertedIndex = new NativeParallelMultiHashMap<int, SparsePosting>(
                initialCapacity * 100, allocator); // 平均100要素/doc想定
            DeletedIds = new NativeParallelHashSet<int>(initialCapacity / 4, allocator);
            DocumentCount = 0;
        }

        /// <summary>
        /// ドキュメントを転置インデックスに追加する。
        /// </summary>
        public Result<bool> Add(int internalId, NativeArray<SparseElement> vector)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                if (vector[i].Index < 0)
                    return Result<bool>.Fail(ErrorCode.InvalidParameter);
            }

            for (int i = 0; i < vector.Length; i++)
            {
                InvertedIndex.Add(vector[i].Index, new SparsePosting
                {
                    InternalId = internalId,
                    Value = vector[i].Value
                });
            }

            DocumentCount++;
            return Result<bool>.Success(true);
        }

        /// <summary>
        /// ドキュメントをソフト削除する。
        /// </summary>
        public void Remove(int internalId)
        {
            DeletedIds.Add(internalId);
        }

        public void Dispose()
        {
            if (InvertedIndex.IsCreated) InvertedIndex.Dispose();
            if (DeletedIds.IsCreated) DeletedIds.Dispose();
        }
    }
}
