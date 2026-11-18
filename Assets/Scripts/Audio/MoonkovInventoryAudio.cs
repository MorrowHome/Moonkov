// 背包在发出挪动命令之前先选好音效。用物品种类区分布料、金属等质感，进出装备槽还有专用声，成功时由回执处理方播放。
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // 先按操作类型选音效，再发库存请求。是否成功由已有的回执追踪器判断，
    // 已失败或重复到达的快照都不会播放成功提示。
    public static class MoonkovInventoryAudio {
        public static SoundDef ForItem(string code, MoonkovAudioLibrary library) {
            if (!library) return null;
            // 先从物品目录查种类，再用种类选常用材质音。例如背包用布料，电池和大部分装备偏金属。
            var definition = InventoryCatalog.Get(code);
            if (definition == null) return library.Equipment;
            switch (definition.Kind) {
                case ItemKind.Rig:
                case ItemKind.Backpack:
                case ItemKind.SecureContainer:
                    return library.InventoryCloth;
                case ItemKind.Food: return library.InventoryFood;
                case ItemKind.Medical:
                case ItemKind.Drink:
                    return library.InventoryPlastic;
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
                case ItemKind.SpecialEquipment:
                    return library.InventoryMetal;
                default: return library.Equipment;
            }
        }

        public static SoundDef ForMove(InventoryGraph graph, InventoryCommand command, MoonkovAudioLibrary library) {
            if (!library || graph == null || command == null) return null;
            var item = graph.Find(command.ItemId);
            if (item == null) return null;
            // 进出装备位有专用穿戴声。布包仍用布料声音，其余动作就回到物品种类的选择结果。
            if (command.Operation == InventoryOperation.Move && (command.Parent == InventoryCatalog.Equipment ||
                                                                 item.Parent == InventoryCatalog.Equipment)) {
                var kind = InventoryCatalog.Get(item.Code)?.Kind;
                return kind == ItemKind.Rig || kind == ItemKind.Backpack
                    ? library.InventoryCloth
                    : library.InventoryEquip;
            }
            return ForItem(item.Code, library);
        }
    }
}