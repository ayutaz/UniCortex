using NUnit.Framework;
using Unity.Collections;
using UniCortex.FullText;

namespace UniCortex.Tests.Editor.FullText
{
    public class BM25Tests
    {
        NativeArray<byte> ToUtf8(string text)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var arr = new NativeArray<byte>(bytes.Length, Allocator.Temp);
            for (int i = 0; i < bytes.Length; i++)
                arr[i] = bytes[i];
            return arr;
        }

        [Test]
        public void BM25_BasicSearch()
        {
            var index = new BM25Index(100, Allocator.Temp);

            // doc0: "the dragon sword"
            var text0 = ToUtf8("the dragon sword");
            var tokens0 = Tokenizer.Tokenize(text0, Allocator.Temp);
            index.Add(0, tokens0);

            // doc1: "the magic staff"
            var text1 = ToUtf8("the magic staff");
            var tokens1 = Tokenizer.Tokenize(text1, Allocator.Temp);
            index.Add(1, tokens1);

            // query: "dragon"
            var qText = ToUtf8("dragon");
            var qTokens = Tokenizer.Tokenize(qText, Allocator.Temp);

            var results = BM25Searcher.Search(
                ref index, qTokens, 10, BM25Searcher.DefaultK1, BM25Searcher.DefaultB, Allocator.Temp);

            // doc0 が "dragon" を含むのでヒット
            Assert.GreaterOrEqual(results.Length, 1);
            Assert.AreEqual(0, results[0].InternalId);
            Assert.Less(results[0].Score, 0f); // 負値変換済み

            results.Dispose();
            qTokens.Dispose();
            qText.Dispose();
            tokens0.Dispose();
            tokens1.Dispose();
            text0.Dispose();
            text1.Dispose();
            index.Dispose();
        }

        [Test]
        public void BM25_EmptyQuery_ReturnsEmpty()
        {
            var index = new BM25Index(100, Allocator.Temp);
            var qTokens = new NativeList<uint>(0, Allocator.Temp);

            var results = BM25Searcher.Search(
                ref index, qTokens, 10, BM25Searcher.DefaultK1, BM25Searcher.DefaultB, Allocator.Temp);
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            qTokens.Dispose();
            index.Dispose();
        }

        [Test]
        public void BM25_NoDocuments_ReturnsEmpty()
        {
            var index = new BM25Index(100, Allocator.Temp);
            var qText = ToUtf8("dragon");
            var qTokens = Tokenizer.Tokenize(qText, Allocator.Temp);

            var results = BM25Searcher.Search(
                ref index, qTokens, 10, BM25Searcher.DefaultK1, BM25Searcher.DefaultB, Allocator.Temp);
            Assert.AreEqual(0, results.Length);

            results.Dispose();
            qTokens.Dispose();
            qText.Dispose();
            index.Dispose();
        }

        [Test]
        public void BM25_DeletedDocExcluded()
        {
            var index = new BM25Index(100, Allocator.Temp);

            var text0 = ToUtf8("dragon sword");
            var tokens0 = Tokenizer.Tokenize(text0, Allocator.Temp);
            index.Add(0, tokens0);

            var text1 = ToUtf8("dragon shield");
            var tokens1 = Tokenizer.Tokenize(text1, Allocator.Temp);
            index.Add(1, tokens1);

            index.Remove(0); // ソフト削除

            var qText = ToUtf8("dragon");
            var qTokens = Tokenizer.Tokenize(qText, Allocator.Temp);

            var results = BM25Searcher.Search(
                ref index, qTokens, 10, BM25Searcher.DefaultK1, BM25Searcher.DefaultB, Allocator.Temp);

            Assert.AreEqual(1, results.Length);
            Assert.AreEqual(1, results[0].InternalId);

            results.Dispose();
            qTokens.Dispose();
            qText.Dispose();
            tokens0.Dispose();
            tokens1.Dispose();
            text0.Dispose();
            text1.Dispose();
            index.Dispose();
        }

        [Test]
        public void BM25_CjkSearch()
        {
            var index = new BM25Index(100, Allocator.Temp);

            var text0 = ToUtf8("東京都渋谷区");
            var tokens0 = Tokenizer.Tokenize(text0, Allocator.Temp);
            index.Add(0, tokens0);

            var text1 = ToUtf8("大阪市北区");
            var tokens1 = Tokenizer.Tokenize(text1, Allocator.Temp);
            index.Add(1, tokens1);

            // "東京" のバイグラムで検索
            var qText = ToUtf8("東京");
            var qTokens = Tokenizer.Tokenize(qText, Allocator.Temp);

            var results = BM25Searcher.Search(
                ref index, qTokens, 10, BM25Searcher.DefaultK1, BM25Searcher.DefaultB, Allocator.Temp);

            // doc0 が "東京" を含む
            Assert.GreaterOrEqual(results.Length, 1);
            Assert.AreEqual(0, results[0].InternalId);

            results.Dispose();
            qTokens.Dispose();
            qText.Dispose();
            tokens0.Dispose();
            tokens1.Dispose();
            text0.Dispose();
            text1.Dispose();
            index.Dispose();
        }

        [Test]
        public void BM25_ScoreNegation()
        {
            var index = new BM25Index(100, Allocator.Temp);

            var text0 = ToUtf8("sword");
            var tokens0 = Tokenizer.Tokenize(text0, Allocator.Temp);
            index.Add(0, tokens0);

            var qText = ToUtf8("sword");
            var qTokens = Tokenizer.Tokenize(qText, Allocator.Temp);

            var results = BM25Searcher.Search(
                ref index, qTokens, 10, BM25Searcher.DefaultK1, BM25Searcher.DefaultB, Allocator.Temp);

            Assert.AreEqual(1, results.Length);
            // スコアは負値 (BM25 正スコアの符号反転)
            Assert.Less(results[0].Score, 0f);

            results.Dispose();
            qTokens.Dispose();
            qText.Dispose();
            tokens0.Dispose();
            text0.Dispose();
            index.Dispose();
        }
    }
}
