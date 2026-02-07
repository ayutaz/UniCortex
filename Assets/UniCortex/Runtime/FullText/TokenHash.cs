using Unity.Collections;

namespace UniCortex.FullText
{
    /// <summary>
    /// トークンの UTF-8 バイト列をハッシュ化する。
    /// FNV-1a 32-bit ハッシュを使用 (Burst 互換、外部依存なし)。
    /// </summary>
    public static class TokenHash
    {
        const uint FnvOffsetBasis = 2166136261u;
        const uint FnvPrime = 16777619u;

        /// <summary>
        /// バイト列の一部分を FNV-1a でハッシュ化する。
        /// </summary>
        public static uint Hash(NativeArray<byte> data, int start, int length)
        {
            if (length <= 0)
                return 0;

            uint hash = FnvOffsetBasis;
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                hash ^= data[i];
                hash *= FnvPrime;
            }
            return hash;
        }
    }
}
