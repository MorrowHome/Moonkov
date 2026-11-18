// 地图任务的网络数据。客户端提出开始、持续、取消或设置的请求，服务端用任务快照回传阶段、目标进度及操作结果。
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.MP_FPS.Missions;

namespace Unity.MP_FPS {
    public enum MissionIntent : byte {
        Begin,
        Renew,
        Cancel,
        Configure
    }

    public struct RaidMissionActionRpc : IRpcCommand {
        // 请求里的节点必须属于当前出战和该轮任务。OperationId 区分同 World 连续发出的不同任务。
        public int RaidId, Epoch, Node;
        public uint RequestId;
        public FixedString64Bytes OperationId;
        public MissionIntent Intent;
        public int Choice;
    }

    // 八个预设节点使用两个定长向量表示。RPC 无需传不定长列表。
    public struct RaidMissionSnapshotRpc : IRpcCommand {
        public int RaidId, Epoch, DefinitionIndex, OutputLootId, OutputRemaining;
        public int MissionFirstsBefore, MissionFirstsSaved, RecoveredArtifacts;
        // 序号给新旧快照，完成位保存节点状态，请求号对应当前玩家的操作回执。
        public uint Sequence, CompletedMask, RequestId;
        public FixedString64Bytes WorldId, OperationId;
        public MissionPhase Phase;
        public MissionActionResult Result;
        public RaidSaveState SaveState;
        public bool BackendSupported;
        public float Remaining;
        // 最多八个节点放进两个固定长度向量。界面可逐节点显示读条，不需要 RPC 带不定长数组。
        public float4 ProgressA, ProgressB;
        // 单人章节信息附到同一份地图任务快照，供章节跟踪和样本标识显示。多人模式可保持关闭。
        public bool CampaignEnabled;

        public byte CampaignChapter,
            CampaignNodes01,
            CampaignNodes02,
            CampaignNodes03,
            CampaignNodes04,
            CampaignCompletedMask;

        public FixedString64Bytes CampaignSampleId, CampaignArchiveId;
    }

    public struct RaidMissionClientState : IComponentData {
        public RaidMissionSnapshotRpc Snapshot;
    }
}