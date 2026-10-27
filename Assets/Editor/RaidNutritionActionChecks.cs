using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

// Exercises the same server-owned inventory action used during a raid.
public static class RaidNutritionActionChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    {
        s_Checks++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static RaidInventoryState Actor(string code, out PredictedPlayerGhost health, out string id)
    {
        var graph = InventoryGraph.Create(false);
        Check(graph.AddSupply(code, 2, graph.Equipped("Backpack").Id) == InventoryError.None, "Create carried supply in backpack");
        id = graph.Items.Find(item => item.Code == code).Id;
        health = new PredictedPlayerGhost();
        RaidHealth.Initialize(ref health);
        health.Energy = 40; health.Hydration = 40;
        return new RaidInventoryState { Graph = graph };
    }

    public static string Run()
    {
        s_Checks = 0;
        var origin = new float3(1, 2, 3);
        var actor = Actor("ration", out var health, out var id);
        Check(actor.BeginConsumable(health, id, actor.Graph.Version, origin, 10) == InventoryError.None, "Food starts");
        Check(actor.MedicalIsConsumable && actor.ActionPending && actor.Graph.Count("ration", true) == 2 && health.Energy == 40, "Start must not consume or restore");
        actor.RefreshMedical(ref health, true, origin, 12.99);
        Check(actor.ActionPending && actor.Graph.Count("ration", true) == 2 && health.Energy == 40, "Food cannot complete early");
        actor.RefreshMedical(ref health, true, origin, 13);
        Check(!actor.ActionPending && !actor.MedicalIsConsumable && actor.Graph.Count("ration", true) == 1 && health.Energy == 85 && health.Hydration == 35, "Food completes once");
        actor.RefreshMedical(ref health, true, origin, 20);
        Check(actor.Graph.Count("ration", true) == 1 && health.Energy == 85, "Repeated refresh cannot consume twice");

        actor = Actor("water", out health, out id);
        Check(actor.BeginConsumable(health, id, actor.Graph.Version, origin, 30) == InventoryError.None, "Drink starts");
        actor.RefreshMedical(ref health, true, origin, 31.99);
        Check(health.Hydration == 40 && actor.Graph.Count("water", true) == 2, "Drink cannot complete early");
        actor.RefreshMedical(ref health, true, origin, 32);
        Check(health.Hydration == 90 && actor.Graph.Count("water", true) == 1, "Drink completes once");

        foreach(string supply in new[] {"ration","water"})
        {
            actor=Actor(supply,out health,out id);
            health.Energy=supply=="ration" ? 0 : 100;health.Hydration=supply=="water" ? 0 : 100;
            float initialHealth=health.CurrentHealth;
            Check(actor.BeginConsumable(health,id,actor.Graph.Version,origin,0)==InventoryError.None,"Critical nutrition action starts: "+supply);
            int steps=supply=="ration" ? 6 : 4;
            for(uint tick=1;tick<=steps;tick++)
            {
                RaidNutrition.Tick(ref health,.5f,1000,1000,300,0,2,2,tick);
                actor.RefreshMedical(ref health,true,origin,tick*.5);
                if(tick<steps)Check(actor.ActionPending,"Metabolic damage interrupted lifesaving supply: "+supply);
            }
            Check(!actor.ActionPending && actor.Error==InventoryError.None && actor.Graph.Count(supply,true)==1 &&
                health.CurrentHealth<initialHealth && (supply=="ration" ? health.Energy>0 : health.Hydration>0),
                "Supply completes under ongoing metabolic damage: "+supply);
        }
        actor=Actor("ration",out health,out id);health.Energy=0;
        Check(actor.BeginConsumable(health,id,actor.Graph.Version,origin,0)==InventoryError.None,"Same-tick attack setup");
        RaidHealth.CombatDamage(ref health,BodyPart.RightArm,5,42,RaidDamageKind.Bullet);
        RaidNutrition.Tick(ref health,.5f,1000,1000,300,0,2,0,42);
        actor.RefreshMedical(ref health,true,origin,.5);
        Check(health.LastHitTick==42 && !actor.ActionPending && actor.Error==InventoryError.Inaccessible && actor.Graph.Count("ration",true)==2,
            "Metabolic damage in the same tick cannot hide a bullet");
        actor=Actor("ration",out health,out id);health.Energy=0;health.ShieldEquipped=true;health.ShieldCharge=100;
        Check(actor.BeginConsumable(health,id,actor.Graph.Version,origin,0)==InventoryError.None,"Same-tick shield setup");
        EnergyShieldRules.Absorb(ref health,5,43,RaidDamageKind.Bullet,new float3(1,1,1));
        RaidNutrition.Tick(ref health,.5f,1000,1000,300,0,2,0,43);
        actor.RefreshMedical(ref health,true,origin,.5);
        Check(!actor.ActionPending && actor.Error==InventoryError.Inaccessible && actor.Graph.Count("ration",true)==2,
            "Metabolic damage cannot hide a shield hit");

        actor = Actor("ration", out health, out id);
        int version = actor.Graph.Version;
        Check(actor.BeginConsumable(health, id, version - 1, origin, 0) == InventoryError.Stale && !actor.ActionPending, "Stale supply is rejected");
        health.ControllerState.IsReloadingState = true;
        Check(actor.BeginConsumable(health, id, version, origin, 0) == InventoryError.Inaccessible && !actor.ActionPending && actor.Graph.Count("ration", true) == 2,
            "Already reloading cannot start a food action");
        health.ControllerState.IsReloadingState = false; health.WeaponEquipRemaining = .2f;
        Check(actor.BeginConsumable(health, id, version, origin, 0) == InventoryError.Inaccessible && !actor.ActionPending,
            "Weapon switch cannot start a food action");
        health.WeaponEquipRemaining = 0;
        health.Energy = 100;
        Check(actor.BeginConsumable(health, id, version, origin, 0) == InventoryError.Invalid && actor.Graph.Count("ration", true) == 2, "Full energy does not consume food");
        health.Energy = 40;
        Check(actor.BeginConsumable(health, id, version, origin, 0) == InventoryError.None, "Cancellation setup");
        actor.CancelActions("已取消操作；未消耗物品或获得收益。");
        actor.RefreshMedical(ref health, true, origin, 10);
        Check(!actor.ActionPending && !actor.MedicalIsConsumable && actor.Graph.Count("ration", true) == 2 && health.Energy == 40, "Cancelled food remains carried");

        for (int interruption = 0; interruption < 11; interruption++)
        {
            actor = Actor("ration", out health, out id);
            Check(actor.BeginConsumable(health, id, actor.Graph.Version, origin, 0) == InventoryError.None, "Interruption setup");
            var position = origin; bool accessible = true;
            switch (interruption)
            {
                case 0: position.x += .76f; break;
                case 1: RaidHealth.Damage(ref health,BodyPart.RightArm,1,health.LastHitTick+1); break;
                case 2: health.ShieldHitSequence++; break;
                case 3: health.LastShotTick++; break;
                case 4: health.LastGrenadeShotTick++; break;
                case 5: health.LastReloadTick++; break;
                case 6: actor.Graph.Version++; break;
                case 7: accessible = false; break;
                case 8: health.EquippedWeaponID++; break;
                case 9: health.ControllerState.IsReloadingState = true; break;
                case 10: health.WeaponEquipRemaining = .2f; break;
            }
            actor.RefreshMedical(ref health, accessible, position, 3);
            Check(!actor.ActionPending && actor.Error != InventoryError.None && actor.Graph.Count("ration", true) == 2 && health.Energy == 40,
                "Interruption must not grant or consume food: " + interruption);
        }

        actor = Actor("ration", out health, out id);
        Check(actor.Graph.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None, "Create medicine");
        var med = actor.Graph.Items.Find(item => item.Code == "medkit");
        RaidHealth.Set(ref health, BodyPart.LeftArm, 20);
        health.ControllerState.IsReloadingState = true;
        Check(actor.BeginMedical(health, med.Id, actor.Graph.Version, BodyPart.LeftArm, origin, 0) == InventoryError.Inaccessible && !actor.ActionPending,
            "Already reloading cannot start treatment");
        health.ControllerState.IsReloadingState = false;
        Check(actor.BeginMedical(health, med.Id, actor.Graph.Version, BodyPart.LeftArm, origin, 0) == InventoryError.None, "Treatment starts");
        Check(actor.BeginConsumable(health, id, actor.Graph.Version, origin, 0) == InventoryError.Inaccessible && actor.MedicalItemId == med.Id,
            "Food cannot replace an active treatment");
        actor.RefreshMedical(ref health, true, origin, 3);
        Check(RaidHealth.Get(health, BodyPart.LeftArm) == 60 && actor.Graph.Count("medkit", true) == 0, "Existing medical action still completes");

        actor=Actor("ration",out health,out id);
        Check(actor.Graph.AddSupply("medkit",1,InventoryCatalog.Pockets)==InventoryError.None,"same-tick medical fixture");
        med=actor.Graph.Items.Find(item=>item.Code=="medkit");
        RaidHealth.Set(ref health,BodyPart.LeftArm,20);health.LastHitTick=42;
        Check(actor.BeginMedical(health,med.Id,actor.Graph.Version,BodyPart.LeftArm,origin,0)==InventoryError.None,"same-tick medical starts");
        RaidHealth.Damage(ref health,BodyPart.RightArm,5,42);
        RaidHealth.Set(ref health,BodyPart.RightArm,60);
        actor.RefreshMedical(ref health,true,origin,3);
        Check(!actor.ActionPending && actor.Error==InventoryError.Inaccessible && actor.Graph.Find(med.Id)!=null &&
            RaidHealth.Get(health,BodyPart.LeftArm)==20,"same-tick hit and recovery cannot hide medical interruption");

        CheckMedicalUnderSurvivalDamage(origin);

        actor = Actor("ration", out health, out id);
        Check(actor.BeginConsumable(health, id, actor.Graph.Version, origin, 0) == InventoryError.None, "Revalidation setup");
        health.Energy = 100;
        actor.RefreshMedical(ref health, true, origin, 3);
        Check(actor.Error == InventoryError.Invalid && actor.Graph.Count("ration", true) == 2, "Completion revalidates need before consumption");

        string json = RaidInventoryTransport.EncodeSnapshot(actor.Graph, -1, 0, medicalItemId: id, medicalRemaining: 1,
            medicalIsConsumable: true);
        var snapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<RaidInventorySnapshot>(json);
        Check(snapshot.MedicalIsConsumable && snapshot.MedicalItemId == id && snapshot.MedicalRemaining == 1, "Owner snapshot preserves food action");
        var client = new RaidInventoryClientState();
        int chunks = (json.Length + RaidInventoryTransport.ChunkCharacters - 1) / RaidInventoryTransport.ChunkCharacters;
        for (int i = 0; i < chunks; i++)
        {
            int start = i * RaidInventoryTransport.ChunkCharacters;
            client.Receive(new RaidInventoryChunkV2Rpc { RaidId = 1, Sequence = 1, RequestId = 1, Index = i, Count = chunks,
                Json = json.Substring(start, Math.Min(RaidInventoryTransport.ChunkCharacters, json.Length - start)) }, RaidInventoryTransport.Decode);
        }
        Check(client.MedicalIsConsumable && client.MedicalItemId == id && client.MedicalRemaining == 1, "Client receives the nutrition action through inventory fragments");
        var old = Newtonsoft.Json.JsonConvert.DeserializeObject<RaidInventorySnapshot>("{}");
        Check(!old.MedicalIsConsumable, "Older snapshots default to medical behavior");
        return "PASS " + s_Checks + " nutrition action checks";
    }

    private static void CheckMedicalUnderSurvivalDamage(float3 origin)
    {
        foreach (int condition in new[] { 1, 2, 4, 7 })
        {
            var actor = Actor("ration", out var health, out _);
            Check(actor.Graph.AddSupply("medkit", 2, InventoryCatalog.Pockets) == InventoryError.None,
                "survival treatment medicine fits");
            var med = actor.Graph.Items.Find(item => item.Code == "medkit");
            RaidHealth.Set(ref health, BodyPart.LeftLeg, 20);
            health.Energy = (condition & 1) != 0 ? 0 : 100;
            health.Hydration = (condition & 2) != 0 ? 0 : 100;
            health.HeadDomeEquipped = true;
            health.Oxygen = (condition & 4) != 0 ? 0 : 100;
            Check(actor.BeginMedical(health, med.Id, actor.Graph.Version, BodyPart.LeftLeg, origin, 0) == InventoryError.None,
                "treatment starts during survival damage " + condition);
            float afterDamage = health.CurrentHealth;
            for (uint tick = 1; tick <= 6; tick++)
            {
                float before = health.CurrentHealth;
                RaidNutrition.Tick(ref health, .5f, 1000, 1000, 300, 0, 2, 2, tick);
                if ((condition & 4) != 0) RaidHealth.TickOxygen(ref health, false, .5f, 600, 0, 0, 3, tick);
                afterDamage = health.CurrentHealth;
                Check(afterDamage < before, "survival damage continues during treatment " + condition);
                actor.RefreshMedical(ref health, health.CurrentHealth > 0, origin, tick * .5);
                if (tick < 6) Check(actor.ActionPending && med.Quantity == 2,
                    "survival damage does not interrupt treatment " + condition);
            }
            Check(actor.Error == InventoryError.None && !actor.ActionPending && med.Quantity == 1 &&
                health.LeftLegHealth > 50 && health.CurrentHealth > afterDamage,
                "treatment consumes one medicine and restores the injured limb " + condition);
            float healed = health.CurrentHealth;
            actor.RefreshMedical(ref health, true, origin, 4);
            Check(med.Quantity == 1 && health.CurrentHealth == healed, "completed treatment cannot repeat " + condition);
        }

        foreach (bool shielded in new[] { false, true })
        {
            var actor = Actor("ration", out var health, out _);
            Check(actor.Graph.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None,
                "same-tick survival attack fixture");
            var med = actor.Graph.Items.Find(item => item.Code == "medkit");
            RaidHealth.Set(ref health, BodyPart.LeftLeg, 20);
            health.Energy = 0;
            health.ShieldEquipped = shielded;
            health.ShieldCharge = shielded ? 100 : 0;
            Check(actor.BeginMedical(health, med.Id, actor.Graph.Version, BodyPart.LeftLeg, origin, 0) == InventoryError.None,
                "same-tick survival treatment starts");
            RaidHealth.CombatDamage(ref health, BodyPart.RightArm, 5, 42, RaidDamageKind.Bullet);
            RaidNutrition.Tick(ref health, .5f, 1000, 1000, 300, 0, 2, 0, 42);
            actor.RefreshMedical(ref health, true, origin, .5);
            Check(!actor.ActionPending && actor.Error == InventoryError.Inaccessible && actor.Graph.Find(med.Id) != null,
                "survival damage cannot hide a " + (shielded ? "shield hit" : "body hit"));
        }

        foreach (var kind in new[] { RaidDamageKind.Fire, RaidDamageKind.Poison, RaidDamageKind.Bleed })
        {
            var actor = Actor("ration", out var health, out _);
            Check(actor.Graph.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None,
                "status damage treatment fixture");
            var med = actor.Graph.Items.Find(item => item.Code == "medkit");
            RaidHealth.Set(ref health, BodyPart.LeftLeg, 20);
            Check(actor.BeginMedical(health, med.Id, actor.Graph.Version, BodyPart.LeftLeg, origin, 0) == InventoryError.None,
                "treatment starts before status damage " + kind);
            RaidHealth.CombatDamage(ref health, BodyPart.Auto, 2, 43, kind);
            actor.RefreshMedical(ref health, true, origin, .5);
            Check(!actor.ActionPending && actor.Error == InventoryError.Inaccessible && actor.Graph.Find(med.Id) != null,
                "harmful status still interrupts ordinary medical treatment " + kind);
        }

        var dying = Actor("ration", out var fatal, out _);
        Check(dying.Graph.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None,
            "fatal survival treatment fixture");
        var medicine = dying.Graph.Items.Find(item => item.Code == "medkit");
        RaidHealth.Set(ref fatal, BodyPart.Chest, 1);
        fatal.HeadDomeEquipped = true; fatal.Oxygen = 0;
        Check(dying.BeginMedical(fatal, medicine.Id, dying.Graph.Version, BodyPart.Chest, origin, 0) == InventoryError.None,
            "fatal survival treatment starts");
        RaidHealth.TickOxygen(ref fatal, false, .5f, 600, 0, 0, 3, 50);
        dying.RefreshMedical(ref fatal, fatal.CurrentHealth > 0, origin, .5);
        Check(fatal.CurrentHealth == 0 && dying.Error == InventoryError.Inaccessible && dying.Graph.Find(medicine.Id) != null,
            "fatal hypoxia cancels treatment without consuming medicine");
    }
}
