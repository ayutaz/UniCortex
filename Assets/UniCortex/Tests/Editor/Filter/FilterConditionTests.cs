using NUnit.Framework;
using Unity.Collections;
using UniCortex.Filter;

namespace UniCortex.Tests.Editor.Filter
{
    public class FilterConditionTests
    {
        [Test]
        public void IntEqual_Pass()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetInt(42, 0, 1000);

            var conditions = new NativeArray<FilterCondition>(1, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 42, Op = FilterOp.Equal, FieldType = FieldType.Int32, IntValue = 1000
            };
            var ops = new NativeArray<LogicalOp>(0, Allocator.Temp);

            Assert.IsTrue(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void IntGreaterThan_Pass()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetInt(42, 0, 1500);

            var conditions = new NativeArray<FilterCondition>(1, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 42, Op = FilterOp.GreaterThan, FieldType = FieldType.Int32, IntValue = 1000
            };
            var ops = new NativeArray<LogicalOp>(0, Allocator.Temp);

            Assert.IsTrue(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void IntGreaterThan_Fail()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetInt(42, 0, 500);

            var conditions = new NativeArray<FilterCondition>(1, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 42, Op = FilterOp.GreaterThan, FieldType = FieldType.Int32, IntValue = 1000
            };
            var ops = new NativeArray<LogicalOp>(0, Allocator.Temp);

            Assert.IsFalse(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void FloatLessOrEqual_Pass()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetFloat(10, 0, 2.5f);

            var conditions = new NativeArray<FilterCondition>(1, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 10, Op = FilterOp.LessOrEqual, FieldType = FieldType.Float32, FloatValue = 3.0f
            };
            var ops = new NativeArray<LogicalOp>(0, Allocator.Temp);

            Assert.IsTrue(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void BoolEqual_Pass()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetBool(5, 0, true);

            var conditions = new NativeArray<FilterCondition>(1, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 5, Op = FilterOp.Equal, FieldType = FieldType.Bool, BoolValue = true
            };
            var ops = new NativeArray<LogicalOp>(0, Allocator.Temp);

            Assert.IsTrue(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void BoolOrderOp_AlwaysFalse()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetBool(5, 0, true);

            var conditions = new NativeArray<FilterCondition>(1, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 5, Op = FilterOp.GreaterThan, FieldType = FieldType.Bool, BoolValue = false
            };
            var ops = new NativeArray<LogicalOp>(0, Allocator.Temp);

            Assert.IsFalse(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void MissingField_ReturnsFalse()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);

            var conditions = new NativeArray<FilterCondition>(1, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 999, Op = FilterOp.Equal, FieldType = FieldType.Int32, IntValue = 100
            };
            var ops = new NativeArray<LogicalOp>(0, Allocator.Temp);

            Assert.IsFalse(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void AndCombination()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetInt(1, 0, 1000);
            metadata.SetFloat(2, 0, 2.5f);

            var conditions = new NativeArray<FilterCondition>(2, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 1, Op = FilterOp.GreaterOrEqual, FieldType = FieldType.Int32, IntValue = 500
            };
            conditions[1] = new FilterCondition
            {
                FieldHash = 2, Op = FilterOp.LessThan, FieldType = FieldType.Float32, FloatValue = 3.0f
            };
            var ops = new NativeArray<LogicalOp>(1, Allocator.Temp);
            ops[0] = LogicalOp.And;

            Assert.IsTrue(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }

        [Test]
        public void OrCombination()
        {
            var metadata = new MetadataStorage(100, Allocator.Temp);
            metadata.SetInt(1, 0, 100); // < 500 → false

            var conditions = new NativeArray<FilterCondition>(2, Allocator.Temp);
            conditions[0] = new FilterCondition
            {
                FieldHash = 1, Op = FilterOp.GreaterOrEqual, FieldType = FieldType.Int32, IntValue = 500
            };
            conditions[1] = new FilterCondition
            {
                FieldHash = 1, Op = FilterOp.Equal, FieldType = FieldType.Int32, IntValue = 100
            };
            var ops = new NativeArray<LogicalOp>(1, Allocator.Temp);
            ops[0] = LogicalOp.Or;

            Assert.IsTrue(FilterEvaluator.Evaluate(conditions, ops, 0, ref metadata));

            conditions.Dispose();
            ops.Dispose();
            metadata.Dispose();
        }
    }
}
