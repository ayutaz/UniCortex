using NUnit.Framework;
using Unity.Collections;

namespace UniCortex.Tests.Editor.Core
{
    public class NativeMaxHeapTests
    {
        [Test]
        public void Push_Pop_ReturnsMaxScore()
        {
            var heap = new NativeMaxHeap(10, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.1f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.5f });
            heap.Push(new SearchResult { InternalId = 2, Score = 0.3f });

            Assert.AreEqual(3, heap.Count);
            var max = heap.Pop();
            Assert.AreEqual(1, max.InternalId);
            Assert.AreEqual(0.5f, max.Score, 1e-6f);

            heap.Dispose();
        }

        [Test]
        public void Peek_ReturnsMaxWithoutRemoving()
        {
            var heap = new NativeMaxHeap(10, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.1f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.9f });

            var peeked = heap.Peek();
            Assert.AreEqual(1, peeked.InternalId);
            Assert.AreEqual(2, heap.Count);

            heap.Dispose();
        }

        [Test]
        public void Push_CapacityExceeded_ReplacesMin()
        {
            var heap = new NativeMaxHeap(3, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.1f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.5f });
            heap.Push(new SearchResult { InternalId = 2, Score = 0.9f });

            // 容量満杯、Score 0.3 > 最小 0.1 なので置換
            heap.Push(new SearchResult { InternalId = 3, Score = 0.3f });
            Assert.AreEqual(3, heap.Count);

            // Pop で降順確認
            var r1 = heap.Pop();
            var r2 = heap.Pop();
            var r3 = heap.Pop();
            Assert.AreEqual(0.9f, r1.Score, 1e-6f);
            Assert.AreEqual(0.5f, r2.Score, 1e-6f);
            Assert.AreEqual(0.3f, r3.Score, 1e-6f);

            heap.Dispose();
        }

        [Test]
        public void Push_CapacityExceeded_IgnoresSmaller()
        {
            var heap = new NativeMaxHeap(2, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.5f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.9f });

            // Score 0.1 < 最小 0.5 なので無視
            heap.Push(new SearchResult { InternalId = 2, Score = 0.1f });
            Assert.AreEqual(2, heap.Count);

            var r1 = heap.Pop();
            Assert.AreEqual(1, r1.InternalId);

            heap.Dispose();
        }

        [Test]
        public void Pop_Empty_ReturnsDefault()
        {
            var heap = new NativeMaxHeap(5, Allocator.Temp);
            var result = heap.Pop();
            Assert.AreEqual(0, result.InternalId);
            Assert.AreEqual(0f, result.Score);
            heap.Dispose();
        }

        [Test]
        public void Clear_ResetsCount()
        {
            var heap = new NativeMaxHeap(5, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 1f });
            heap.Clear();
            Assert.AreEqual(0, heap.Count);
            heap.Dispose();
        }
    }
}
