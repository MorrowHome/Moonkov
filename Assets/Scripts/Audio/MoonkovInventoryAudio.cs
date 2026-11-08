using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // Choose before sending the transaction. The existing receipt tracker decides
    // whether it succeeded; failed or repeated snapshots never play these cues.
    public static class MoonkovInventoryAudio
    {
        public static SoundDef ForItem(string code, MoonkovAudioLibrary library)
        {
            if (!library) return null;
            var definition = InventoryCatalog.Get(code);
            if (definition == null) return library.Equipment;
            switch (definition.Kind)
            {
                case ItemKind.Rig:
                case ItemKind.Backpack:
                case ItemKind.SecureContainer: return library.InventoryCloth;
                case ItemKind.Food: return library.InventoryFood;
                case ItemKind.Medical:
                case ItemKind.Drink: return library.InventoryPlastic;
                case ItemKind.Cell:
                case ItemKind.Material:
                case ItemKind.Oxygen:
                case ItemKind.Helmet:
                case ItemKind.LongGun:
                case ItemKind.Pistol:
                case ItemKind.Melee:
                case ItemKind.Shield:
                case ItemKind.Throwable:
                case ItemKind.StorageModule:
                case ItemKind.ShapingModule:
                case ItemKind.UtilityModule:
                case ItemKind.SpecialEquipment: return library.InventoryMetal;
                default: return library.Equipment;
            }
        }

        public static SoundDef ForMove(InventoryGraph graph, InventoryCommand command, MoonkovAudioLibrary library)
        {
            if (!library || graph == null || command == null) return null;
            var item = graph.Find(command.ItemId);
            if (item == null) return null;
            if (command.Operation == InventoryOperation.Move &&
                (command.Parent == InventoryCatalog.Equipment || item.Parent == InventoryCatalog.Equipment))
            {
                var kind = InventoryCatalog.Get(item.Code)?.Kind;
                return kind == ItemKind.Rig || kind == ItemKind.Backpack ? library.InventoryCloth : library.InventoryEquip;
            }
            return ForItem(item.Code, library);
        }
    }
}
