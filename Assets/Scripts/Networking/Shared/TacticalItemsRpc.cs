using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public struct RaidTacticalItemActionRpc : IRpcCommand
    {
        public int RaidId,ExpectedVersion;
        public uint RequestId,TargetId;
        public FixedString64Bytes ItemId;
        public TacticalItemAction Action;
    }
    public struct TacticalItemOwnerRpc : IRpcCommand
    {
        public int RaidId;
        public uint RequestId;
        public InventoryError Error;
        public TacticalItemFeedback Feedback;
        public FixedString64Bytes ItemId;
        public float Remaining,FireSeconds,PoisonSeconds,BleedSeconds,StaminaSeconds,AntidoteSeconds,WireSeconds,CoughSeconds;
    }
    public struct TacticalWorldStateRpc : IRpcCommand
    {
        public uint Epoch,Id,Sequence;
        public TacticalItemKind Kind;
        public TacticalWorldPhase Phase;
        public float3 Position,Velocity;
        public float Remaining,Radius;
        public int Energy;
    }
    // A private server owner sends this component's snapshot only to its connection.
    public sealed class TacticalItemClientState : IComponentData
    {
        public TacticalItemOwnerRpc Owner;
    }
}
