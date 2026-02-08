using Unity.Collections;
using Unity.Mathematics;
using UniCortex.Hnsw;
using UniCortex.Sparse;
using UniCortex.FullText;

namespace UniCortex.Hybrid
{
    /// <summary>
    /// ハイブリッド検索オーケストレータ。
    /// 複数のサブ検索を実行し、RRF で結果をマージして Top-K を返す。
    /// </summary>
    public static class HybridSearcher
    {
        /// <summary>
        /// ハイブリッド検索を実行する。
        /// </summary>
        public static Result<NativeArray<SearchResult>> Execute(
            ref HnswGraph hnswGraph,
            ref VectorStorage vectorStorage,
            ref SparseIndex sparseIndex,
            ref BM25Index bm25Index,
            HybridSearchParams param,
            Allocator resultAllocator)
        {
            if (param.K <= 0)
                return Result<NativeArray<SearchResult>>.Fail(ErrorCode.InvalidParameter);

            int subK = math.max(param.SubSearchK, param.K);

            bool hasDense = param.DenseQuery.IsCreated && param.DenseQuery.Length > 0;
            bool hasSparse = param.SparseQuery.IsCreated && param.SparseQuery.Length > 0;
            bool hasBm25 = param.TextQuery.IsCreated && param.TextQuery.Length > 0;

            if (!hasDense && !hasSparse && !hasBm25)
                return Result<NativeArray<SearchResult>>.Fail(ErrorCode.InvalidParameter);

            // 各サブ検索を実行
            NativeArray<SearchResult> denseResults = default;
            NativeArray<SearchResult> sparseResults = default;
            NativeArray<SearchResult> bm25Results = default;

            int denseCount = 0;
            int sparseCount = 0;
            int bm25Count = 0;

            bool denseOk = false;
            bool sparseOk = false;
            bool bm25Ok = false;

            // Dense 検索
            if (hasDense)
            {
                int efSearch = param.DenseParams.EfSearch > 0 ? param.DenseParams.EfSearch : 50;
                var denseResult = HnswSearcher.Search(
                    ref hnswGraph, ref vectorStorage, param.DenseQuery,
                    subK, efSearch, param.DenseParams.DistanceType, Allocator.TempJob);

                if (denseResult.Length > 0)
                {
                    denseResults = denseResult;
                    denseCount = denseResult.Length;
                    denseOk = true;
                }
                else
                {
                    denseResults = new NativeArray<SearchResult>(0, Allocator.TempJob);
                    // 空グラフの場合は結果0件として許容、データがあるのに0件なら失敗
                    denseOk = (hnswGraph.Count == 0);
                }
            }

            // Sparse 検索
            if (hasSparse)
            {
                var sparseResult = SparseSearcher.Search(ref sparseIndex, param.SparseQuery, subK, Allocator.TempJob);
                if (sparseResult.Length > 0)
                {
                    sparseResults = sparseResult;
                    sparseCount = sparseResult.Length;
                    sparseOk = true;
                }
                else
                {
                    sparseResults = new NativeArray<SearchResult>(0, Allocator.TempJob);
                }
            }

            // BM25 検索
            if (hasBm25)
            {
                // テキストをトークナイズしてハッシュ列に変換
                var tokenHashes = Tokenizer.Tokenize(param.TextQuery, Allocator.TempJob);
                if (tokenHashes.Length > 0)
                {
                    var bm25Result = BM25Searcher.Search(
                        ref bm25Index, tokenHashes, subK,
                        BM25Searcher.DefaultK1, BM25Searcher.DefaultB, Allocator.TempJob);
                    if (bm25Result.Length > 0)
                    {
                        bm25Results = bm25Result;
                        bm25Count = bm25Result.Length;
                        bm25Ok = true;
                    }
                    else
                    {
                        bm25Results = new NativeArray<SearchResult>(0, Allocator.TempJob);
                    }
                }
                else
                {
                    bm25Results = new NativeArray<SearchResult>(0, Allocator.TempJob);
                }
                tokenHashes.Dispose();
            }

            // Graceful Degradation: 全サブ検索が結果0件の場合
            if (!denseOk && !sparseOk && !bm25Ok)
            {
                if (denseResults.IsCreated) denseResults.Dispose();
                if (sparseResults.IsCreated) sparseResults.Dispose();
                if (bm25Results.IsCreated) bm25Results.Dispose();
                return Result<NativeArray<SearchResult>>.Success(
                    new NativeArray<SearchResult>(0, resultAllocator));
            }

            // RRF マージ
            int actualK = math.min(param.K, denseCount + sparseCount + bm25Count);
            var mergedResults = new NativeArray<SearchResult>(actualK, Allocator.TempJob);

            // 入力配列が未作成の場合は空配列を作成
            if (!denseResults.IsCreated)
                denseResults = new NativeArray<SearchResult>(0, Allocator.TempJob);
            if (!sparseResults.IsCreated)
                sparseResults = new NativeArray<SearchResult>(0, Allocator.TempJob);
            if (!bm25Results.IsCreated)
                bm25Results = new NativeArray<SearchResult>(0, Allocator.TempJob);

            var mergeJob = new RrfMergeJob
            {
                DenseResults = denseResults,
                SparseResults = sparseResults,
                Bm25Results = bm25Results,
                MergedResults = mergedResults,
                Config = param.RrfConfig,
                K = actualK,
                DenseCount = denseCount,
                SparseCount = sparseCount,
                Bm25Count = bm25Count,
            };
            mergeJob.Execute();

            // 結果をコピー (resultAllocator で確保)
            int resultCount = 0;
            for (int i = 0; i < mergedResults.Length; i++)
            {
                if (mergedResults[i].Score > 0f) resultCount++;
                else break;
            }

            var finalResults = new NativeArray<SearchResult>(resultCount, resultAllocator);
            for (int i = 0; i < resultCount; i++)
                finalResults[i] = mergedResults[i];

            // Cleanup
            mergedResults.Dispose();
            denseResults.Dispose();
            sparseResults.Dispose();
            bm25Results.Dispose();

            return Result<NativeArray<SearchResult>>.Success(finalResults);
        }
    }
}
