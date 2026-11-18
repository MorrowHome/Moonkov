// 战术物品的消息格式。玩家发出使用、布置等意图。服务器给本人回复动作进度和结果，也把场景物品状态发给客户端画出来。
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public struct RaidTacticalItemActionRpc : IRpcCommand {
        // 客户端请求同时锁住本局与库存版本。操作已有地面物用 TargetId，从包里取则用 ItemId。
        public int RaidId, ExpectedVersion;
        public uint RequestId, TargetId;
        public FixedString64Bytes ItemId;
        public TacticalItemAction Action;
    }

    public struct TacticalItemOwnerRpc : IRpcCommand {
        public int RaidId;
        public uint RequestId;
        public InventoryError Error;
        public TacticalItemFeedback Feedback;
        // 本人接收耐力、剩余负面状态和手部进度。该包用实际连接定向，不公开其他玩家的私有状态。
        public float SprintStamina;
        public bool SprintExhausted, SprintTracked;
        public FixedString64Bytes ItemId;

        public float Remaining,
            FireSeconds,
            PoisonSeconds,
            BleedSeconds,
            StaminaSeconds,
            AntidoteSeconds,
            WireSeconds,
            CoughSeconds;

        public float RegenerationSeconds;
    }

    public struct TacticalWorldStateRpc : IRpcCommand {
        // 场景物品号可在换图后重新使用。加地图代数与序号，客户端能分清旧移除和这一局刚建的道具。
        public uint Epoch, Id, Sequence;
        public TacticalItemKind Kind;
        public TacticalWorldPhase Phase;
        public float3 Position, Velocity;
        public float Remaining, Radius;
        public int Energy;
    }

    // A private server owner sends this component's snapshot only to its connection.
    public sealed class TacticalItemClientState : IComponentData {
        public TacticalItemOwnerRpc Owner;

        public bool Apply(in TacticalItemOwnerRpc snapshot) {
            // 只挡上一局及过时的操作回执。同一请求号仍可更新读条和状态时间，所以相同请求号也能替换。
            if (snapshot.RaidId < Owner.RaidId ||
                (snapshot.RaidId == Owner.RaidId && snapshot.RequestId < Owner.RequestId))
                return false;
            Owner = snapshot;
            return true;
        }
    }
}