// 光环扫描的距离、能耗、冷却和扫描起点。光环贴身、外放或放在地上时起点不同，脉冲和发现目标各有自己的 RPC。
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public static class HaloScanRules {
        public const float Range = 24, Speed = 18, Cost = 25, Cooldown = 8, RevealSeconds = 3;
        public const int MaxContacts = 24;
        public const float Duration = Range / Speed;

        // 能外放的角色仍需满足冷却、能量和在途阶段。刚飞出或正收回时起点尚在变化，不开启扫描。
        public static bool CanScan(in PredictedPlayerGhost state) {
            return HaloTransferRules.CanSend(state) && state.HaloTransferPhase != HaloTransferPhase.Extending &&
                   state.HaloTransferPhase != HaloTransferPhase.Returning && state.HaloScanCooldown <= 0 &&
                   HaloEnergyRules.Energy(state) >= Cost;
        }

        // 未外放从人物附近发出；放在地上从固定点发出；发给队友时按目标点与返回方向算悬浮光环的位置。
        public static float3 Origin(in PredictedPlayerGhost state) {
            if (!HaloTransferRules.Occupied(state)) return HaloTransferRules.Anchor(state) + new float3(0, .5f, 0);
            if (HaloSupportRules.Anchored(state)) return state.HaloTransferPoint;
            var home = HaloTransferRules.Anchor(state) - state.HaloTransferPoint;
            home.y = 0;
            return state.HaloTransferPoint + new float3(0, .83f, 0) + math.normalizesafe(home) * .52f;
        }
    }

    // 脉冲给出扫描者的身份、起点和序号，与后面单条目标消息匹配。同一光环可多次发脉冲。
    public struct HaloScanPulseRpc : IRpcCommand {
        public int OwnerGhostId;
        public uint OwnerSpawnTick, Sequence, Weapon;
        public float3 Origin;
    }

    // 每条目标带自己的 Ghost 与出生 tick。客户端据此找当前真正同步到的模型。
    public struct HaloScanContactRpc : IRpcCommand {
        public int OwnerGhostId, GhostId, Index;
        public uint OwnerSpawnTick, SpawnTick, Sequence;
        public float3 Position;
    }
}