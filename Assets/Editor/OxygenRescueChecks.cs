using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class OxygenRescueChecks
{
    private static int s_Checks;
    private static void Check(bool condition,string message)
    {s_Checks++;if(!condition)throw new InvalidOperationException(message);}

    public static string Run()
    {
        s_Checks=0;var origin=new float3(1,0,2);
        var caches=new RaidLootContainers();caches.Initialize(1);var cache=caches.Containers[0];
        cache.Items.RemoveAll(item=>InventoryCatalog.Get(item.Code).Kind!=ItemKind.Root);cache.Version++;
        Check(cache.AddSupply("oxygen_canister",1,LootInventoryExchange.Root,true)==InventoryError.None,"oxygen cache fixture");
        var health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);
        health.ProtectionInitialized=true;health.HeadDomeEquipped=true;health.Oxygen=0;
        var inventory=new RaidInventoryState {Graph=InventoryGraph.Create(false)};
        uint damage=health.NonPhysiologicalDamageSequence,shield=health.ShieldHitSequence;float before=health.CurrentHealth;
        inventory.BeginDissection(0,0,origin,health,2);
        for(uint tick=1;tick<=4;tick++)
        {
            RaidHealth.TickOxygen(ref health,false,.5f,600,0,0,3,tick);
            inventory.RefreshDissection(caches,true,origin,health,tick*.5);
        }
        Check(health.CurrentHealth<before && health.LastHitTick==4 && health.Oxygen==0,"hypoxia continues to damage the player");
        Check(inventory.OpenedLootId==0 && RaidHealth.SelfTreatmentDamageUnchanged(health,damage,shield),"hypoxia does not prevent lifesaving actions");
        var canister=cache.Items.Find(item=>item.Code==RaidOxygenConsumables.CanisterCode);
        string rig=inventory.Graph.Equipped("ChestRig").Id;string region=null;int x=0,y=0;
        Check(canister!=null && inventory.Graph.FindSpace(canister,rig,out region,out x,out y),"accessible canister fits rig");
        Check(LootInventoryExchange.TryApply(inventory.Graph,cache,new InventoryCommand {ExpectedVersion=inventory.Graph.Version,
            Operation=InventoryOperation.Move,ItemId=canister.Id,Parent=rig,Region=region,X=x,Y=y},cache.Version)==InventoryError.None,
            "searched canister transfers to owned inventory");
        Check(RaidOxygenConsumables.Consume(ref health,inventory.Graph,canister.Id,inventory.Graph.Version)==InventoryError.None &&
            health.Oxygen>0 && inventory.Graph.Find(canister.Id)==null,"found canister refills the suit exactly once");
        float recoveredHealth=health.CurrentHealth;
        RaidHealth.TickOxygen(ref health,false,.5f,600,0,0,3,5);
        Check(health.CurrentHealth==recoveredHealth && health.HypoxiaSeconds==0,"refill stops hypoxia damage");
        health.Oxygen=0;inventory=new RaidInventoryState();inventory.BeginDissection(0,10,origin,health,2);
        RaidHealth.CombatDamage(ref health,BodyPart.RightArm,5,50,RaidDamageKind.Bullet);
        RaidHealth.TickOxygen(ref health,false,.5f,600,0,0,3,50);
        inventory.RefreshDissection(caches,true,origin,health,10.5);
        Check(health.LastHitTick==50 && inventory.OpenedLootId<0 && inventory.Error==InventoryError.Inaccessible,
            "same-tick hypoxia cannot hide an external injury");
        health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);health.Oxygen=0;
        health.ShieldEquipped=true;health.ShieldCharge=100;
        inventory=new RaidInventoryState();inventory.BeginDissection(0,20,origin,health,2);
        RaidHealth.CombatDamage(ref health,BodyPart.RightArm,5,60,RaidDamageKind.Bullet);
        RaidHealth.TickOxygen(ref health,false,.5f,600,0,0,3,60);
        inventory.RefreshDissection(caches,true,origin,health,20.5);
        Check(inventory.OpenedLootId<0 && inventory.Error==InventoryError.Inaccessible,"hypoxia cannot hide a shield-only hit");
        health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);health.Oxygen=0;
        RaidHealth.Set(ref health,BodyPart.Chest,1);
        inventory=new RaidInventoryState();inventory.BeginDissection(0,30,origin,health,2);
        RaidHealth.TickOxygen(ref health,false,.5f,600,0,0,3,70);
        inventory.RefreshDissection(caches,health.CurrentHealth>0,origin,health,30.5);
        Check(health.CurrentHealth==0 && inventory.OpenedLootId<0,"fatal hypoxia still cancels search");
        return "PASS "+s_Checks+" hypoxia rescue checks";
    }
}
