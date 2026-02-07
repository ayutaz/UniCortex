namespace UniCortex.Persistence
{
    /// <summary>
    /// CRC32 チェックサム計算。IEEE 802.3 準拠。
    /// </summary>
    public static class Crc32
    {
        static readonly uint[] Table;

        static Crc32()
        {
            Table = new uint[256];
            const uint poly = 0xEDB88320;
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ poly;
                    else
                        crc >>= 1;
                }
                Table[i] = crc;
            }
        }

        /// <summary>
        /// バイト配列の CRC32 を計算する。
        /// </summary>
        public static uint Compute(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
            {
                crc = (crc >> 8) ^ Table[(crc ^ data[i]) & 0xFF];
            }
            return crc ^ 0xFFFFFFFF;
        }
    }
}
