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
    public static string CheckReopen()
    {
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var go=new UnityEngine.GameObject("Inventory interrupted receipt check");go.SetActive(false);
        var hud=go.AddComponent<RaidHUD>();
        using var world=new Unity.Entities.World("Inventory repeated interruption fixture");
        var entity=world.EntityManager.CreateEntity();
        var inventory=new RaidInventoryClientState {Graph=InventoryGraph.Create(false),RaidId=7,Error=InventoryError.Inaccessible,ActionFeedback="使用中断：受到攻击。 未消耗物品。"};
        world.EntityManager.AddComponentObject(entity,inventory);
        var query=world.EntityManager.CreateEntityQuery(typeof(RaidInventoryClientState));
        var root=new UnityEngine.UIElements.VisualElement();
        using var view=new Unity.MP_FPS.Client.ContainerInventoryView(root,false,_=>{});
        var healthView=new Unity.MP_FPS.Client.RaidHealthView(root,_=>{});
        void Set(string field,object value)=>typeof(RaidHUD).GetField(field,flags).SetValue(hud,value);
        bool Visible()=>(bool)typeof(RaidHUD).GetField("m_InventoryVisible",flags).GetValue(hud);
        var update=typeof(RaidHUD).GetMethod("UpdateInventoryView",flags);
        try
        {
            Set("m_World",world);Set("m_InventoryQuery",query);
            Set("m_Snapshot",new RaidSnapshotV2Rpc {RaidId=7,Phase=RaidPhase.Active});
            Set("m_InventoryView",view);Set("m_HealthView",healthView);
            Set("m_Inventory",root);Set("m_InventoryTitle",new UnityEngine.UIElements.Label());
            for(uint sequence=1;sequence<=3;sequence++)
            {
                inventory.Sequence=sequence;Set("m_InventoryVisible",true);
                update.Invoke(hud,null);
                if(!Visible())throw new InvalidOperationException("Repeated interrupted receipt closes the reopened carried inventory");
            }
            return "PASS: carried inventory stays open across 3 repeated interrupted receipts";
        }
        finally
        {
            Set("m_World",null);Set("m_InventoryView",null);Set("m_HealthView",null);
            query.Dispose();healthView.Dispose();UnityEngine.Object.DestroyImmediate(go);
        }
    }

}
