using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class RaidSearchActionChecks
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
        var caches = new RaidLootContainers(); caches.Initialize(2);
        var origin = new float3(3, 0, 5);
        var health = new PredictedPlayerGhost { LastHitTick = 1, ShieldHitSequence = 2, LastShotTick = 3,
            LastGrenadeShotTick = 4, LastReloadTick = 5, EquippedWeaponID = 6 };
        var actor = new RaidInventoryState();
        var busy = health; busy.ControllerState.IsReloadingState = true;
        actor.BeginDissection(0, 0, origin, busy, 2);
        Check(actor.DissectingLootId == -1 && actor.OpenedLootId == -1, "Already reloading cannot start search");
        busy.ControllerState.IsReloadingState = false; busy.WeaponEquipRemaining = .2f;
        actor.BeginDissection(0, 0, origin, busy, 2);
        Check(actor.DissectingLootId == -1 && actor.OpenedLootId == -1, "Weapon switch cannot start search");
        actor.BeginDissection(0, 10, origin, health, 2);
        actor.RefreshDissection(caches, true, origin, health, 11.99);
        Check(actor.DissectingLootId == 0 && actor.OpenedLootId == -1 && !actor.SearchedLoot.Contains(0), "Search cannot reveal contents early");
        actor.RefreshDissection(caches, true, origin, health, 12);
        Check(actor.OpenedLootId == 0 && actor.SearchedLoot.Contains(0) && !actor.NeedsSearch(caches, 0), "Search opens and remembers the completed container");

        for (int interruption = 0; interruption < 10; interruption++)
        {
            actor = new RaidInventoryState();
            var changed = health;
            actor.BeginDissection(0, 20, origin, changed, 2);
            var position = origin; bool accessible = true;
            switch (interruption)
            {
                case 0: changed.LastHitTick++; break;
                case 1: changed.ShieldHitSequence++; break;
                case 2: changed.LastShotTick++; break;
                case 3: changed.LastGrenadeShotTick++; break;
                case 4: changed.LastReloadTick++; break;
                case 5: changed.EquippedWeaponID++; break;
                case 6: position.x += .76f; break;
                case 7: accessible = false; break;
                case 8: changed.ControllerState.IsReloadingState = true; break;
                case 9: changed.WeaponEquipRemaining = .2f; break;
            }
            actor.RefreshDissection(caches, accessible, position, changed, 22);
            Check(actor.DissectingLootId == -1 && actor.OpenedLootId == -1 && !actor.SearchedLoot.Contains(0) &&
                actor.Error == InventoryError.Inaccessible, "Interruption must not reveal or remember loot: " + interruption);
            Check(!string.IsNullOrEmpty(actor.ActionFeedback), "Interruption needs feedback: " + interruption);
            changed.ControllerState.IsReloadingState = false; changed.WeaponEquipRemaining = 0;
            actor.BeginDissection(0, 30, origin, changed, 2);
            actor.RefreshDissection(caches, true, origin, changed, 32);
            Check(actor.OpenedLootId == 0 && actor.SearchedLoot.Contains(0), "Search can be restarted after interruption: " + interruption);
        }

        var shielded = new PredictedPlayerGhost(); RaidHealth.Initialize(ref shielded);
        shielded.ShieldEquipped = true; shielded.ShieldCharge = 100; shielded.LastHitTick = 42;
        actor = new RaidInventoryState(); actor.BeginDissection(0, 0, origin, shielded, 2);
        Check(EnergyShieldRules.Absorb(ref shielded, 25, 42, RaidDamageKind.Bullet, new float3(1, 1, 1)) == 0 && shielded.LastHitTick == 42,
            "Shield absorbs a second impact in the same tick");
        actor.RefreshDissection(caches, true, origin, shielded, 2);
        Check(actor.OpenedLootId == -1 && !actor.SearchedLoot.Contains(0), "Same-tick shield impact interrupts search");

        var remains = AlienHarvest.Create(false, new System.Random(7));
        int alienId = caches.DropHarvest(remains, origin);
        actor = new RaidInventoryState();
        actor.BeginDissection(alienId, 0, origin, health);
        var hit = health; hit.ShieldHitSequence++;
        actor.RefreshDissection(caches, true, origin, hit, 6);
        Check(caches.RequiresDissection(alienId) && actor.OpenedLootId == -1, "Shield hit cannot reveal alien remains");
        actor.BeginDissection(alienId, 10, origin, hit);
        actor.RefreshDissection(caches, true, origin, hit, 16);
        Check(!caches.RequiresDissection(alienId) && actor.OpenedLootId == alienId && caches.Containers[alienId].Version == 2,
            "Restarted dissection reveals shared remains once");
        return "PASS " + s_Checks + " search interruption checks";
    }
}
