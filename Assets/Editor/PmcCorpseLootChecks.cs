using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class PmcCorpseLootChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static InventoryGraph EmptyCache()
    {
        var graph = InventoryGraph.Create(false, false);
        graph.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
        return graph;
    }
    private static InventoryItem Mission(InventoryGraph graph, string parent)
    {
        var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "mooncore_fragment", FoundInRaid = true,
            MissionWorldId = Guid.NewGuid().ToString("D"), MissionOperationId = Guid.NewGuid().ToString("D"), MissionContentVersion = 1 };
        if (!graph.FindSpace(item, parent, out string region, out int x, out int y)) throw new InvalidOperationException("Mission fixture has no space");
        item.Parent = parent; item.Region = region; item.X = x; item.Y = y; graph.Items.Add(item); return item;
    }

    public static string Run()
    {
        s_Checks = 0;
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        Check(PmcLootRules.CanSearch(health, -100, 0), "uninjured and idle PMC can search");
        health.ControllerState.IsReloadingState = true;
        Check(!PmcLootRules.CanSearch(health, -100, 0), "charging blocks a search");
        health.ControllerState.IsReloadingState = false; health.WeaponEquipRemaining = .5f;
        Check(!PmcLootRules.CanSearch(health, -100, 0), "switching weapons blocks a search");
        health.WeaponEquipRemaining = 0;
        Check(!PmcLootRules.CanSearch(health, 10, 10.99), "new damage interrupts searching during the safety interval");
        Check(PmcLootRules.CanSearch(health, 10, 11), "search can restart after the safety interval");
        var victim = InventoryGraph.Create(false, false);
        var bag = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "backpack", Parent = "equipment", Region = "Backpack" };
        var rig = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "rig", Parent = "equipment", Region = "ChestRig" };
        victim.Items.Add(bag); victim.Items.Add(rig);
        Check(victim.AddSupply("data_drive", 1, bag.Id, true) == InventoryError.None &&
            victim.AddSupply("medkit", 2, rig.Id, true) == InventoryError.None, "victim supplies fit their real equipment");
        var valuable = victim.Items.Single(i => i.Code == "data_drive"); var mission = Mission(victim, bag.Id);
        Check(victim.Validate() == InventoryError.None, "victim inventory is valid before death");
        var containers = new RaidLootContainers(); containers.Initialize(1);
        int corpseId = containers.Drop(victim, new float3(1, 0, 0), characterIndex: 2);
        var corpse = containers.Containers[corpseId]; int corpseVersion = corpse.Version;
        Check(victim.Find(valuable.Id) == null && corpse.Find(valuable.Id) != null && corpse.Validate() == InventoryError.None,
            "death moves the real nested contents to one corpse");
        var alienId = containers.DropHarvest(EmptyCache(), new float3(1.5f, 0, 0));
        var droppedOwner = InventoryGraph.Create(false, false);
        Check(droppedOwner.AddSupply("dust", 1, InventoryCatalog.Pockets, true) == InventoryError.None, "ground item fixture");
        string droppedId = droppedOwner.Items.Single(i => i.Code == "dust").Id;
        Check(containers.DropItem(droppedOwner, droppedId, droppedOwner.Version, new float3(1.7f, 0, 0), 1, out int groundId) == InventoryError.None,
            "ground drop fixture");
        int distantId = containers.Drop(InventoryGraph.Create(false, false), new float3(10, 0, 0), characterIndex: 1);
        var nearby = PmcLootRules.NearbyHumanCorpses(containers, Vector3.zero, null);
        Check(nearby.SequenceEqual(new[] { corpseId }) && !nearby.Contains(alienId) && !nearby.Contains(groundId) && !nearby.Contains(distantId),
            "only a nearby human corpse is an opportunistic search target");
        Check(PmcLootRules.TryPosition(null, containers, corpseId, out var point) &&
            point == new Vector3(1, .25f, 0) && !PmcLootRules.TryPosition(null, containers, alienId, out _) &&
            !PmcLootRules.TryPosition(null, containers, groundId, out _), "body interaction point and exclusions match server records");
        Check(PmcLootRules.NearbyHumanCorpses(containers, Vector3.zero, new HashSet<int> { corpseId }).Length == 0,
            "a PMC does not repeatedly search the same corpse");

        var npc = InventoryGraph.Create(false);
        Check(PmcLootRules.TakeAvailable(npc, corpse) == 0 && corpse.Version == corpseVersion,
            "ordinary fixed caches do not gain nested-container access");
        Check(PmcLootRules.TakeAvailable(npc, corpse, includeNested: true) == 2 && npc.Find(valuable.Id) != null &&
            npc.Items.Any(i => i.Code == "medkit" && npc.MedicalAccessible(i)),
            "a PMC takes real valuables and usable medicine from the corpse's equipment");
        Check(corpse.Find(valuable.Id) == null && corpse.Find(mission.Id) != null && corpse.Find(bag.Id) != null &&
            npc.Find(mission.Id) == null && npc.Validate() == InventoryError.None && corpse.Validate() == InventoryError.None,
            "unique mission property and containing bag remain while both owners stay valid");
        var human = InventoryGraph.Create(false);
        var stale = new InventoryCommand { Operation = InventoryOperation.Move, ItemId = valuable.Id,
            Parent = human.Equipped("Backpack").Id, Region = "main", ExpectedVersion = human.Version };
        Check(LootInventoryExchange.TryApply(human, corpse, stale, corpseVersion) == InventoryError.Stale && human.Find(valuable.Id) == null,
            "the old player snapshot cannot duplicate a PMC's pickup");
        var secondCorpse = LootInventoryExchange.DropOnDeath(npc); var item = secondCorpse.Find(valuable.Id);
        var humanBag = human.Equipped("Backpack").Id;
        Check(item != null && human.FindSpace(item, humanBag, out string region, out int x, out int y) &&
            LootInventoryExchange.TryApply(human, secondCorpse, new InventoryCommand { Operation = InventoryOperation.Move,
                ExpectedVersion = human.Version, ItemId = valuable.Id, Parent = humanBag, Region = region,
                X = x, Y = y, Rotated = item.Rotated }, secondCorpse.Version) == InventoryError.None &&
            human.Find(valuable.Id) != null && secondCorpse.Find(valuable.Id) == null,
            "defeating the PMC lets the player retrieve the same item");
        containers.Containers.Remove(corpseId);
        Check(!PmcLootRules.TryPosition(null, containers, corpseId, out _) &&
            PmcLootRules.NearbyHumanCorpses(containers, Vector3.zero, null).Length == 0,
            "a removed body inventory cannot be selected or searched");
        return "PASS " + s_Checks + " PMC corpse competition checks";
    }
}
