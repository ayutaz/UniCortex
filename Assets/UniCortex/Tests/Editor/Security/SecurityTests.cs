using NUnit.Framework;
using Unity.Collections;
using UniCortex;
using UniCortex.Hnsw;
using UniCortex.Sparse;
using UniCortex.Persistence;

namespace UniCortex.Tests.Editor.Security
{
    /// <summary>
    /// セキュリティ / 境界値テスト。
    /// 11-security-guidelines.md に準拠。
    /// </summary>
    public class SecurityTests
    {
        DatabaseConfig config;

        [SetUp]
        public void Setup()
        {
            config = new DatabaseConfig
            {
                Capacity = 50,
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

        // --- NaN/Inf バリデーション ---

        [Test]
        public void Add_NaNVector_Rejected()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(float.NaN, 0, 0, 0);
            var result = db.Add(1, denseVector: vec);
            Assert.IsFalse(result.IsSuccess);
            vec.Dispose();
            db.Dispose();
        }

        [Test]
        public void Add_InfVector_Rejected()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(float.PositiveInfinity, 0, 0, 0);
            var result = db.Add(1, denseVector: vec);
            Assert.IsFalse(result.IsSuccess);
            vec.Dispose();
            db.Dispose();
        }

        [Test]
        public void Add_NegInfVector_Rejected()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(float.NegativeInfinity, 0, 0, 0);
            var result = db.Add(1, denseVector: vec);
            Assert.IsFalse(result.IsSuccess);
            vec.Dispose();
            db.Dispose();
        }

        // --- 次元不一致 ---

        [Test]
        public void Add_DimensionMismatch_Rejected()
        {
            var db = new UniCortexDatabase(config);
            // config.Dimension=4 だが3次元ベクトルを渡す
            var vec = new NativeArray<float>(3, Allocator.Temp);
            vec[0] = 1; vec[1] = 0; vec[2] = 0;
            var result = db.Add(1, denseVector: vec);
            Assert.AreEqual(ErrorCode.DimensionMismatch, result.Error);
            vec.Dispose();
            db.Dispose();
        }

        // --- 容量超過 ---

        [Test]
        public void Add_ExceedCapacity_Rejected()
        {
            var smallConfig = new DatabaseConfig
            {
                Capacity = 3,
                Dimension = 2,
                HnswConfig = HnswConfig.Default,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };
            var db = new UniCortexDatabase(smallConfig);

            for (ulong i = 1; i <= 3; i++)
            {
                var vec = new NativeArray<float>(2, Allocator.Temp);
                vec[0] = i; vec[1] = 0;
                var result = db.Add(i, denseVector: vec);
                Assert.IsTrue(result.IsSuccess, $"Add {i} should succeed");
                vec.Dispose();
            }

            // 4つ目は失敗すべき
            var vec4 = new NativeArray<float>(2, Allocator.Temp);
            vec4[0] = 4; vec4[1] = 0;
            var result4 = db.Add(4, denseVector: vec4);
            Assert.IsFalse(result4.IsSuccess);
            vec4.Dispose();
            db.Dispose();
        }

        // --- Sparse バリデーション ---

        [Test]
        public void Add_NegativeSparseIndex_Rejected()
        {
            var db = new UniCortexDatabase(config);
            var sv = new NativeArray<SparseElement>(1, Allocator.Temp);
            sv[0] = new SparseElement { Index = -1, Value = 1.0f };
            var result = db.Add(1, sparseVector: sv);
            Assert.IsFalse(result.IsSuccess);
            sv.Dispose();
            db.Dispose();
        }

        // --- 検索 K=0 ガード ---

        [Test]
        public void SearchDense_KZero_ReturnsEmpty()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(1, 0, 0, 0);
            db.Add(1, denseVector: vec);

            var query = MakeVector(1, 0, 0, 0);
            var results = db.SearchDense(query, new SearchParams { K = 0, EfSearch = 50 });
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            query.Dispose();
            vec.Dispose();
            db.Dispose();
        }

        // --- Persistence セキュリティ ---

        [Test]
        public void Load_InvalidVersion_ReturnsIncompatibleVersion()
        {
            var tempPath = System.IO.Path.GetTempFileName();
            try
            {
                // ヘッダに正しいマジックナンバーだが不正バージョンを書き込み
                var data = new byte[256];
                // Magic: UNCX
                data[0] = 0x58; data[1] = 0x43; data[2] = 0x4E; data[3] = 0x55;
                // VersionMajor = 99 (不正)
                data[4] = 99; data[5] = 0;
                System.IO.File.WriteAllBytes(tempPath, data);

                var result = IndexSerializer.Load(tempPath);
                Assert.AreEqual(ErrorCode.IncompatibleVersion, result.Error);
            }
            finally
            {
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }

        // --- 重複 ID ---

        [Test]
        public void Add_DuplicateExternalId_Rejected()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(1, 0, 0, 0);
            Assert.IsTrue(db.Add(1, denseVector: vec).IsSuccess);
            Assert.AreEqual(ErrorCode.DuplicateId, db.Add(1, denseVector: vec).Error);
            vec.Dispose();
            db.Dispose();
        }

        // --- ExternalId = 0 動作確認 ---

        [Test]
        public void Add_ExternalIdZero_Works()
        {
            var db = new UniCortexDatabase(config);
            var vec = MakeVector(1, 0, 0, 0);
            var result = db.Add(0, denseVector: vec);
            Assert.IsTrue(result.IsSuccess);

            var ext = db.GetExternalId(result.Value);
            Assert.IsTrue(ext.IsSuccess);
            Assert.AreEqual(0UL, ext.Value);

            vec.Dispose();
            db.Dispose();
        }
    }
}
