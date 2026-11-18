// 用定长环形缓冲记录最近几拍的位置和旋转。移动系统在诊断预测时按 tick 找回记录，比对同一拍是否算出了同一结果。
using UnityEngine;
using System;

namespace Unity.MP_FPS {
    public class PlayerMovementHistory {
        private readonly struct PlayerTickData {
            public uint Tick { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }

            public PlayerTickData(uint tick, Vector3 position, Quaternion rotation) {
                Tick = tick;
                Position = position;
                Rotation = rotation;
            }
        }

        private readonly PlayerTickData[] m_Buffer;
        private readonly int m_Capacity;
        private int m_WriteIndex; // 下一条记录要写入的下标。
        private int m_CurrentSize; // 缓冲内当前有效的记录数。

        public PlayerMovementHistory(int capacity = 20) {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            m_Capacity = capacity;
            m_Buffer = new PlayerTickData[m_Capacity];
            m_WriteIndex = 0;
            m_CurrentSize = 0;
        }

        public void Add(uint tick, Vector3 pos, Quaternion rot) {
            // 把这一拍的位姿写到当前可写槽。
            // 缓冲满时覆盖最旧一项，记录长度固定，不会因战局持续时间越来越占内存。
            m_Buffer[m_WriteIndex] = new PlayerTickData(tick, pos, rot);

            // 写入下标向后移一格，走到末尾时回开头。
            m_WriteIndex = (m_WriteIndex + 1) % m_Capacity;

            // 还有空位时增加有效记录数。满了就只覆盖旧记录。
            if (m_CurrentSize < m_Capacity) m_CurrentSize++;
        }

        public bool TryGetTick(uint tick, out Vector3 pos, out Quaternion rot) {
            // 从最新写入的有效记录往最早的找。
            // m_WriteIndex 指下一次写入槽。写满时它指向最旧记录。
            // 因此刚写完的一项在 (m_WriteIndex - 1)。
            // 从最近保存的状态往回扫。目标拍找不到时返回 false，调用者可新建这一拍的记录。
            for (int i = 0; i < m_CurrentSize; i++) {
                // 下面算从新到旧的环形数组下标。
                // i = 0 对应最新项，i = m_CurrentSize - 1 对应最旧项。
                // 表达式 `m_WriteIndex - 1 - i` 可能小于零。先加上 `m_Capacity`，
                // 再取余可确保下标在数组有效范围内。
                int bufferIndex = (m_WriteIndex - 1 - i + m_Capacity) % m_Capacity;

                // 检查这个槽保存的 tick 是否是所求时间。
                if (m_Buffer[bufferIndex].Tick == tick) {
                    pos = m_Buffer[bufferIndex].Position;
                    rot = m_Buffer[bufferIndex].Rotation;
                    return true;
                }
            }

            // 缓冲内没有这个 tick。
            pos = default; // 例如 Vector3.zero 或 new Vector3()。
            rot = default; // 例如 Quaternion.identity 或 new Quaternion()。
            return false;
        }

        private const float k_MatchPositionToleranceSquared = 0.0001f * 0.0001f;
        private const float k_MatchQuaternionEpsilon = 1.5e-6f;

        public static bool HistoryMatches(
            Vector3 historyPos,
            Quaternion historyRot,
            Vector3 currentPos,
            Quaternion currentRot) {
            // 检查位置。
            // 计算距离平方以省一次开平方。
            // Vector3 需要支持相减和 LengthSquared()，也可使用等效的 sqrMagnitude。
            float positionDifferenceSquared = (historyPos - currentPos).sqrMagnitude;
            // UnityEngine.Vector3 可用 (historyPos - currentPos).sqrMagnitude;
            // System.Numerics.Vector3 可用 Vector3.DistanceSquared(historyPos, currentPos);
            // 也可用 (historyPos - currentPos).LengthSquared();
            // 比较留一点浮点误差余量。服务端和本机数值并不一定逐位相同，但游戏里足够近可以按同一处理解。
            if (positionDifferenceSquared > k_MatchPositionToleranceSquared)
                return false; // 位置超过误差容限。

            // 检查旋转。
            // Quaternion.Dot 给出两个旋转夹角一半的余弦 cos(angle/2)。
            // 旋转相同或四元数 q 和 -q 表示同一姿势时，|DotProduct| 为 1。
            // 检查 1 - |DotProduct| 是否小于容差。
            // 四元数的全部分量取反后仍代表同一个旋转。比较时取点积的绝对值，避免把同方向误报为回滚差异。
            float dotProduct = Quaternion.Dot(historyRot, currentRot);
            if (1.0f - Math.Abs(dotProduct) > k_MatchQuaternionEpsilon) return false; // 旋转超过误差容限。
            return true; // 位置与旋转都在容限内。
        }
    }
}