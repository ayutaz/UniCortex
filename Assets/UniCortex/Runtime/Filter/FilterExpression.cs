using System;
using Unity.Collections;

namespace UniCortex.Filter
{
    /// <summary>
    /// フィルタ条件式。複数の FilterCondition を論理演算子で結合する。
    /// </summary>
    public struct FilterExpression : IDisposable
    {
        /// <summary>条件リスト。</summary>
        public NativeList<FilterCondition> Conditions;

        /// <summary>
        /// 条件間の論理演算子。LogicalOps.Length == Conditions.Length - 1。
        /// </summary>
        public NativeList<LogicalOp> LogicalOps;

        public FilterExpression(Allocator allocator)
        {
            Conditions = new NativeList<FilterCondition>(4, allocator);
            LogicalOps = new NativeList<LogicalOp>(4, allocator);
        }

        /// <summary>Int32 条件を追加する。2つ目以降は先に AddLogicalOp を呼ぶこと。</summary>
        public void AddCondition(int fieldHash, FilterOp op, int intValue)
        {
            Conditions.Add(new FilterCondition
            {
                FieldHash = fieldHash,
                Op = op,
                FieldType = FieldType.Int32,
                IntValue = intValue
            });
        }

        /// <summary>Float32 条件を追加する。</summary>
        public void AddCondition(int fieldHash, FilterOp op, float floatValue)
        {
            Conditions.Add(new FilterCondition
            {
                FieldHash = fieldHash,
                Op = op,
                FieldType = FieldType.Float32,
                FloatValue = floatValue
            });
        }

        /// <summary>Bool 条件を追加する。</summary>
        public void AddCondition(int fieldHash, FilterOp op, bool boolValue)
        {
            Conditions.Add(new FilterCondition
            {
                FieldHash = fieldHash,
                Op = op,
                FieldType = FieldType.Bool,
                BoolValue = boolValue
            });
        }

        /// <summary>論理演算子を追加する。</summary>
        public void AddLogicalOp(LogicalOp logicalOp)
        {
            LogicalOps.Add(logicalOp);
        }

        public void Dispose()
        {
            if (Conditions.IsCreated) Conditions.Dispose();
            if (LogicalOps.IsCreated) LogicalOps.Dispose();
        }
    }
}
