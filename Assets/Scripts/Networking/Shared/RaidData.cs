using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum RaidPhase : byte { Active, Extracted, Dead, TimedOut }
    public enum RaidSaveState : byte { SessionOnly, Saved, Saving, Retrying }
    public enum RaidLoadoutError : byte { None, InvalidCount, InsufficientCells, Rejected }

    // This component lives on the connection, so settlement survives character despawning.
    public struct RaidSession : IComponentData
    {
        public int RaidId;
        public uint SnapshotSequence;
        public uint LoadoutRequestId;
        public RaidPhase Phase;
        public RaidSaveState SaveState;
        public FixedString64Bytes SettlementId;
        public FixedString64Bytes CellStackId;
        public int Dust, Alloy, Cells;
        public int StashDust, StashAlloy, StashCells;
        public bool PersistentDeployment, DeployPending;
        public RaidLoadoutError LoadoutError;
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

    public struct RaidDeployRpc : IRpcCommand { public int SettledRaidId, CarryCells; public uint RequestId; }

    // Reliable, connection-targeted snapshots include shared loot for observers and late joiners.
    public struct RaidSnapshotRpc : IRpcCommand
    {
        public int RaidId;
        public uint Sequence;
        public uint LoadoutRequestId;
        public RaidPhase Phase;
        public RaidSaveState SaveState;
        public int Dust, Alloy, Cells;
        public int StashDust, StashAlloy, StashCells;
        public bool DeployPending;
        public RaidLoadoutError LoadoutError;
        public FixedString64Bytes CellStackId;
        public float TimeLeft, ExtractionRemaining;
        public uint TakenMask;
    }

    public struct RaidClientState : IComponentData { public RaidSnapshotRpc Snapshot; }

    public static class RaidRules
    {
        public const int BagCapacity = 12;
        public const float PickupRange = 3f;
        public static bool ValidLoadout(int cells) => cells >= 0 && cells <= BagCapacity;

        public static bool TryConsumeCell(ref RaidSession session)
        {
            if (session.Phase != RaidPhase.Active || session.Cells <= 0) return false;
            session.Cells--;
            session.SnapshotTimer = 0;
            return true;
        }

        public static bool TrySettle(ref RaidSession session, RaidPhase outcome, bool awardImmediately = true)
        {
            if (session.Phase != RaidPhase.Active || outcome == RaidPhase.Active) return false;
            if (outcome == RaidPhase.Extracted && awardImmediately)
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
            session.SettlementId = System.Guid.NewGuid().ToString("D");
            session.Phase = RaidPhase.Active;
            session.Dust = session.Alloy = session.Cells = 0;
            session.CellStackId = default;
            session.PersistentDeployment = session.DeployPending = false;
            session.LoadoutError = RaidLoadoutError.None;
            session.ExtractionProgress = session.SnapshotTimer = 0;
            session.TimeLeft = duration;
        }

        public static bool CanDeploy(RaidSession session, int settledRaidId) => session.Phase != RaidPhase.Active &&
            session.RaidId == settledRaidId && !session.DeployPending &&
            session.SaveState != RaidSaveState.Saving && session.SaveState != RaidSaveState.Retrying;

        public static bool TryDeploy(ref RaidSession session, int settledRaidId, float duration, int carryCells = 0)
        {
            if (!CanDeploy(session, settledRaidId) || !ValidLoadout(carryCells) || carryCells > session.StashCells) return false;
            session.StashCells -= carryCells;
            BeginNext(ref session, duration);
            session.Cells = carryCells;
            if (carryCells > 0) session.CellStackId = System.Guid.NewGuid().ToString("D");
            return true;
        }
    }
}
