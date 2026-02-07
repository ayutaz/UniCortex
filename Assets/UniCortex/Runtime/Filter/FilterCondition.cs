namespace UniCortex.Filter
{
    /// <summary>比較演算子。</summary>
    public enum FilterOp : byte
    {
        Equal,           // ==
        NotEqual,        // !=
        LessThan,        // <
        LessOrEqual,     // <=
        GreaterThan,     // >
        GreaterOrEqual,  // >=
    }

    /// <summary>論理演算子。</summary>
    public enum LogicalOp : byte
    {
        And,  // 全条件が真のとき真
        Or,   // いずれかの条件が真のとき真
    }

    /// <summary>フィールドの型。</summary>
    public enum FieldType : byte
    {
        Int32,
        Float32,
        Bool,
    }

    /// <summary>
    /// フィルタ条件1つを表す構造体。
    /// </summary>
    public struct FilterCondition
    {
        /// <summary>フィールド名のハッシュ値。</summary>
        public int FieldHash;

        /// <summary>比較演算子。</summary>
        public FilterOp Op;

        /// <summary>フィールドの型。</summary>
        public FieldType FieldType;

        /// <summary>Int32 比較値。</summary>
        public int IntValue;

        /// <summary>Float32 比較値。</summary>
        public float FloatValue;

        /// <summary>Bool 比較値。</summary>
        public bool BoolValue;
    }
}
