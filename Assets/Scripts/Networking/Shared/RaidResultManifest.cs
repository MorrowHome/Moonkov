using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    [Serializable]
    public sealed class RaidResultItem
    {
        public string Code;
        public int Returned, Lost;
    }

    // Frozen before death transfers ownership. Never used to grant or save items.
    [Serializable]
    public sealed class RaidResultManifest
    {
        public int RaidId;
        public RaidPhase Outcome;
        public List<RaidResultItem> Items = new List<RaidResultItem>();
        public int ReturnedCount => Items.Sum(i => i.Returned);
        public int LostCount => Items.Sum(i => i.Lost);

        public static RaidResultManifest Capture(InventoryGraph graph, int raid, RaidPhase outcome)
        {
            var result = new RaidResultManifest { RaidId = raid, Outcome = outcome };
            var protectedIds = outcome == RaidPhase.Extracted ? null : SecureInventory.ProtectedIds(graph);
            var rows = new Dictionary<string, RaidResultItem>(StringComparer.Ordinal);
            foreach (var item in graph.Items)
            {
                if (InventoryCatalog.Get(item.Code).Kind == ItemKind.Root || !graph.Carried(item)) continue;
                if (!rows.TryGetValue(item.Code, out var row))
                    rows.Add(item.Code, row = new RaidResultItem { Code = item.Code });
                if (outcome == RaidPhase.Extracted || protectedIds.Contains(item.Id)) row.Returned += item.Quantity;
                else row.Lost += item.Quantity;
            }
            result.Items = rows.Values.OrderBy(i => i.Code, StringComparer.Ordinal).ToList();
            return result;
        }

        public bool Valid(int raid)
        {
            if (RaidId != raid || raid <= 0 || Items == null || Items.Count > InventoryCatalog.Definitions.Count ||
                Outcome != RaidPhase.Extracted && Outcome != RaidPhase.Dead && Outcome != RaidPhase.TimedOut) return false;
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in Items)
            {
                var definition = row?.Code == null ? null : InventoryCatalog.Get(row.Code);
                if (definition == null || definition.Kind == ItemKind.Root || !codes.Add(row.Code) || row.Returned < 0 || row.Lost < 0 ||
                    (long)row.Returned + row.Lost <= 0 || (long)row.Returned + row.Lost > (long)InventoryCatalog.MaxItems * definition.MaxStack ||
                    Outcome == RaidPhase.Extracted && row.Lost != 0) return false;
            }
            return true;
        }
    }
}
