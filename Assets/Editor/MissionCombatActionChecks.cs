using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Missions;

public static class MissionCombatActionChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static MissionOperation Operation() => new MissionOperation(Guid.NewGuid().ToString("D"),
        Guid.NewGuid().ToString("D"), 1, MissionCatalog.Archive(), 0);

    private static PredictedPlayerGhost Health()
    {
        var health = default(PredictedPlayerGhost); RaidHealth.Initialize(ref health);
        health.LastHitTick=7;health.ShieldHitSequence=3;health.LastShotTick=4;health.LastGrenadeShotTick=5;
        health.LastReloadTick=6;health.EquippedWeaponID=1;
        return health;
    }

    public static string Run()
    {
        s_Checks = 0;
        for (int kind = 0; kind < 10; kind++)
        {
            var health = Health();
            var actor = new RaidMissionWorld.Actor { Key = "player", Node = 0, Request = 1 };
            var operation = Operation(); var position=float3.zero; actor.Capture(health,position);
            Check(operation.Begin(actor.Key, 1, 0, 1, 0) == MissionActionResult.Started && !actor.Interrupted(health,position),
                "Manual operation starts: " + kind);
            Check(operation.Renew(actor.Key, 1, 1.5) == MissionActionResult.Running, "Hold remains active: " + kind);
            switch (kind)
            {
                case 0: health.LastHitTick++; break;
                case 1: health.ShieldHitSequence++; break;
                case 2: health.LastShotTick++; break;
                case 3: health.LastGrenadeShotTick++; break;
                case 4: health.LastReloadTick++; break;
                case 5: health.EquippedWeaponID++; break;
                case 6: health.ControllerState.IsReloadingState=true; break;
                case 7: health.WeaponEquipRemaining=.2f; break;
                case 8: health.CurrentHealth--; break;
                case 9: position.x=.76f; break;
            }
            Check(actor.Interrupted(health,position), "Combat, movement or weapon action interrupts: " + kind);
            if (actor.Interrupted(health,position)) operation.Cancel(actor.Key);
            Check(operation.Renew(actor.Key, 1, 3.1) == MissionActionResult.Stale && !operation.Completed(0),
                "Interrupted final renewal cannot complete the operation: " + kind);
        }

        var graph = InventoryGraph.Create(false);
        Check(graph.AddSupply(EnergyGrenadeRules.Code, 1, InventoryCatalog.Pockets) == InventoryError.None,
            "Create a real grenade");
        var grenade = graph.Items.Find(item => item.Code == EnergyGrenadeRules.Code);
        var inventory = new RaidInventoryState { Graph = graph };
        var current = Health(); var marker = new RaidMissionWorld.Actor { Key = "player", Node = 0, Request = 1 };
        var active = Operation(); marker.Capture(current,float3.zero);
        Check(active.Begin(marker.Key, 1, 0, 1, 0) == MissionActionResult.Started, "Grenade interruption setup");
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref current, true, grenade.Id, graph.Version - 1, 8) == InventoryError.Stale &&
              !marker.Interrupted(current,float3.zero), "Rejected throw does not cancel the operation");
        Check(EnergyGrenadeServerSystem.CommitThrow(inventory, ref current, true, grenade.Id, graph.Version, 8) == InventoryError.None &&
              marker.Interrupted(current,float3.zero), "A real accepted grenade interrupts the operation");
        active.Cancel(marker.Key);
        Check(active.Renew(marker.Key, 1, 1) == MissionActionResult.Stale && !active.Completed(0), "Grenade cannot grant mission progress");

        var normal = Health(); var normalActor = new RaidMissionWorld.Actor { Key = "other" }; normalActor.Capture(normal,float3.zero);
        var uninterrupted = Operation();
        Check(uninterrupted.Begin(normalActor.Key, 1, 0, 1, 0) == MissionActionResult.Started &&
              uninterrupted.Renew(normalActor.Key, 1, 1.5) == MissionActionResult.Running &&
              !normalActor.Interrupted(normal,new float3(.75f,0,0)) && uninterrupted.Renew(normalActor.Key, 1, 3.1) == MissionActionResult.Completed,
            "An uninterrupted hold still completes");
        return "PASS " + s_Checks + " mission combat action checks";
    }
}
