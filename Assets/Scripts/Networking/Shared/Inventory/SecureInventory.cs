#nullable disable
using System;
using System.Collections.Generic;

namespace Unity.MP_FPS.Inventory {
    // 保险箱装在专用槽中时才保护其全部子物品。
    // 失败仍退回的物品不授予成功撤离标记、任务进度或奖励。
    public static class SecureInventory {
        public static bool Allowed(string code) {
            var def = InventoryCatalog.Get(code);
            if (def == null || def.Container || MissionInventory.IsArtifact(code) ||
                InventoryCatalog.IsResearchSupply(code))
                return false;
            return def.Kind == ItemKind.Material || def.Kind == ItemKind.Cell || def.Kind == ItemKind.Medical ||
                   def.Kind == ItemKind.Food || def.Kind == ItemKind.Drink || def.Kind == ItemKind.Oxygen ||
                   def.Kind == ItemKind.StorageModule || def.Kind == ItemKind.ShapingModule ||
                   def.Kind == ItemKind.UtilityModule;
        }

        // 出战失败时，先找保险区和允许退还的特殊装备，再保留这些容器完整子树。没被保护的交掉落端。
        public static HashSet<string> ProtectedIds(InventoryGraph graph) {
            var ids = new HashSet<string>();
            var equipped = graph.Equipped("SecureContainer");
            if (equipped?.Code == "secure_case") ids.Add(equipped.Id);
            foreach (string slot in new[] { "Special1", "Special2", "Special3" }) {
                var tool = graph.Equipped(slot);
                if (tool != null && InventoryCatalog.Get(tool.Code)?.Kind == ItemKind.SpecialEquipment)
                    ids.Add(tool.Id);
            }
            for (int depth = 0; depth < InventoryCatalog.MaxDepth; depth++) {
                int before = ids.Count;
                foreach (var item in graph.Items)
                    if (item.Parent != null && ids.Contains(item.Parent))
                        ids.Add(item.Id);
                if (ids.Count == before) break;
            }
            return ids;
        }

        // 截取失败仍能返回的物品，形成一份独立的可结算图。原本在掉落中的未保护物品不会在这里再复制。
        public static InventoryGraph FailureReturn(InventoryGraph raid) {
            if (raid == null || raid.Validate() != InventoryError.None || raid.Find("stash") != null ||
                raid.Find("loot") != null)
                throw new InvalidOperationException("Invalid failed-raid inventory.");
            var protectedIds = ProtectedIds(raid);
            var result = raid.Clone();
            result.Items.RemoveAll(i =>
                InventoryCatalog.Get(i.Code).Kind != ItemKind.Root && !protectedIds.Contains(i.Id));
            foreach (var item in result.Items) item.FoundInRaid = false;
            result.MissionFirsts = 0;
            return result;
        }
    }
}
