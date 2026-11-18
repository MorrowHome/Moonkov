// 能量手雷同步用的数据包。服务端用状态消息告诉客户端手雷的变化和对应 tick，客户端用它安排表现。
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public struct RaidGrenadeThrowRpc : IRpcCommand {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        public GrenadeCommand Command;
        public uint HoldId;
        public bool Underhand;
    }

    // Reliable, low-rate authoritative poses also make active grenades visible to late joiners.
    public struct EnergyGrenadeStateRpc : IRpcCommand {
        public uint Id, Tick;
        public float3 Position, Velocity;
        public float Remaining;
        public byte Exploded;
    }
}