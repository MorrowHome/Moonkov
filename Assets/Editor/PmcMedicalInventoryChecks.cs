using System;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class PmcMedicalInventoryChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static InventoryItem Supply(string code, string parent, string region, int x = 0, int y = 0) =>
        new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code, Parent = parent, Region = region, X = x, Y = y };

    public static string Run()
    {
        s_Checks = 0;
        var graph = InventoryGraph.Create(false); var pack = graph.Equipped("Backpack");
        Check(graph.AddSupply("trauma_kit", 1, pack.Id, true) == InventoryError.None, "backpack medical fixture");
        string itemId = graph.Items.Single(i => i.Code == "trauma_kit").Id; int beforeVersion = graph.Version;
        Check(PmcMedicalInventory.HasCarried(graph) && PmcMedicalInventory.FindAccessible(graph) == null,
            "backpack medicine is carried but not directly accessible");
        var ready = PmcMedicalInventory.Prepare(graph);
        Check(ready != null && ready.Id == itemId && graph.MedicalAccessible(ready) && graph.Version == beforeVersion + 1 &&
            graph.Validate() == InventoryError.None, "real inventory movement prepares the same medicine once");
        int preparedVersion = graph.Version;
        Check(PmcMedicalInventory.Prepare(graph).Id == itemId && graph.Version == preparedVersion,
            "an already accessible item is not moved again");
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        RaidHealth.Damage(ref health, BodyPart.Chest, 30, 1);
        Check(RaidHealth.UseMedical(ref health, graph, itemId, graph.Version, BodyPart.Chest) == InventoryError.None &&
            health.ChestHealth == RaidHealth.Maximum(BodyPart.Chest) && graph.Find(itemId) == null,
            "prepared medicine can be consumed by the actual healing transaction");

        graph = InventoryGraph.Create(false); pack = graph.Equipped("Backpack"); string rig = graph.Equipped("ChestRig").Id;
        Check(graph.AddSupply("med_gel", 1, pack.Id, true) == InventoryError.None, "blocked medical fixture");
        for (int n = 1; n <= 4; n++) graph.Items.Add(Supply("dust", InventoryCatalog.Pockets, n.ToString()));
        foreach (string region in new[] { "left", "center", "right" })
            for (int y = 0; y < 2; y++) for (int x = 0; x < (region == "center" ? 2 : 1); x++)
                graph.Items.Add(Supply("dust", rig, region, x, y));
        Check(graph.Validate() == InventoryError.None, "occupied access slots remain a valid inventory");
        string snapshot = RaidInventoryTransport.Encode(graph); beforeVersion = graph.Version;
        Check(PmcMedicalInventory.Prepare(graph) == null && graph.Version == beforeVersion && RaidInventoryTransport.Encode(graph) == snapshot,
            "no free access slot leaves the medicine and every other item unchanged");
        var failure = new PmcMedicalFailure(); failure.Record(graph.Version);
        Check(failure.Matches(graph.Version) && !failure.Matches(graph.Version + 1), "the same blocked inventory is not repeatedly retried");
        var carried = graph.Items.First(i => i.Parent == InventoryCatalog.Pockets);
        Check(graph.FindSpace(carried, pack.Id, out string regionName, out int xPosition, out int yPosition) &&
            graph.TryApply(new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = graph.Version,
                ItemId = carried.Id, Parent = pack.Id, Region = regionName, X = xPosition, Y = yPosition }) == InventoryError.None,
            "a real inventory change frees a pocket");
        Check(!failure.Matches(graph.Version) && PmcMedicalInventory.Prepare(graph)?.Code == "med_gel",
            "a changed inventory permits a fresh preparation attempt");
        failure.Clear(); Check(!failure.Matches(graph.Version), "success clears old access failures");

        var stored = InventoryGraph.Create(true, false);
        Check(stored.AddSupply("medkit", 1, InventoryCatalog.Stash) == InventoryError.None &&
            !PmcMedicalInventory.HasCarried(stored) && PmcMedicalInventory.Prepare(stored) == null,
            "medicine in storage is not usable during a raid");
        Check(PmcMedicalInventory.FindAccessible(null) == null && PmcMedicalInventory.Prepare(null) == null &&
            !PmcMedicalInventory.HasCarried(null), "missing inventory is safe");

        graph = InventoryGraph.Create(false, false);
        pack = Supply("backpack", InventoryCatalog.Equipment, "Backpack");
        var chest = Supply("rig", InventoryCatalog.Equipment, "ChestRig"); graph.Items.Add(pack); graph.Items.Add(chest);
        for (int n = 1; n <= 4; n++) graph.Items.Add(Supply("dust", InventoryCatalog.Pockets, n.ToString()));
        foreach (string side in new[] { "left", "right" }) for (int y = 0; y < 2; y++) graph.Items.Add(Supply("dust", chest.Id, side, 0, y));
        graph.Items.Add(Supply("dust", chest.Id, "center", 0, 0)); graph.Items.Add(Supply("dust", chest.Id, "center", 1, 0));
        Check(graph.AddSupply("trauma_kit", 1, pack.Id, true) == InventoryError.None && graph.Validate() == InventoryError.None,
            "horizontal-only rig fixture");
        ready = PmcMedicalInventory.Prepare(graph);
        Check(ready != null && ready.Code == "trauma_kit" && ready.Parent == chest.Id && ready.Rotated && graph.Validate() == InventoryError.None,
            "a medical kit rotates into the only usable rig space");

        graph = InventoryGraph.Create(false); pack = graph.Equipped("Backpack");
        var nested = Supply("small_pack", pack.Id, "main"); graph.Items.Add(nested);
        Check(graph.AddSupply("medkit", 1, nested.Id, true) == InventoryError.None && graph.Validate() == InventoryError.None,
            "nested carried medicine fixture");
        ready = PmcMedicalInventory.Prepare(graph);
        Check(ready != null && ready.Code == "medkit" && graph.MedicalAccessible(ready) && graph.Validate() == InventoryError.None,
            "medicine inside a carried nested bag can be moved to usable storage");
        return "PASS " + s_Checks + " PMC backpack medicine checks";
    }
}
