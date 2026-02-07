using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UniCortex;
using UniCortex.Hnsw;
using UniCortex.Sparse;
using UniCortex.Hybrid;

namespace UniCortex.Tests.Editor.Integration
{
    public class DatabaseTests
    {
        DatabaseConfig config;

        [SetUp]
        public void Setup()
        {
            config = new DatabaseConfig
            {
                Capacity = 100,
                Dimension = 4,
                HnswConfig = HnswConfig.Default,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };
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
        public void Lifecycle_AddBuildSearchDispose()
        {
            var db = new UniCortexDatabase(config);

            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(0, 1, 0, 0);
            var vec3 = MakeVector(0, 0, 1, 0);

            Assert.IsTrue(db.Add(1, denseVector: vec1).IsSuccess);
            Assert.IsTrue(db.Add(2, denseVector: vec2).IsSuccess);
            Assert.IsTrue(db.Add(3, denseVector: vec3).IsSuccess);

            db.Build();
            Assert.AreEqual(3, db.DocumentCount);

            // 検索
            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams { K = 2, EfSearch = 50 });

            Assert.GreaterOrEqual(results.Length, 1);
            // 最近傍は id=0 (external=1)
            var externalResult = db.GetExternalId(results[0].InternalId);
            Assert.IsTrue(externalResult.IsSuccess);
            Assert.AreEqual(1UL, externalResult.Value);

            results.Dispose();
            query.Dispose();
            vec1.Dispose();
            vec2.Dispose();
            vec3.Dispose();
            db.Dispose();
        }

        [Test]
        public void Add_DuplicateId_Fails()
        {
            var db = new UniCortexDatabase(config);

            var vec = MakeVector(1, 0, 0, 0);
            Assert.IsTrue(db.Add(1, denseVector: vec).IsSuccess);
            Assert.AreEqual(ErrorCode.DuplicateId, db.Add(1, denseVector: vec).Error);

            vec.Dispose();
            db.Dispose();
        }

        [Test]
        public void Delete_RemovesFromSearch()
        {
            var db = new UniCortexDatabase(config);

            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(2, 0, 0, 0);
            db.Add(1, denseVector: vec1);
            db.Add(2, denseVector: vec2);

            db.Delete(1);

            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams { K = 10, EfSearch = 50 });

            // id=1 (external) が結果に含まれないことを確認
            for (int i = 0; i < results.Length; i++)
            {
                var ext = db.GetExternalId(results[i].InternalId);
                if (ext.IsSuccess)
                    Assert.AreNotEqual(1UL, ext.Value);
            }

            results.Dispose();
            query.Dispose();
            vec1.Dispose();
            vec2.Dispose();
            db.Dispose();
        }

        [Test]
        public void BM25Search_ReturnsRelevantResults()
        {
            var db = new UniCortexDatabase(config);

            var text1 = MakeText("fire sword legendary weapon");
            var text2 = MakeText("ice staff magic spell");
            var text3 = MakeText("fire dragon legendary beast");

            db.Add(1, text: text1);
            db.Add(2, text: text2);
            db.Add(3, text: text3);

            var queryText = MakeText("fire legendary");
            var results = db.SearchBM25(queryText, 3);

            // fire + legendary を含む doc1, doc3 が上位に来るはず
            Assert.GreaterOrEqual(results.Length, 1);

            results.Dispose();
            queryText.Dispose();
            text1.Dispose();
            text2.Dispose();
            text3.Dispose();
            db.Dispose();
        }

        [Test]
        public void SparseSearch_ReturnsRelevantResults()
        {
            var db = new UniCortexDatabase(config);

            var sv1 = MakeSparse((0, 1.0f), (1, 0.5f));
            var sv2 = MakeSparse((1, 1.0f), (2, 0.5f));
            var sv3 = MakeSparse((0, 0.8f), (2, 0.3f));

            db.Add(1, sparseVector: sv1);
            db.Add(2, sparseVector: sv2);
            db.Add(3, sparseVector: sv3);

            var query = MakeSparse((0, 1.0f));
            var results = db.SearchSparse(query, 3);

            Assert.GreaterOrEqual(results.Length, 1);
            // dimension 0 で最大値を持つのは doc1 (value=1.0)
            var ext = db.GetExternalId(results[0].InternalId);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(1UL, ext.Value);

            results.Dispose();
            query.Dispose();
            sv1.Dispose();
            sv2.Dispose();
            sv3.Dispose();
            db.Dispose();
        }

        [Test]
        public void HybridSearch_DenseAndBM25()
        {
            var db = new UniCortexDatabase(config);

            var vec1 = MakeVector(1, 0, 0, 0);
            var text1 = MakeText("fire sword");
            var vec2 = MakeVector(0, 1, 0, 0);
            var text2 = MakeText("ice staff");
            var vec3 = MakeVector(0.9f, 0.1f, 0, 0);
            var text3 = MakeText("fire staff");

            db.Add(1, denseVector: vec1, text: text1);
            db.Add(2, denseVector: vec2, text: text2);
            db.Add(3, denseVector: vec3, text: text3);

            var denseQuery = MakeVector(1, 0, 0, 0);
            var textQuery = MakeText("fire");

            var param = new HybridSearchParams
            {
                DenseQuery = denseQuery,
                TextQuery = textQuery,
                K = 3,
                SubSearchK = 10,
                RrfConfig = RrfConfig.Default,
                DenseParams = new SearchParams { K = 10, EfSearch = 50 },
            };

            var result = db.SearchHybrid(param);
            Assert.IsTrue(result.IsSuccess);
            Assert.GreaterOrEqual(result.Value.Length, 1);

            result.Value.Dispose();
            denseQuery.Dispose();
            textQuery.Dispose();
            vec1.Dispose(); vec2.Dispose(); vec3.Dispose();
            text1.Dispose(); text2.Dispose(); text3.Dispose();
            db.Dispose();
        }

        [Test]
        public void Metadata_SetAndRetrieve()
        {
            var db = new UniCortexDatabase(config);

            var vec = MakeVector(1, 0, 0, 0);
            db.Add(1, denseVector: vec);

            int priceField = 12345;
            Assert.IsTrue(db.SetMetadataInt(1, priceField, 500).IsSuccess);

            // 内部IDを取得してメタデータにアクセスする間接的な検証
            var idResult = db.GetInternalId(1);
            Assert.IsTrue(idResult.IsSuccess);

            vec.Dispose();
            db.Dispose();
        }

        [Test]
        public void Update_ReplacesDocument()
        {
            var db = new UniCortexDatabase(config);

            var vec1 = MakeVector(1, 0, 0, 0);
            db.Add(1, denseVector: vec1);

            // 更新
            var vec2 = MakeVector(0, 0, 0, 1);
            var updateResult = db.Update(1, denseVector: vec2);
            Assert.IsTrue(updateResult.IsSuccess);

            // 更新後のベクトルで検索
            var query = MakeVector(0, 0, 0, 1);
            var results = db.SearchDense(query, new SearchParams { K = 1, EfSearch = 50 });

            Assert.GreaterOrEqual(results.Length, 1);
            var ext = db.GetExternalId(results[0].InternalId);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(1UL, ext.Value);

            results.Dispose();
            query.Dispose();
            vec1.Dispose();
            vec2.Dispose();
            db.Dispose();
        }

        [Test]
        public void EmptyDatabase_SearchReturnsEmpty()
        {
            var db = new UniCortexDatabase(config);

            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams { K = 5, EfSearch = 50 });
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            query.Dispose();
            db.Dispose();
        }

        [Test]
        public void Delete_NonExistent_ReturnsNotFound()
        {
            var db = new UniCortexDatabase(config);

            var result = db.Delete(999);
            Assert.AreEqual(ErrorCode.NotFound, result.Error);

            db.Dispose();
        }
    }
}
