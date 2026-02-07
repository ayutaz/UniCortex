using System;
using Unity.Collections;

namespace UniCortex
{
    /// <summary>
    /// NativeArray ベースの固定容量バイナリ最小ヒープ。
    /// Score が最小の要素を先頭に保持する。最終結果の収集に使用。
    /// </summary>
    public struct NativeMinHeap : IDisposable
    {
        public NativeArray<SearchResult> Data;
        public int Count;
        public int Capacity;

        public NativeMinHeap(int capacity, Allocator allocator)
        {
            Data = new NativeArray<SearchResult>(capacity, allocator);
            Count = 0;
            Capacity = capacity;
        }

        /// <summary>
        /// 要素を追加する。
        /// 容量超過時: item.Score が最大要素の Score より小さければ最大要素を置換。
        /// </summary>
        public void Push(SearchResult item)
        {
            if (Count < Capacity)
            {
                Data[Count] = item;
                BubbleUp(Count);
                Count++;
            }
            else if (Capacity > 0)
            {
                // 最大要素を見つけて置換（MinHeap なので最大は葉にある）
                int maxIdx = FindMaxIndex();
                if (item.Score < Data[maxIdx].Score)
                {
                    Data[maxIdx] = item;
                    // 上方向・下方向両方調整
                    BubbleUp(maxIdx);
                    BubbleDown(maxIdx);
                }
            }
        }

        /// <summary>
        /// 最小スコアの要素を取り出す。空なら default(SearchResult)。
        /// </summary>
        public SearchResult Pop()
        {
            if (Count == 0)
                return default;

            SearchResult min = Data[0];
            Count--;
            if (Count > 0)
            {
                Data[0] = Data[Count];
                BubbleDown(0);
            }
            return min;
        }

        /// <summary>
        /// 最小スコアの要素を参照する。空なら default(SearchResult)。
        /// </summary>
        public SearchResult Peek()
        {
            return Count > 0 ? Data[0] : default;
        }

        public void Clear()
        {
            Count = 0;
        }

        public void Dispose()
        {
            if (Data.IsCreated) Data.Dispose();
        }

        private void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (Data[index].Score < Data[parent].Score)
                {
                    var tmp = Data[index];
                    Data[index] = Data[parent];
                    Data[parent] = tmp;
                    index = parent;
                }
                else
                {
                    break;
                }
            }
        }

        private void BubbleDown(int index)
        {
            while (true)
            {
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                int smallest = index;

                if (left < Count && Data[left].Score < Data[smallest].Score)
                    smallest = left;
                if (right < Count && Data[right].Score < Data[smallest].Score)
                    smallest = right;

                if (smallest != index)
                {
                    var tmp = Data[index];
                    Data[index] = Data[smallest];
                    Data[smallest] = tmp;
                    index = smallest;
                }
                else
                {
                    break;
                }
            }
        }

        private int FindMaxIndex()
        {
            int firstLeaf = Count / 2;
            int maxIdx = firstLeaf;
            float maxScore = Data[firstLeaf].Score;
            for (int i = firstLeaf + 1; i < Count; i++)
            {
                if (Data[i].Score > maxScore)
                {
                    maxScore = Data[i].Score;
                    maxIdx = i;
                }
            }
            return maxIdx;
        }
    }
}
