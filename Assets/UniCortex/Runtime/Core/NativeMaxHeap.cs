using System;
using Unity.Collections;

namespace UniCortex
{
    /// <summary>
    /// NativeArray ベースの固定容量バイナリ最大ヒープ。
    /// Score が最大の要素を先頭に保持する。HNSW 探索の候補管理に使用。
    /// </summary>
    public struct NativeMaxHeap : IDisposable
    {
        public NativeArray<SearchResult> Data;
        public int Count;
        public int Capacity;

        public NativeMaxHeap(int capacity, Allocator allocator)
        {
            Data = new NativeArray<SearchResult>(capacity, allocator);
            Count = 0;
            Capacity = capacity;
        }

        /// <summary>
        /// 要素を追加する。
        /// 容量超過時: item.Score が最小要素の Score より大きければ最小要素を置換。
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
                // 最小要素を見つけて置換（MaxHeap なので最小は葉にある）
                int minIdx = FindMinIndex();
                if (item.Score > Data[minIdx].Score)
                {
                    Data[minIdx] = item;
                    BubbleUp(minIdx);
                    BubbleDown(minIdx);
                }
            }
        }

        /// <summary>
        /// 最大スコアの要素を取り出す。空なら default(SearchResult)。
        /// </summary>
        public SearchResult Pop()
        {
            if (Count == 0)
                return default;

            SearchResult max = Data[0];
            Count--;
            if (Count > 0)
            {
                Data[0] = Data[Count];
                BubbleDown(0);
            }
            return max;
        }

        /// <summary>
        /// 最大スコアの要素を参照する。空なら default(SearchResult)。
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
                if (Data[index].Score > Data[parent].Score)
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
                int largest = index;

                if (left < Count && Data[left].Score > Data[largest].Score)
                    largest = left;
                if (right < Count && Data[right].Score > Data[largest].Score)
                    largest = right;

                if (largest != index)
                {
                    var tmp = Data[index];
                    Data[index] = Data[largest];
                    Data[largest] = tmp;
                    index = largest;
                }
                else
                {
                    break;
                }
            }
        }

        private int FindMinIndex()
        {
            int firstLeaf = Count / 2;
            int minIdx = firstLeaf;
            float minScore = Data[firstLeaf].Score;
            for (int i = firstLeaf + 1; i < Count; i++)
            {
                if (Data[i].Score < minScore)
                {
                    minScore = Data[i].Score;
                    minIdx = i;
                }
            }
            return minIdx;
        }
    }
}
