// 给战后界面显示的带回和遗失清单。它在死亡掉落前按物品类型数一遍，并标出保护栏留住的东西。正式落盘仍走档案结算。
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    [Serializable]
    public sealed class RaidResultItem {
        public string Code;
        public int Returned, Lost;
    }

    // 在死亡导致装备转移前冻结清单。清单只展示，不能发放或保存物品。
    [Serializable]
    public sealed class RaidResultManifest {
        public int RaidId;
        public RaidPhase Outcome;
        public List<RaidResultItem> Items = new();
        // HUD 所见总数从冻结好的物品行求和。正式库存与存档没有靠这两个数做扣装。
        public int ReturnedCount => Items.Sum(i => i.Returned);
        public int LostCount => Items.Sum(i => i.Lost);

        public static RaidResultManifest Capture(InventoryGraph graph, int raid, RaidPhase outcome) {
            var result = new RaidResultManifest { RaidId = raid, Outcome = outcome };
            var protectedIds = outcome == RaidPhase.Extracted ? null : SecureInventory.ProtectedIds(graph);
            var rows = new Dictionary<string, RaidResultItem>(StringComparer.Ordinal);
            // 仅统计可带走的实际游戏物品。相同物品码合成一行，撤离全部记为带回；失败按受保护 ID 分组。
            foreach (var item in graph.Items) {
                if (InventoryCatalog.Get(item.Code).Kind == ItemKind.Root ||
                    InventoryCatalog.IsResearchSupply(item.Code) || !graph.Carried(item))
                    continue;
                if (!rows.TryGetValue(item.Code, out var row))
                    rows.Add(item.Code, row = new RaidResultItem { Code = item.Code });
                if (outcome == RaidPhase.Extracted || protectedIds.Contains(item.Id))
                    row.Returned += item.Quantity;
                else
                    row.Lost += item.Quantity;
            }
            result.Items = rows.Values.OrderBy(i => i.Code, StringComparer.Ordinal).ToList();
            return result;
        }

        // 客户端处理快照要检查清单是否属于自己当前局，数量、物品码和结果阶段能否成立。撤离成功不应带遗失数。
        public bool Valid(int raid) {
            if (RaidId != raid || raid <= 0 || Items == null || Items.Count > InventoryCatalog.Definitions.Count ||
                (Outcome != RaidPhase.Extracted && Outcome != RaidPhase.Dead && Outcome != RaidPhase.TimedOut))
                return false;
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Items) {
                var definition = row?.Code == null ? null : InventoryCatalog.Get(row.Code);
                if (definition == null || definition.Kind == ItemKind.Root || !codes.Add(row.Code) ||
                    row.Returned < 0 || row.Lost < 0 || (long)row.Returned + row.Lost <= 0 ||
                    (long)row.Returned + row.Lost > (long)InventoryCatalog.MaxItems * definition.MaxStack ||
                    (Outcome == RaidPhase.Extracted && row.Lost != 0))
                    return false;
            }
            return true;
        }
    }
}