// 行动准备页的装备提醒。它从库存树检查光环是否已装备、医疗物品能否够到，以及有没有资格领取应急补给。真正能否出战由后续流程检查。
using System.Linq;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client {
    public enum DepartureEquipmentIssue {
        Loading,
        Halo,
        Medicine,
        None
    }

    // 只用来给玩家提示。物品还是由库存移动，是否能出战仍由出发流程判断。
    public readonly struct DepartureEquipmentCheck {
        public readonly DepartureEquipmentIssue Issue;
        public readonly bool EmergencyAvailable;

        private DepartureEquipmentCheck(DepartureEquipmentIssue issue, bool emergency = false) {
            Issue = issue;
            EmergencyAvailable = emergency;
        }

        public static DepartureEquipmentCheck Read(InventoryGraph graph) {
            // 档案没回来时用等候状态。这样加载中的空视图不会被当成玩家真的没有光环。
            if (graph == null) return new DepartureEquipmentCheck(DepartureEquipmentIssue.Loading);
            // 光环要穿在装备槽里才算带上身。虽然箱包中可能也有光环，应急资格按单独的商品规则算。
            bool halo = graph.Items.Any(i => i.Parent == InventoryCatalog.Equipment &&
                                             (InventoryCatalog.Get(i.Code)?.Kind == ItemKind.LongGun ||
                                              InventoryCatalog.Get(i.Code)?.Kind == ItemKind.Pistol));
            if (!halo) return new DepartureEquipmentCheck(DepartureEquipmentIssue.Halo, ShopCatalog.CanClaim(graph));
            // 治疗物资若放在战斗中够不到的容器里，同样需要回来整理。这里只给提示，继续出发由后续流程决定。
            return new DepartureEquipmentCheck(graph.Items.Any(graph.MedicalAccessible)
                ? DepartureEquipmentIssue.None
                : DepartureEquipmentIssue.Medicine);
        }

        public string Copy =>
            Issue == DepartureEquipmentIssue.Loading
                ? "Loading equipment..."
                :
                Issue == DepartureEquipmentIssue.Halo
                    ?
                    EmergencyAvailable
                        ? "No halo weapon remains. Emergency supplies are available."
                        : "No halo equipped. Open storage to equip one."
                    : Issue == DepartureEquipmentIssue.Medicine
                        ? "Place medical supplies in pockets or your chest badge."
                        : "Halo equipped. Medicine accessible.";

        public string Action => EmergencyAvailable ? "EMERGENCY SUPPLY" : "OPEN STORAGE";
    }
}