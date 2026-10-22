using Unity.Collections;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum MeleeAction : byte { Equip, Light, Heavy, Cancel }
    public enum MeleeFeedback : byte { None, Accepted, Equipped, Cancelled, Unavailable, Inactive, Busy, Exhausted, Stale, Invalid }

    // Intent only. The server resolves the owning actor, origin, facing, sweep and victims.
    public struct RaidMeleeActionRpc : IRpcCommand
    {
        public int RaidId, Epoch;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        public MeleeAction Action;
        public byte EquipmentSlot;
    }
}
