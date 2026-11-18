// MOONRAT 局内任务的同步数据。客户端用 Hold 消息表达正在操作哪台设备，服务端发本局证据、进度与提示给任务界面。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    // 地图设备用稳定枚举对应真实锚点。HUD、按住交互的请求和需要设备的位掩码共同用这个编号。
    public enum ResearchDevice : byte {
        Tablet,
        Administrator,
        Server,
        LowerSeal,
        NadirTerminal,
        CredentialsLocker,
        ControlAccess,
        OriginalArchive,
        Shutdown
    }

    public struct ResearchQuestHoldRpc : IRpcCommand {
        public int RaidId;
        public ResearchDevice Device;
        // 按住与松开都上报。服务器按租期和现场条件推进，迟到的旧局输入不会直接设置设备完成。
        public bool Pressed;
    }

    // This snapshot is sent only to the player whose raid produced it.
    public struct ResearchQuestSnapshotRpc : IRpcCommand {
        public int RaidId;
        public uint Sequence;
        // 这里给本人显示活动任务、暴露时长、击杀与证据。定稿档案不是从客户端快照另写回去的。
        public int Active, Seconds, HaloKills, SentryKills, Samples, Recovered, Issued;
        public ushort Devices;
        public bool CanOperate, TabletAvailable, PackageRemoved, HeardNadir, LowerOpened;
        public bool RecordsHeld, CredentialsHeld, ControlOpened, ArchiveHeld, SystemsDisabled;
        public ResearchDevice Target;
        public byte InteractionPercent;
        public FixedString32Bytes Notice;
        // 检查设备那一位是否亮着。界面不用逐组解读所有剧情标记，按服务端当前可提示的设备展示。
        public bool Needs(ResearchDevice device) { return (Devices & (1 << (int)device)) != 0; }
    }

    public struct ResearchQuestClientState : IComponentData {
        public ResearchQuestSnapshotRpc Snapshot;
    }
}