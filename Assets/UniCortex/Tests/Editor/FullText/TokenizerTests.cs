using NUnit.Framework;
using Unity.Collections;
using UniCortex.FullText;

namespace UniCortex.Tests.Editor.FullText
{
    public class TokenizerTests
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
        public void Tokenize_AsciiBasic()
        {
            var text = ToUtf8("The Dragon Sword");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);

            // "the", "dragon", "sword" → 3 tokens
            Assert.AreEqual(3, hashes.Length);

            // 小文字化されるので "The" と "the" は同じハッシュ
            var text2 = ToUtf8("the dragon sword");
            var hashes2 = Tokenizer.Tokenize(text2, Allocator.Temp);
            Assert.AreEqual(hashes[0], hashes2[0]); // "the" == "the"
            Assert.AreEqual(hashes[1], hashes2[1]); // "dragon"
            Assert.AreEqual(hashes[2], hashes2[2]); // "sword"

            hashes.Dispose();
            hashes2.Dispose();
            text.Dispose();
            text2.Dispose();
        }

        [Test]
        public void Tokenize_CjkUnigramBigram()
        {
            // "東京都" → ["東", "京", "都", "東京", "京都"] = 5 tokens
            var text = ToUtf8("東京都");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);

            Assert.AreEqual(5, hashes.Length);

            hashes.Dispose();
            text.Dispose();
        }

        [Test]
        public void Tokenize_MixedAsciiCjk()
        {
            // "HP回復potion" → ["hp", "回", "復", "回復", "potion"] = 5 tokens
            var text = ToUtf8("HP回復potion");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);

            Assert.AreEqual(5, hashes.Length);

            hashes.Dispose();
            text.Dispose();
        }

        [Test]
        public void Tokenize_EmptyText()
        {
            var text = new NativeArray<byte>(0, Allocator.Temp);
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);
            Assert.AreEqual(0, hashes.Length);
            hashes.Dispose();
            text.Dispose();
        }

        [Test]
        public void Tokenize_WhitespaceOnly()
        {
            var text = ToUtf8("   \t\n");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);
            Assert.AreEqual(0, hashes.Length);
            hashes.Dispose();
            text.Dispose();
        }

        [Test]
        public void Tokenize_SingleCjkCharacter()
        {
            // "剣" → ["剣"] = 1 unigram
            var text = ToUtf8("剣");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);
            Assert.AreEqual(1, hashes.Length);
            hashes.Dispose();
            text.Dispose();
        }

        [Test]
        public void Tokenize_NumbersIncluded()
        {
            var text = ToUtf8("damage 150 HP");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);
            // "damage", "150", "hp"
            Assert.AreEqual(3, hashes.Length);
            hashes.Dispose();
            text.Dispose();
        }

        [Test]
        public void Tokenize_Hiragana()
        {
            // "あいう" → ["あ","い","う","あい","いう"] = 5
            var text = ToUtf8("あいう");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);
            Assert.AreEqual(5, hashes.Length);
            hashes.Dispose();
            text.Dispose();
        }

        [Test]
        public void Tokenize_Katakana()
        {
            // "アイテム" → 4 unigrams + 3 bigrams = 7
            var text = ToUtf8("アイテム");
            var hashes = Tokenizer.Tokenize(text, Allocator.Temp);
            Assert.AreEqual(7, hashes.Length);
            hashes.Dispose();
            text.Dispose();
        }
    }
}
