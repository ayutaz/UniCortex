namespace UniCortex.Samples
{
    /// <summary>
    /// RPG アイテムの表示情報 (managed class)。
    /// NativeContainer には入れず、UI表示用途にのみ使用する。
    /// </summary>
    public class SampleItemInfo
    {
        public ulong Id;
        public string Name;
        public string Category;
        public int Rarity;
        public int Price;
        public float Weight;
        public bool IsEquipable;
        public string Description;

        /// <summary>Sparse ベクトルのキーワードラベル (表示用)。</summary>
        public string SparseKeywords;
    }
}
