// 近战操作的网络协议。客户端只提出装备、轻击、重击或取消等意图，ServerMeleeSystem 用该连接真实角色做时序和命中计算。
using Unity.Collections;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public enum MeleeAction : byte {
        Equip,
        Light,
        Heavy,
        Cancel
    }

    public enum MeleeFeedback : byte {
        None,
        Accepted,
        Equipped,
        Cancelled,
        Unavailable,
        Inactive,
        Busy,
        Exhausted,
        Stale,
        Invalid
    }

    // 仅提交意图。服务器决定实际角色、起点、朝向、挥砍和命中目标。
    public struct RaidMeleeActionRpc : IRpcCommand {
        // 近战请求用本局与回合约束，RequestId 防重复。槽位和物品 ID 让服务端确认这真是刚想选的武器。
        public int RaidId, Epoch;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        // 轻、重、切换与取消只是按键意图。真实位置、接触方向和受伤者由 ServerMeleeSystem 扫掠来确定。
        public MeleeAction Action;
        public byte EquipmentSlot;
    }
}