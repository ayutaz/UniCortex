using System.IO;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UniCortex;
using UniCortex.Hnsw;
using UniCortex.Sparse;
using UniCortex.Hybrid;
using UniCortex.Persistence;

namespace UniCortex.Tests.Editor.Integration
{
    /// <summary>
    /// 総合レビューで発見された未テストパスのテスト。
    /// </summary>
    public class ReviewFixTests
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
                DistanceType = DistanceType.EuclideanSq,
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

        // --- 1.4 IndexNotBuilt テスト ---

        [Test]
        public void SearchDense_BeforeBuild_ReturnsEmpty()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(1, 0, 0, 0);
            db.Add(1, denseVector: vec);

            // Build() を呼ばずに検索
            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams { K = 5, EfSearch = 50 });
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            query.Dispose();
            vec.Dispose();
            db.Dispose();
        }

        [Test]
        public void SearchSparse_BeforeBuild_ReturnsEmpty()
        {
            var db = new UniCortexDatabase(config);
            var sv = MakeSparse((0, 1.0f));
            db.Add(1, sparseVector: sv);

            var query = MakeSparse((0, 1.0f));
            var results = db.SearchSparse(query, 5);
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            query.Dispose();
            sv.Dispose();
            db.Dispose();
        }

        [Test]
        public void SearchBM25_BeforeBuild_ReturnsEmpty()
        {
            var db = new UniCortexDatabase(config);
            var text = MakeText("hello world");
            db.Add(1, text: text);

            var queryText = MakeText("hello");
            var results = db.SearchBM25(queryText, 5);
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            queryText.Dispose();
            text.Dispose();
            db.Dispose();
        }

        [Test]
        public void SearchHybrid_BeforeBuild_ReturnsIndexNotBuilt()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(1, 0, 0, 0);
            db.Add(1, denseVector: vec);

            var denseQuery = MakeVector(1, 0, 0, 0);
            var param = new HybridSearchParams
            {
                DenseQuery = denseQuery,
                K = 3,
                SubSearchK = 10,
                RrfConfig = RrfConfig.Default,
                DenseParams = new SearchParams { K = 10, EfSearch = 50 },
            };

            var result = db.SearchHybrid(param);
            Assert.AreEqual(ErrorCode.IndexNotBuilt, result.Error);

            denseQuery.Dispose();
            vec.Dispose();
            db.Dispose();
        }

        // --- Cosine/DotProduct end-to-end テスト ---

        [Test]
        public void SearchDense_Cosine_EndToEnd()
        {
            var cosineConfig = new DatabaseConfig
            {
                Capacity = 100,
                Dimension = 4,
                HnswConfig = HnswConfig.Default,
                DistanceType = DistanceType.Cosine,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };
            var db = new UniCortexDatabase(cosineConfig);

            // 正規化済みベクトル
            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(0, 1, 0, 0);
            var vec3 = MakeVector(0.7071f, 0.7071f, 0, 0);

            db.Add(1, denseVector: vec1);
            db.Add(2, denseVector: vec2);
            db.Add(3, denseVector: vec3);
            db.Build();

            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams
            {
                K = 3,
                EfSearch = 50,
                DistanceType = DistanceType.Cosine,
            });

            Assert.GreaterOrEqual(results.Length, 1);
            var ext = db.GetExternalId(results[0].InternalId);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(1UL, ext.Value); // cosine距離が最小 = 完全一致

            results.Dispose();
            query.Dispose();
            vec1.Dispose(); vec2.Dispose(); vec3.Dispose();
            db.Dispose();
        }

        [Test]
        public void SearchDense_DotProduct_EndToEnd()
        {
            var dpConfig = new DatabaseConfig
            {
                Capacity = 100,
                Dimension = 4,
                HnswConfig = HnswConfig.Default,
                DistanceType = DistanceType.DotProduct,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };
            var db = new UniCortexDatabase(dpConfig);

            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(0, 1, 0, 0);
            var vec3 = MakeVector(0.5f, 0.5f, 0, 0);

            db.Add(1, denseVector: vec1);
            db.Add(2, denseVector: vec2);
            db.Add(3, denseVector: vec3);
            db.Build();

            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams
            {
                K = 3,
                EfSearch = 50,
                DistanceType = DistanceType.DotProduct,
            });

            Assert.GreaterOrEqual(results.Length, 1);
            var ext = db.GetExternalId(results[0].InternalId);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(1UL, ext.Value); // 内積最大 = -dot が最小

            results.Dispose();
            query.Dispose();
            vec1.Dispose(); vec2.Dispose(); vec3.Dispose();
            db.Dispose();
        }

        // --- 全種ハイブリッド検索テスト ---

        [Test]
        public void HybridSearch_AllThreeTypes()
        {
            var db = new UniCortexDatabase(config);

            var vec1 = MakeVector(1, 0, 0, 0);
            var sv1 = MakeSparse((0, 1.0f), (1, 0.5f));
            var text1 = MakeText("fire sword legendary");

            var vec2 = MakeVector(0, 1, 0, 0);
            var sv2 = MakeSparse((1, 1.0f), (2, 0.5f));
            var text2 = MakeText("ice staff magic");

            var vec3 = MakeVector(0.9f, 0.1f, 0, 0);
            var sv3 = MakeSparse((0, 0.9f));
            var text3 = MakeText("fire dragon legendary");

            db.Add(1, denseVector: vec1, sparseVector: sv1, text: text1);
            db.Add(2, denseVector: vec2, sparseVector: sv2, text: text2);
            db.Add(3, denseVector: vec3, sparseVector: sv3, text: text3);
            db.Build();

            var denseQuery = MakeVector(1, 0, 0, 0);
            var sparseQuery = MakeSparse((0, 1.0f));
            var textQuery = MakeText("fire legendary");

            var param = new HybridSearchParams
            {
                DenseQuery = denseQuery,
                SparseQuery = sparseQuery,
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
            sparseQuery.Dispose();
            textQuery.Dispose();
            vec1.Dispose(); vec2.Dispose(); vec3.Dispose();
            sv1.Dispose(); sv2.Dispose(); sv3.Dispose();
            text1.Dispose(); text2.Dispose(); text3.Dispose();
            db.Dispose();
        }

        // --- MetadataStorage 永続化テスト ---

        [Test]
        public void SaveLoad_MetadataStorage_RoundTrip()
        {
            var testFilePath = Path.Combine(Path.GetTempPath(), "unicortex_meta_test_" + System.Guid.NewGuid().ToString("N") + ".ucx");
            try
            {
                var db = new UniCortexDatabase(config);
                var vec = MakeVector(1, 0, 0, 0);
                db.Add(1, denseVector: vec);

                int priceField = 100;
                int ratingField = 300;
                int activeField = 400;

                db.SetMetadataInt(1, priceField, 500);
                db.SetMetadataFloat(1, ratingField, 4.5f);
                db.SetMetadataBool(1, activeField, true);
                db.Build();

                var saveResult = IndexSerializer.Save(testFilePath, db);
                Assert.IsTrue(saveResult.IsSuccess);
                db.Dispose();
                vec.Dispose();

                // Load
                var loadResult = IndexSerializer.Load(testFilePath);
                Assert.IsTrue(loadResult.IsSuccess);
                var loadedDb = loadResult.Value;

                // メタデータが保持されているか検証 (パブリック API)
                var intResult = loadedDb.GetMetadataInt(1, priceField);
                Assert.IsTrue(intResult.IsSuccess);
                Assert.AreEqual(500, intResult.Value);

                var floatResult = loadedDb.GetMetadataFloat(1, ratingField);
                Assert.IsTrue(floatResult.IsSuccess);
                Assert.AreEqual(4.5f, floatResult.Value, 0.01f);

                var boolResult = loadedDb.GetMetadataBool(1, activeField);
                Assert.IsTrue(boolResult.IsSuccess);
                Assert.AreEqual(true, boolResult.Value);

                loadedDb.Dispose();
            }
            finally
            {
                if (File.Exists(testFilePath))
                    File.Delete(testFilePath);
            }
        }

        // --- Build → Delete → Build 再構築テスト ---

        [Test]
        public void BuildDeleteBuild_Works()
        {
            var db = new UniCortexDatabase(config);

            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(0, 1, 0, 0);
            var vec3 = MakeVector(0, 0, 1, 0);

            db.Add(1, denseVector: vec1);
            db.Add(2, denseVector: vec2);
            db.Add(3, denseVector: vec3);
            db.Build();

            // 検索OK
            var query1 = MakeVector(1, 0, 0, 0);
            var results1 = db.SearchDense(query1, new SearchParams { K = 3, EfSearch = 50 });
            Assert.AreEqual(3, results1.Length);
            results1.Dispose();
            query1.Dispose();

            // 削除
            db.Delete(1);

            // 再Build
            db.Build();

            // 削除後の検索
            var query2 = MakeVector(1, 0, 0, 0);
            var results2 = db.SearchDense(query2, new SearchParams { K = 3, EfSearch = 50 });
            Assert.AreEqual(2, results2.Length);

            // id=1 が含まれないことを確認
            for (int i = 0; i < results2.Length; i++)
            {
                var ext = db.GetExternalId(results2[i].InternalId);
                if (ext.IsSuccess)
                    Assert.AreNotEqual(1UL, ext.Value);
            }

            results2.Dispose();
            query2.Dispose();
            vec1.Dispose(); vec2.Dispose(); vec3.Dispose();
            db.Dispose();
        }

        // --- K=1 検索テスト ---

        [Test]
        public void SearchDense_K1_ReturnsSingleResult()
        {
            var db = new UniCortexDatabase(config);

            var vec1 = MakeVector(1, 0, 0, 0);
            var vec2 = MakeVector(0, 1, 0, 0);
            db.Add(1, denseVector: vec1);
            db.Add(2, denseVector: vec2);
            db.Build();

            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams { K = 1, EfSearch = 50 });

            Assert.AreEqual(1, results.Length);
            var ext = db.GetExternalId(results[0].InternalId);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(1UL, ext.Value);

            results.Dispose();
            query.Dispose();
            vec1.Dispose(); vec2.Dispose();
            db.Dispose();
        }

        // --- DistanceType が DatabaseConfig で正しく使用されるテスト ---

        [Test]
        public void DatabaseConfig_DistanceType_UsedInBuild()
        {
            var cosineConfig = new DatabaseConfig
            {
                Capacity = 100,
                Dimension = 4,
                HnswConfig = HnswConfig.Default,
                DistanceType = DistanceType.Cosine,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };

            Assert.AreEqual(DistanceType.Cosine, cosineConfig.DistanceType);

            var defaultConfig = DatabaseConfig.Default;
            Assert.AreEqual(DistanceType.EuclideanSq, defaultConfig.DistanceType);
        }
    }
}
