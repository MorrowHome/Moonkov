using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum RaidPhase : byte { Active, Extracted, Dead, TimedOut }

    // This component lives on the connection, so settlement survives character despawning.
    public struct RaidSession : IComponentData
    {
        public int RaidId;
        public uint SnapshotSequence;
        public RaidPhase Phase;
        public int Dust, Alloy, Cells;
        public int StashDust, StashAlloy, StashCells;
        public float TimeLeft, ExtractionProgress, SnapshotTimer;
        public int BagCount => Dust + Alloy + Cells;
    }

    public struct RaidLootWorld : IComponentData
    {
        public uint TakenMask;
        public FixedList128Bytes<float> RespawnTimers;
    }

    public struct RaidPickupRpc : IRpcCommand
    {
        public int RaidId;
        public int LootId;
    }

    public struct RaidDeployRpc : IRpcCommand { public int SettledRaidId; }

    // Reliable, connection-targeted snapshots include shared loot for observers and late joiners.
    public struct RaidSnapshotRpc : IRpcCommand
    {
        public int RaidId;
        public uint Sequence;
        public RaidPhase Phase;
        public int Dust, Alloy, Cells;
        public int StashDust, StashAlloy, StashCells;
        public float TimeLeft, ExtractionRemaining;
        public uint TakenMask;
    }

    public struct RaidClientState : IComponentData { public RaidSnapshotRpc Snapshot; }

    public static class RaidRules
    {
        public const int BagCapacity = 12;
        public const float PickupRange = 3f;

        public static bool TrySettle(ref RaidSession session, RaidPhase outcome)
        {
            if (session.Phase != RaidPhase.Active || outcome == RaidPhase.Active) return false;
            if (outcome == RaidPhase.Extracted)
            {
                session.StashDust += session.Dust;
                session.StashAlloy += session.Alloy;
                session.StashCells += session.Cells;
            }
            session.Phase = outcome;
            session.ExtractionProgress = 0;
            session.SnapshotTimer = 0;
            return true;
        }

        public static void BeginNext(ref RaidSession session, float duration)
        {
            session.RaidId++;
            session.Phase = RaidPhase.Active;
            session.Dust = session.Alloy = session.Cells = 0;
            session.ExtractionProgress = session.SnapshotTimer = 0;
            session.TimeLeft = duration;
        }

        public static bool TryDeploy(ref RaidSession session, int settledRaidId, float duration)
        {
            if (session.Phase == RaidPhase.Active || session.RaidId != settledRaidId) return false;
            BeginNext(ref session, duration);
            return true;
        }
    }
}
