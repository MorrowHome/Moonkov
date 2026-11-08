using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum ResearchDevice : byte
    {
        Tablet, Administrator, Server, LowerSeal, NadirTerminal, CredentialsLocker, ControlAccess, OriginalArchive, Shutdown
    }

    public struct ResearchQuestHoldRpc : IRpcCommand
    {
        public int RaidId;
        public ResearchDevice Device;
        public bool Pressed;
    }

    // This snapshot is sent only to the player whose raid produced it.
    public struct ResearchQuestSnapshotRpc : IRpcCommand
    {
        public int RaidId;
        public uint Sequence;
        public int Active, Seconds, HaloKills, SentryKills, Samples, Recovered, Issued;
        public ushort Devices;
        public bool CanOperate, TabletAvailable, PackageRemoved, HeardNadir, LowerOpened;
        public bool RecordsHeld, CredentialsHeld, ControlOpened, ArchiveHeld, SystemsDisabled;
        public ResearchDevice Target;
        public byte InteractionPercent;
        public FixedString32Bytes Notice;

        public bool Needs(ResearchDevice device) => (Devices & (1 << (int)device)) != 0;
    }

    public struct ResearchQuestClientState : IComponentData
    {
        public ResearchQuestSnapshotRpc Snapshot;
    }
}
