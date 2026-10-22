using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS.Inventory
{
    public static class MeleeInventory
    {
        public const string Slot = "Melee", SwordCode = "energy_sword", DaggerCode = "dagger";
        public const int EquipmentIndex = 4;
        public static MeleeWeaponKind Kind(string code) => code == SwordCode ? MeleeWeaponKind.EnergySword :
            code == DaggerCode ? MeleeWeaponKind.Dagger : MeleeWeaponKind.None;
        public static InventoryItem Equipped(InventoryGraph graph)
        {
            var item = graph?.Equipped(Slot);
            return item != null && item.Quantity == 1 && InventoryCatalog.Get(item.Code) != null &&
                Kind(item.Code) != MeleeWeaponKind.None ? item : null;
        }
        public static bool CanEquip(InventoryGraph graph, InventoryItem item) => item != null &&
            graph?.Find(item.Id) == item && Equipped(graph) == item;
        public static MeleeWeaponKind ActiveKind(InventoryGraph graph, int equipmentIndex, bool hasFirearm)
        {
            if (equipmentIndex == EquipmentIndex)
            {
                var kind = Kind(Equipped(graph)?.Code);
                return kind != MeleeWeaponKind.None ? kind : MeleeWeaponKind.Fists;
            }
            return hasFirearm ? MeleeWeaponKind.None : MeleeWeaponKind.Fists;
        }
    }
}
