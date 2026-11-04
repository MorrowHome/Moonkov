using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private bool CanRechargeShieldCell(InventoryItem item)
        {
            if (item == null || !QuickAlive || QuickBusy || TimedActionInputActive || QuickGraph == null) return false;
            var current = QuickGraph.Find(item.Id);
            return current != null && HaloEnergyInventory.CanRecharge(m_PlayerQuery.GetSingleton<PredictedPlayerGhost>(), QuickGraph, current);
        }
    }
}
