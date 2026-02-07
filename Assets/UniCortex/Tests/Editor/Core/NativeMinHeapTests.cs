using NUnit.Framework;
using Unity.Collections;

namespace UniCortex.Tests.Editor.Core
{
    public class NativeMinHeapTests
    {
        [Test]
        public void Push_Pop_ReturnsMinScore()
        {
            var heap = new NativeMinHeap(10, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.5f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.1f });
            heap.Push(new SearchResult { InternalId = 2, Score = 0.3f });

            Assert.AreEqual(3, heap.Count);
            var min = heap.Pop();
            Assert.AreEqual(1, min.InternalId);
            Assert.AreEqual(0.1f, min.Score, 1e-6f);

            heap.Dispose();
        }

        [Test]
        public void Peek_ReturnsMinWithoutRemoving()
        {
            var heap = new NativeMinHeap(10, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.3f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.1f });

            var peeked = heap.Peek();
            Assert.AreEqual(1, peeked.InternalId);
            Assert.AreEqual(2, heap.Count);

            heap.Dispose();
        }

        [Test]
        public void Push_CapacityExceeded_ReplacesMax()
        {
            var heap = new NativeMinHeap(3, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.5f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.3f });
            heap.Push(new SearchResult { InternalId = 2, Score = 0.1f });

            // 容量満杯、Score 0.2 < 最大 0.5 なので置換
            heap.Push(new SearchResult { InternalId = 3, Score = 0.2f });
            Assert.AreEqual(3, heap.Count);

            // Pop で昇順確認
            var r1 = heap.Pop();
            var r2 = heap.Pop();
            var r3 = heap.Pop();
            Assert.AreEqual(0.1f, r1.Score, 1e-6f);
            Assert.AreEqual(0.2f, r2.Score, 1e-6f);
            Assert.AreEqual(0.3f, r3.Score, 1e-6f);

            heap.Dispose();
        }

        [Test]
        public void Push_CapacityExceeded_IgnoresLarger()
        {
            var heap = new NativeMinHeap(2, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 0.1f });
            heap.Push(new SearchResult { InternalId = 1, Score = 0.2f });

            // Score 0.5 > 最大 0.2 なので無視
            heap.Push(new SearchResult { InternalId = 2, Score = 0.5f });
            Assert.AreEqual(2, heap.Count);

            var r1 = heap.Pop();
            Assert.AreEqual(0, r1.InternalId);

            heap.Dispose();
        }

        [Test]
        public void Pop_Empty_ReturnsDefault()
        {
            var heap = new NativeMinHeap(5, Allocator.Temp);
            var result = heap.Pop();
            Assert.AreEqual(0, result.InternalId);
            Assert.AreEqual(0f, result.Score);
            heap.Dispose();
        }

        [Test]
        public void Peek_Empty_ReturnsDefault()
        {
            var heap = new NativeMinHeap(5, Allocator.Temp);
            var result = heap.Peek();
            Assert.AreEqual(0, result.InternalId);
            Assert.AreEqual(0f, result.Score);
            heap.Dispose();
        }

        [Test]
        public void Clear_ResetsCount()
        {
            var heap = new NativeMinHeap(5, Allocator.Temp);
            heap.Push(new SearchResult { InternalId = 0, Score = 1f });
            heap.Push(new SearchResult { InternalId = 1, Score = 2f });
            heap.Clear();
            Assert.AreEqual(0, heap.Count);
            heap.Dispose();
        }
    }
}
