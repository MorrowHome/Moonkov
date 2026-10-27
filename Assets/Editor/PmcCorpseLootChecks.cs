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
        var nearby = PmcLootRules.NearbyLoot(containers, Vector3.zero, null);
        Check(nearby.SequenceEqual(new[] { corpseId, groundId }) && !nearby.Contains(alienId) && !nearby.Contains(distantId),
            "nearby human corpses and nonempty dropped items are opportunistic search targets");
        Check(PmcLootRules.TryPosition(null, containers, corpseId, out var point) &&
            point == new Vector3(1, .25f, 0) && !PmcLootRules.TryPosition(null, containers, alienId, out _) &&
            PmcLootRules.TryPosition(null, containers, groundId, out var groundPoint) && groundPoint == new Vector3(1.7f, .25f, 0),
            "body and dropped-item positions match server records");
        Check(PmcLootRules.NearbyLoot(containers, Vector3.zero, new HashSet<int> { corpseId }).SequenceEqual(new[] { groundId }),
            "a PMC does not repeatedly search the same body");

        var groundCache = containers.Containers[groundId]; int groundVersion = groundCache.Version;
        var groundScavenger = InventoryGraph.Create(false);
        Check(PmcLootRules.TakeAvailable(groundScavenger, groundCache, includeNested: true, equipShield: true) == 1 &&
            groundScavenger.Find(droppedId) != null && groundCache.Find(droppedId) == null && containers.IsEmptyDrop(groundId),
            "a PMC takes the same real dropped item, leaving an empty ground container");
        var groundPlayer = InventoryGraph.Create(false);
        Check(LootInventoryExchange.TryApply(groundPlayer, groundCache, new InventoryCommand { Operation = InventoryOperation.Move,
            ItemId = droppedId, Parent = InventoryCatalog.Pockets, Region = "1", ExpectedVersion = groundPlayer.Version }, groundVersion) == InventoryError.Stale &&
            groundPlayer.Find(droppedId) == null, "a stale player request cannot duplicate a PMC's ground pickup");
        Check(!PmcLootRules.TryPosition(null, containers, groundId, out _) &&
            !PmcLootRules.NearbyLoot(containers, Vector3.zero, null).Contains(groundId),
            "an empty ground container is no longer a search target");
        var searching = (DollSingerEnemyBrain)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        searching.Action = PmcAction.Scavenge; searching.Cache = groundId; searching.LootUntil = 50;
        var protectedIds = new HashSet<int>();
        if (PmcLootRules.SearchingCache(searching)) protectedIds.Add(searching.Cache);
        containers.RecycleEmptyDrops(20, protectedIds); containers.RecycleEmptyDrops(40, protectedIds);
        Check(containers.DeathBags.ContainsKey(groundId), "an in-progress NPC search protects an emptied ground item from recycling");
        searching.LootUntil = 0; protectedIds.Clear();
        containers.RecycleEmptyDrops(41, protectedIds); containers.RecycleEmptyDrops(51, protectedIds);
        Check(!containers.DeathBags.ContainsKey(groundId), "the ordinary grace period recycles an empty ground item after search ends");
        PmcLootRules.RecordSearch(searching, false, true, 0);
        Check(searching.GroundItemsLooted == 0 && searching.LootedCaches == 0, "an empty ground search does not count toward departure");
        PmcLootRules.RecordSearch(searching, false, true, 1); PmcLootRules.RecordSearch(searching, false, true, 1);
        Check(searching.GroundItemsLooted == 2 && searching.LootedCaches == 0, "two loose pickups do not count as a full cache");
        PmcLootRules.RecordSearch(searching, false, true, 1);
        Check(searching.GroundItemsLooted == 3 && searching.LootedCaches == 1, "three loose pickups count as one cache");

        var carried = InventoryGraph.Create(false, false);
        var droppedBag = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "backpack", Parent = InventoryCatalog.Equipment, Region = "Backpack" };
        carried.Items.Add(droppedBag);
        Check(carried.AddSupply("data_drive", 1, droppedBag.Id, true) == InventoryError.None, "dropped bag has a real valuable");
        string nestedId = carried.Items.Single(i => i.Code == "data_drive").Id;
        var protectedMission = Mission(carried, droppedBag.Id);
        Check(containers.DropItem(carried, droppedBag.Id, carried.Version, new float3(1.8f, 0, 0), 2, out int bagId) == InventoryError.None,
            "whole carried bag drops with its contents");
        var bagCache = containers.Containers[bagId];
        Check(PmcLootRules.TakeAvailable(groundScavenger, bagCache, includeNested: true, equipShield: true) == 1 &&
            groundScavenger.Find(nestedId) != null && bagCache.Find(nestedId) == null &&
            bagCache.Find(protectedMission.Id) != null && bagCache.Find(droppedBag.Id) != null,
            "searching a dropped bag transfers ordinary contents but keeps the unique mission item");
        Check(groundScavenger.Validate() == InventoryError.None && bagCache.Validate() == InventoryError.None,
            "both inventories remain valid after the ground competition");

        var shieldOwner = InventoryGraph.Create(false, false);
        var shield = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = EnergyShieldEquipment.ShieldCode,
            Parent = InventoryCatalog.Equipment, Region = "Shield", FoundInRaid = true, ShieldCharge = 35, ShieldEnergy = 60 };
        shieldOwner.Items.Add(shield);
        Check(shieldOwner.Validate() == InventoryError.None, "dropped shield owner is valid");
        Check(containers.DropItem(shieldOwner, shield.Id, shieldOwner.Version,
            new float3(1.5f, 0, 0), 3, out int shieldDrop) == InventoryError.None,
            "a real worn shield can be dropped with its remaining resources");
        var shieldCache = containers.Containers[shieldDrop];
        var shieldScavenger = InventoryGraph.Create(false, false);
        Check(PmcLootRules.TakeAvailable(shieldScavenger, shieldCache, includeNested: true, equipShield: true) == 1 &&
            shieldScavenger.Equipped("Shield")?.Id == shield.Id && shieldCache.Find(shield.Id) == null,
            "a PMC equips the exact shield taken from the ground");
        var shieldHealth = new PredictedPlayerGhost(); RaidHealth.Initialize(ref shieldHealth);
        Check(PmcProtectionLoadout.BindLootedShield(ref shieldHealth, shieldScavenger) &&
            shieldHealth.ShieldCharge == 35 && shieldHealth.ShieldEnergy == 60,
            "ground pickup binds the shield without manufacturing charge");
        RaidHealth.CombatDamage(ref shieldHealth, BodyPart.Chest, 10, 1, RaidDamageKind.Bullet);
        EnergyShieldEquipment.Flush(ref shieldHealth, shieldScavenger);
        var shieldCorpse = LootInventoryExchange.DropOnDeath(shieldScavenger);
        Check(shieldCorpse.Find(shield.Id)?.ShieldCharge == 25 && shieldCorpse.Find(shield.Id)?.ShieldEnergy == 60 &&
            shieldScavenger.Find(shield.Id) == null,
            "the scavenger's corpse keeps the same shield and the actual charge remaining after combat");

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
            !PmcLootRules.NearbyLoot(containers, Vector3.zero, null).Contains(corpseId),
            "a removed body inventory cannot be selected or searched");
        return "PASS " + s_Checks + " PMC corpse and ground loot competition checks";
    }
}
