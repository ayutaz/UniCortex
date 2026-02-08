using System.IO;
using NUnit.Framework;
using Unity.Collections;
using UniCortex;
using UniCortex.Hnsw;
using UniCortex.Sparse;
using UniCortex.Persistence;

namespace UniCortex.Tests.Editor.Persistence
{
    public class PersistenceTests
    {
        string testFilePath;
        DatabaseConfig config;

        [SetUp]
        public void Setup()
        {
            testFilePath = Path.Combine(Path.GetTempPath(), "unicortex_test_" + System.Guid.NewGuid().ToString("N") + ".ucx");
            config = new DatabaseConfig
            {
                Capacity = 50,
                Dimension = 4,
                HnswConfig = HnswConfig.Default,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(testFilePath))
                File.Delete(testFilePath);
        }

        NativeArray<float> MakeVector(params float[] values)
        {
            var v = new NativeArray<float>(values.Length, Allocator.Temp);
            for (int i = 0; i < values.Length; i++) v[i] = values[i];
            return v;
        }

        NativeArray<byte> MakeText(string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var arr = new NativeArray<byte>(bytes.Length, Allocator.Temp);
            for (int i = 0; i < bytes.Length; i++) arr[i] = bytes[i];
            return arr;
        }

        NativeArray<SparseElement> MakeSparse(params (int idx, float val)[] elements)
        {
            var arr = new NativeArray<SparseElement>(elements.Length, Allocator.Temp);
            for (int i = 0; i < elements.Length; i++)
                arr[i] = new SparseElement { Index = elements[i].idx, Value = elements[i].val };
            return arr;
        }

        [Test]
        public void SaveLoad_RoundTrip_DenseSearch()
        {
            // Save
            var db = new UniCortexDatabase(config);
            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(0, 1, 0, 0);
            var vec3 = MakeVector(0, 0, 1, 0);
            db.Add(1, denseVector: vec1);
            db.Add(2, denseVector: vec2);
            db.Add(3, denseVector: vec3);
            db.Build();

            var saveResult = IndexSerializer.Save(testFilePath, db);
            Assert.IsTrue(saveResult.IsSuccess);
            db.Dispose();
            vec1.Dispose(); vec2.Dispose(); vec3.Dispose();

            // Load
            var loadResult = IndexSerializer.Load(testFilePath);
            Assert.IsTrue(loadResult.IsSuccess);
            var loadedDb = loadResult.Value;

            // 検索で検証
            var query = MakeVector(1, 0, 0, 0);
            var results = loadedDb.SearchDense(query, new SearchParams { K = 3, EfSearch = 50 });
            Assert.GreaterOrEqual(results.Length, 1);

            var ext = loadedDb.GetExternalId(results[0].InternalId);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(1UL, ext.Value);

            results.Dispose();
            query.Dispose();
            loadedDb.Dispose();
        }

        [Test]
        public void SaveLoad_RoundTrip_BM25Search()
        {
            var db = new UniCortexDatabase(config);
            var text1 = MakeText("fire sword legendary");
            var text2 = MakeText("ice staff magic");
            db.Add(1, text: text1);
            db.Add(2, text: text2);
            db.Build();

            var saveResult = IndexSerializer.Save(testFilePath, db);
            Assert.IsTrue(saveResult.IsSuccess);
            db.Dispose();
            text1.Dispose(); text2.Dispose();

            var loadResult = IndexSerializer.Load(testFilePath);
            Assert.IsTrue(loadResult.IsSuccess);
            var loadedDb = loadResult.Value;

            var queryText = MakeText("fire");
            var results = loadedDb.SearchBM25(queryText, 2);
            Assert.GreaterOrEqual(results.Length, 1);

            results.Dispose();
            queryText.Dispose();
            loadedDb.Dispose();
        }

        [Test]
        public void SaveLoad_RoundTrip_SparseSearch()
        {
            var db = new UniCortexDatabase(config);
            var sv1 = MakeSparse((0, 1.0f), (1, 0.5f));
            var sv2 = MakeSparse((1, 1.0f), (2, 0.5f));
            db.Add(1, sparseVector: sv1);
            db.Add(2, sparseVector: sv2);
            db.Build();

            var saveResult = IndexSerializer.Save(testFilePath, db);
            Assert.IsTrue(saveResult.IsSuccess);
            db.Dispose();
            sv1.Dispose(); sv2.Dispose();

            var loadResult = IndexSerializer.Load(testFilePath);
            Assert.IsTrue(loadResult.IsSuccess);
            var loadedDb = loadResult.Value;

            var query = MakeSparse((0, 1.0f));
            var results = loadedDb.SearchSparse(query, 2);
            Assert.GreaterOrEqual(results.Length, 1);

            var ext = loadedDb.GetExternalId(results[0].InternalId);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(1UL, ext.Value);

            results.Dispose();
            query.Dispose();
            loadedDb.Dispose();
        }

        [Test]
        public void Load_NonExistentFile_ReturnsFileNotFound()
        {
            var result = IndexSerializer.Load("nonexistent_file.ucx");
            Assert.AreEqual(ErrorCode.FileNotFound, result.Error);
        }

        [Test]
        public void Load_InvalidMagic_ReturnsInvalidFileFormat()
        {
            // 不正なマジックナンバーのファイルを作成
            var data = new byte[256];
            data[0] = 0x00; data[1] = 0x00; data[2] = 0x00; data[3] = 0x00;
            File.WriteAllBytes(testFilePath, data);

            var result = IndexSerializer.Load(testFilePath);
            Assert.AreEqual(ErrorCode.InvalidFileFormat, result.Error);
        }

        [Test]
        public void Load_TruncatedFile_ReturnsDataCorrupted()
        {
            // ヘッダより短いファイル
            File.WriteAllBytes(testFilePath, new byte[64]);

            var result = IndexSerializer.Load(testFilePath);
            Assert.AreEqual(ErrorCode.DataCorrupted, result.Error);
        }

        [Test]
        public void Load_CorruptedData_ReturnsDataCorrupted()
        {
            // 正常なファイルを作成
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(1, 0, 0, 0);
            db.Add(1, denseVector: vec);
            db.Build();
            IndexSerializer.Save(testFilePath, db);
            db.Dispose();
            vec.Dispose();

            // ファイルの末尾を破壊
            var data = File.ReadAllBytes(testFilePath);
            if (data.Length > FileHeader.HeaderSize + 10)
            {
                data[data.Length - 1] ^= 0xFF;
                data[data.Length - 2] ^= 0xFF;
            }
            File.WriteAllBytes(testFilePath, data);

            var result = IndexSerializer.Load(testFilePath);
            Assert.AreEqual(ErrorCode.DataCorrupted, result.Error);
        }

        [Test]
        public void FileHeader_HasCorrectMagic()
        {
            Assert.AreEqual(0x554E4358u, FileHeader.ExpectedMagic);
        }

        [Test]
        public void Crc32_KnownValue()
        {
            // CRC32 of "123456789" = 0xCBF43926
            var data = System.Text.Encoding.ASCII.GetBytes("123456789");
            uint crc = Crc32.Compute(data, 0, data.Length);
            Assert.AreEqual(0xCBF43926u, crc);
        }
    }
}
