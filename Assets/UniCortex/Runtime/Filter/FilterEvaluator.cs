using Unity.Collections;

namespace UniCortex.Filter
{
    /// <summary>
    /// フィルタ条件の評価ロジック。
    /// </summary>
    public static class FilterEvaluator
    {
        /// <summary>
        /// 指定した internalId が条件式を満たすか評価する。
        /// </summary>
        public static bool Evaluate(
            NativeArray<FilterCondition> conditions,
            NativeArray<LogicalOp> logicalOps,
            int internalId,
            ref MetadataStorage metadata)
        {
            if (conditions.Length == 0)
                return true;

            bool result = EvaluateCondition(conditions[0], internalId, ref metadata);

            for (int i = 1; i < conditions.Length; i++)
            {
                bool condResult = EvaluateCondition(conditions[i], internalId, ref metadata);

                if (logicalOps[i - 1] == LogicalOp.And)
                    result = result && condResult;
                else
                    result = result || condResult;
            }

            return result;
        }

        static bool EvaluateCondition(FilterCondition cond, int internalId, ref MetadataStorage metadata)
        {
            switch (cond.FieldType)
            {
                case FieldType.Int32:
                    if (!metadata.TryGetInt(cond.FieldHash, internalId, out int intVal))
                        return false;
                    return EvaluateInt(intVal, cond.Op, cond.IntValue);

                case FieldType.Float32:
                    if (!metadata.TryGetFloat(cond.FieldHash, internalId, out float floatVal))
                        return false;
                    return EvaluateFloat(floatVal, cond.Op, cond.FloatValue);

                case FieldType.Bool:
                    if (!metadata.TryGetBool(cond.FieldHash, internalId, out bool boolVal))
                        return false;
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
                // Bool の順序演算は常に false
                default: return false;
            }
        }
    }
}
