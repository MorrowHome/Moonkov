// 装备项圈或光环的数据在角色和物品上各有一份。背包移动、死亡或结算前先把实时余量写回物品，装备变化后再让角色载入新的那件。
using Unity.Entities;

namespace Unity.MP_FPS {
    public partial struct ServerGameSystem {
        // 装配的物品树移入箱子、尸体包或结算回执前，先将当前资源写回物品。
        // 同步物品余量不增加背包结构版本。
        private void UpdateInventoryProtection(ref SystemState state, Entity connection, bool synchronize = false) {
            // 服务端先按连接找当前角色与背包。入场还在加载、角色已经销毁或没有局内档时直接跳过。
            if (!SystemAPI.Exists(connection) || !SystemAPI.HasComponent<JoinedClient>(connection) ||
                !state.EntityManager.HasComponent<RaidInventoryState>(connection))
                return;
            var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<PredictedPlayerGhost>(player)) return;
            var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
            var graph = GetRaidInventory(ref state, connection).Graph;
            // 普通 Flush 将现有项圈氧气写回对应物品。库存事务成功后用 Synchronize 还会读取刚换上的装备，更新角色状态。
            if (synchronize)
                EnergyShieldEquipment.Synchronize(ref health.ValueRW, graph);
            else
                EnergyShieldEquipment.Flush(ref health.ValueRW, graph);
        }
    }
}