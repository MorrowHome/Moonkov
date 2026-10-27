using System;
using System.Linq;
using System.Runtime.Serialization;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class PmcLootCompetitionChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static InventoryGraph Cache()
    {
        var graph = InventoryGraph.Create(false, false);
        graph.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
        return graph;
    }
    private static void Add(InventoryGraph graph, string code, int quantity = 1, int charge = -1)
    {
        if (graph.AddSupply(code, quantity, LootInventoryExchange.Root, true, cellCharge: charge) != InventoryError.None)
            throw new InvalidOperationException("Cannot set up " + code);
    }
    private static void AddMissionArtifact(InventoryGraph cache, string parent)
    {
        var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "mooncore_fragment", FoundInRaid = true,
            MissionWorldId = Guid.NewGuid().ToString("D"), MissionOperationId = Guid.NewGuid().ToString("D"), MissionContentVersion = 1 };
        if (!cache.FindSpace(item, parent, out string region, out int x, out int y)) throw new InvalidOperationException("No mission space");
        item.Parent = parent; item.Region = region; item.X = x; item.Y = y; cache.Items.Add(item);
    }

    public static string Run()
    {
        s_Checks = 0;
        var inventory = InventoryGraph.Create(false); var cache = Cache();
        Add(cache, "quantum_core"); Add(cache, "cells", 3); Add(cache, "med_gel", 2); Add(cache, "protein_bar");
        var rareId = cache.Items.Single(i => i.Code == "quantum_core").Id;
        int initialInventoryVersion = inventory.Version, initialCacheVersion = cache.Version;
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 4, "PMC collects varied items from the shared cache");
        Check(inventory.Find(rareId) != null && cache.Find(rareId) == null && inventory.Find(rareId).FoundInRaid,
            "valuable loot keeps its real identity and found-in-raid status");
        Check(inventory.Find(rareId).Parent == inventory.Equipped("Backpack").Id,
            "valuables go in the backpack before accessible supplies");
        Check(inventory.CellEnergy(accessibleOnly: true) == 300 && inventory.Items.Any(i => i.Code == "med_gel" && inventory.MedicalAccessible(i)),
            "ammo and medical supplies go into usable slots");
        Check(inventory.Count("protein_bar", true) == 1 && inventory.Validate() == InventoryError.None && cache.Validate() == InventoryError.None,
            "food and both resulting inventory graphs remain valid");
        Check(inventory.Version == initialInventoryVersion + 4 && cache.Version == initialCacheVersion + 4,
            "each transfer advances both versions once");
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 0 && inventory.Version == initialInventoryVersion + 4 && cache.Version == initialCacheVersion + 4,
            "a second search cannot duplicate the same items");
        var human = InventoryGraph.Create(false);
        var oldRequest = new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = human.Version,
            ItemId = rareId, Parent = InventoryCatalog.Pockets, Region = "1" };
        Check(LootInventoryExchange.TryApply(human, cache, oldRequest, initialCacheVersion) == InventoryError.Stale && human.Find(rareId) == null,
            "a player viewing the old cache cannot also take the item");
        var corpse = LootInventoryExchange.DropOnDeath(inventory); var recovered = corpse.Find(rareId);
        Check(recovered != null && corpse.RootOf(rareId) == LootInventoryExchange.Root && inventory.Find(rareId) == null,
            "a killed PMC drops the actual valuable item in its corpse inventory");
        string humanPack = human.Equipped("Backpack").Id;
        Check(human.FindSpace(recovered, humanPack, out string recoverRegion, out int recoverX, out int recoverY) &&
            LootInventoryExchange.TryApply(human, corpse, new InventoryCommand { Operation = InventoryOperation.Move,
                ExpectedVersion = human.Version, ItemId = rareId, Parent = humanPack, Region = recoverRegion,
                X = recoverX, Y = recoverY, Rotated = recovered.Rotated }, corpse.Version) == InventoryError.None &&
            human.Find(rareId) != null && corpse.Find(rareId) == null,
            "the player can recover that same item from the corpse");

        inventory = InventoryGraph.Create(false); cache = Cache();
        Check(inventory.AddSupply("cells", 2, InventoryCatalog.Pockets, true) == InventoryError.None, "battery stack fixture");
        string original = inventory.Items.Single(i => i.Code == "cells").Id;
        Add(cache, "cells", 3); int beforeVersion = inventory.Version;
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 1 && inventory.Find(original).Quantity == 5 &&
            inventory.CellEnergy(accessibleOnly: true) == 500 && inventory.Version == beforeVersion + 1,
            "compatible stacks merge through the real transaction");
        Check(cache.Count("cells") == 0 && inventory.Validate() == InventoryError.None && cache.Validate() == InventoryError.None,
            "merging removes the source without making a second copy");

        inventory = InventoryGraph.Create(false); cache = Cache();
        Check(inventory.AddSupply("cells", 1, InventoryCatalog.Pockets, true, cellCharge: 45) == InventoryError.None, "partial battery fixture");
        Add(cache, "cells", 1, 20);
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 1 && inventory.Count("cells", true) == 2 &&
            inventory.CellEnergy(accessibleOnly: true) == 65 && inventory.Items.Count(i => i.Code == "cells") == 2,
            "two partially used cells never merge or create energy");

        inventory = InventoryGraph.Create(false, false); cache = Cache();
        for (int region = 1; region <= 3; region++)
            inventory.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "dust", Parent = "pockets", Region = region.ToString() });
        Add(cache, "circuit_board"); Add(cache, "data_drive");
        Check(inventory.Validate() == InventoryError.None && PmcLootRules.TakeAvailable(inventory, cache) == 1,
            "one remaining pocket accepts only one candidate");
        Check(inventory.Count("data_drive", true) == 1 && cache.Count("circuit_board") == 1,
            "limited space goes to the higher-value loot first");
        int carriedVersion = inventory.Version, cacheVersion = cache.Version;
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 0 && inventory.Version == carriedVersion && cache.Version == cacheVersion,
            "full pockets leave the other item and both versions unchanged");

        inventory = InventoryGraph.Create(false, false); cache = Cache();
        var narrowPack = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "backpack", Parent = "equipment", Region = "Backpack" };
        inventory.Items.Add(narrowPack);
        for (int y = 0; y < 6; y++) for (int x = 0; x < 5; x++)
            if (x != 4 || y < 4) inventory.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "dust",
                Parent = narrowPack.Id, Region = "main", X = x, Y = y });
        Add(cache, "copper_coil");
        Check(inventory.Validate() == InventoryError.None && PmcLootRules.TakeAvailable(inventory, cache) == 1 &&
            inventory.Items.Single(i => i.Code == "copper_coil").Rotated,
            "a rectangular valuable can rotate into the only available backpack space");

        inventory = InventoryGraph.Create(false, false); cache = Cache();
        var pack = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "backpack", Parent = "equipment", Region = "Backpack" };
        inventory.Items.Add(pack);
        Check(inventory.AddSupply("alloy", 68, pack.Id) == InventoryError.None && inventory.AddSupply("dust", 18, pack.Id) == InventoryError.None,
            "near-limit inventory fixture");
        Add(cache, "cells", 3, 50);
        Check(inventory.CarriedWeight > 44.79f && inventory.CarriedWeight < 44.81f, "fixture leaves room for one cell");
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 1 && inventory.Count("cells", true) == 1 && cache.Count("cells") == 2,
            "only the weight that fits is taken from a larger stack");
        Check(inventory.CellEnergy(carriedOnly: true) + cache.CellEnergy() == 250 && inventory.CarriedWeight <= InventoryCatalog.CarryWeightLimit + .001f &&
            inventory.Validate() == InventoryError.None && cache.Validate() == InventoryError.None,
            "split preserves partial-cell energy, weight limit and both valid inventories");

        inventory = InventoryGraph.Create(false); cache = Cache();
        AddMissionArtifact(cache, LootInventoryExchange.Root);
        var nested = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "small_pack", FoundInRaid = true };
        Check(cache.FindSpace(nested, LootInventoryExchange.Root, out string bagRegion, out int bagX, out int bagY), "mission bag fits");
        nested.Parent = LootInventoryExchange.Root; nested.Region = bagRegion; nested.X = bagX; nested.Y = bagY; cache.Items.Add(nested);
        AddMissionArtifact(cache, nested.Id); Add(cache, "data_drive");
        Check(cache.Validate() == InventoryError.None, "mission artifact fixture is valid");
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 1 && inventory.Count("data_drive", true) == 1,
            "ordinary valuables still compete while mission objects stay protected");
        Check(cache.Count("mooncore_fragment") == 2 && cache.Find(nested.Id) != null && inventory.Count("mooncore_fragment", true) == 0,
            "PMC cannot remove unique mission items directly or inside a bag");

        var searching = (DollSingerEnemyBrain)FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        searching.Action = PmcAction.Scavenge; searching.Cache = 4; searching.LootUntil = 20;
        Check(PmcLootRules.SearchingCache(searching) && !PmcLootRules.SearchingCache(null),
            "an active ordinary PMC search protects its selected cache");
        var empty = RaidLootTable.Create(RaidLootProfile.Industrial, new Random(42));
        empty.Items.RemoveAll(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root);
        int emptyVersion = empty.Version;
        var refill = new RaidLootRefillState(RaidLootProfile.Industrial);
        Check(!refill.TryRefill(empty, 1, 300, 2, PmcLootRules.SearchingCache(searching), new Random(1)) &&
            !refill.TryRefill(empty, 301, 300, 2, PmcLootRules.SearchingCache(searching), new Random(1)) &&
            empty.Version == emptyVersion && refill.RefillCount == 0 && !RaidLootRefillState.HasContents(empty),
            "an empty cache cannot refresh while a PMC search is still pending");
        searching.Action = PmcAction.Engage;
        Check(!PmcLootRules.SearchingCache(searching) &&
            refill.TryRefill(empty, 302, 300, 2, PmcLootRules.SearchingCache(searching), new Random(1)) &&
            empty.Version == emptyVersion + 1 && refill.RefillCount == 1 && RaidLootRefillState.HasContents(empty),
            "after interruption the normal refill can proceed once");
        searching.Action = PmcAction.Scavenge; searching.LootUntil = 0;
        Check(!PmcLootRules.SearchingCache(searching), "approaching or finished searches do not reserve a cache");
        searching.LootUntil = 10; searching.Cache = -1;
        Check(!PmcLootRules.SearchingCache(searching), "a missing target cannot reserve a cache");
        searching.Cache = 4; searching.ClashParticipantId = 1;
        Check(!PmcLootRules.SearchingCache(searching), "bounty actors do not affect ordinary raid refill rules");
        return "PASS " + s_Checks + " PMC loot competition checks";
    }
}
