using System;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class RaidEncumbranceChecks
{
    [MenuItem("Tools/Moonkov/Check Raid Encumbrance")]
    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;

        var constants = new FirstPersonController.ControllerConsts();
        constants.Walk.Speed = 4; constants.Sprint.Speed = 8; constants.JumpHeight = 2;
        var health = default(PredictedPlayerGhost); RaidHealth.Initialize(ref health);
        health.CarriedWeight = RaidEncumbrance.ComfortableWeight;
        var light = RaidHealth.Movement(health, constants);
        Check(Near(light.Walk.Speed, 4) && Near(light.Sprint.Speed, 8) && Near(light.JumpHeight, 2),
            "A normal load changed healthy movement.");
        health.CarriedWeight = 30;
        var medium = RaidHealth.Movement(health, constants);
        health.CarriedWeight = InventoryCatalog.CarryWeightLimit;
        var heavy = RaidHealth.Movement(health, constants);
        Check(heavy.Walk.Speed < medium.Walk.Speed && medium.Walk.Speed < light.Walk.Speed &&
            heavy.Sprint.Speed < medium.Sprint.Speed && medium.Sprint.Speed < light.Sprint.Speed,
            "Additional loot does not progressively slow movement.");
        Check(Near(heavy.Walk.Speed, 3.52f) && Near(heavy.Sprint.Speed, 6.56f) && Near(heavy.JumpHeight, 1.76f),
            "Maximum weight movement differs from the declared limits.");
        Check(Near(RaidEncumbrance.Burden(float.NaN), 0) && Near(RaidEncumbrance.Burden(-10), 0) &&
            Near(RaidEncumbrance.Burden(90), 1), "Invalid or migrated weight escaped the safe range.");
        RaidHealth.Damage(ref health, BodyPart.LeftLeg, 65, 1);
        Check(Near(RaidHealth.Movement(health, constants).Walk.Speed, heavy.Walk.Speed * .65f) &&
            !RaidNutrition.CanSprint(health), "Weight bypassed the existing leg injury rules.");
        health.ClashEnabled = true;
        Check(Near(RaidEncumbrance.Movement(health, constants).Walk.Speed, constants.Walk.Speed),
            "The separate bounty mode acquired the expedition weight penalty.");

        var sprintLight = new TacticalSprintState(); var sprintHeavy = new TacticalSprintState();
        TacticalSprintRules.Tick(ref sprintLight, 20, true, false, true, 0);
        TacticalSprintRules.Tick(ref sprintHeavy, 20, true, false, true, InventoryCatalog.CarryWeightLimit);
        Check(Near(sprintLight.Value, 60) && Near(sprintHeavy.Value, 30), "Heavy sprint did not cost more endurance.");
        TacticalSprintRules.Tick(ref sprintLight, 6.5f, false, false, true, 0);
        TacticalSprintRules.Tick(ref sprintHeavy, 6.5f, false, false, true, InventoryCatalog.CarryWeightLimit);
        Check(Near(sprintLight.Value, 100) && Near(sprintHeavy.Value, 95), "Heavy endurance recovery did not use the original rest delay.");
        TacticalSprintRules.Tick(ref sprintHeavy, 30, true, false, true, InventoryCatalog.CarryWeightLimit);
        Check(sprintHeavy.Exhausted && Near(sprintHeavy.Value, 0), "Heavy sprint never reached fatigue.");
        TacticalSprintRules.Tick(ref sprintHeavy, 1, true, true, true, InventoryCatalog.CarryWeightLimit);
        Check(!sprintHeavy.Exhausted && Near(sprintHeavy.Value, 100), "The existing stamina injection no longer prevents fatigue.");

        using (var world = new World("Encumbrance inventory fixture", WorldFlags.GameServer))
        {
            var system = world.GetOrCreateSystemManaged<ServerPlayerMovementSystem>();
            var method = typeof(ServerPlayerMovementSystem).GetMethod("CarriedWeight", BindingFlags.Instance | BindingFlags.NonPublic);
            float Weight(InventoryGraph graph) => (float)method.Invoke(system, new object[] { graph });
            var inventory = InventoryGraph.Create(true);
            float initial = Weight(inventory);
            Check(Near(initial, inventory.CarriedWeight), "Server weight did not use equipped inventory.");
            Check(inventory.AddSupply("alloy", 10, "stash") == InventoryError.None && Near(Weight(inventory), initial),
                "Stored items were counted as carried loot.");
            Check(inventory.AddSupply("alloy", 10) == InventoryError.None && Near(Weight(inventory), initial + 6),
                "A carried stack did not update weight after the inventory transaction.");
            var alloy = inventory.Items.Find(i => i.Code == "alloy" && inventory.Carried(i));
            Check(inventory.FindSpace(alloy, "stash", out var region, out int x, out int y), "No storage space for the test stack.");
            var error = inventory.TryApply(new InventoryCommand { Operation = InventoryOperation.Move,
                ExpectedVersion = inventory.Version, ItemId = alloy.Id, Parent = "stash", Region = region, X = x, Y = y });
            Check(error == InventoryError.None && Near(Weight(inventory), initial), "Moving loot back to storage did not restore movement weight.");
            var replacement = inventory.Clone(); replacement.Items = inventory.Items.ConvertAll(i => i.Clone());
            Check(replacement.AddSupply("water", 2) == InventoryError.None && Near(Weight(replacement), initial + 1),
                "A replaced inventory inherited a stale weight sample.");
            Check(Near(Weight(null), 0), "Missing inventory did not safely reset weight.");
        }
        foreach (string locale in new[] { "en", "zh-Hans" })
        {
            var table = AssetDatabase.LoadAssetAtPath<StringTable>($"Assets/Localization/Tables/MoonkovUI_{locale}.asset");
            Check(table.GetEntry("CARRIED {0:0.0} / {1:0} KG / HEAVY") != null, "Heavy load label is missing in " + locale);
        }
        return $"PASS {checks} encumbrance checks: real inventory changes, movement, fatigue, injury and mode isolation.";
    }
}
