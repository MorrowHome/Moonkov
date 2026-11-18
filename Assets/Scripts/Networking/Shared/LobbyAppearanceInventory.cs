// 把完整背包提取成小队预览需要的外观。只带公开的装备类型和改装样子，接收方可以临时搭出预览角色。
#nullable disable
using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Online {
    // Only equipped appearance leaves the account service. No real item IDs,
    // contents, ammunition counts or account inventory are sent to teammates.
    public static class LobbyAppearanceInventory {
        // 从正式图校验装备槽，再提取物品码与改装位。来宾提交的装备外观不直接拿来当真正的背包。
        public static LobbyAppearance FromInventory(InventoryGraph graph, int character, int pose) {
            var appearance = new LobbyAppearance {
                CharacterIndex = character >= 2 && character <= 4 ? character : 2,
                PoseIndex = pose >= -1 && pose <= 3 ? pose : -1,
                Headgear = Equipped(graph, "Helmet", ItemKind.Helmet),
                ChestBadge = Equipped(graph, "ChestRig", ItemKind.Rig),
                Backpack = Equipped(graph, "Backpack", ItemKind.Backpack),
                Melee = Equipped(graph, "Melee", ItemKind.Melee),
                Shield = Equipped(graph, "Shield", ItemKind.Shield) == "energy_shield_generator"
            };
            foreach (string slot in new[] { "Primary", "Secondary", "Pistol" }) {
                var weapon = graph?.Equipped(slot);
                var kind = InventoryCatalog.Get(weapon?.Code)?.Kind;
                if (kind != ItemKind.LongGun && kind != ItemKind.Pistol) continue;
                appearance.Weapon = weapon.Code;
                appearance.Modules = (ushort)HaloModules.Mask(graph, weapon);
                break;
            }
            return appearance;
        }

        // 只接受槽中匹配类别的已知物品，避免异常库存使预览请求去加载未知模型。
        private static string Equipped(InventoryGraph graph, string slot, ItemKind kind) {
            var item = graph?.Equipped(slot);
            return InventoryCatalog.Get(item?.Code)?.Kind == kind ? item.Code : "";
        }

        // 接收方用公开外观建专供菜单模型读取的库存。物品 ID 重新生成，里面没有玩家真实背包内容。
        public static InventoryGraph PreviewInventory(LobbyAppearance appearance) {
            var graph = InventoryGraph.Create(false, false);
            if (appearance == null) return graph;

            void Add(string code, string slot, ItemKind kind) {
                if (InventoryCatalog.Get(code)?.Kind != kind) return;
                graph.Items.Add(new InventoryItem {
                    Id = "squad-" + slot, Code = code,
                    Parent = InventoryCatalog.Equipment, Region = slot
                });
            }

            var weaponKind = InventoryCatalog.Get(appearance.Weapon)?.Kind;
            if (weaponKind == ItemKind.LongGun || weaponKind == ItemKind.Pistol) {
                string slot = weaponKind == ItemKind.Pistol ? "Pistol" : "Primary";
                Add(appearance.Weapon, slot, weaponKind.Value);
                foreach (string code in HaloModules.Codes) AddModule(code, slot);
                AddModule("halo_mooncore_recycler", slot);
            }
            Add(appearance.Headgear, "Helmet", ItemKind.Helmet);
            Add(appearance.ChestBadge, "ChestRig", ItemKind.Rig);
            Add(appearance.Backpack, "Backpack", ItemKind.Backpack);
            Add(appearance.Melee, "Melee", ItemKind.Melee);
            if (appearance.Shield) Add("energy_shield_generator", "Shield", ItemKind.Shield);
            graph.HaloResearchLevel = (appearance.Modules & (ushort)HaloModule.BiologicalResearch) != 0 ? 1 : 0;
            graph.HaloResearchBranches = (appearance.Modules >> 8) & 7;
            return graph;

            void AddModule(string code, string slot) {
                var module = HaloModules.FromCode(code);
                if (((ushort)module & appearance.Modules) == 0 || !HaloModules.Compatible(appearance.Weapon, code))
                    return;
                graph.Items.Add(new InventoryItem {
                    Id = "squad-" + code, Code = code,
                    Parent = "squad-" + slot, Region = HaloModules.Slot(module)
                });
            }
        }
    }
}