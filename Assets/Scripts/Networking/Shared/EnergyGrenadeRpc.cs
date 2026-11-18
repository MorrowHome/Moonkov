// 能量手雷同步用的数据包。服务端用状态消息告诉客户端手雷的变化和对应 tick，客户端用它安排表现。
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public struct RaidGrenadeThrowRpc : IRpcCommand {
        // RaidId 约束本局，ExpectedVersion 用来核对当时的背包。RequestId 区分一条操作，HoldId 对应此前拿出的同一颗。
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        // 命令区分拿出、拉环、松手、收回等阶段。客户端不直接指定真实飞行轨迹与伤害。
        public GrenadeCommand Command;
        public uint HoldId;
        public bool Underhand;
    }

    // Reliable, low-rate authoritative poses also make active grenades visible to late joiners.
    public struct EnergyGrenadeStateRpc : IRpcCommand {
        // 服务端给飞出去的雷唯一 ID 与状态 tick。客户端拿位置、速度、剩余引信和爆炸标记绘制，并根据收到时机补进度。
        public uint Id, Tick;
        public float3 Position, Velocity;
        public float Remaining;
        public byte Exploded;
    }
}