using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class RaidDrawerLootChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    public static string Run()
    {
        s_Checks = 0; InventoryGraph rareDrawer = null; string rareId = null;
        var counts = new Dictionary<(RaidLootProfile, string), int>();
        foreach (var profile in new[] { RaidLootProfile.Supplies, RaidLootProfile.Medical, RaidLootProfile.Industrial, RaidLootProfile.Equipment })
        {
            var random = new Random(8817 + (int)profile); int empty = 0;
            for (int n = 0; n < 1000; n++)
            {
                var drawer = RaidLootTable.CreateDrawer(profile, random);
                var items = drawer.Items.Where(i => i.Parent == LootInventoryExchange.Root).ToArray();
                if (items.Length == 0) empty++;
                Check(drawer.Validate() == InventoryError.None && drawer.Version == 1 && items.Length <= 2 &&
                    items.All(i => i.FoundInRaid && !i.EmergencySupply && InventoryCatalog.Get(i.Code) != null) &&
                    items.Select(i => i.Code).Distinct().Count() == items.Length,
                    "drawer must have a valid bounded, nonduplicated, found-in-raid inventory");
                foreach (var item in items)
                {
                    var key = (profile, item.Code); counts.TryGetValue(key, out int seen); counts[key] = seen + 1;
                    if (profile == RaidLootProfile.Industrial && item.Code == "quantum_core" && rareDrawer == null)
                    { rareDrawer = drawer; rareId = item.Id; }
                }
            }
            Check(empty >= 140 && empty <= 260, "empty drawers remain possible without dominating the sample");
        }
        int Count(RaidLootProfile profile, string code) => counts.TryGetValue((profile, code), out var count) ? count : 0;
        Check(Count(RaidLootProfile.Supplies, "electrolyte") > 0 && Count(RaidLootProfile.Supplies, "oxygen_canister") > 0,
            "living supplies can include immediate survival resources");
        Check(Count(RaidLootProfile.Medical, "vitality_shot") > 0 && Count(RaidLootProfile.Medical, "antidote_shot") > 0 &&
            Count(RaidLootProfile.Medical, "oxygen_canister") > 0, "medical drawers can provide treatment and rescue supplies");
        Check(Count(RaidLootProfile.Industrial, "halo_capacitor") > 0 && Count(RaidLootProfile.Industrial, "halo_charge_bridge") > 0 &&
            Count(RaidLootProfile.Industrial, "helium3_sample") > 0 && Count(RaidLootProfile.Industrial, "quantum_core") > 0,
            "industrial drawers can contain useful modules and rare valuables");
        Check(Count(RaidLootProfile.Industrial, "quantum_core") < Count(RaidLootProfile.Industrial, "circuit_board") &&
            Count(RaidLootProfile.Industrial, "helium3_sample") < Count(RaidLootProfile.Industrial, "alloy"),
            "high-value industrial results stay rarer than common materials");
        Check(Count(RaidLootProfile.Medical, "quantum_core") == 0 && Count(RaidLootProfile.Supplies, "halo_capacitor") == 0,
            "new results remain tied to the intended room type");
        var first = RaidLootTable.CreateDrawer(RaidLootProfile.Industrial, new Random(455));
        var second = RaidLootTable.CreateDrawer(RaidLootProfile.Industrial, new Random(455));
        Check(first.Items.Select(i => (i.Code, i.Quantity, i.Parent, i.Region, i.X, i.Y, i.Rotated))
                .SequenceEqual(second.Items.Select(i => (i.Code, i.Quantity, i.Parent, i.Region, i.X, i.Y, i.Rotated))),
            "the same seeded roll produces the same contents and layout");
        Check(rareDrawer != null, "seeded industrial sample contains a real rare item");
        var player = InventoryGraph.Create(false); var rare = rareDrawer.Find(rareId); string backpack = player.Equipped("Backpack").Id;
        Check(player.FindSpace(rare, backpack, out string region, out int x, out int y) &&
            LootInventoryExchange.TryApply(player, rareDrawer, new InventoryCommand { ExpectedVersion = player.Version,
                Operation = InventoryOperation.Move, ItemId = rare.Id, Parent = backpack, Region = region, X = x, Y = y,
                Rotated = rare.Rotated }, rareDrawer.Version) == InventoryError.None && player.Find(rareId) != null && rareDrawer.Find(rareId) == null,
            "a generated rare item transfers through the real shared-inventory transaction");
        Check(player.Find(rareId).FoundInRaid && player.Validate() == InventoryError.None && rareDrawer.Validate() == InventoryError.None,
            "transfer preserves identity, found-in-raid state and both inventory graphs");
        return "PASS " + s_Checks + " building drawer loot checks; per 1000 industrial drawers: " +
            Count(RaidLootProfile.Industrial, "quantum_core") + " quantum cores, " +
            Count(RaidLootProfile.Industrial, "helium3_sample") + " helium samples, " +
            Count(RaidLootProfile.Industrial, "data_drive") + " data drives";
    }
}
