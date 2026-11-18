// 背包电池右键补能的前置提示。先用最新库存按 ID 找电池，再用 HaloEnergyInventory 判断是否能使用。
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private bool CanRechargeShieldCell(InventoryItem item) {
            // 忙于治疗、投掷或其他动作时先挡住补能按钮。真实电量和电池存放位置再交给统一规则判断。
            if (item == null || !QuickAlive || QuickBusy || TimedActionInputActive || QuickGraph == null) return false;
            var current = QuickGraph.Find(item.Id);
            return current != null &&
                   HaloEnergyInventory.CanRecharge(m_PlayerQuery.GetSingleton<PredictedPlayerGhost>(), QuickGraph,
                       current);
        }
    }
}