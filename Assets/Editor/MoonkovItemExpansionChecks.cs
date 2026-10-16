using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class MoonkovItemExpansionChecks
{
    private static readonly string[] Supplies =
        { "med_gel", "trauma_kit", "energy_gel", "protein_bar", "electrolyte", "oxygen_reserve" };
    private static readonly string[] Containers =
        { "scout_rig", "field_rig", "sling_pack", "cargo_pack" };
    private static readonly string[] Materials =
        { "circuit_board", "copper_coil", "ceramic_plate", "optical_sensor", "data_drive", "helium3_sample", "quantum_core", "black_box" };

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Item expansion: " + message); }

    private static PredictedPlayerGhost Player()
    { var state = default(PredictedPlayerGhost); RaidHealth.Initialize(ref state); return state; }

    private static ShopCommand Trade(InventoryGraph graph, string code, ShopOperation operation, string itemId = null) =>
        new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = graph.Version,
            Code = code, Operation = operation, ItemId = itemId, Quantity = 1 };

    private static InventoryCommand Move(InventoryGraph graph, string id, string parent, string region, int x = 0, int y = 0) =>
        new InventoryCommand { ExpectedVersion = graph.Version, ItemId = id, Parent = parent, Region = region, X = x, Y = y };

    [MenuItem("Tools/Moonkov/Check Item Expansion")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run item expansion checks in Edit mode.");
        Medical();
        Nutrition();
        Oxygen();
        ContainerTrades();
        MaterialTrades();
        Loot();
        Debug.Log("Item expansion checks passed: 18 items, medical/nutrition/oxygen use and replay rejection, 4 container purchases/equipment/nested saves, 8 sell-only materials, and 512 fixed-seed loot layouts including cargo packs.");
    }

    private static void Medical()
    {
        foreach (string code in new[] { "med_gel", "trauma_kit" })
        {
            var graph = InventoryGraph.Create(false);
            Require(graph.AddSupply(code, 2, graph.Equipped("ChestRig").Id) == InventoryError.None &&
                graph.AddSupply(code, 1, graph.Equipped("Backpack").Id) == InventoryError.None, code + " medical fixture");
            var item = graph.Items.Single(i => i.Code == code && i.Parent == graph.Equipped("ChestRig").Id);
            var packed = graph.Items.Single(i => i.Code == code && i.Parent == graph.Equipped("Backpack").Id);
            string before = JsonUtility.ToJson(graph);
            Require(graph.UseMedical(packed.Id, graph.Version, 10, 100, out _) == InventoryError.Inaccessible &&
                JsonUtility.ToJson(graph) == before, code + " must not be usable inside backpack");
            int version = graph.Version;
            float restore = code == "med_gel" ? 20 : 70;
            Require(graph.UseMedical(item.Id, version, 10, 100, out float healed) == InventoryError.None &&
                healed == 10 + restore && item.Quantity == 1 && graph.Version == version + 1, code + " exact recovery");
            before = JsonUtility.ToJson(graph);
            Require(graph.UseMedical(item.Id, version, healed, 100, out _) == InventoryError.Stale &&
                JsonUtility.ToJson(graph) == before, code + " stale replay must not consume twice");
            Require(graph.UseMedical(item.Id, graph.Version, 90, 100, out healed) == InventoryError.None &&
                healed == 100 && graph.Find(item.Id) == null, code + " recovery must cap at maximum");
        }
    }

    private static void Nutrition()
    {
        var cases = new[]
        {
            (Code: "energy_gel", Energy: 65f, Hydration: 48f, Satiety: 20f),
            (Code: "protein_bar", Energy: 75f, Hydration: 42f, Satiety: 30f),
            (Code: "electrolyte", Energy: 40f, Hydration: 85f, Satiety: 15f)
        };
        foreach (var expected in cases)
        {
            var graph = InventoryGraph.Create(false);
            Require(graph.AddSupply(expected.Code, 2, graph.Equipped("Backpack").Id) == InventoryError.None,
                expected.Code + " nutrition fixture");
            var item = graph.Items.Single(i => i.Code == expected.Code);
            var state = Player(); state.Energy = 40; state.Hydration = 50; state.Satiety = 10;
            int version = graph.Version;
            Require(RaidNutrition.Consume(ref state, graph, item.Id, version) == InventoryError.None &&
                state.Energy == expected.Energy && state.Hydration == expected.Hydration && state.Satiety == expected.Satiety &&
                item.Quantity == 1 && graph.Version == version + 1, expected.Code + " actual nutrition transaction");
            var before = state; string inventory = JsonUtility.ToJson(graph);
            Require(RaidNutrition.Consume(ref state, graph, item.Id, version) == InventoryError.Stale &&
                state.Equals(before) && JsonUtility.ToJson(graph) == inventory, expected.Code + " stale replay");
        }
    }

    private static void Oxygen()
    {
        var graph = InventoryGraph.Create(false);
        Require(graph.AddSupply("oxygen_reserve", 2, graph.Equipped("ChestRig").Id) == InventoryError.None &&
            graph.AddSupply("oxygen_reserve", 1, graph.Equipped("Backpack").Id) == InventoryError.None, "oxygen reserve fixture");
        var accessible = graph.Items.Single(i => i.Code == "oxygen_reserve" && i.Parent == graph.Equipped("ChestRig").Id);
        var packed = graph.Items.Single(i => i.Code == "oxygen_reserve" && i.Parent == graph.Equipped("Backpack").Id);
        var state = Player(); state.Oxygen = 10; state.HypoxiaSeconds = 3;
        var before = state; string inventory = JsonUtility.ToJson(graph);
        Require(RaidOxygenConsumables.Consume(ref state, graph, packed.Id, graph.Version) == InventoryError.Inaccessible &&
            state.Equals(before) && JsonUtility.ToJson(graph) == inventory, "backpack oxygen must remain inaccessible");
        int version = graph.Version;
        Require(RaidOxygenConsumables.Consume(ref state, graph, accessible.Id, version) == InventoryError.None &&
            state.Oxygen == 90 && state.HypoxiaSeconds == 0 && accessible.Quantity == 1 && graph.Version == version + 1,
            "reserve must restore 80 oxygen and consume once");
        before = state; inventory = JsonUtility.ToJson(graph);
        Require(RaidOxygenConsumables.Consume(ref state, graph, accessible.Id, version) == InventoryError.Stale &&
            state.Equals(before) && JsonUtility.ToJson(graph) == inventory, "oxygen stale replay");
        state.Oxygen = 95;
        Require(RaidOxygenConsumables.Consume(ref state, graph, accessible.Id, graph.Version) == InventoryError.None &&
            state.Oxygen == 100 && graph.Find(accessible.Id) == null, "reserve oxygen must cap at 100");
    }

    private static void ContainerTrades()
    {
        foreach (string code in Containers)
        {
            var graph = InventoryGraph.Create(true, false);
            var offer = ShopCatalog.Find(code); var definition = InventoryCatalog.Get(code);
            Require(offer != null && offer.BuyDust > 0 && definition.Container, code + " paid container offer");
            Require(graph.AddSupply("dust", offer.BuyDust + 3, "stash") == InventoryError.None, code + " purchase funding");
            Require(ShopRules.Apply(graph, Trade(graph, code, ShopOperation.Buy), out var bought) == null &&
                bought.Count("dust") == 3 && bought.Count(code) == 1, code + " purchase and debit");
            graph = bought;
            var item = graph.Items.Single(i => i.Code == code);
            string slot = definition.Kind == ItemKind.Rig ? "ChestRig" : "Backpack";
            Require(graph.TryApply(Move(graph, item.Id, "equipment", slot)) == InventoryError.None &&
                graph.Equipped(slot)?.Id == item.Id, code + " equip purchased container");
            Require(graph.AddSupply("cells", 2, item.Id, foundInRaid: true, cellCharge: 37) == InventoryError.None,
                code + " nested storage");
            var payload = graph.Children(item.Id).Single();
            var restored = JsonUtility.FromJson<InventoryGraph>(JsonUtility.ToJson(graph));
            var savedPayload = restored.Find(payload.Id);
            Require(restored.Validate() == InventoryError.None && restored.Version == graph.Version &&
                restored.Equipped(slot)?.Id == item.Id && savedPayload != null && savedPayload.Parent == item.Id &&
                savedPayload.Region == payload.Region && savedPayload.X == payload.X && savedPayload.Y == payload.Y &&
                savedPayload.Code == "cells" && savedPayload.Quantity == 2 && savedPayload.CellCharge == 37 && savedPayload.FoundInRaid,
                code + " JSON round trip must preserve equipment and nested item state");
            graph = restored;
            if (definition.Kind == ItemKind.Rig)
            {
                var bag = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "sling_pack" };
                Require(graph.FindSpace(bag, "stash", out var bagRegion, out var bagX, out var bagY), "rig rejection fixture");
                bag.Parent = "stash"; bag.Region = bagRegion; bag.X = bagX; bag.Y = bagY; graph.Items.Add(bag);
                string inventory = JsonUtility.ToJson(graph);
                Require(graph.TryApply(Move(graph, bag.Id, item.Id, definition.Regions[0].Id)) == InventoryError.Incompatible &&
                    JsonUtility.ToJson(graph) == inventory, code + " must reject nested bags");
            }
            Require(graph.FindSpace(graph.Find(item.Id), "stash", out var region, out var x, out var y) &&
                graph.TryApply(Move(graph, item.Id, "stash", region, x, y)) == InventoryError.None, code + " return to stash");
            string before = JsonUtility.ToJson(graph);
            Require(ShopRules.Apply(graph, Trade(graph, code, ShopOperation.Sell, item.Id), out var rejected) == "shop_remove_modules_first" &&
                rejected == null && JsonUtility.ToJson(graph) == before, code + " filled container must not be sold");
        }
    }

    private static void MaterialTrades()
    {
        foreach (string code in Materials)
        {
            var graph = InventoryGraph.Create(true, false);
            var offer = ShopCatalog.Find(code);
            Require(InventoryCatalog.Get(code)?.Kind == ItemKind.Material && offer != null && offer.BuyDust == -1 && offer.SellDust > 0,
                code + " sell-only material offer");
            Require(graph.AddSupply(code, 1, "stash", foundInRaid: true) == InventoryError.None &&
                graph.AddSupply("dust", 3, "stash") == InventoryError.None, code + " sale fixture");
            string before = JsonUtility.ToJson(graph);
            Require(ShopRules.Apply(graph, Trade(graph, code, ShopOperation.Buy), out var rejected) == "shop_invalid" &&
                rejected == null && JsonUtility.ToJson(graph) == before, code + " must reject purchases without mutation");
            var item = graph.Items.Single(i => i.Code == code);
            Require(ShopRules.Apply(graph, Trade(graph, code, ShopOperation.Sell, item.Id), out var sold) == null &&
                sold.Find(item.Id) == null && sold.Count("dust") == 3 + offer.SellDust && sold.Validate() == InventoryError.None,
                code + " sale must pay its authored value");
        }
    }

    private static void Loot()
    {
        var found = new HashSet<string>();
        foreach (RaidLootProfile profile in Enum.GetValues(typeof(RaidLootProfile)))
        {
            for (int seed = 0; seed < 128; seed++)
            {
                var graph = RaidLootTable.Create(profile, new System.Random((int)profile * 1000 + seed));
                Require(graph.Validate() == InventoryError.None && graph.LootRows == 5, profile + " seeded cache must fit standard bounds");
                bool guaranteed = profile == RaidLootProfile.Supplies ? graph.Count("ration") >= 1 && graph.Count("water") >= 1 :
                    profile == RaidLootProfile.Medical ? graph.Count("medkit") >= 2 :
                    profile == RaidLootProfile.Industrial ? graph.Count("alloy") >= 2 && graph.Count("dust") >= 3 : graph.Count("cells") >= 2;
                Require(guaranteed, profile + " must preserve guaranteed supplies");
                int area = 0;
                foreach (var item in graph.Items.Where(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root))
                {
                    var definition = InventoryCatalog.Get(item.Code);
                    Require(item.FoundInRaid && item.Parent == LootInventoryExchange.Root, profile + " loot ownership/provenance");
                    area += definition.Width * definition.Height;
                    found.Add(item.Code);
                }
                Require(area <= 6 * 5, profile + " loot footprints must fit cache capacity");
            }
        }
        foreach (string code in Supplies.Concat(Containers).Concat(Materials))
            Require(found.Contains(code), code + " must be reachable in fixed-seed loot samples");
    }
}
