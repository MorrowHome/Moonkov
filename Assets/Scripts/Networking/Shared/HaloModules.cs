// 光环改装工具表。把模块物品对应到可安装槽和武器，给库存校验、面板展示及武器参数读取使用的模块位掩码。
using System;
using System.Linq;

namespace Unity.MP_FPS.Inventory {
    // Modules remain independent items. Their parent is the exact weapon instance,
    // so the existing item tree handles ownership, deployment, death and settlement.
    public static class HaloModules {
        // Detachable modules use bits 0..5 and 11. Persistent biology uses bits 6..10;
        // the active species-organ effect is encoded in bits 12..15.
        public static readonly string[] Codes = {
            "halo_capacitor", "halo_charge_bridge", "halo_focus_lens",
            "halo_fan_array", "halo_pulse_controller", "halo_hit_marker"
        };

        public static readonly InventoryRegion[] Slots = {
            new("Storage", 1, 1, ItemKind.StorageModule),
            new("Shaping", 1, 1, ItemKind.ShapingModule),
            new("Utility", 1, 1, ItemKind.UtilityModule)
        };

        // 物品码换为内部模块标记。改装接口不能拿不认识的道具直接影响武器参数。
        public static HaloModule FromCode(string code) {
            // Keep Codes as the original cache-loot pool; this module requires fabrication.
            if (code == "halo_mooncore_recycler") return HaloModule.MooncoreRecycler;
            int index = Array.IndexOf(Codes, code);
            return index < 0 ? HaloModule.None : (HaloModule)(1 << index);
        }

        public static bool IsWeapon(string code) { return code == "shotgun" || code == "rifle" || code == "compact"; }

        public static string Slot(HaloModule module) {
            return module == HaloModule.Capacitor || module == HaloModule.ChargeBridge ||
                   module == HaloModule.MooncoreRecycler ? "Storage" :
                module == HaloModule.HitMarker ? "Utility" : "Shaping";
        }

        // 安装先看这是可改装光环，随后检查该模块能否放到它指定的区域。
        public static bool Compatible(string weapon, string module) {
            var value = FromCode(module);
            if (!IsWeapon(weapon) || value == HaloModule.None) return false;
            if (value == HaloModule.FocusLens || value == HaloModule.FanArray) return weapon == "shotgun";
            return value != HaloModule.PulseController || weapon == "rifle" || weapon == "compact";
        }

        // 逐个读取该武器附着的真实子物品。把合法模块合成位掩码，让战斗和模型同步不必每帧传整棵库存。
        public static HaloModule Mask(InventoryGraph graph, InventoryItem weapon) {
            if (graph == null || weapon == null) return HaloModule.None;
            var kind = InventoryCatalog.Get(weapon.Code)?.Kind;
            if (kind != ItemKind.LongGun && kind != ItemKind.Pistol) return HaloModule.None;
            var result = graph.HaloResearchLevel > 0 ? HaloModule.BiologicalResearch : HaloModule.None;
            if ((graph.HaloResearchBranches & 1) != 0) result |= HaloModule.BiologicalCapacity;
            if ((graph.HaloResearchBranches & 2) != 0) result |= HaloModule.BiologicalCharging;
            if ((graph.HaloResearchBranches & 4) != 0) result |= HaloModule.BiologicalStability;
            foreach (var item in graph.Children(weapon.Id))
                if (Compatible(weapon.Code, item.Code) && item.Region == Slot(FromCode(item.Code)))
                    result |= FromCode(item.Code);
            return result;
        }

        // 商店和改装界面共用这些作用说明。真正的数值计算从对应武器参数与能量规则读取。
        public static string Description(string code) {
            switch (FromCode(code)) {
                case HaloModule.Capacitor: return "Shared halo capacity +50%; recovery speed -26%.";
                case HaloModule.ChargeBridge: return "Recovery speed +67%; energy per round +50%.";
                case HaloModule.MooncoreRecycler:
                    return "Energy per round reduced by one third (rounded up); recovery speed -17%.";
                case HaloModule.FocusLens: return "Shotgun spread -55%; energy per round +33% (rounded up).";
                case HaloModule.FanArray: return "Shotgun spread +60%; 12 pellets share the original total damage.";
                case HaloModule.PulseController:
                    return "Rifle fires three rounds per trigger press; 0.42s between bursts.";
                case HaloModule.HitMarker:
                    return "Energy per round +25%; confirmed hits mark the last impact position for allies for 3s.";
                default: return "";
            }
        }

        public static string Summary(InventoryGraph graph, InventoryItem weapon) {
            return string.Join(" / ",
                graph.Children(weapon.Id).Where(i => FromCode(i.Code) != HaloModule.None)
                    .Select(i => InventoryCatalog.Get(i.Code).Name));
        }
    }
}