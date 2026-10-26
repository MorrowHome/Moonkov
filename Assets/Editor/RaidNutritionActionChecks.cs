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

        actor = Actor("ration", out health, out id);
        int version = actor.Graph.Version;
        Check(actor.BeginConsumable(health, id, version - 1, origin, 0) == InventoryError.Stale && !actor.ActionPending, "Stale supply is rejected");
        health.Energy = 100;
        Check(actor.BeginConsumable(health, id, version, origin, 0) == InventoryError.Invalid && actor.Graph.Count("ration", true) == 2, "Full energy does not consume food");
        health.Energy = 40;
        Check(actor.BeginConsumable(health, id, version, origin, 0) == InventoryError.None, "Cancellation setup");
        actor.CancelActions("已取消操作；未消耗物品或获得收益。");
        actor.RefreshMedical(ref health, true, origin, 10);
        Check(!actor.ActionPending && !actor.MedicalIsConsumable && actor.Graph.Count("ration", true) == 2 && health.Energy == 40, "Cancelled food remains carried");

        for (int interruption = 0; interruption < 9; interruption++)
        {
            actor = Actor("ration", out health, out id);
            Check(actor.BeginConsumable(health, id, actor.Graph.Version, origin, 0) == InventoryError.None, "Interruption setup");
            var position = origin; bool accessible = true;
            switch (interruption)
            {
                case 0: position.x += .76f; break;
                case 1: health.LastHitTick++; break;
                case 2: health.ShieldHitSequence++; break;
                case 3: health.LastShotTick++; break;
                case 4: health.LastGrenadeShotTick++; break;
                case 5: health.LastReloadTick++; break;
                case 6: actor.Graph.Version++; break;
                case 7: accessible = false; break;
                case 8: health.EquippedWeaponID++; break;
            }
            actor.RefreshMedical(ref health, accessible, position, 3);
            Check(!actor.ActionPending && actor.Error != InventoryError.None && actor.Graph.Count("ration", true) == 2 && health.Energy == 40,
                "Interruption must not grant or consume food: " + interruption);
        }

        actor = Actor("ration", out health, out id);
        Check(actor.Graph.AddSupply("medkit", 1, InventoryCatalog.Pockets) == InventoryError.None, "Create medicine");
        var med = actor.Graph.Items.Find(item => item.Code == "medkit");
        RaidHealth.Set(ref health, BodyPart.LeftArm, 20);
        Check(actor.BeginMedical(health, med.Id, actor.Graph.Version, BodyPart.LeftArm, origin, 0) == InventoryError.None, "Treatment starts");
        Check(actor.BeginConsumable(health, id, actor.Graph.Version, origin, 0) == InventoryError.Inaccessible && actor.MedicalItemId == med.Id,
            "Food cannot replace an active treatment");
        actor.RefreshMedical(ref health, true, origin, 3);
        Check(RaidHealth.Get(health, BodyPart.LeftArm) == 60 && actor.Graph.Count("medkit", true) == 0, "Existing medical action still completes");

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
}
