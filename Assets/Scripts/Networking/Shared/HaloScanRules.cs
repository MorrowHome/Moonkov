using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public static class HaloScanRules
    {
        public const float Range = 24, Speed = 18, Cost = 25, Cooldown = 8, RevealSeconds = 3;
        public const int MaxContacts = 24;
        public const float Duration = Range / Speed;
        public static bool CanScan(in PredictedPlayerGhost state) => HaloTransferRules.CanSend(state) &&
            state.HaloTransferPhase != HaloTransferPhase.Extending && state.HaloTransferPhase != HaloTransferPhase.Returning &&
            state.HaloScanCooldown <= 0 && HaloEnergyRules.Energy(state) >= Cost;
        public static float3 Origin(in PredictedPlayerGhost state)
        {
            if (!HaloTransferRules.Occupied(state)) return HaloTransferRules.Anchor(state) + new float3(0, .5f, 0);
            if (HaloSupportRules.Anchored(state)) return state.HaloTransferPoint;
            float3 home = HaloTransferRules.Anchor(state) - state.HaloTransferPoint; home.y = 0;
            return state.HaloTransferPoint + new float3(0, .83f, 0) + math.normalizesafe(home) * .52f;
        }
    }

    public struct HaloScanPulseRpc : IRpcCommand
    {
        public int OwnerGhostId;
        public uint OwnerSpawnTick, Sequence, Weapon;
        public float3 Origin;
    }

    public struct HaloScanContactRpc : IRpcCommand
    {
        public int OwnerGhostId, GhostId, Index;
        public uint OwnerSpawnTick, SpawnTick, Sequence;
        public float3 Position;
    }
}
