using System;
using Unity.Collections;
using Unity.Mathematics;
using UniCortex.Hnsw;
using UniCortex.Sparse;
using UniCortex.FullText;
using UniCortex.Hybrid;
using UniCortex.Filter;

namespace UniCortex
{
    /// <summary>
    /// UniCortex のファサード API。
    /// 全コンポーネントを集約し、統一された高レベル API を提供する。
    /// </summary>
    public class UniCortexDatabase : IDisposable
    {
        DatabaseConfig config;
        IdMap idMap;
        VectorStorage vectorStorage;
        MetadataStorage metadataStorage;
        HnswGraph hnswGraph;
        SparseIndex sparseIndex;
        BM25Index bm25Index;
        Unity.Mathematics.Random rng;
        bool isBuilt;
        bool isDisposed;

        /// <summary>現在の登録ドキュメント数。</summary>
        public int DocumentCount => idMap.ExternalToInternal.Count();

        /// <summary>データベースの設定。</summary>
        public DatabaseConfig Config => config;

        public UniCortexDatabase(DatabaseConfig config)
        {
            this.config = config;
            int cap = config.Capacity;

            idMap = new IdMap(cap, Allocator.Persistent);
            metadataStorage = new MetadataStorage(cap, Allocator.Persistent);

            // Dense 検索用
            if (config.Dimension > 0)
            {
                vectorStorage = new VectorStorage(cap, config.Dimension, Allocator.Persistent);
                int maxLevel = HnswConfig.CalculateMaxLevel(cap, config.HnswConfig.ML);
                hnswGraph = new HnswGraph(cap, config.HnswConfig.M, config.HnswConfig.M0, maxLevel, Allocator.Persistent);
            }

            sparseIndex = new SparseIndex(cap, Allocator.Persistent);
            bm25Index = new BM25Index(cap, Allocator.Persistent);
            rng = new Unity.Mathematics.Random(42);
            isBuilt = false;
            isDisposed = false;
        }

        /// <summary>
        /// ドキュメントを追加する。
        /// </summary>
        /// <param name="id">外部 ID。</param>
        /// <param name="denseVector">Dense ベクトル (省略可)。</param>
        /// <param name="sparseVector">Sparse ベクトル (省略可)。</param>
        /// <param name="text">BM25 テキスト UTF-8 バイト列 (省略可)。</param>
        public Result<int> Add(
            ulong id,
            NativeArray<float> denseVector = default,
            NativeArray<SparseElement> sparseVector = default,
            NativeArray<byte> text = default)
        {
            if (isDisposed)
                return Result<int>.Fail(ErrorCode.InvalidParameter);

            // ID マッピング
            var idResult = idMap.Add(id);
            if (!idResult.IsSuccess)
                return idResult;

            int internalId = idResult.Value;

            // Dense ベクトル
            if (denseVector.IsCreated && denseVector.Length > 0 && config.Dimension > 0)
            {
                var addResult = vectorStorage.Add(internalId, denseVector);
                if (!addResult.IsSuccess)
                {
                    idMap.Remove(id);
                    return Result<int>.Fail(addResult.Error);
                }

                // HNSW グラフに挿入
                var insertResult = HnswBuilder.Insert(
                    ref hnswGraph, ref vectorStorage, internalId, denseVector,
                    config.HnswConfig, config.DistanceType, ref rng);
                if (!insertResult.IsSuccess)
                {
                    idMap.Remove(id);
                    return Result<int>.Fail(insertResult.Error);
                }
            }

            // Sparse ベクトル
            bool hasDense = denseVector.IsCreated && denseVector.Length > 0 && config.Dimension > 0;
            if (sparseVector.IsCreated && sparseVector.Length > 0)
            {
                var sparseResult = sparseIndex.Add(internalId, sparseVector);
                if (!sparseResult.IsSuccess)
                {
                    // ロールバック: Dense + HNSW
                    if (hasDense)
                        HnswSearcher.Delete(ref hnswGraph, internalId);
                    idMap.Remove(id);
                    return Result<int>.Fail(sparseResult.Error);
                }
            }

            // BM25 テキスト
            bool hasSparse = sparseVector.IsCreated && sparseVector.Length > 0;
            if (text.IsCreated && text.Length > 0)
            {
                var tokenHashes = Tokenizer.Tokenize(text, Allocator.Temp);
                if (tokenHashes.Length > 0)
                {
                    var bm25Result = bm25Index.Add(internalId, tokenHashes);
                    tokenHashes.Dispose();
                    if (!bm25Result.IsSuccess)
                    {
                        // ロールバック: Dense + HNSW + Sparse
                        if (hasSparse)
                            sparseIndex.Remove(internalId);
                        if (hasDense)
                            HnswSearcher.Delete(ref hnswGraph, internalId);
                        idMap.Remove(id);
                        return Result<int>.Fail(bm25Result.Error);
                    }
                }
                else
                {
                    tokenHashes.Dispose();
                }
            }

            return Result<int>.Success(internalId);
        }

        /// <summary>
        /// ドキュメントを削除する。
        /// </summary>
        public Result<int> Delete(ulong id)
        {
            if (isDisposed)
                return Result<int>.Fail(ErrorCode.InvalidParameter);

            var idResult = idMap.GetInternal(id);
            if (!idResult.IsSuccess)
                return Result<int>.Fail(ErrorCode.NotFound);

            int internalId = idResult.Value;

            // 各インデックスからソフト削除
            if (config.Dimension > 0)
                HnswSearcher.Delete(ref hnswGraph, internalId);

            sparseIndex.Remove(internalId);
            bm25Index.Remove(internalId);
            metadataStorage.Remove(internalId);

            // ID マッピングから削除
            idMap.Remove(id);

            return Result<int>.Success(internalId);
        }

        /// <summary>
        /// ドキュメントを更新する。
        /// Dense ベクトルは上書き更新 (HNSW グラフ構造は維持)。
        /// Sparse / BM25 はソフト削除 + 再追加。
        /// </summary>
        public Result<int> Update(
            ulong id,
            NativeArray<float> denseVector = default,
            NativeArray<SparseElement> sparseVector = default,
            NativeArray<byte> text = default)
        {
            if (isDisposed)
                return Result<int>.Fail(ErrorCode.InvalidParameter);

            var idResult = idMap.GetInternal(id);
            if (!idResult.IsSuccess)
                return Result<int>.Fail(ErrorCode.NotFound);

            int internalId = idResult.Value;

            // Dense ベクトルは上書き更新（グラフ構造はそのまま、距離再計算で反映）
            if (denseVector.IsCreated && denseVector.Length > 0 && config.Dimension > 0)
            {
                var updateResult = vectorStorage.Update(internalId, denseVector);
                if (!updateResult.IsSuccess)
                    return Result<int>.Fail(updateResult.Error);
            }

            // Sparse はソフト削除 + 再追加
            if (sparseVector.IsCreated && sparseVector.Length > 0)
            {
                sparseIndex.Remove(internalId);
                var sparseResult = sparseIndex.Add(internalId, sparseVector);
                if (!sparseResult.IsSuccess)
                    return Result<int>.Fail(sparseResult.Error);
            }

            // BM25 はソフト削除 + 再追加
            if (text.IsCreated && text.Length > 0)
            {
                bm25Index.Remove(internalId);
                var tokenHashes = Tokenizer.Tokenize(text, Allocator.Temp);
                if (tokenHashes.Length > 0)
                {
                    var bm25Result = bm25Index.Add(internalId, tokenHashes);
                    tokenHashes.Dispose();
                    if (!bm25Result.IsSuccess)
                        return Result<int>.Fail(bm25Result.Error);
                }
                else
                {
                    tokenHashes.Dispose();
                }
            }

            return Result<int>.Success(internalId);
        }

        /// <summary>
        /// Dense ベクトル検索 (HNSW)。
        /// </summary>
        public NativeArray<SearchResult> SearchDense(NativeArray<float> query, SearchParams param)
        {
            if (isDisposed || config.Dimension <= 0 || !isBuilt)
                return new NativeArray<SearchResult>(0, Allocator.TempJob);

            int efSearch = param.EfSearch > 0 ? param.EfSearch : 50;
            return HnswSearcher.Search(
                ref hnswGraph, ref vectorStorage, query,
                param.K, efSearch, param.DistanceType, Allocator.TempJob);
        }

        /// <summary>
        /// Sparse ベクトル検索。
        /// </summary>
        public NativeArray<SearchResult> SearchSparse(NativeArray<SparseElement> query, int k)
        {
            if (isDisposed || !isBuilt)
                return new NativeArray<SearchResult>(0, Allocator.TempJob);

            return SparseSearcher.Search(ref sparseIndex, query, k, Allocator.TempJob);
        }

        /// <summary>
        /// BM25 全文検索。
        /// </summary>
        public NativeArray<SearchResult> SearchBM25(NativeArray<byte> queryText, int k)
        {
            if (isDisposed || !isBuilt)
                return new NativeArray<SearchResult>(0, Allocator.TempJob);

            var tokenHashes = Tokenizer.Tokenize(queryText, Allocator.Temp);
            if (tokenHashes.Length == 0)
            {
                tokenHashes.Dispose();
                return new NativeArray<SearchResult>(0, Allocator.TempJob);
            }

            var results = BM25Searcher.Search(
                ref bm25Index, tokenHashes, k,
                config.BM25K1, config.BM25B, Allocator.TempJob);
            tokenHashes.Dispose();
            return results;
        }

        /// <summary>
        /// ハイブリッド検索 (RRF)。
        /// </summary>
        public Result<NativeArray<SearchResult>> SearchHybrid(HybridSearchParams param)
        {
            if (isDisposed || !isBuilt)
                return Result<NativeArray<SearchResult>>.Fail(ErrorCode.IndexNotBuilt);

            return HybridSearcher.Execute(
                ref hnswGraph, ref vectorStorage,
                ref sparseIndex, ref bm25Index,
                param, Allocator.TempJob);
        }

        /// <summary>
        /// 外部 ID から内部 ID を取得する。
        /// </summary>
        public Result<int> GetInternalId(ulong externalId)
        {
            return idMap.GetInternal(externalId);
        }

        /// <summary>
        /// 内部 ID から外部 ID を取得する。
        /// </summary>
        public Result<ulong> GetExternalId(int internalId)
        {
            return idMap.GetExternal(internalId);
        }

        /// <summary>
        /// メタデータ (int) を設定する。
        /// </summary>
        public Result<bool> SetMetadataInt(ulong docId, int fieldHash, int value)
        {
            var idResult = idMap.GetInternal(docId);
            if (!idResult.IsSuccess)
                return Result<bool>.Fail(ErrorCode.NotFound);
            return metadataStorage.SetInt(fieldHash, idResult.Value, value);
        }

        /// <summary>
        /// メタデータ (float) を設定する。
        /// </summary>
        public Result<bool> SetMetadataFloat(ulong docId, int fieldHash, float value)
        {
            var idResult = idMap.GetInternal(docId);
            if (!idResult.IsSuccess)
                return Result<bool>.Fail(ErrorCode.NotFound);
            return metadataStorage.SetFloat(fieldHash, idResult.Value, value);
        }

        /// <summary>
        /// メタデータ (int) を取得する。
        /// </summary>
        public Result<int> GetMetadataInt(ulong docId, int fieldHash)
        {
            var idResult = idMap.GetInternal(docId);
            if (!idResult.IsSuccess)
                return Result<int>.Fail(ErrorCode.NotFound);
            return metadataStorage.GetInt(fieldHash, idResult.Value);
        }

        /// <summary>
        /// メタデータ (float) を取得する。
        /// </summary>
        public Result<float> GetMetadataFloat(ulong docId, int fieldHash)
        {
            var idResult = idMap.GetInternal(docId);
            if (!idResult.IsSuccess)
                return Result<float>.Fail(ErrorCode.NotFound);
            return metadataStorage.GetFloat(fieldHash, idResult.Value);
        }

        /// <summary>
        /// メタデータ (bool) を取得する。
        /// </summary>
        public Result<bool> GetMetadataBool(ulong docId, int fieldHash)
        {
            var idResult = idMap.GetInternal(docId);
            if (!idResult.IsSuccess)
                return Result<bool>.Fail(ErrorCode.NotFound);
            return metadataStorage.GetBool(fieldHash, idResult.Value);
        }

        /// <summary>
        /// メタデータ (bool) を設定する。
        /// </summary>
        public Result<bool> SetMetadataBool(ulong docId, int fieldHash, bool value)
        {
            var idResult = idMap.GetInternal(docId);
            if (!idResult.IsSuccess)
                return Result<bool>.Fail(ErrorCode.NotFound);
            return metadataStorage.SetBool(fieldHash, idResult.Value, value);
        }

        /// <summary>
        /// インデックスを構築する (現在は即時挿入のため no-op)。
        /// </summary>
        public void Build()
        {
            isBuilt = true;
        }

        /// <summary>isBuilt フラグを設定する (復元用)。</summary>
        internal void SetBuilt(bool value) { isBuilt = value; }

        // --- 永続化用内部アクセッサ (IndexSerializer から使用) ---

        /// <summary>内部 VectorStorage を返す。</summary>
        internal VectorStorage GetVectorStorage() => vectorStorage;

        /// <summary>内部 HnswGraph を返す。</summary>
        internal HnswGraph GetHnswGraph() => hnswGraph;

        /// <summary>内部 HnswGraph の参照を返す (復元用)。</summary>
        internal ref HnswGraph GetHnswGraphRef() => ref hnswGraph;

        /// <summary>内部 SparseIndex を返す。</summary>
        internal SparseIndex GetSparseIndex() => sparseIndex;

        /// <summary>内部 SparseIndex の参照を返す (復元用)。</summary>
        internal ref SparseIndex GetSparseIndexRef() => ref sparseIndex;

        /// <summary>内部 BM25Index を返す。</summary>
        internal BM25Index GetBm25Index() => bm25Index;

        /// <summary>内部 BM25Index の参照を返す (復元用)。</summary>
        internal ref BM25Index GetBm25IndexRef() => ref bm25Index;

        /// <summary>内部 MetadataStorage を返す。</summary>
        internal MetadataStorage GetMetadataStorage() => metadataStorage;

        /// <summary>内部 MetadataStorage の参照を返す (復元用)。</summary>
        internal ref MetadataStorage GetMetadataStorageRef() => ref metadataStorage;

        /// <summary>内部 IdMap を返す。</summary>
        internal IdMap GetIdMap() => idMap;

        /// <summary>内部 IdMap の参照を返す (復元用)。</summary>
        internal ref IdMap GetIdMapRef() => ref idMap;

        /// <summary>
        /// 全リソースを解放する。
        /// </summary>
        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            idMap.Dispose();
            metadataStorage.Dispose();
            if (config.Dimension > 0)
            {
                vectorStorage.Dispose();
                hnswGraph.Dispose();
            }
            sparseIndex.Dispose();
            bm25Index.Dispose();
        }
    }
}
