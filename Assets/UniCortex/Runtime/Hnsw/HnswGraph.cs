using System;
using Unity.Collections;

namespace UniCortex.Hnsw
{
    /// <summary>
    /// HNSW ノードのメタデータ。
    /// </summary>
    public struct HnswNodeMeta
    {
        /// <summary>このノードが存在する最大レイヤー。</summary>
        public int MaxLayer;

        /// <summary>Neighbors 配列内の隣接リスト開始オフセット。</summary>
        public int NeighborOffset;
    }

    /// <summary>
    /// HNSW グラフ構造。flat 隣接リストで Burst/Jobs 互換。
    /// </summary>
    public struct HnswGraph : IDisposable
    {
        public NativeArray<HnswNodeMeta> Nodes;
        public NativeArray<int> Neighbors;
        public NativeArray<int> NeighborCounts;
        public NativeArray<bool> Deleted;

        /// <summary>現在のエントリポイント。-1 は空グラフ。</summary>
        public int EntryPoint;

        /// <summary>グラフの現在の最大レイヤー。</summary>
        public int MaxLayer;

        public int M;
        public int M0;

        /// <summary>登録済みノード数。</summary>
        public int Count;

        /// <summary>削除済みノード数。</summary>
        public int DeletedCount;

        /// <summary>次の Neighbor 確保オフセット。</summary>
        int nextNeighborOffset;

        public HnswGraph(int capacity, int m, int m0, int maxPossibleLevel, Allocator allocator)
        {
            M = m;
            M0 = m0;

            Nodes = new NativeArray<HnswNodeMeta>(capacity, allocator);
            Deleted = new NativeArray<bool>(capacity, allocator);

            // 最悪ケース: 全ノードが maxLevel を持つ場合のスロット数
            // 平均: M0 + M * mL ≈ 37.8 per node
            long avgSlots = (long)(m0 + m * 0.36f + m); // 余裕を持って確保
            long totalSlots = avgSlots * capacity;
            if (totalSlots > int.MaxValue / 2)
                totalSlots = int.MaxValue / 2;

            Neighbors = new NativeArray<int>((int)totalSlots, allocator);
            NeighborCounts = new NativeArray<int>((int)totalSlots, allocator);

            // -1 で初期化
            for (int i = 0; i < Neighbors.Length; i++)
                Neighbors[i] = -1;

            EntryPoint = -1;
            MaxLayer = -1;
            Count = 0;
            DeletedCount = 0;
            nextNeighborOffset = 0;
        }

        /// <summary>
        /// ノードを確保し、隣接リストスロットを割り当てる。
        /// </summary>
        public Result<int> AllocateNode(int maxLayer)
        {
            if (Count >= Nodes.Length)
                return Result<int>.Fail(ErrorCode.CapacityExceeded);

            int nodeId = Count;
            int slotsNeeded = M0 + M * maxLayer;

            // オーバーフローチェック
            long newOffset = (long)nextNeighborOffset + slotsNeeded;
            if (newOffset > Neighbors.Length)
                return Result<int>.Fail(ErrorCode.CapacityExceeded);

            Nodes[nodeId] = new HnswNodeMeta
            {
                MaxLayer = maxLayer,
                NeighborOffset = nextNeighborOffset
            };

            nextNeighborOffset += slotsNeeded;
            Count++;

            return Result<int>.Success(nodeId);
        }

        /// <summary>
        /// ノードのレイヤー l の隣接リスト開始オフセットを取得する。
        /// </summary>
        public int GetLayerOffset(int nodeId, int layer)
        {
            int baseOffset = Nodes[nodeId].NeighborOffset;
            if (layer == 0)
                return baseOffset;
            return baseOffset + M0 + M * (layer - 1);
        }

        /// <summary>
        /// ノードのレイヤー l の最大接続数を取得する。
        /// </summary>
        public int GetMaxConnections(int layer)
        {
            return layer == 0 ? M0 : M;
        }

        /// <summary>
        /// ノードのレイヤー l の隣接ノード数を取得する。
        /// </summary>
        public int GetNeighborCount(int nodeId, int layer)
        {
            int offset = GetLayerOffset(nodeId, layer);
            return NeighborCounts[offset];
        }

        /// <summary>
        /// ノードのレイヤー l に隣接ノードを追加する。maxConn を超えない。
        /// </summary>
        public bool AddNeighbor(int nodeId, int neighborId, int layer)
        {
            int offset = GetLayerOffset(nodeId, layer);
            int maxConn = GetMaxConnections(layer);
            int count = NeighborCounts[offset];

            if (count >= maxConn)
                return false;

            Neighbors[offset + count] = neighborId;
            NeighborCounts[offset] = count + 1;
            return true;
        }

        /// <summary>
        /// ノードのレイヤー l の隣接リストを設定する (プルーニング用)。
        /// </summary>
        public void SetNeighbors(int nodeId, int layer, NativeList<int> newNeighbors)
        {
            int offset = GetLayerOffset(nodeId, layer);
            int maxConn = GetMaxConnections(layer);
            int count = newNeighbors.Length < maxConn ? newNeighbors.Length : maxConn;

            for (int i = 0; i < count; i++)
                Neighbors[offset + i] = newNeighbors[i];
            for (int i = count; i < maxConn; i++)
                Neighbors[offset + i] = -1;

            NeighborCounts[offset] = count;
        }

        public void Dispose()
        {
            if (Nodes.IsCreated) Nodes.Dispose();
            if (Neighbors.IsCreated) Neighbors.Dispose();
            if (NeighborCounts.IsCreated) NeighborCounts.Dispose();
            if (Deleted.IsCreated) Deleted.Dispose();
        }
    }
}
