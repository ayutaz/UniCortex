using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace UniCortex.Filter
{
    /// <summary>
    /// フィルタ評価を並列実行する Burst Job。
    /// 各候補を独立に評価する IJobParallelFor。
    /// </summary>
    [BurstCompile]
    public struct FilterEvaluateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<SearchResult> Candidates;
        [ReadOnly] public NativeArray<FilterCondition> Conditions;
        [ReadOnly] public NativeArray<LogicalOp> LogicalOps;

        [ReadOnly] public NativeParallelHashMap<long, int> IntValues;
        [ReadOnly] public NativeParallelHashMap<long, float> FloatValues;
        [ReadOnly] public NativeParallelHashMap<long, bool> BoolValues;

        public int MaxDocs;

        /// <summary>出力: 各候補がフィルタを通過したか。</summary>
        public NativeArray<bool> PassFilter;

        public void Execute(int index)
        {
            int internalId = Candidates[index].InternalId;

            if (Conditions.Length == 0)
            {
                PassFilter[index] = true;
                return;
            }

            bool result = EvaluateCondition(Conditions[0], internalId);

            for (int i = 1; i < Conditions.Length; i++)
            {
                bool condResult = EvaluateCondition(Conditions[i], internalId);

                if (LogicalOps[i - 1] == LogicalOp.And)
                    result = result && condResult;
                else
                    result = result || condResult;
            }

            PassFilter[index] = result;
        }

        bool EvaluateCondition(FilterCondition cond, int internalId)
        {
            long key = (long)cond.FieldHash * MaxDocs + internalId;

            switch (cond.FieldType)
            {
                case FieldType.Int32:
                    if (!IntValues.TryGetValue(key, out int intVal)) return false;
                    return EvaluateInt(intVal, cond.Op, cond.IntValue);

                case FieldType.Float32:
                    if (!FloatValues.TryGetValue(key, out float floatVal)) return false;
                    return EvaluateFloat(floatVal, cond.Op, cond.FloatValue);

                case FieldType.Bool:
                    if (!BoolValues.TryGetValue(key, out bool boolVal)) return false;
                    return EvaluateBool(boolVal, cond.Op, cond.BoolValue);

                default:
                    return false;
            }
        }

        static bool EvaluateInt(int actual, FilterOp op, int expected)
        {
            switch (op)
            {
                case FilterOp.Equal:          return actual == expected;
                case FilterOp.NotEqual:       return actual != expected;
                case FilterOp.LessThan:       return actual < expected;
                case FilterOp.LessOrEqual:    return actual <= expected;
                case FilterOp.GreaterThan:    return actual > expected;
                case FilterOp.GreaterOrEqual: return actual >= expected;
                default: return false;
            }
        }

        static bool EvaluateFloat(float actual, FilterOp op, float expected)
        {
            switch (op)
            {
                case FilterOp.Equal:          return actual == expected;
                case FilterOp.NotEqual:       return actual != expected;
                case FilterOp.LessThan:       return actual < expected;
                case FilterOp.LessOrEqual:    return actual <= expected;
                case FilterOp.GreaterThan:    return actual > expected;
                case FilterOp.GreaterOrEqual: return actual >= expected;
                default: return false;
            }
        }

        static bool EvaluateBool(bool actual, FilterOp op, bool expected)
        {
            switch (op)
            {
                case FilterOp.Equal:    return actual == expected;
                case FilterOp.NotEqual: return actual != expected;
                default: return false;
            }
        }
    }
}
