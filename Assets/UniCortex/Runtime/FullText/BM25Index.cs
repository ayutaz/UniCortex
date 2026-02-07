using System;
using Unity.Collections;

namespace UniCortex.FullText
{
    /// <summary>
    /// 転置インデックスの Posting エントリ。
    /// </summary>
    public struct BM25Posting
    {
        /// <summary>内部ドキュメント ID。</summary>
        public int InternalId;

        /// <summary>ドキュメント内でのトークン出現頻度。</summary>
        public int TermFrequency;
    }

    /// <summary>
    /// BM25 全文検索インデックス。
    /// </summary>
    public struct BM25Index : IDisposable
    {
        /// <summary>転置インデックス: tokenHash → Posting リスト。</summary>
        public NativeParallelMultiHashMap<uint, BM25Posting> InvertedIndex;

        /// <summary>Document Frequency: tokenHash → そのトークンを含むドキュメント数。</summary>
        public NativeParallelHashMap<uint, int> DocumentFrequency;

        /// <summary>各ドキュメントのトークン数。</summary>
        public NativeArray<int> DocumentLengths;

        /// <summary>ソフト削除済み ID セット。</summary>
        public NativeParallelHashSet<int> DeletedIds;

        /// <summary>登録済みドキュメント総数。</summary>
        public int TotalDocuments;

        /// <summary>平均文書長。</summary>
        public float AverageDocumentLength;

        /// <summary>全ドキュメントのトークン数合計。</summary>
        int totalTokenCount;

        public BM25Index(int capacity, Allocator allocator)
        {
            InvertedIndex = new NativeParallelMultiHashMap<uint, BM25Posting>(
                capacity * 100, allocator);
            DocumentFrequency = new NativeParallelHashMap<uint, int>(
                capacity * 10, allocator);
            DocumentLengths = new NativeArray<int>(capacity, allocator);
            DeletedIds = new NativeParallelHashSet<int>(capacity / 4, allocator);
            TotalDocuments = 0;
            AverageDocumentLength = 0;
            totalTokenCount = 0;
        }

        /// <summary>
        /// ドキュメントを追加する。トークンハッシュのリストから転置インデックスを構築する。
        /// </summary>
        public Result<bool> Add(int internalId, NativeList<uint> tokenHashes)
        {
            if (internalId < 0 || internalId >= DocumentLengths.Length)
                return Result<bool>.Fail(ErrorCode.InvalidParameter);

            // 各トークンの TF をカウント
            var tfMap = new NativeParallelHashMap<uint, int>(tokenHashes.Length, Allocator.Temp);
            for (int i = 0; i < tokenHashes.Length; i++)
            {
                uint hash = tokenHashes[i];
                if (tfMap.TryGetValue(hash, out int count))
                {
                    tfMap.Remove(hash);
                    tfMap.Add(hash, count + 1);
                }
                else
                {
                    tfMap.Add(hash, 1);
                }
            }

            // 転置インデックスに Posting を追加
            var keys = tfMap.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
            {
                uint tokenHash = keys[i];
                int tf = tfMap[tokenHash];

                InvertedIndex.Add(tokenHash, new BM25Posting
                {
                    InternalId = internalId,
                    TermFrequency = tf
                });

                // DF 更新
                if (DocumentFrequency.TryGetValue(tokenHash, out int df))
                {
                    DocumentFrequency.Remove(tokenHash);
                    DocumentFrequency.Add(tokenHash, df + 1);
                }
                else
                {
                    DocumentFrequency.Add(tokenHash, 1);
                }
            }
            keys.Dispose();
            tfMap.Dispose();

            // ドキュメント統計更新
            DocumentLengths[internalId] = tokenHashes.Length;
            totalTokenCount += tokenHashes.Length;
            TotalDocuments++;
            AverageDocumentLength = TotalDocuments > 0
                ? (float)totalTokenCount / TotalDocuments
                : 0f;

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
            if (DocumentFrequency.IsCreated) DocumentFrequency.Dispose();
            if (DocumentLengths.IsCreated) DocumentLengths.Dispose();
            if (DeletedIds.IsCreated) DeletedIds.Dispose();
        }
    }
}
