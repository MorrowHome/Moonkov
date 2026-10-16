using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

/// <summary>Focused deterministic checks; no saved scene or player state is changed.</summary>
public static class MoonkovOxygenChecks
{
    private static int s_Passed;
    private static readonly AtmosphereSample Vacuum = default;
    private static readonly OxygenParameters Parameters = new OxygenParameters(100, 5, 3, 3, 1.1f, 1.75f);

    [MenuItem("Tools/Moonkov/Check Oxygen System")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running oxygen checks.");
        s_Passed = 0;
        DepletionAndExertion();
        BreathableAir();
        Stations();
        Canisters();
        NutritionAndCompatibility();
        Debug.Log("Oxygen checks passed: " + s_Passed + " assertions (simulation, atmosphere, stations, inventory and legacy compatibility).");
    }

    private static PredictedPlayerGhost Player(float oxygen = 100)
    {
        var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state); state.Oxygen = oxygen;
        return state;
    }
    private static bool Near(float value, float expected) => Mathf.Abs(value - expected) < .001f;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Oxygen check failed: " + message);
        s_Passed++;
    }

    private static void DepletionAndExertion()
    {
        var whole = Player(2); var partitioned = whole;
        RaidOxygen.Tick(ref whole, Vacuum, Parameters, 8, 1);
        for (int i = 0; i < 32; i++) RaidOxygen.Tick(ref partitioned, Vacuum, Parameters, .25f, 1);
        Check(Near(whole.Oxygen, 0) && Near(whole.HypoxiaSeconds, 6) && Near(whole.ChestHealth, 76),
            "depletion uses only exposed time and damage begins after the grace period");
        Check(Near(whole.Oxygen, partitioned.Oxygen) && Near(whole.HypoxiaSeconds, partitioned.HypoxiaSeconds) &&
            Near(whole.CurrentHealth, partitioned.CurrentHealth), "frame partition preserves reserve, exposure and damage");
        var boundary = Player(2);
        RaidOxygen.Tick(ref boundary, Vacuum, Parameters, 5, 1);
        Check(Near(boundary.ChestHealth, 85) && Near(boundary.HypoxiaSeconds, 3), "exact grace boundary causes no damage");
        RaidOxygen.Tick(ref boundary, Vacuum, Parameters, .5f, 1);
        Check(Near(boundary.ChestHealth, 83.5f), "first interval after grace applies only its damage");

        var rest = Player(); var walk = rest; var sprint = rest;
        walk.ControllerState.MovementSpeed = 1;
        sprint.ControllerState.MovementSpeed = 5; sprint.ControllerState.Sprinting = true;
        RaidOxygen.Tick(ref rest, Vacuum, Parameters, 10, 1);
        RaidOxygen.Tick(ref walk, Vacuum, Parameters, 10, 1);
        RaidOxygen.Tick(ref sprint, Vacuum, Parameters, 10, 1);
        Check(Near(rest.Oxygen, 90) && Near(walk.Oxygen, 89) && Near(sprint.Oxygen, 82.5f), "rest, walk and sprint have distinct drain");
        Check(Near(RaidOxygen.RemainingSeconds(rest), 90), "remaining time uses the current net drain");
        var defaults = Player(); defaults.ControllerState.MovementSpeed = 1;
        RaidOxygen.Tick(ref defaults, Vacuum, default, 60, 1);
        Check(Near(defaults.Oxygen, 89), "default value settings still use safe walking consumption");
        var justEmpty = Player(1);
        RaidOxygen.Tick(ref justEmpty, Vacuum, Parameters, 1, 1);
        Check(justEmpty.Oxygen == 0 && Near(justEmpty.OxygenGraceRemaining, 3) && justEmpty.ChestHealth == 85,
            "exact depletion exposes the full grace countdown before any damage");
        var invalid = Player(42); invalid.HypoxiaSeconds = 2; var before = invalid;
        foreach (float dt in new[] { 0, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            RaidOxygen.Tick(ref invalid, Vacuum, Parameters, dt, 1);
            Check(invalid.Equals(before), "invalid dt leaves every player field unchanged: " + dt);
        }
    }

    private static void BreathableAir()
    {
        var state = Player(0); state.HypoxiaSeconds = 10;
        RaidOxygen.Tick(ref state, new AtmosphereSample("test.room", 101.325f, .21f, true, false), Parameters, 5, 1);
        Check(state.BreathableAir && state.Oxygen == 0 && state.HypoxiaSeconds == 0 && Near(state.ChestHealth, 85),
            "breathable room prevents exposure without automatically filling the tank");
        Check((RaidNutrition.Effects(state) & (SurvivalEffects.LowOxygen | SurvivalEffects.Hypoxia)) == 0 &&
            RaidNutrition.CanSprint(state) && Near(RaidNutrition.HandlingMultiplier(state), 1), "breathable air clears oxygen penalties and permits sprint");
        var movement = new FirstPersonController.ControllerConsts
        {
            Walk = new FirstPersonController.ControllerConsts.StateConsts { Speed = 4 },
            Sprint = new FirstPersonController.ControllerConsts.StateConsts { Speed = 7 }, JumpHeight = 2
        };
        var actual = RaidNutrition.Movement(state, movement);
        Check(Near(actual.Walk.Speed, 4) && Near(actual.Sprint.Speed, 7) && Near(actual.JumpHeight, 2),
            "empty tank in breathable air does not slow movement or jumping");
        RaidOxygen.Tick(ref state, Vacuum, Parameters, .5f, 1);
        Check(!RaidNutrition.CanSprint(state) && (RaidNutrition.Effects(state) & SurvivalEffects.Hypoxia) != 0,
            "leaving breathable air with an empty tank restores exposure penalties");
    }

    private static GameObject Temporary(string name, Scene scene, Vector3 position)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = position;
        return go;
    }
    private static void Stations()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        GameObject weakObject = null, strongObject = null, roomObject = null, overrideObject = null;
        var point = new Vector3(95000, 95000, 95000);
        try
        {
            weakObject = Temporary("Oxygen check weak station", scene, point);
            strongObject = Temporary("Oxygen check strong station", scene, point);
            roomObject = Temporary("Oxygen check breathing room", scene, point);
            var weak = weakObject.AddComponent<OxygenRefillZone>(); weak.Configure(3);
            var strong = strongObject.AddComponent<OxygenRefillZone>(); strong.Configure(7, false);
            var room = roomObject.AddComponent<BreathableZone>(); room.Configure("oxygen.check", 101.325f, .21f);
            room.AllowsSuitRefill = false;
            Check(BreathableZone.TrySample(point, out var air) && air.Breathable && !air.AllowsSuitRefill,
                "authored room forwards the tank refill policy");
            overrideObject = Temporary("Oxygen check priority chamber", scene, point);
            var chamber = overrideObject.AddComponent<BreathableZone>(); chamber.Configure("oxygen.check.chamber", 20, .21f, 10);
            Check(BreathableZone.TrySample(point, out air) && !air.Breathable && air.AirspaceId == "oxygen.check.chamber",
                "higher priority low-pressure chamber overrides the surrounding breathable room");
            chamber.Configure("oxygen.check.chamber", 101.325f, .01f, 10);
            Check(BreathableZone.TrySample(point, out air) && !air.Breathable,
                "normal total pressure with insufficient oxygen partial pressure is not breathable");
            chamber.enabled = false;
            Check(BreathableZone.TrySample(point, out air) && air.Breathable && !air.AllowsSuitRefill,
                "disabled chamber falls back to the surrounding room and its refill policy");
            Check(Near(OxygenRefillZone.SampleSupply(point, true), 7), "overlapping stations select maximum supply instead of adding");
            Check(Near(OxygenRefillZone.SampleSupply(point, false), 7), "vacuum-capable station remains available outside breathable air");
            strong.Powered = false;
            Check(Near(OxygenRefillZone.SampleSupply(point, true), 3) && OxygenRefillZone.SampleSupply(point, false) == 0,
                "unpowered station and unmet breathable requirement contribute no supply");
            weak.enabled = false;
            Check(OxygenRefillZone.SampleSupply(point, true) == 0, "disabled station contributes no supply");
            weak.enabled = true; weakObject.GetComponent<BoxCollider>().enabled = false;
            Check(OxygenRefillZone.SampleSupply(point, true) == 0, "disabled station collider contributes no supply");
            strong.Powered = true;
            Check(OxygenRefillZone.SampleSupply(point + Vector3.right * 10, true) == 0, "supply is limited to the authored volume");
            var state = Player(20);
            RaidOxygen.Tick(ref state, new AtmosphereSample("refill", 101.325f, .21f, true), Parameters, 2, 1,
                OxygenRefillZone.SampleSupply(point, true));
            Check(Near(state.Oxygen, 34) && Near(state.OxygenSupplyPerSecond, 7), "station and ambient refill also use the maximum source");
            state = Player(0);
            RaidOxygen.Tick(ref state, Vacuum, Parameters, 2, 1, 7);
            Check(Near(state.Oxygen, 12) && state.HypoxiaSeconds == 0, "vacuum station fills reserve after current breathing demand");
        }
        finally
        {
            if (weakObject) UnityEngine.Object.DestroyImmediate(weakObject);
            if (strongObject) UnityEngine.Object.DestroyImmediate(strongObject);
            if (roomObject) UnityEngine.Object.DestroyImmediate(roomObject);
            if (overrideObject) UnityEngine.Object.DestroyImmediate(overrideObject);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Reject(ref PredictedPlayerGhost state, InventoryGraph graph, string id, int version,
        InventoryError expected, string message)
    {
        var before = state; string inventory = RaidInventoryTransport.Encode(graph);
        Check(RaidOxygenConsumables.Consume(ref state, graph, id, version) == expected && state.Equals(before) &&
            RaidInventoryTransport.Encode(graph) == inventory, message);
    }
    private static void Canisters()
    {
        var graph = InventoryGraph.Create(false, false);
        Check(graph.AddSupply("oxygen_canister", 3) == InventoryError.None, "canister fits an ordinary pocket");
        var item = graph.Items.Find(i => i.Code == "oxygen_canister"); var state = Player(85);
        state.Energy = 31; state.Hydration = 42; state.Satiety = 22; state.HypoxiaSeconds = 4;
        int version = graph.Version;
        Check(RaidOxygenConsumables.Consume(ref state, graph, item.Id, version) == InventoryError.None &&
            state.Oxygen == 100 && state.HypoxiaSeconds == 0 && graph.Version == version + 1 && item.Quantity == 2,
            "partial refill clamps at capacity and consumes exactly one canister");
        Check(state.Energy == 31 && state.Hydration == 42 && state.Satiety == 22, "oxygen canister leaves nutrition unchanged");
        Reject(ref state, graph, item.Id, graph.Version, InventoryError.Invalid, "full suit rejects without consuming");
        state.Oxygen = 20; state.CurrentHealth = 0;
        Reject(ref state, graph, item.Id, graph.Version, InventoryError.Invalid, "dead player rejects without consuming");
        state = Player(20);
        Reject(ref state, graph, item.Id, version, InventoryError.Stale, "stale inventory version rejects without consuming");
        Check(RaidOxygenConsumables.Consume(ref state, graph, item.Id, graph.Version) == InventoryError.None &&
            Near(state.Oxygen, 60) && item.Quantity == 1, "ordinary canister restores exactly forty reserve");

        var carried = InventoryGraph.Create(); var backpack = carried.Equipped("Backpack"); var rig = carried.Equipped("ChestRig");
        Check(carried.AddSupply("oxygen_canister", 1, backpack.Id) == InventoryError.None &&
            carried.AddSupply("oxygen_canister", 1, "stash") == InventoryError.None &&
            carried.AddSupply("oxygen_canister", 1, rig.Id) == InventoryError.None, "inventory fixtures fit backpack, stash and rig");
        state = Player(20);
        Reject(ref state, carried, carried.Items.Find(i => i.Code == "oxygen_canister" && i.Parent == backpack.Id).Id,
            carried.Version, InventoryError.Inaccessible, "backpack canister must be moved to an accessible slot");
        Reject(ref state, carried, carried.Items.Find(i => i.Code == "oxygen_canister" && i.Parent == "stash").Id,
            carried.Version, InventoryError.Inaccessible, "stash canister cannot refill a raid suit");
        Check(RaidOxygenConsumables.Consume(ref state, carried,
            carried.Items.Find(i => i.Code == "oxygen_canister" && i.Parent == rig.Id).Id, carried.Version) == InventoryError.None,
            "equipped rig canister is accessible");
    }

    private static void NutritionAndCompatibility()
    {
        var state = Player(15); state.Energy = 30; state.Hydration = 40; state.Satiety = 20;
        Check(!RaidNutrition.CanConsume(state, ConsumableCatalog.Get("oxygen_canister")), "nutrition path rejects oxygen canisters");
        var graph = InventoryGraph.Create(false);
        Check(graph.AddSupply("ration", 1) == InventoryError.None && graph.AddSupply("water", 1) == InventoryError.None,
            "existing nutrition inventory fixtures remain valid");
        var food = graph.Items.Find(i => i.Code == "ration"); var water = graph.Items.Find(i => i.Code == "water");
        Check(RaidNutrition.Consume(ref state, graph, food.Id, graph.Version) == InventoryError.None &&
            Near(state.Energy, 75) && Near(state.Hydration, 35) && Near(state.Satiety, 55) && state.Oxygen == 15,
            "food retains its existing values and does not refill oxygen");
        Check(RaidNutrition.Consume(ref state, graph, water.Id, graph.Version) == InventoryError.None &&
            Near(state.Hydration, 85) && Near(state.Satiety, 65) && state.Oxygen == 15,
            "water retains its existing values and does not refill oxygen");
        state = Player(50); state.ControllerState.MovementSpeed = 5; state.ControllerState.Sprinting = true;
        RaidHealth.TickOxygen(ref state, false, 6, 600, 5, 8, 3, 1);
        Check(Near(state.Oxygen, 49), "legacy tick preserves constant drain even while sprinting");
        RaidHealth.TickOxygen(ref state, true, 1, 600, 5, 8, 3, 1);
        Check(Near(state.Oxygen, 54), "legacy tick preserves breathable recovery");
    }
}
