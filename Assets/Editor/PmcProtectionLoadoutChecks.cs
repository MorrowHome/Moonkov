using System;
using System.Linq;
using System.Reflection;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class PmcProtectionLoadoutChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < .001f;
    private delegate void ProtectionTick(ref ServerGameSystem system, ref SystemState state);

    [MenuItem("Moonkov/AI/Check Shield Loadout")]
    public static void RunFromEditor() { Debug.Log(Run()); Debug.Log(RunAuthority()); }

    public static string Run()
    {
        s_Checks = 0;
        Check(PmcProtectionLoadout.ShouldEquip(false, false, 0) &&
            PmcProtectionLoadout.ShouldEquip(false, false, PmcProtectionLoadout.ShieldChance - .001f),
            "ordinary raids may produce protected enemies");
        Check(!PmcProtectionLoadout.ShouldEquip(false, false, PmcProtectionLoadout.ShieldChance) &&
            !PmcProtectionLoadout.ShouldEquip(true, false, 0) && !PmcProtectionLoadout.ShouldEquip(false, true, 0) &&
            !PmcProtectionLoadout.ShouldEquip(false, false, float.NaN) && !PmcProtectionLoadout.ShouldEquip(false, false, -1),
            "the other loadouts, tutorial and bounty mode remain unchanged");

        var inventory = InventoryGraph.Create(false);
        int version = inventory.Version;
        Check(PmcProtectionLoadout.TryEquip(inventory, false, false, 0) && inventory.Version == version + 1 &&
            inventory.Validate() == InventoryError.None, "a real shield occupies the equipment slot");
        var shield = inventory.Equipped("Shield");
        string id = shield.Id;
        Check(shield.Code == EnergyShieldEquipment.ShieldCode && shield.FoundInRaid &&
            !PmcProtectionLoadout.TryEquip(inventory, false, false, 0) && inventory.Version == version + 1,
            "the original item is unique and the occupied slot is never replaced");

        var enemy = new PredictedPlayerGhost(); RaidHealth.Initialize(ref enemy);
        PmcProtectionLoadout.Initialize(ref enemy, inventory);
        Check(enemy.ShieldEquipped && Near(enemy.ShieldCharge, 100) && Near(enemy.ShieldEnergy, 100),
            "the shared equipment rules initialize the shield and finite energy");
        Check(Near(enemy.Oxygen, 100) && RaidNutrition.CanSprint(enemy),
            "binding a shield without a collar preserves the existing oxygen and movement");

        float hp = enemy.CurrentHealth;
        Check(Near(RaidHealth.CombatDamage(ref enemy, BodyPart.Chest, 60, 1, RaidDamageKind.Bullet, new float3(1, 1, 1)), 0) &&
            Near(enemy.CurrentHealth, hp) && Near(enemy.ShieldCharge, 40), "real combat damage drains the shield first");
        EnergyShieldEquipment.Tick(ref enemy, inventory, 6);
        Check(Near(enemy.ShieldCharge, 40) && Near(enemy.ShieldEnergy, 100), "the existing recovery delay applies");
        EnergyShieldEquipment.Tick(ref enemy, inventory, 1);
        Check(Near(enemy.ShieldCharge, 48) && Near(enemy.ShieldEnergy, 96) &&
            Near(inventory.Find(id).ShieldCharge, 48) && Near(inventory.Find(id).ShieldEnergy, 96),
            "recovery spends the same finite energy and flushes the real item");

        RaidHealth.CombatDamage(ref enemy, BodyPart.Head, 100, 2, RaidDamageKind.Bullet, new float3(1, 1, 1));
        Check(enemy.CurrentHealth <= 0 && Near(enemy.ShieldCharge, 0) && Near(inventory.Find(id).ShieldCharge, 48),
            "a lethal hit can change charge after the previous item update");
        EnergyShieldEquipment.Flush(ref enemy, inventory);
        var containers = new RaidLootContainers(); containers.Initialize(1);
        int corpseId = containers.Drop(inventory, float3.zero, characterIndex: 2);
        var corpse = containers.Containers[corpseId];
        Check(inventory.Find(id) == null && corpse.Find(id) != null && corpse.Find(id).FoundInRaid &&
            Near(corpse.Find(id).ShieldCharge, 0) && Near(corpse.Find(id).ShieldEnergy, 96),
            "the corpse owns the same depleted item after the lethal hit");
        Check(containers.DeathBags[corpseId].EquipmentSlots["Shield"] == id && corpse.Validate() == InventoryError.None,
            "the corpse preserves its equipment identity");

        var playerInventory = InventoryGraph.Create(false);
        var move = new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = playerInventory.Version,
            ItemId = id, Parent = InventoryCatalog.Equipment, Region = "Shield" };
        Check(LootInventoryExchange.TryApply(playerInventory, corpse, move, corpse.Version) == InventoryError.None &&
            corpse.Find(id) == null && playerInventory.Equipped("Shield")?.Id == id,
            "the player can take the same item without duplication");
        var player = new PredictedPlayerGhost(); RaidHealth.Initialize(ref player);
        EnergyShieldEquipment.Synchronize(ref player, playerInventory);
        Check(player.ShieldEquipped && Near(player.ShieldCharge, 0) && Near(player.ShieldEnergy, 96),
            "looting does not refill a depleted generator");
        EnergyShieldEquipment.Tick(ref player, playerInventory, 7);
        Check(Near(player.ShieldCharge, 8) && Near(player.ShieldEnergy, 92),
            "the looted item recovers only by consuming its remaining energy");
        var decoded = RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(playerInventory));
        Check(decoded.Equipped("Shield")?.Id == id && Near(decoded.Find(id).ShieldCharge, 8) &&
            Near(decoded.Find(id).ShieldEnergy, 92) && decoded.Find(id).FoundInRaid,
            "inventory serialization retains the same recovered item and its actual resources");
        CheckScavengedProtection();
        CheckShieldSupply();
        return "PASS " + s_Checks + " PMC shield loadout and corpse checks";
    }

    private static void CheckShieldSupply()
    {
        var inventory = InventoryGraph.Create(false);
        Check(PmcProtectionLoadout.TryEquip(inventory, false, false, 0) &&
            inventory.AddSupply("cells", 3, InventoryCatalog.Pockets, true) == InventoryError.None,
            "shield supply fixture uses real carried cells");
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        PmcProtectionLoadout.Initialize(ref health, inventory);
        string shieldId = health.ShieldItem.ToString();
        health.ShieldCharge = 20; health.ShieldEnergy = 15.5f;
        EnergyShieldEquipment.Flush(ref health, inventory);
        var brain = (DollSingerEnemyBrain)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        brain.Inventory = inventory; brain.Action = PmcAction.Scavenge;
        Check(PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "safe raider can replenish a low shield");
        Check(!PmcShieldSupply.MayRecharge(brain, health, true, true, 10) &&
            !PmcShieldSupply.MayRecharge(brain, health, false, false, 10), "tutorial or unsafe raiders do not replenish");
        brain.ClashParticipantId = 1;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "bounty loadouts are excluded");
        brain.ClashParticipantId = 0; brain.Visible = true;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "visible combat blocks supply");
        brain.Visible = false; brain.HazardDanger = true;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "hazards take priority over supply");
        brain.HazardDanger = false; brain.LootUntil = 12;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "search is not interrupted by supply");
        brain.LootUntil = 0; brain.ExtractionProgress = 1;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "extraction is not interrupted by supply");
        brain.ExtractionProgress = 0; brain.HealUntil = 12;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "healing is not interrupted by supply");
        brain.HealUntil = 0; brain.SelfStatus = new TacticalServerWorld.Status { Bleed = 12 };
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "bleeding takes priority over supply");
        brain.SelfStatus.Bleed = 0; brain.SelfStatus.Fire = 12;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "burning takes priority over supply");
        brain.SelfStatus.Fire = 0; brain.SelfStatus.Poison = 12;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "poison takes priority over supply");
        brain.SelfStatus.Antidote = 12;
        Check(PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "active antidote permits a safe supply");
        brain.Action = PmcAction.Engage;
        Check(!PmcShieldSupply.MayRecharge(brain, health, false, true, 10), "combat actions do not replenish");
        brain.Action = PmcAction.Scavenge;

        int before = inventory.CellEnergy(accessibleOnly: true), version = inventory.Version;
        Check(!PmcShieldSupply.TryRecharge(ref health, inventory, 10, 30) &&
            inventory.Version == version && inventory.CellEnergy(accessibleOnly: true) == before && Near(health.ShieldEnergy, 15.5f),
            "a full magazine's reserve has priority over the shield");
        Check(!PmcShieldSupply.TryRecharge(ref health, inventory, 0, 10) &&
            !PmcShieldSupply.TryRecharge(ref health, inventory, 2, 0), "invalid weapon data does not spend cells");
        Check(PmcShieldSupply.TryRecharge(ref health, inventory, 2, 30) && inventory.Version == version + 1 &&
            inventory.CellEnergy(accessibleOnly: true) == before - 84 && Near(health.ShieldEnergy, 99.5f) &&
            Near(inventory.Find(shieldId).ShieldEnergy, 99.5f) && Near(health.ShieldCharge, 20),
            "the real recharge spends only whole cell energy and preserves shield identity and charge");
        Check(!PmcShieldSupply.TryRecharge(ref health, inventory, 2, 30) && inventory.Version == version + 1,
            "energy above the threshold does not repeatedly consume cells");
        EnergyShieldEquipment.Tick(ref health, inventory, 7);
        Check(Near(health.ShieldCharge, 28) && Near(health.ShieldEnergy, 95.5f),
            "recharged energy powers the ordinary delayed shield recovery");
        EnergyShieldEquipment.Flush(ref health, inventory);
        var corpse = LootInventoryExchange.DropOnDeath(inventory);
        Check(corpse.Find(shieldId) != null && Near(corpse.Find(shieldId).ShieldCharge, 28) &&
            Near(corpse.Find(shieldId).ShieldEnergy, 95.5f) &&
            corpse.Items.Where(i => i.Code == "cells").Sum(BatteryEnergy.Stored) == before - 84,
            "death leaves the actual shield and remaining cell energy in the corpse");

        inventory = InventoryGraph.Create(false);
        Check(PmcProtectionLoadout.TryEquip(inventory, false, false, 0) &&
            inventory.AddSupply("cells", 3, inventory.Equipped("Backpack").Id, true) == InventoryError.None,
            "backpack-only cell fixture");
        health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); PmcProtectionLoadout.Initialize(ref health, inventory);
        health.ShieldEnergy = 0;
        version = inventory.Version;
        Check(!PmcShieldSupply.TryRecharge(ref health, inventory, 1, 1) && inventory.Version == version &&
            inventory.CellEnergy(carriedOnly: true) == 300,
            "inaccessible backpack cells are neither spent nor counted as the ready reserve");
    }

    private static InventoryGraph ShieldCache(float charge, float energy, out string itemId)
    {
        var cache = InventoryGraph.Create(false, false);
        cache.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
        var shield = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = EnergyShieldEquipment.ShieldCode,
            FoundInRaid = true, ShieldCharge = charge, ShieldEnergy = energy };
        if (!cache.FindSpace(shield, LootInventoryExchange.Root, out string region, out int x, out int y))
            throw new InvalidOperationException("shield cache fixture");
        shield.Parent = LootInventoryExchange.Root; shield.Region = region; shield.X = x; shield.Y = y;
        cache.Items.Add(shield); itemId = shield.Id;
        return cache;
    }

    private static void CheckScavengedProtection()
    {
        var inventory = InventoryGraph.Create(false);
        var cache = ShieldCache(35, 60, out string id);
        Check(inventory.Validate() == InventoryError.None && cache.Validate() == InventoryError.None,
            "scavenged shield fixture is valid");
        Check(PmcLootRules.TakeAvailable(inventory, cache, equipShield: true) == 1 && inventory.Equipped("Shield")?.Id == id &&
            cache.Find(id) == null, "an unprotected PMC equips the actual shield it finds");
        var enemy = new PredictedPlayerGhost(); RaidHealth.Initialize(ref enemy);
        Check(PmcProtectionLoadout.BindLootedShield(ref enemy, inventory) && enemy.ShieldEquipped &&
            Near(enemy.ShieldCharge, 35) && Near(enemy.ShieldEnergy, 60) && Near(enemy.Oxygen, 100),
            "the scavenged shield keeps its real charge and energy without changing NPC oxygen");
        float hp = enemy.CurrentHealth;
        RaidHealth.CombatDamage(ref enemy, BodyPart.Chest, 20, 1, RaidDamageKind.Bullet);
        Check(Near(enemy.CurrentHealth, hp) && Near(enemy.ShieldCharge, 15), "the scavenged shield actually protects its wearer");
        Check(!PmcProtectionLoadout.BindLootedShield(ref enemy, inventory) && Near(enemy.ShieldCharge, 15),
            "rebinding an equipped shield cannot refill it");
        EnergyShieldEquipment.Flush(ref enemy, inventory);
        var corpse = LootInventoryExchange.DropOnDeath(inventory);
        Check(corpse.Find(id) != null && Near(corpse.Find(id).ShieldCharge, 15) && Near(corpse.Find(id).ShieldEnergy, 60),
            "death drops the same used shield with its remaining resources");
        var player = InventoryGraph.Create(false);
        Check(LootInventoryExchange.TryApply(player, corpse, new InventoryCommand { Operation = InventoryOperation.Move,
            ExpectedVersion = player.Version, ItemId = id, Parent = InventoryCatalog.Equipment, Region = "Shield" }, corpse.Version) == InventoryError.None &&
            player.Equipped("Shield")?.Id == id && corpse.Find(id) == null,
            "a player can recover the same shield from the scavenger without duplication");

        inventory = InventoryGraph.Create(false);
        Check(PmcProtectionLoadout.TryEquip(inventory, false, false, 0), "existing shield fixture");
        string original = inventory.Equipped("Shield").Id;
        cache = ShieldCache(80, 60, out id);
        Check(PmcLootRules.TakeAvailable(inventory, cache, equipShield: true) == 1 && inventory.Equipped("Shield")?.Id == original &&
            inventory.Find(id) != null && inventory.Find(id).Parent == inventory.Equipped("Backpack").Id,
            "looting does not replace an already equipped shield");

        inventory = InventoryGraph.Create(false);
        cache = ShieldCache(0, 0, out id);
        Check(PmcLootRules.TakeAvailable(inventory, cache, equipShield: true) == 1 && inventory.Equipped("Shield") == null &&
            inventory.Find(id) != null, "a fully depleted shield can be carried but is not equipped as protection");

        inventory = InventoryGraph.Create(false);
        cache = ShieldCache(40, 60, out id);
        Check(PmcLootRules.TakeAvailable(inventory, cache) == 1 && inventory.Equipped("Shield") == null &&
            inventory.Find(id) != null, "disabled equip behavior leaves the tutorial loadout unchanged");

        inventory = InventoryGraph.Create(false, false);
        cache = ShieldCache(0, 60, out id);
        Check(PmcLootRules.TakeAvailable(inventory, cache, equipShield: true) == 1 && inventory.Equipped("Shield")?.Id == id,
            "a usable shield fits its equipment slot even with no backpack");
        enemy = new PredictedPlayerGhost(); RaidHealth.Initialize(ref enemy);
        Check(PmcProtectionLoadout.BindLootedShield(ref enemy, inventory) && Near(enemy.ShieldCharge, 0) && Near(enemy.ShieldEnergy, 60),
            "an empty shield does not gain free charge when equipped");
        EnergyShieldEquipment.Tick(ref enemy, inventory, 7);
        Check(Near(enemy.ShieldCharge, 8) && Near(enemy.ShieldEnergy, 56),
            "an empty shield recovers only after the delay and spends its own energy");

        inventory = InventoryGraph.Create(false, false);
        var pack = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "backpack",
            Parent = InventoryCatalog.Equipment, Region = "Backpack" };
        inventory.Items.Add(pack);
        Check(inventory.AddSupply("alloy", 68, pack.Id) == InventoryError.None &&
            inventory.AddSupply("dust", 18, pack.Id) == InventoryError.None, "near-weight-limit shield fixture");
        cache = ShieldCache(70, 60, out id);
        int inventoryVersion = inventory.Version, cacheVersion = cache.Version;
        Check(PmcLootRules.TakeAvailable(inventory, cache, equipShield: true) == 0 &&
            inventory.Equipped("Shield") == null && cache.Find(id) != null &&
            inventory.Version == inventoryVersion && cache.Version == cacheVersion,
            "a shield that exceeds the carry limit remains in the cache");

        inventory = InventoryGraph.Create(false);
        cache = ShieldCache(30, 45, out id);
        var nestedShield = cache.Find(id);
        var bag = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "small_pack", FoundInRaid = true };
        Check(cache.FindSpace(bag, LootInventoryExchange.Root, out string region, out int x, out int y), "nested shield bag fits corpse");
        bag.Parent = LootInventoryExchange.Root; bag.Region = region; bag.X = x; bag.Y = y; cache.Items.Add(bag);
        nestedShield.Parent = bag.Id; nestedShield.Region = "main"; nestedShield.X = nestedShield.Y = 0;
        Check(cache.Validate() == InventoryError.None && PmcLootRules.TakeAvailable(inventory, cache, equipShield: true) == 0,
            "ordinary cache search does not reveal the contents of nested bags");
        Check(PmcLootRules.TakeAvailable(inventory, cache, includeNested: true, equipShield: true) == 1 &&
            inventory.Equipped("Shield")?.Id == id && cache.Find(id) == null,
            "corpse search can equip a shield from the searched backpack");
    }

    // Uses the real server update in an isolated ECS world; no raid, profile or scene.
    public static string RunAuthority()
    {
        using var world = new World("PMC shield authority fixture", WorldFlags.GameServer);
        var manager = world.EntityManager;
        var handle = world.GetOrCreateSystem<ServerGameSystem>();
        var tick = (ProtectionTick)typeof(ServerGameSystem).GetMethod("UpdateProtectionGear",
            BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(ProtectionTick));
        var inventory = InventoryGraph.Create(false);
        if (!PmcProtectionLoadout.TryEquip(inventory, false, false, 0)) throw new InvalidOperationException("Shield fixture");
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        PmcProtectionLoadout.Initialize(ref health, inventory);
        RaidHealth.CombatDamage(ref health, BodyPart.Chest, 60, 1, RaidDamageKind.Bullet, new float3(1, 1, 1));
        var npc = manager.CreateEntity(typeof(DollSingerEnemy), typeof(Simulate), typeof(PredictedPlayerGhost));
        manager.SetComponentData(npc, health);
        manager.AddComponentObject(npc, new DollSingerEnemyBrain { Inventory = inventory });
        var clash = manager.CreateEntity(typeof(DollSingerEnemy), typeof(ClashBot), typeof(Simulate), typeof(PredictedPlayerGhost));
        var clashInventory = inventory.Clone();
        manager.SetComponentData(clash, health);
        manager.AddComponentObject(clash, new DollSingerEnemyBrain { Inventory = clashInventory });
        var scavengerInventory = InventoryGraph.Create(false);
        var cache = ShieldCache(35, 60, out string scavengedId);
        var scavengerHealth = new PredictedPlayerGhost(); RaidHealth.Initialize(ref scavengerHealth);
        if (PmcLootRules.TakeAvailable(scavengerInventory, cache, equipShield: true) != 1 ||
            !PmcProtectionLoadout.BindLootedShield(ref scavengerHealth, scavengerInventory))
            throw new InvalidOperationException("Scavenged shield fixture");
        var scavenger = manager.CreateEntity(typeof(DollSingerEnemy), typeof(Simulate), typeof(PredictedPlayerGhost));
        manager.SetComponentData(scavenger, scavengerHealth);
        manager.AddComponentObject(scavenger, new DollSingerEnemyBrain { Inventory = scavengerInventory });
        world.SetTime(new TimeData(7, 7));
        ref var system = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
        tick(ref system, ref state);
        var updated = manager.GetComponentData<PredictedPlayerGhost>(npc);
        var excluded = manager.GetComponentData<PredictedPlayerGhost>(clash);
        var scavenged = manager.GetComponentData<PredictedPlayerGhost>(scavenger);
        if (!Near(updated.ShieldCharge, 48) || !Near(updated.ShieldEnergy, 96) ||
            !Near(inventory.Equipped("Shield").ShieldCharge, 48) || !Near(updated.Oxygen, 100) ||
            !Near(excluded.ShieldCharge, 40) || !Near(clashInventory.Equipped("Shield").ShieldCharge, 100) ||
            !Near(scavenged.ShieldCharge, 43) || !Near(scavenged.ShieldEnergy, 56) ||
            !Near(scavengerInventory.Find(scavengedId).ShieldCharge, 43) || !Near(scavenged.Oxygen, 100))
            throw new InvalidOperationException("Server PMC protection update or bounty exclusion failed");
        return "PASS server PMC loadout and scavenged shield updates with bounty exclusion";
    }
}
