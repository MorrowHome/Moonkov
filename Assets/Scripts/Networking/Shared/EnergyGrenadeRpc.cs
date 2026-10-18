using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public struct RaidGrenadeThrowRpc : IRpcCommand
    {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
    }

    // Reliable, low-rate authoritative poses also make active grenades visible to late joiners.
    public struct EnergyGrenadeStateRpc : IRpcCommand
    {
        public uint Id, Tick;
        public float3 Position, Velocity;
        public float Remaining;
        public byte Exploded;
    }
}
