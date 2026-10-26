using Unity.MP_FPS.Inventory;

internal static class QuickMoveChecks
{
    public static void Run(Action<bool, string> check)
    {
        static InventoryItem Add(InventoryGraph graph, string parent, bool free = false, bool found = false, int quantity = 1, int charge = -1)
        {
            if (graph.AddSupply("cells", quantity, parent, foundInRaid: found, emergencySupply: free, cellCharge: charge) != InventoryError.None)
                throw new InvalidOperationException("Cannot build quick-transfer fixture.");
            return graph.Children(parent).Last(i => i.Code == "cells" && i.EmergencySupply == free && i.FoundInRaid == found);
        }
        static InventoryError Apply(InventoryGraph graph, InventoryCommand command)
        { command.ExpectedVersion = graph.Version; return graph.TryApply(command); }

        var graph = InventoryGraph.Create(starter: false);
        Add(graph, "pockets");
        var source = Add(graph, "stash", free: true, quantity: 2);
        var move = graph.PlanQuickMove(source.Id, "pockets");
        check(move != null && move.Operation == InventoryOperation.Move && move.Parent == "pockets",
            "free cells avoid an incompatible normal stack and use a free slot");
        check(Apply(graph, move!) == InventoryError.None && graph.Find(source.Id)?.EmergencySupply == true &&
            graph.Count("cells", true) == 3 && graph.Validate() == InventoryError.None,
            "planned move succeeds without changing provenance or quantities");

        graph = InventoryGraph.Create(starter: false);
        Add(graph, "pockets");
        var matching = Add(graph, "pockets", free: true);
        source = Add(graph, "stash", free: true, quantity: 2);
        move = graph.PlanQuickMove(source.Id, "pockets");
        check(move?.Operation == InventoryOperation.Merge && move.TargetId == matching.Id,
            "an incompatible first stack does not hide a later compatible one");
        check(Apply(graph, move!) == InventoryError.None && graph.Find(matching.Id)?.Quantity == 3 && graph.Find(source.Id) == null,
            "a compatible free stack merges using the normal transaction");

        graph = InventoryGraph.Create(starter: false);
        var partial = Add(graph, "pockets", free: true, charge: 50);
        source = Add(graph, "stash", free: true, charge: 20);
        move = graph.PlanQuickMove(source.Id, "pockets");
        check(move?.Operation == InventoryOperation.Move,
            "two partial cells cannot merge, so quick transfer chooses another slot");
        check(Apply(graph, move!) == InventoryError.None && graph.CellEnergy(carriedOnly: true) == 70 &&
            graph.Find(partial.Id)?.CellCharge == 50 && graph.Find(source.Id)?.CellCharge == 20,
            "separate partial cells keep their exact energy");

        graph = InventoryGraph.Create(starter: false);
        Add(graph, "pockets", found: true);
        source = Add(graph, "stash");
        move = graph.PlanQuickMove(source.Id, "pockets");
        check(move?.Operation == InventoryOperation.Move && Apply(graph, move) == InventoryError.None,
            "different found-in-raid origins use separate slots");

        graph = InventoryGraph.Create(starter: false);
        Add(graph, "pockets", quantity: 10);
        source = Add(graph, "stash", quantity: 3);
        move = graph.PlanQuickMove(source.Id, "pockets");
        check(move?.Operation == InventoryOperation.Move && Apply(graph, move) == InventoryError.None && graph.Count("cells", true) == 13,
            "an over-capacity stack falls back to a new slot");

        graph = InventoryGraph.Create(starter: false);
        if (graph.AddSupply("ration", 1, "pockets") != InventoryError.None ||
            graph.AddSupply("ration", 1, "stash", emergencySupply: true) != InventoryError.None)
            throw new InvalidOperationException("Food provenance fixture failed.");
        var freeRation = graph.Items.Single(i => i.Code == "ration" && i.EmergencySupply);
        move = graph.PlanQuickMove(freeRation.Id, "pockets");
        check(move?.Operation == InventoryOperation.Move && Apply(graph, move) == InventoryError.None &&
            graph.Find(freeRation.Id)?.EmergencySupply == true, "ordinary supplies also respect free-supply provenance");

        graph = InventoryGraph.Create(starter: false);
        Add(graph, "pockets");
        foreach (string code in new[] { "medkit", "ration", "oxygen_canister" })
            if (graph.AddSupply(code, 1, "pockets") != InventoryError.None) throw new InvalidOperationException("Full pocket fixture failed.");
        source = Add(graph, "stash", free: true);
        int version = graph.Version;
        check(graph.PlanQuickMove(source.Id, "pockets") == null && graph.Version == version && graph.Find(source.Id) != null,
            "a full destination with incompatible stacks reports no move without mutating inventory");
        var rig = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "rig", Parent = "equipment", Region = "ChestRig" };
        graph.Items.Add(rig);
        move = graph.PlanQuickMove(source.Id, rig.Id);
        check(move?.Operation == InventoryOperation.Move && Apply(graph, move) == InventoryError.None && graph.Find(source.Id)?.Parent == rig.Id,
            "after full pockets the caller can use a free chest-rig slot");
        check(graph.PlanQuickMove(Guid.NewGuid().ToString("D"), "pockets") == null,
            "a missing source cannot create a move");

        var carried = InventoryGraph.Create(stash: false, starter: false);
        Add(carried, "pockets");
        var cache = InventoryGraph.Create(stash: false, starter: false);
        cache.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
        source = Add(cache, LootInventoryExchange.Root, free: true);
        var joined = LootInventoryExchange.Snapshot(carried, cache);
        move = joined.PlanQuickMove(source.Id, "pockets");
        check(move?.Operation == InventoryOperation.Move, "a real shared-loot snapshot chooses a separate slot");
        move!.ExpectedVersion = carried.Version;
        check(LootInventoryExchange.TryApply(carried, cache, move, cache.Version) == InventoryError.None &&
            carried.Find(source.Id)?.EmergencySupply == true && cache.Find(source.Id) == null &&
            carried.Validate() == InventoryError.None && cache.Validate() == InventoryError.None,
            "the planned quick move succeeds across real loot owners without duplication");
    }
}
