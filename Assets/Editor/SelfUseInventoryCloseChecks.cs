using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.Mathematics;
using UnityEditor;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class SelfUseInventoryCloseChecks
{
    [MenuItem("Tools/Moonkov/Check Self Use Inventory Close")]
    public static string Run()
    {
        int count=0;
        void Check(bool ok,string message) {if(!ok)throw new InvalidOperationException(message);count++;}
        PredictedPlayerGhost Health()
        {var h=default(PredictedPlayerGhost);RaidHealth.Initialize(ref h);return h;}
        RaidInventoryState Inventory(string code)
        {
            var graph=InventoryGraph.Create(false,true);
            string parent=code=="water" ? graph.Equipped("ChestRig").Id : "pockets";
            Check(graph.AddSupply(code,2,parent)==InventoryError.None,"Place accessible fixture: "+code);
            Check(graph.Validate()==InventoryError.None,"Valid carried fixture: "+code);
            return new RaidInventoryState {Graph=graph,RaidId=7,OpenedLootId=3};
        }
        var health=Health();RaidHealth.Damage(ref health,BodyPart.RightArm,50,1);
        var actor=Inventory("medkit");var id=actor.Graph.Items.First(i=>i.Code=="medkit").Id;
        int version=actor.Graph.Version;float before=health.CurrentHealth;
        Check(actor.BeginMedical(health,id,version,BodyPart.RightArm,float3.zero,10)==InventoryError.None,"Medical begins");
        actor.CloseLoot();actor.CloseLoot();
        Check(actor.OpenedLootId<0 && actor.MedicalItemId==id && actor.MedicalUntil==13,"Closing twice preserves original treatment and deadline");
        Check(actor.Graph.Version==version && actor.Graph.Count("medkit")==2,"Hiding inventory cannot consume medicine");
        actor.RefreshMedical(ref health,true,float3.zero,12.99);
        Check(health.CurrentHealth==before,"Closing cannot heal early");
        actor.RefreshMedical(ref health,true,float3.zero,13);actor.RefreshMedical(ref health,true,float3.zero,20);
        Check(health.CurrentHealth==before+40 && actor.Graph.Count("medkit")==1,"Treatment finishes exactly once after closing");
        foreach(string code in new[]{"ration","water"})
        {
            health=Health();health.Energy=20;health.Hydration=20;health.Satiety=0;
            actor=Inventory(code);id=actor.Graph.Items.First(i=>i.Code==code).Id;version=actor.Graph.Version;
            Check(actor.BeginConsumable(health,id,version,float3.zero,10)==InventoryError.None,"Consumable begins: "+code);
            double deadline=actor.MedicalUntil;actor.CloseLoot();
            Check(actor.MedicalItemId==id && actor.MedicalIsConsumable && actor.MedicalUntil==deadline,"Closing preserves consumable: "+code);
            actor.RefreshMedical(ref health,true,float3.zero,deadline);actor.RefreshMedical(ref health,true,float3.zero,deadline+5);
            Check(actor.Graph.Count(code)==1 && (code=="ration" ? health.Energy>20 : health.Hydration>20),"Consumable commits once after closing: "+code);
        }
        actor=Inventory("medkit");actor.TacticalPending=true;version=actor.Graph.Version;actor.CloseLoot();
        Check(actor.TacticalPending && actor.Graph.Version==version,"Closing cannot cancel a tactical tool or mutate inventory");
        actor.CancelActions();Check(!actor.ActionPending,"Explicit cancellation still cancels self-use");
        actor=Inventory("medkit");health=Health();actor.BeginDissection(3,10,float3.zero,health,2);actor.CloseLoot();
        Check(actor.DissectingLootId<0 && actor.OpenedLootId<0 && !actor.SearchedLoot.Contains(3),"Closing still cancels incomplete search without unlocking it");
        actor=Inventory("medkit");health=Health();RaidHealth.Damage(ref health,BodyPart.RightArm,50,1);
        id=actor.Graph.Items.First(i=>i.Code=="medkit").Id;actor.BeginMedical(health,id,actor.Graph.Version,BodyPart.RightArm,float3.zero,10);actor.CloseLoot();
        actor.RefreshMedical(ref health,true,new float3(2,0,0),11);
        Check(!actor.ActionPending && actor.Graph.Count("medkit")==2,"Movement still interrupts treatment without consuming medicine");
        return $"PASS: {count} inventory-close / self-use checks.";
    }
}
