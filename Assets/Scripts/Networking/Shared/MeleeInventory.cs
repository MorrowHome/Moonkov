// 近战装备槽的共用判断。把物品代码换成刀或剑等类型，核对是否真在近战槽。空手时给拳击这个默认动作。
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS.Inventory {
    public static class MeleeInventory {
        public const string Slot = "Melee", SwordCode = "energy_sword", DaggerCode = "dagger";
        // 近战槽在输入的装备编号中是单独一位，背包里的逻辑槽名叫 Melee。两边映射用于换武器。
        public const int EquipmentIndex = 4;

        public static MeleeWeaponKind Kind(string code) {
            return code == SwordCode ? MeleeWeaponKind.EnergySword :
                code == DaggerCode ? MeleeWeaponKind.Dagger : MeleeWeaponKind.None;
        }

        // 从真实装备槽取已知的合法单件物品。空槽、堆了多件或不是近战物都不能当作手上刀具。
        public static InventoryItem Equipped(InventoryGraph graph) {
            var item = graph?.Equipped(Slot);
            return item != null && item.Quantity == 1 && InventoryCatalog.Get(item.Code) != null &&
                   Kind(item.Code) != MeleeWeaponKind.None
                ? item
                : null;
        }

        // 拿到同码其他刀具的 ID 还不够，确保它就是当前背包中的那件且在近战槽。
        public static bool CanEquip(InventoryGraph graph, InventoryItem item) {
            return item != null && graph?.Find(item.Id) == item && Equipped(graph) == item;
        }

        public static MeleeWeaponKind ActiveKind(InventoryGraph graph, int equipmentIndex, bool hasFirearm) {
            // 主动切到近战但槽空时默认用拳。当前还有可用枪且没切换时由枪处理输入，近战状态为空。
            if (equipmentIndex == EquipmentIndex) {
                var kind = Kind(Equipped(graph)?.Code);
                return kind != MeleeWeaponKind.None ? kind : MeleeWeaponKind.Fists;
            }
            return hasFirearm ? MeleeWeaponKind.None : MeleeWeaponKind.Fists;
        }
    }
}