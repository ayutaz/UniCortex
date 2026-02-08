using System;
using System.IO;
using Unity.Collections;
using UniCortex.Hnsw;
using UniCortex.Sparse;
using UniCortex.FullText;

namespace UniCortex.Persistence
{
    /// <summary>
    /// Load の戻り値。UniCortexDatabase は class のため Result&lt;T&gt; (unmanaged) が使えない。
    /// </summary>
    public class LoadResult
    {
        public ErrorCode Error;
        public UniCortexDatabase Value;
        public bool IsSuccess => Error == ErrorCode.None;
    }

    /// <summary>
    /// UniCortex インデックスのシリアライズ/デシリアライズ。
    /// BinaryWriter/BinaryReader ベースの実装。
    /// </summary>
    public static class IndexSerializer
    {
        /// <summary>
        /// データベースをファイルに保存する。
        /// </summary>
        public static Result<bool> Save(string path, UniCortexDatabase db)
        {
            try
            {
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    // ヘッダ用のプレースホルダを書き込み
                    writer.Write(new byte[FileHeader.HeaderSize]);

                    // セクション位置を追跡
                    long vectorDataOffset = ms.Position;
                    WriteVectorData(writer, db);
                    long vectorDataSize = ms.Position - vectorDataOffset;

                    long hnswOffset = ms.Position;
                    WriteHnswGraph(writer, db);
                    long hnswSize = ms.Position - hnswOffset;

                    long sparseOffset = ms.Position;
                    WriteSparseIndex(writer, db);
                    long sparseSize = ms.Position - sparseOffset;

                    long bm25Offset = ms.Position;
                    WriteBm25Index(writer, db);
                    long bm25Size = ms.Position - bm25Offset;

                    long idMapOffset = ms.Position;
                    WriteIdMap(writer, db);
                    long idMapSize = ms.Position - idMapOffset;

                    long metadataOffset = ms.Position;
                    WriteMetadata(writer, db);
                    long metadataSize = ms.Position - metadataOffset;

                    // CRC32 計算
                    writer.Flush();
                    var allData = ms.ToArray();
                    uint checksum = Crc32.Compute(allData, FileHeader.HeaderSize, allData.Length - FileHeader.HeaderSize);

                    // ヘッダを書き込み
                    ms.Seek(0, SeekOrigin.Begin);
                    var headerWriter = new BinaryWriter(ms);
                    headerWriter.Write(FileHeader.ExpectedMagic);
                    headerWriter.Write(FileHeader.CurrentVersionMajor);
                    headerWriter.Write(FileHeader.CurrentVersionMinor);
                    headerWriter.Write(db.Config.Dimension);
                    headerWriter.Write(db.DocumentCount);
                    headerWriter.Write(vectorDataOffset);
                    headerWriter.Write(vectorDataSize);
                    headerWriter.Write(hnswOffset);
                    headerWriter.Write(hnswSize);
                    headerWriter.Write(sparseOffset);
                    headerWriter.Write(sparseSize);
                    headerWriter.Write(bm25Offset);
                    headerWriter.Write(bm25Size);
                    headerWriter.Write(idMapOffset);
                    headerWriter.Write(idMapSize);
                    headerWriter.Write(metadataOffset);
                    headerWriter.Write(metadataSize);
                    headerWriter.Write(checksum);

                    headerWriter.Flush();
                    var finalData = ms.ToArray();
                    File.WriteAllBytes(path, finalData);
                }

                return Result<bool>.Success(true);
            }
            catch (Exception)
            {
                return Result<bool>.Fail(ErrorCode.IoError);
            }
        }

        /// <summary>
        /// ファイルからデータベースを読み込む。
        /// エラー時は db = null, ErrorCode を返す。
        /// </summary>
        public static LoadResult Load(string path)
        {
            if (!File.Exists(path))
                return new LoadResult { Error = ErrorCode.FileNotFound };

            try
            {
                var data = File.ReadAllBytes(path);
                if (data.Length < FileHeader.HeaderSize)
                    return new LoadResult { Error = ErrorCode.DataCorrupted };

                using (var ms = new MemoryStream(data))
                using (var reader = new BinaryReader(ms))
                {
                    // ヘッダ読み取り
                    uint magic = reader.ReadUInt32();
                    if (magic != FileHeader.ExpectedMagic)
                        return new LoadResult { Error = ErrorCode.InvalidFileFormat };

                    ushort vMajor = reader.ReadUInt16();
                    if (vMajor != FileHeader.CurrentVersionMajor)
                        return new LoadResult { Error = ErrorCode.IncompatibleVersion };

                    ushort vMinor = reader.ReadUInt16();
                    int dimension = reader.ReadInt32();
                    int documentCount = reader.ReadInt32();

                    // バリデーション
                    if (documentCount < 0 || documentCount > 1_000_000)
                        return new LoadResult { Error = ErrorCode.InvalidParameter };
                    if (dimension < 0 || dimension > 4096)
                        return new LoadResult { Error = ErrorCode.InvalidParameter };

                    long vectorDataOffset = reader.ReadInt64();
                    long vectorDataSize = reader.ReadInt64();
                    long hnswOffset = reader.ReadInt64();
                    long hnswSize = reader.ReadInt64();
                    long sparseOffset = reader.ReadInt64();
                    long sparseSize = reader.ReadInt64();
                    long bm25Offset = reader.ReadInt64();
                    long bm25Size = reader.ReadInt64();
                    long idMapOffset = reader.ReadInt64();
                    long idMapSize = reader.ReadInt64();
                    long metadataOffset = reader.ReadInt64();
                    long metadataSize = reader.ReadInt64();
                    uint checksum = reader.ReadUInt32();

                    // CRC32 検証
                    uint computed = Crc32.Compute(data, FileHeader.HeaderSize, data.Length - FileHeader.HeaderSize);
                    if (computed != checksum)
                        return new LoadResult { Error = ErrorCode.DataCorrupted };

                    // データベース再構築
                    int capacity = Math.Max(documentCount * 2, 100);
                    var config = new DatabaseConfig
                    {
                        Capacity = capacity,
                        Dimension = dimension,
                        HnswConfig = HnswConfig.Default,
                        BM25K1 = 1.2f,
                        BM25B = 0.75f,
                    };

                    var db = new UniCortexDatabase(config);

                    // VectorData + HNSW + Sparse + BM25 + IdMap の復元
                    ms.Seek(vectorDataOffset, SeekOrigin.Begin);
                    ReadVectorData(reader, db, dimension, documentCount);

                    ms.Seek(hnswOffset, SeekOrigin.Begin);
                    ReadHnswGraph(reader, db, documentCount);

                    ms.Seek(sparseOffset, SeekOrigin.Begin);
                    ReadSparseIndex(reader, db);

                    ms.Seek(bm25Offset, SeekOrigin.Begin);
                    ReadBm25Index(reader, db, documentCount);

                    ms.Seek(idMapOffset, SeekOrigin.Begin);
                    ReadIdMap(reader, db);

                    ms.Seek(metadataOffset, SeekOrigin.Begin);
                    ReadMetadata(reader, db);

                    db.SetBuilt(true);
                    return new LoadResult { Value = db };
                }
            }
            catch (Exception)
            {
                return new LoadResult { Error = ErrorCode.DataCorrupted };
            }
        }

        // --- Write helpers ---

        static void WriteVectorData(BinaryWriter w, UniCortexDatabase db)
        {
            var storage = db.GetVectorStorage();
            // documentCount * dimension 分のみ書き出し
            int docCount = db.DocumentCount;
            int totalFloats = docCount * storage.Dimension;
            w.Write(totalFloats);
            for (int i = 0; i < totalFloats && i < storage.Data.Length; i++)
                w.Write(storage.Data[i]);
        }

        static void WriteHnswGraph(BinaryWriter w, UniCortexDatabase db)
        {
            var graph = db.GetHnswGraph();
            w.Write(graph.MaxLayer);
            w.Write(graph.EntryPoint);
            w.Write(graph.M);
            w.Write(graph.M0);
            w.Write(graph.Count);
            w.Write(graph.DeletedCount);

            // Nodes メタデータ
            for (int i = 0; i < graph.Count; i++)
            {
                var node = graph.Nodes[i];
                w.Write(node.MaxLayer);
                w.Write(node.NeighborOffset);
            }

            // Deleted フラグ
            for (int i = 0; i < graph.Count; i++)
                w.Write(graph.Deleted[i]);

            // Neighbors
            int neighborLen = graph.Neighbors.Length;
            w.Write(neighborLen);
            for (int i = 0; i < neighborLen; i++)
                w.Write(graph.Neighbors[i]);

            // NeighborCounts
            int countLen = graph.NeighborCounts.Length;
            w.Write(countLen);
            for (int i = 0; i < countLen; i++)
                w.Write(graph.NeighborCounts[i]);
        }

        static void WriteSparseIndex(BinaryWriter w, UniCortexDatabase db)
        {
            var idx = db.GetSparseIndex();
            w.Write(idx.DocumentCount);

            // 転置インデックスを flat に書き出し
            var keys = idx.InvertedIndex.GetKeyArray(Allocator.Temp);
            try
            {
                w.Write(keys.Length);
                for (int k = 0; k < keys.Length; k++)
                {
                    int dimKey = keys[k];
                    var postings = new NativeList<SparsePosting>(16, Allocator.Temp);
                    try
                    {
                        if (idx.InvertedIndex.TryGetFirstValue(dimKey, out SparsePosting posting, out var iter))
                        {
                            do { postings.Add(posting); }
                            while (idx.InvertedIndex.TryGetNextValue(out posting, ref iter));
                        }

                        w.Write(dimKey);
                        w.Write(postings.Length);
                        for (int p = 0; p < postings.Length; p++)
                        {
                            w.Write(postings[p].InternalId);
                            w.Write(postings[p].Value);
                        }
                    }
                    finally { postings.Dispose(); }
                }
            }
            finally { keys.Dispose(); }

            // DeletedIds
            var deletedKeys = idx.DeletedIds.ToNativeArray(Allocator.Temp);
            try
            {
                w.Write(deletedKeys.Length);
                for (int i = 0; i < deletedKeys.Length; i++)
                    w.Write(deletedKeys[i]);
            }
            finally { deletedKeys.Dispose(); }
        }

        static void WriteBm25Index(BinaryWriter w, UniCortexDatabase db)
        {
            var idx = db.GetBm25Index();
            w.Write(idx.TotalDocuments);
            w.Write(idx.AverageDocumentLength);

            // DocumentLengths (有効なドキュメント分のみ)
            int docLenCount = Math.Min(idx.TotalDocuments, idx.DocumentLengths.Length);
            w.Write(docLenCount);
            for (int i = 0; i < docLenCount; i++)
                w.Write(idx.DocumentLengths[i]);

            // 転置インデックス
            var termKeys = idx.InvertedIndex.GetKeyArray(Allocator.Temp);
            try
            {
                w.Write(termKeys.Length);
                for (int k = 0; k < termKeys.Length; k++)
                {
                    uint termHash = termKeys[k];
                    var postings = new NativeList<BM25Posting>(16, Allocator.Temp);
                    try
                    {
                        if (idx.InvertedIndex.TryGetFirstValue(termHash, out BM25Posting posting, out var iter))
                        {
                            do { postings.Add(posting); }
                            while (idx.InvertedIndex.TryGetNextValue(out posting, ref iter));
                        }

                        w.Write(termHash);
                        w.Write(postings.Length);
                        for (int p = 0; p < postings.Length; p++)
                        {
                            w.Write(postings[p].InternalId);
                            w.Write(postings[p].TermFrequency);
                        }
                    }
                    finally { postings.Dispose(); }
                }
            }
            finally { termKeys.Dispose(); }

            // DocumentFrequency
            var dfKeys = idx.DocumentFrequency.GetKeyArray(Allocator.Temp);
            try
            {
                w.Write(dfKeys.Length);
                for (int k = 0; k < dfKeys.Length; k++)
                {
                    w.Write(dfKeys[k]);
                    w.Write(idx.DocumentFrequency[dfKeys[k]]);
                }
            }
            finally { dfKeys.Dispose(); }

            // DeletedIds
            var deletedKeys = idx.DeletedIds.ToNativeArray(Allocator.Temp);
            try
            {
                w.Write(deletedKeys.Length);
                for (int i = 0; i < deletedKeys.Length; i++)
                    w.Write(deletedKeys[i]);
            }
            finally { deletedKeys.Dispose(); }
        }

        static void WriteMetadata(BinaryWriter w, UniCortexDatabase db)
        {
            var meta = db.GetMetadataStorage();

            // Int values
            var intKeys = meta.IntValues.GetKeyArray(Allocator.Temp);
            try
            {
                w.Write(intKeys.Length);
                for (int i = 0; i < intKeys.Length; i++)
                {
                    w.Write(intKeys[i]);
                    w.Write(meta.IntValues[intKeys[i]]);
                }
            }
            finally { intKeys.Dispose(); }

            // Float values
            var floatKeys = meta.FloatValues.GetKeyArray(Allocator.Temp);
            try
            {
                w.Write(floatKeys.Length);
                for (int i = 0; i < floatKeys.Length; i++)
                {
                    w.Write(floatKeys[i]);
                    w.Write(meta.FloatValues[floatKeys[i]]);
                }
            }
            finally { floatKeys.Dispose(); }

            // Bool values
            var boolKeys = meta.BoolValues.GetKeyArray(Allocator.Temp);
            try
            {
                w.Write(boolKeys.Length);
                for (int i = 0; i < boolKeys.Length; i++)
                {
                    w.Write(boolKeys[i]);
                    w.Write(meta.BoolValues[boolKeys[i]]);
                }
            }
            finally { boolKeys.Dispose(); }
        }

        static void WriteIdMap(BinaryWriter w, UniCortexDatabase db)
        {
            var idMap = db.GetIdMap();
            w.Write(idMap.Count);
            w.Write(idMap.Capacity);

            // InternalToExternal
            for (int i = 0; i < idMap.Count; i++)
                w.Write(idMap.InternalToExternal[i]);

            // FreeList
            w.Write(idMap.FreeList.Length);
            for (int i = 0; i < idMap.FreeList.Length; i++)
                w.Write(idMap.FreeList[i]);
        }

        // --- Read helpers ---

        static void ReadVectorData(BinaryReader r, UniCortexDatabase db, int dimension, int documentCount)
        {
            var storage = db.GetVectorStorage();
            int totalFloats = r.ReadInt32();
            int readCount = Math.Min(totalFloats, storage.Data.Length);
            for (int i = 0; i < readCount; i++)
                storage.Data[i] = r.ReadSingle();
            for (int i = readCount; i < totalFloats; i++)
                r.ReadSingle(); // skip excess
        }

        static void ReadHnswGraph(BinaryReader r, UniCortexDatabase db, int documentCount)
        {
            ref var graph = ref db.GetHnswGraphRef();
            graph.MaxLayer = r.ReadInt32();
            graph.EntryPoint = r.ReadInt32();
            int m = r.ReadInt32();
            int m0 = r.ReadInt32();
            graph.Count = r.ReadInt32();
            graph.DeletedCount = r.ReadInt32();

            // Nodes
            for (int i = 0; i < graph.Count; i++)
            {
                graph.Nodes[i] = new HnswNodeMeta
                {
                    MaxLayer = r.ReadInt32(),
                    NeighborOffset = r.ReadInt32()
                };
            }

            // Deleted
            for (int i = 0; i < graph.Count; i++)
                graph.Deleted[i] = r.ReadBoolean();

            // Neighbors
            int neighborLen = r.ReadInt32();
            int readNeighbors = Math.Min(neighborLen, graph.Neighbors.Length);
            for (int i = 0; i < readNeighbors; i++)
                graph.Neighbors[i] = r.ReadInt32();
            for (int i = readNeighbors; i < neighborLen; i++)
                r.ReadInt32(); // skip excess

            // NeighborCounts
            int countLen = r.ReadInt32();
            int readCounts = Math.Min(countLen, graph.NeighborCounts.Length);
            for (int i = 0; i < readCounts; i++)
                graph.NeighborCounts[i] = r.ReadInt32();
            for (int i = readCounts; i < countLen; i++)
                r.ReadInt32();
        }

        static void ReadSparseIndex(BinaryReader r, UniCortexDatabase db)
        {
            ref var idx = ref db.GetSparseIndexRef();
            idx.DocumentCount = r.ReadInt32();

            int keyCount = r.ReadInt32();
            for (int k = 0; k < keyCount; k++)
            {
                int dimKey = r.ReadInt32();
                int postingCount = r.ReadInt32();
                for (int p = 0; p < postingCount; p++)
                {
                    int internalId = r.ReadInt32();
                    float value = r.ReadSingle();
                    idx.InvertedIndex.Add(dimKey, new SparsePosting
                    {
                        InternalId = internalId,
                        Value = value
                    });
                }
            }

            int deletedCount = r.ReadInt32();
            for (int i = 0; i < deletedCount; i++)
                idx.DeletedIds.Add(r.ReadInt32());
        }

        static void ReadBm25Index(BinaryReader r, UniCortexDatabase db, int documentCount)
        {
            ref var idx = ref db.GetBm25IndexRef();
            idx.TotalDocuments = r.ReadInt32();
            idx.AverageDocumentLength = r.ReadSingle();

            // DocumentLengths
            int docLenCount = r.ReadInt32();
            int readLen = Math.Min(docLenCount, idx.DocumentLengths.Length);
            for (int i = 0; i < readLen; i++)
                idx.DocumentLengths[i] = r.ReadInt32();
            for (int i = readLen; i < docLenCount; i++)
                r.ReadInt32(); // skip excess

            // Inverted index
            int termCount = r.ReadInt32();
            for (int k = 0; k < termCount; k++)
            {
                uint termHash = r.ReadUInt32();
                int postingCount = r.ReadInt32();
                for (int p = 0; p < postingCount; p++)
                {
                    int internalId = r.ReadInt32();
                    int tf = r.ReadInt32();
                    idx.InvertedIndex.Add(termHash, new BM25Posting
                    {
                        InternalId = internalId,
                        TermFrequency = tf
                    });
                }
            }

            // DocumentFrequency
            int dfCount = r.ReadInt32();
            for (int k = 0; k < dfCount; k++)
            {
                uint hash = r.ReadUInt32();
                int df = r.ReadInt32();
                idx.DocumentFrequency.Add(hash, df);
            }

            // DeletedIds
            int deletedCount = r.ReadInt32();
            for (int i = 0; i < deletedCount; i++)
                idx.DeletedIds.Add(r.ReadInt32());
        }

        static void ReadIdMap(BinaryReader r, UniCortexDatabase db)
        {
            ref var idMap = ref db.GetIdMapRef();
            int count = r.ReadInt32();
            int capacity = r.ReadInt32();

            // InternalToExternal
            for (int i = 0; i < count; i++)
            {
                ulong externalId = r.ReadUInt64();
                idMap.InternalToExternal[i] = externalId;
                if (externalId != IdMap.Sentinel)
                {
                    idMap.ExternalToInternal.Add(externalId, i);
                }
            }
            idMap.Count = count;

            // FreeList
            int freeCount = r.ReadInt32();
            for (int i = 0; i < freeCount; i++)
                idMap.FreeList.Add(r.ReadInt32());
        }

        static void ReadMetadata(BinaryReader r, UniCortexDatabase db)
        {
            ref var meta = ref db.GetMetadataStorageRef();

            // Int values
            int intCount = r.ReadInt32();
            for (int i = 0; i < intCount; i++)
            {
                long key = r.ReadInt64();
                int value = r.ReadInt32();
                meta.IntValues.Add(key, value);
            }

            // Float values
            int floatCount = r.ReadInt32();
            for (int i = 0; i < floatCount; i++)
            {
                long key = r.ReadInt64();
                float value = r.ReadSingle();
                meta.FloatValues.Add(key, value);
            }

            // Bool values
            int boolCount = r.ReadInt32();
            for (int i = 0; i < boolCount; i++)
            {
                long key = r.ReadInt64();
                bool value = r.ReadBoolean();
                meta.BoolValues.Add(key, value);
            }
        }
    }
}
