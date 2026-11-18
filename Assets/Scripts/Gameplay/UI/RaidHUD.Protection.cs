// 背包电池右键补能的前置提示。先用最新库存按 ID 找电池，再用 HaloEnergyInventory 判断是否能使用。
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private bool CanRechargeShieldCell(InventoryItem item) {
            if (item == null || !QuickAlive || QuickBusy || TimedActionInputActive || QuickGraph == null) return false;
            var current = QuickGraph.Find(item.Id);
            return current != null &&
                   HaloEnergyInventory.CanRecharge(m_PlayerQuery.GetSingleton<PredictedPlayerGhost>(), QuickGraph,
                       current);
        }
    }
}