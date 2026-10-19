using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.MP_FPS.Missions;

namespace Unity.MP_FPS
{
    public enum MissionIntent : byte { Begin, Renew, Cancel, Configure }
    public struct RaidMissionActionRpc : IRpcCommand
    {
        public int RaidId, Epoch, Node;
        public uint RequestId;
        public FixedString64Bytes OperationId;
        public MissionIntent Intent;
        public int Choice;
    }
    // Two bounded vectors cover all eight authored nodes. No unbounded list in an RPC.
    public struct RaidMissionSnapshotRpc : IRpcCommand
    {
        public int RaidId, Epoch, DefinitionIndex, OutputLootId;
        public uint Sequence, CompletedMask, RequestId;
        public FixedString64Bytes WorldId, OperationId;
        public MissionPhase Phase;
        public MissionActionResult Result;
        public float Remaining;
        public float4 ProgressA, ProgressB;
    }
    public struct RaidMissionClientState : IComponentData
    {
        public RaidMissionSnapshotRpc Snapshot;
    }
}
