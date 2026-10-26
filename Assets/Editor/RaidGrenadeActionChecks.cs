using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class RaidGrenadeActionChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static string Run()
    {
        s_Checks = 0;
        var graph = InventoryGraph.Create(false);
        Check(graph.AddSupply(EnergyGrenadeRules.Code, 3, InventoryCatalog.Pockets) == InventoryError.None,
            "Create accessible grenades");
        var grenade = graph.Items.Find(item => item.Code == EnergyGrenadeRules.Code);
        var inventory = new RaidInventoryState { Graph = graph };
        var health = default(PredictedPlayerGhost); RaidHealth.Initialize(ref health);
        health.LastGrenadeShotTick = 8;
        int version = graph.Version;

        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, false, grenade.Id, version, 9) == InventoryError.Inaccessible &&
              grenade.Quantity == 3 && graph.Version == version && health.LastGrenadeShotTick == 8,
            "Disabled player cannot throw or interrupt");
        health.ClashDarkSight = true;
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version, 9) == InventoryError.Inaccessible &&
              grenade.Quantity == 3 && health.LastGrenadeShotTick == 8, "Dark sight cannot throw");
        health.ClashDarkSight = false; health.ControllerState.IsReloadingState = true;
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version, 9) == InventoryError.Inaccessible &&
              grenade.Quantity == 3 && health.LastGrenadeShotTick == 8, "Reload cannot throw");
        health.ControllerState.IsReloadingState = false; health.WeaponEquipRemaining = .2f;
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version, 9) == InventoryError.Inaccessible &&
              grenade.Quantity == 3 && health.LastGrenadeShotTick == 8, "Weapon change cannot throw");
        health.WeaponEquipRemaining = 0;
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version - 1, 9) == InventoryError.Stale &&
              grenade.Quantity == 3 && health.LastGrenadeShotTick == 8, "Stale inventory cannot throw or interrupt");
        health.CurrentHealth = 0;
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version, 9) == InventoryError.Inaccessible &&
              grenade.Quantity == 3 && health.LastGrenadeShotTick == 8, "Dead player cannot throw");
        health.CurrentHealth = float.NaN;
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version, 9) == InventoryError.Inaccessible &&
              grenade.Quantity == 3 && health.LastGrenadeShotTick == 8, "Invalid player health cannot throw");
        RaidHealth.Initialize(ref health); health.LastGrenadeShotTick = 8;

        var caches = new RaidLootContainers(); caches.Initialize(1);
        Check(inventory.BeginDissection(0, 0, float3.zero, health, 2), "Search starts");
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version, 9) == InventoryError.None &&
              grenade.Quantity == 2 && graph.Version == version + 1 && health.LastGrenadeShotTick == 9,
            "Accepted throw spends exactly one and records the server event");
        inventory.RefreshDissection(caches, true, float3.zero, health, 2);
        Check(!inventory.ActionPending && inventory.OpenedLootId < 0 && !inventory.SearchedLoot.Contains(0) &&
              inventory.Error == InventoryError.Inaccessible, "Actual throw interrupts search before contents open");
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, version, 10) == InventoryError.Stale &&
              grenade.Quantity == 2 && health.LastGrenadeShotTick == 9, "Replay cannot consume or emit another throw");

        Check(graph.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None, "Create carried medicine");
        var medicine = graph.Items.Find(item => item.Code == "medkit");
        RaidHealth.Set(ref health, BodyPart.LeftArm, 20);
        Check(inventory.BeginMedical(health, medicine.Id, graph.Version, BodyPart.LeftArm, float3.zero, 10) == InventoryError.None,
            "Treatment starts");
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref health, true, grenade.Id, graph.Version, 11) == InventoryError.None,
            "Accepted throw during a pending treatment");
        inventory.RefreshMedical(ref health, true, float3.zero, 13);
        Check(!inventory.ActionPending && health.LeftArmHealth == 20 && graph.Count("medkit", true) == 1,
            "Treatment does not complete or consume medicine after throw");
        return "PASS " + s_Checks + " energy grenade action checks";
    }
}
