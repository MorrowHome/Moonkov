// 战术物品的消息格式。玩家发出使用、布置等意图。服务器给本人回复动作进度和结果，也把场景物品状态发给客户端画出来。
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
        public float SprintStamina;
        public bool SprintExhausted,SprintTracked;
        public FixedString64Bytes ItemId;
        public float Remaining,FireSeconds,PoisonSeconds,BleedSeconds,StaminaSeconds,AntidoteSeconds,WireSeconds,CoughSeconds;
        public float RegenerationSeconds;
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
        public bool Apply(in TacticalItemOwnerRpc snapshot)
        {
            if(snapshot.RaidId<Owner.RaidId || snapshot.RaidId==Owner.RaidId && snapshot.RequestId<Owner.RequestId)return false;
            Owner=snapshot;return true;
        }
    }
}
