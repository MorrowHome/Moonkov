using System;
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
            "binding the shield preserves the PMC's existing non-simulated oxygen and movement");

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
        return "PASS " + s_Checks + " PMC shield loadout and corpse checks";
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
        world.SetTime(new TimeData(7, 7));
        ref var system = ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
        ref var state = ref world.Unmanaged.ResolveSystemStateRef(handle);
        tick(ref system, ref state);
        var updated = manager.GetComponentData<PredictedPlayerGhost>(npc);
        var excluded = manager.GetComponentData<PredictedPlayerGhost>(clash);
        if (!Near(updated.ShieldCharge, 48) || !Near(updated.ShieldEnergy, 96) ||
            !Near(inventory.Equipped("Shield").ShieldCharge, 48) || !Near(updated.Oxygen, 100) ||
            !Near(excluded.ShieldCharge, 40) || !Near(clashInventory.Equipped("Shield").ShieldCharge, 100))
            throw new InvalidOperationException("Server PMC protection update or bounty exclusion failed");
        return "PASS server PMC shield update and bounty exclusion";
    }
}
