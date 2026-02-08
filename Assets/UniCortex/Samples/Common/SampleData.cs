using System.Text;
using Unity.Collections;
using UniCortex;
using UniCortex.Hnsw;
using UniCortex.Sparse;

namespace UniCortex.Samples
{
    /// <summary>
    /// サンプル用 RPG アイテムデータ (20件) と DB 構築ヘルパー。
    /// Dense ベクトル 8次元: [Fire, Ice, Earth/Thunder, Defense, Agility, Healing, Dark, Holy]
    /// </summary>
    public static class SampleData
    {
        public const int ItemCount = 20;
        public const int VectorDimension = 8;

        // Metadata field hash constants
        public const int FieldPrice = 100;
        public const int FieldRarity = 101;
        public const int FieldWeight = 102;
        public const int FieldIsEquipable = 103;
        public const int FieldCategory = 104;

        // Category hash constants (for int metadata)
        public const int CategoryWeapon = 1;
        public const int CategoryArmor = 2;
        public const int CategoryAccessory = 3;
        public const int CategoryConsumable = 4;
        public const int CategoryMaterial = 5;

        // Sparse dimension indices (keyword features)
        public const int SparseFire = 0;
        public const int SparseIce = 1;
        public const int SparseThunder = 2;
        public const int SparseSlash = 3;
        public const int SparseMagic = 4;
        public const int SparseCrush = 5;
        public const int SparseDefense = 6;
        public const int SparseHealing = 7;
        public const int SparseDark = 8;
        public const int SparseHoly = 9;
        public const int SparsePoison = 10;
        public const int SparseAgility = 11;
        public const int SparsePiercing = 12;
        public const int SparseDragon = 13;
        public const int SparseUndead = 14;
        public const int SparseNature = 15;

        /// <summary>Sparse ディメンション名ラベル (UI表示用)。</summary>
        public static readonly string[] SparseLabels = new string[]
        {
            "Fire", "Ice", "Thunder", "Slash", "Magic", "Crush",
            "Defense", "Healing", "Dark", "Holy", "Poison", "Agility",
            "Piercing", "Dragon", "Undead", "Nature"
        };

        /// <summary>Dense ディメンション名ラベル (UI表示用)。</summary>
        public static readonly string[] DenseLabels = new string[]
        {
            "Fire", "Ice", "Earth/Thunder", "Defense", "Agility", "Healing", "Dark", "Holy"
        };

        /// <summary>全20件のアイテム表示情報を返す。</summary>
        public static SampleItemInfo[] GetItemInfos()
        {
            return new SampleItemInfo[]
            {
                new SampleItemInfo { Id = 1,  Name = "Fire Sword",        Category = "Weapon",     Rarity = 3, Price = 1500,  Weight = 3.2f,  IsEquipable = true,  Description = "A blazing sword forged in volcanic fire",            SparseKeywords = "Fire, Slash" },
                new SampleItemInfo { Id = 2,  Name = "Ice Staff",         Category = "Weapon",     Rarity = 2, Price = 800,   Weight = 2.1f,  IsEquipable = true,  Description = "A frost staff channeling arctic ice",                SparseKeywords = "Ice, Magic" },
                new SampleItemInfo { Id = 3,  Name = "Thunder Hammer",    Category = "Weapon",     Rarity = 4, Price = 3000,  Weight = 5.5f,  IsEquipable = true,  Description = "Mighty hammer crackling with thunder",               SparseKeywords = "Thunder, Crush" },
                new SampleItemInfo { Id = 4,  Name = "Healing Potion",    Category = "Consumable", Rarity = 1, Price = 100,   Weight = 0.3f,  IsEquipable = false, Description = "A potion that restores health points",               SparseKeywords = "Healing" },
                new SampleItemInfo { Id = 5,  Name = "Dragon Shield",     Category = "Armor",      Rarity = 4, Price = 2800,  Weight = 6.0f,  IsEquipable = true,  Description = "A shield made from dragon scales for fire defense",  SparseKeywords = "Fire, Defense, Dragon" },
                new SampleItemInfo { Id = 6,  Name = "Shadow Dagger",     Category = "Weapon",     Rarity = 3, Price = 1200,  Weight = 1.0f,  IsEquipable = true,  Description = "A dark dagger infused with shadow magic",            SparseKeywords = "Dark, Slash, Agility" },
                new SampleItemInfo { Id = 7,  Name = "Holy Armor",        Category = "Armor",      Rarity = 5, Price = 5000,  Weight = 8.0f,  IsEquipable = true,  Description = "Sacred armor blessed by holy light",                 SparseKeywords = "Holy, Defense" },
                new SampleItemInfo { Id = 8,  Name = "Mana Crystal",      Category = "Material",   Rarity = 2, Price = 400,   Weight = 0.5f,  IsEquipable = false, Description = "A crystal pulsing with raw magical energy",          SparseKeywords = "Magic" },
                new SampleItemInfo { Id = 9,  Name = "Poison Bow",        Category = "Weapon",     Rarity = 3, Price = 1800,  Weight = 2.5f,  IsEquipable = true,  Description = "A bow with arrows tipped in deadly poison",          SparseKeywords = "Poison, Piercing, Agility" },
                new SampleItemInfo { Id = 10, Name = "Frost Ring",        Category = "Accessory",  Rarity = 3, Price = 2000,  Weight = 0.1f,  IsEquipable = true,  Description = "A ring radiating cold frost and ice power",          SparseKeywords = "Ice, Magic" },
                new SampleItemInfo { Id = 11, Name = "Earth Plate",       Category = "Armor",      Rarity = 3, Price = 2200,  Weight = 7.0f,  IsEquipable = true,  Description = "Heavy armor forged from enchanted earth stone",      SparseKeywords = "Thunder, Defense" },
                new SampleItemInfo { Id = 12, Name = "Healing Herb",      Category = "Consumable", Rarity = 1, Price = 50,    Weight = 0.1f,  IsEquipable = false, Description = "A natural herb with gentle healing properties",      SparseKeywords = "Healing, Nature" },
                new SampleItemInfo { Id = 13, Name = "Dark Grimoire",     Category = "Weapon",     Rarity = 5, Price = 4500,  Weight = 3.0f,  IsEquipable = true,  Description = "An ancient grimoire containing forbidden dark magic", SparseKeywords = "Dark, Magic" },
                new SampleItemInfo { Id = 14, Name = "Speed Boots",       Category = "Accessory",  Rarity = 2, Price = 900,   Weight = 1.5f,  IsEquipable = true,  Description = "Lightweight boots that enhance agility and speed",   SparseKeywords = "Agility" },
                new SampleItemInfo { Id = 15, Name = "Holy Water",        Category = "Consumable", Rarity = 2, Price = 300,   Weight = 0.5f,  IsEquipable = false, Description = "Blessed water effective against undead creatures",    SparseKeywords = "Holy, Healing, Undead" },
                new SampleItemInfo { Id = 16, Name = "Fire Crystal",      Category = "Material",   Rarity = 3, Price = 700,   Weight = 0.4f,  IsEquipable = false, Description = "A crystal containing concentrated fire energy",      SparseKeywords = "Fire, Magic" },
                new SampleItemInfo { Id = 17, Name = "Ice Lance",         Category = "Weapon",     Rarity = 3, Price = 1600,  Weight = 3.8f,  IsEquipable = true,  Description = "A lance coated in eternal frost and piercing ice",   SparseKeywords = "Ice, Piercing" },
                new SampleItemInfo { Id = 18, Name = "Dragon Amulet",     Category = "Accessory",  Rarity = 4, Price = 3500,  Weight = 0.2f,  IsEquipable = true,  Description = "An amulet with the power of an ancient dragon",      SparseKeywords = "Fire, Dragon, Defense" },
                new SampleItemInfo { Id = 19, Name = "Undead Slayer",     Category = "Weapon",     Rarity = 4, Price = 2500,  Weight = 4.0f,  IsEquipable = true,  Description = "A holy sword forged to slay undead monsters",        SparseKeywords = "Holy, Slash, Undead" },
                new SampleItemInfo { Id = 20, Name = "Nature Staff",      Category = "Weapon",     Rarity = 2, Price = 600,   Weight = 2.0f,  IsEquipable = true,  Description = "A wooden staff channeling nature healing magic",     SparseKeywords = "Nature, Healing, Magic" },
            };
        }

        // Dense vectors: [Fire, Ice, Earth/Thunder, Defense, Agility, Healing, Dark, Holy]
        static readonly float[][] DenseVectors = new float[][]
        {
            new float[] { 0.9f, 0.0f, 0.0f, 0.1f, 0.2f, 0.0f, 0.0f, 0.0f }, // 1  Fire Sword
            new float[] { 0.0f, 0.9f, 0.0f, 0.0f, 0.1f, 0.0f, 0.0f, 0.0f }, // 2  Ice Staff
            new float[] { 0.0f, 0.0f, 0.9f, 0.1f, 0.0f, 0.0f, 0.0f, 0.0f }, // 3  Thunder Hammer
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.9f, 0.0f, 0.0f }, // 4  Healing Potion
            new float[] { 0.5f, 0.0f, 0.0f, 0.8f, 0.0f, 0.0f, 0.0f, 0.0f }, // 5  Dragon Shield
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.7f, 0.0f, 0.9f, 0.0f }, // 6  Shadow Dagger
            new float[] { 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.1f, 0.0f, 0.9f }, // 7  Holy Armor
            new float[] { 0.1f, 0.1f, 0.1f, 0.0f, 0.0f, 0.1f, 0.1f, 0.1f }, // 8  Mana Crystal
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.4f, 0.0f }, // 9  Poison Bow
            new float[] { 0.0f, 0.8f, 0.0f, 0.1f, 0.0f, 0.0f, 0.0f, 0.0f }, // 10 Frost Ring
            new float[] { 0.0f, 0.0f, 0.7f, 0.8f, 0.0f, 0.0f, 0.0f, 0.0f }, // 11 Earth Plate
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.8f, 0.0f, 0.1f }, // 12 Healing Herb
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.9f, 0.0f }, // 13 Dark Grimoire
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.9f, 0.0f, 0.0f, 0.0f }, // 14 Speed Boots
            new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.5f, 0.0f, 0.7f }, // 15 Holy Water
            new float[] { 0.8f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f }, // 16 Fire Crystal
            new float[] { 0.0f, 0.8f, 0.0f, 0.0f, 0.2f, 0.0f, 0.0f, 0.0f }, // 17 Ice Lance
            new float[] { 0.6f, 0.0f, 0.0f, 0.4f, 0.0f, 0.0f, 0.0f, 0.0f }, // 18 Dragon Amulet
            new float[] { 0.0f, 0.0f, 0.0f, 0.1f, 0.2f, 0.0f, 0.0f, 0.9f }, // 19 Undead Slayer
            new float[] { 0.0f, 0.0f, 0.1f, 0.0f, 0.0f, 0.7f, 0.0f, 0.2f }, // 20 Nature Staff
        };

        // Sparse elements per item: (dimensionIndex, weight)
        static readonly (int, float)[][] SparseVectors = new (int, float)[][]
        {
            new (int, float)[] { (SparseFire, 0.9f), (SparseSlash, 0.7f) },                                    // 1  Fire Sword
            new (int, float)[] { (SparseIce, 0.9f), (SparseMagic, 0.6f) },                                     // 2  Ice Staff
            new (int, float)[] { (SparseThunder, 0.9f), (SparseCrush, 0.8f) },                                  // 3  Thunder Hammer
            new (int, float)[] { (SparseHealing, 0.9f) },                                                       // 4  Healing Potion
            new (int, float)[] { (SparseFire, 0.4f), (SparseDefense, 0.8f), (SparseDragon, 0.7f) },             // 5  Dragon Shield
            new (int, float)[] { (SparseDark, 0.8f), (SparseSlash, 0.5f), (SparseAgility, 0.6f) },              // 6  Shadow Dagger
            new (int, float)[] { (SparseHoly, 0.9f), (SparseDefense, 0.8f) },                                   // 7  Holy Armor
            new (int, float)[] { (SparseMagic, 0.9f) },                                                         // 8  Mana Crystal
            new (int, float)[] { (SparsePoison, 0.8f), (SparsePiercing, 0.7f), (SparseAgility, 0.5f) },         // 9  Poison Bow
            new (int, float)[] { (SparseIce, 0.7f), (SparseMagic, 0.5f) },                                      // 10 Frost Ring
            new (int, float)[] { (SparseThunder, 0.5f), (SparseDefense, 0.9f) },                                 // 11 Earth Plate
            new (int, float)[] { (SparseHealing, 0.7f), (SparseNature, 0.6f) },                                  // 12 Healing Herb
            new (int, float)[] { (SparseDark, 0.9f), (SparseMagic, 0.8f) },                                      // 13 Dark Grimoire
            new (int, float)[] { (SparseAgility, 0.9f) },                                                        // 14 Speed Boots
            new (int, float)[] { (SparseHoly, 0.7f), (SparseHealing, 0.4f), (SparseUndead, 0.6f) },              // 15 Holy Water
            new (int, float)[] { (SparseFire, 0.8f), (SparseMagic, 0.4f) },                                      // 16 Fire Crystal
            new (int, float)[] { (SparseIce, 0.8f), (SparsePiercing, 0.7f) },                                    // 17 Ice Lance
            new (int, float)[] { (SparseFire, 0.5f), (SparseDragon, 0.8f), (SparseDefense, 0.4f) },              // 18 Dragon Amulet
            new (int, float)[] { (SparseHoly, 0.8f), (SparseSlash, 0.6f), (SparseUndead, 0.7f) },                // 19 Undead Slayer
            new (int, float)[] { (SparseNature, 0.8f), (SparseHealing, 0.6f), (SparseMagic, 0.5f) },             // 20 Nature Staff
        };

        /// <summary>
        /// 20件登録済み + Build() 済みの UniCortexDatabase を生成する。
        /// 呼び出し元が Dispose() する責務を負う。
        /// </summary>
        public static UniCortexDatabase CreateAndPopulateDatabase()
        {
            var config = new DatabaseConfig
            {
                Capacity = 100,
                Dimension = VectorDimension,
                HnswConfig = HnswConfig.Default,
                DistanceType = DistanceType.EuclideanSq,
                BM25K1 = 1.2f,
                BM25B = 0.75f,
            };

            var db = new UniCortexDatabase(config);
            var items = GetItemInfos();

            for (int i = 0; i < ItemCount; i++)
            {
                var item = items[i];

                // Dense vector
                var dense = new NativeArray<float>(VectorDimension, Allocator.Temp);
                for (int d = 0; d < VectorDimension; d++)
                    dense[d] = DenseVectors[i][d];

                // Sparse vector
                var sparseData = SparseVectors[i];
                var sparse = new NativeArray<SparseElement>(sparseData.Length, Allocator.Temp);
                for (int s = 0; s < sparseData.Length; s++)
                    sparse[s] = new SparseElement { Index = sparseData[s].Item1, Value = sparseData[s].Item2 };

                // BM25 text (UTF-8)
                var textBytes = Encoding.UTF8.GetBytes(item.Description);
                var text = new NativeArray<byte>(textBytes.Length, Allocator.Temp);
                text.CopyFrom(textBytes);

                db.Add(item.Id, dense, sparse, text);

                dense.Dispose();
                sparse.Dispose();
                text.Dispose();

                // Metadata
                db.SetMetadataInt(item.Id, FieldPrice, item.Price);
                db.SetMetadataInt(item.Id, FieldRarity, item.Rarity);
                db.SetMetadataFloat(item.Id, FieldWeight, item.Weight);
                db.SetMetadataBool(item.Id, FieldIsEquipable, item.IsEquipable);
                db.SetMetadataInt(item.Id, FieldCategory, CategoryFromString(item.Category));
            }

            db.Build();
            return db;
        }

        /// <summary>クエリ用 Dense ベクトルを生成する。呼び出し元が Dispose() する。</summary>
        public static NativeArray<float> MakeQueryVector(Allocator allocator, params float[] values)
        {
            var arr = new NativeArray<float>(VectorDimension, allocator);
            for (int i = 0; i < VectorDimension && i < values.Length; i++)
                arr[i] = values[i];
            return arr;
        }

        /// <summary>テキストを UTF-8 バイト配列に変換する。呼び出し元が Dispose() する。</summary>
        public static NativeArray<byte> MakeTextQuery(string text, Allocator allocator)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var arr = new NativeArray<byte>(bytes.Length, allocator);
            arr.CopyFrom(bytes);
            return arr;
        }

        /// <summary>Sparse クエリを生成する。呼び出し元が Dispose() する。</summary>
        public static NativeArray<SparseElement> MakeSparseQuery(Allocator allocator, params (int index, float value)[] elements)
        {
            var arr = new NativeArray<SparseElement>(elements.Length, allocator);
            for (int i = 0; i < elements.Length; i++)
                arr[i] = new SparseElement { Index = elements[i].index, Value = elements[i].value };
            return arr;
        }

        /// <summary>検索結果を表示用文字列にフォーマットする。</summary>
        public static string FormatResult(SearchResult result, UniCortexDatabase db, SampleItemInfo[] items)
        {
            var extResult = db.GetExternalId(result.InternalId);
            if (!extResult.IsSuccess)
                return $"[InternalId={result.InternalId}] Score={result.Score:F4} (ID lookup failed)";

            ulong extId = extResult.Value;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Id == extId)
                {
                    var item = items[i];
                    return $"#{extId} {item.Name} [{item.Category}] R{item.Rarity} ${item.Price} - Score={result.Score:F4}";
                }
            }
            return $"[ExternalId={extId}] Score={result.Score:F4} (item not found)";
        }

        /// <summary>検索結果の詳細行を生成する (Description 含む)。</summary>
        public static string FormatResultDetailed(SearchResult result, UniCortexDatabase db, SampleItemInfo[] items)
        {
            var extResult = db.GetExternalId(result.InternalId);
            if (!extResult.IsSuccess)
                return $"[InternalId={result.InternalId}] Score={result.Score:F4}";

            ulong extId = extResult.Value;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Id == extId)
                {
                    var item = items[i];
                    return $"#{extId} {item.Name} [{item.Category}] R{item.Rarity} ${item.Price} W{item.Weight:F1}kg {(item.IsEquipable ? "Equip" : "")}\n  Score={result.Score:F4} | {item.Description}";
                }
            }
            return $"[ExternalId={extId}] Score={result.Score:F4}";
        }

        static int CategoryFromString(string cat)
        {
            switch (cat)
            {
                case "Weapon":     return CategoryWeapon;
                case "Armor":      return CategoryArmor;
                case "Accessory":  return CategoryAccessory;
                case "Consumable": return CategoryConsumable;
                case "Material":   return CategoryMaterial;
                default:           return 0;
            }
        }

        /// <summary>カテゴリ定数を表示名に変換する。</summary>
        public static string CategoryToString(int cat)
        {
            switch (cat)
            {
                case CategoryWeapon:     return "Weapon";
                case CategoryArmor:      return "Armor";
                case CategoryAccessory:  return "Accessory";
                case CategoryConsumable: return "Consumable";
                case CategoryMaterial:   return "Material";
                default:                 return "Unknown";
            }
        }
    }
}
