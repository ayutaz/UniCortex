using Unity.Collections;
using Unity.Mathematics;

namespace UniCortex.FullText
{
    /// <summary>
    /// Burst 互換 UTF-8 トークナイザ。
    /// ASCII: 空白/句読点分割 + 小文字化。
    /// CJK/ひらがな/カタカナ: ユニグラム + バイグラム。
    /// </summary>
    public static class Tokenizer
    {
        /// <summary>テキスト最大バイト長。</summary>
        public const int MaxTextBytes = 65536;

        /// <summary>ドキュメントあたり最大トークン数。</summary>
        public const int MaxTokensPerDocument = 1000;

        public static bool IsAsciiAlpha(byte b) => (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z');
        public static bool IsAsciiDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';
        public static bool IsAsciiAlphaNum(byte b) => IsAsciiAlpha(b) || IsAsciiDigit(b);
        public static bool IsWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t' || b == (byte)'\n' || b == (byte)'\r';

        public static bool IsCjk(int codepoint) =>
            (codepoint >= 0x4E00 && codepoint <= 0x9FFF) ||
            (codepoint >= 0x3400 && codepoint <= 0x4DBF) ||
            (codepoint >= 0x20000 && codepoint <= 0x2A6DF);

        public static bool IsHiragana(int codepoint) => codepoint >= 0x3040 && codepoint <= 0x309F;
        public static bool IsKatakana(int codepoint) => codepoint >= 0x30A0 && codepoint <= 0x30FF;

        public static bool IsCjkLike(int codepoint) => IsCjk(codepoint) || IsHiragana(codepoint) || IsKatakana(codepoint);

        /// <summary>
        /// UTF-8 バイト列から1文字のコードポイントをデコードする。
        /// </summary>
        public static int DecodeUtf8(NativeArray<byte> data, int offset, out int bytesRead)
        {
            if (offset >= data.Length)
            {
                bytesRead = 0;
                return 0;
            }

            byte b0 = data[offset];
            if (b0 < 0x80)
            {
                bytesRead = 1;
                return b0;
            }
            if ((b0 & 0xE0) == 0xC0 && offset + 1 < data.Length)
            {
                bytesRead = 2;
                return ((b0 & 0x1F) << 6) | (data[offset + 1] & 0x3F);
            }
            if ((b0 & 0xF0) == 0xE0 && offset + 2 < data.Length)
            {
                bytesRead = 3;
                return ((b0 & 0x0F) << 12)
                     | ((data[offset + 1] & 0x3F) << 6)
                     | (data[offset + 2] & 0x3F);
            }
            if ((b0 & 0xF8) == 0xF0 && offset + 3 < data.Length)
            {
                bytesRead = 4;
                return ((b0 & 0x07) << 18)
                     | ((data[offset + 1] & 0x3F) << 12)
                     | ((data[offset + 2] & 0x3F) << 6)
                     | (data[offset + 3] & 0x3F);
            }

            // 不正 UTF-8: スキップ
            bytesRead = 1;
            return 0xFFFD; // replacement
        }

        /// <summary>
        /// UTF-8 テキストをトークナイズし、各トークンのハッシュを返す。
        /// 戻り値は呼び出し元が Dispose する。
        /// </summary>
        public static NativeList<uint> Tokenize(NativeArray<byte> text, Allocator allocator)
        {
            var hashes = new NativeList<uint>(64, allocator);

            if (text.Length == 0)
                return hashes;

            int pos = 0;
            int prevCjkCodepoint = -1;
            int prevCjkOffset = -1;
            int prevCjkBytes = 0;

            // ASCII ワードバッファ
            int wordStart = -1;

            while (pos < text.Length && hashes.Length < MaxTokensPerDocument)
            {
                int codepoint = DecodeUtf8(text, pos, out int bytesRead);
                if (bytesRead == 0) break;

                if (codepoint < 0x80)
                {
                    byte b = (byte)codepoint;

                    // CJK 系が直前にあったらバイグラムチェーンをリセット
                    if (prevCjkCodepoint >= 0)
                    {
                        prevCjkCodepoint = -1;
                        prevCjkOffset = -1;
                        prevCjkBytes = 0;
                    }

                    if (IsAsciiAlphaNum(b))
                    {
                        if (wordStart < 0)
                            wordStart = pos;
                    }
                    else
                    {
                        // 単語終了
                        if (wordStart >= 0)
                        {
                            hashes.Add(HashAsciiWord(text, wordStart, pos - wordStart));
                            wordStart = -1;
                        }
                    }
                }
                else if (IsCjkLike(codepoint))
                {
                    // ASCII 単語終了
                    if (wordStart >= 0)
                    {
                        hashes.Add(HashAsciiWord(text, wordStart, pos - wordStart));
                        wordStart = -1;
                    }

                    // ユニグラム
                    hashes.Add(HashBytes(text, pos, bytesRead));

                    // バイグラム (前の CJK 文字との結合)
                    if (prevCjkCodepoint >= 0 && hashes.Length < MaxTokensPerDocument)
                    {
                        hashes.Add(HashBytes(text, prevCjkOffset, prevCjkBytes + bytesRead));
                    }

                    prevCjkCodepoint = codepoint;
                    prevCjkOffset = pos;
                    prevCjkBytes = bytesRead;
                }
                else
                {
                    // その他の文字 (記号等) → 単語区切り
                    if (wordStart >= 0)
                    {
                        hashes.Add(HashAsciiWord(text, wordStart, pos - wordStart));
                        wordStart = -1;
                    }
                    prevCjkCodepoint = -1;
                }

                pos += bytesRead;
            }

            // 末尾の ASCII ワード
            if (wordStart >= 0)
            {
                hashes.Add(HashAsciiWord(text, wordStart, pos - wordStart));
            }

            return hashes;
        }

        /// <summary>
        /// ASCII ワードを小文字化してハッシュ計算する。
        /// </summary>
        static uint HashAsciiWord(NativeArray<byte> data, int start, int length)
        {
            // 小文字化した一時バッファ
            var lower = new NativeArray<byte>(length, Allocator.Temp);
            for (int i = 0; i < length; i++)
            {
                byte b = data[start + i];
                if (b >= (byte)'A' && b <= (byte)'Z')
                    lower[i] = (byte)(b + 32);
                else
                    lower[i] = b;
            }
            uint hash = TokenHash.Hash(lower, 0, length);
            lower.Dispose();
            return hash;
        }

        /// <summary>
        /// バイト列のハッシュを計算する。
        /// </summary>
        static uint HashBytes(NativeArray<byte> data, int start, int length)
        {
            return TokenHash.Hash(data, start, length);
        }
    }
}
