using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.UIElements;

public static class OxygenFeedbackChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }
    private static PredictedPlayerGhost Player(float oxygen = 20)
    {
        var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state);
        state.Oxygen = oxygen; state.ProtectionInitialized = state.HeadDomeEquipped = true;
        state.OxygenDrainPerSecond = 1;
        return state;
    }

    [MenuItem("Tools/Moonkov/Oxygen/Check Feedback and Refill Action")]
    public static string Run()
    {
        s_Checks = 0;
        Alerts(); Breathing(); RefillAction(); Monitor(); Receipts();
        return $"PASS: {s_Checks} oxygen alert, breathing, timed refill and monitor checks.";
    }

    private static void Alerts()
    {
        var cue = new MoonkovOxygenFeedback(); var state = Player();
        Check(cue.Observe(state, 100), "Low reserve alerts immediately");
        Check(!cue.Observe(state, 100) && !cue.Observe(state, 119), "Repeated snapshots do not replay low alert");
        Check(cue.Observe(state, 120), "Persistent low reserve reminds after twenty seconds");
        state.Oxygen = 10;
        Check(cue.Observe(state, 121), "Critical oxygen bypasses low-alert cooldown");
        state.Oxygen = 11;
        Check(!cue.Observe(state, 122), "Small refill does not rearm critical threshold");
        state.Oxygen = 10;
        Check(!cue.Observe(state, 123), "Threshold fluctuation does not replay critical alarm");
        state.Oxygen = 0;
        Check(cue.Observe(state, 124), "Empty reserve bypasses critical-alert cooldown");
        Check(!cue.Observe(state, 128) && cue.Observe(state, 129), "Depleted reserve uses bounded five-second cadence");
        state.BreathableAir = true;
        Check(!cue.Observe(state, 130), "Breathable room silences empty-reserve warning");
        state.BreathableAir = false; state.OxygenSupplyPerSecond = 1;
        Check(!cue.Observe(state, 131), "Adequate external supply silences reserve warning");
        state.OxygenSupplyPerSecond = .5f;
        Check(cue.Observe(state, 132), "Insufficient supply still warns");
        state.CurrentHealth = 0; state.Oxygen = 11;
        Check(!cue.Observe(state, 140), "Dead character does not warn");
        cue.Reset(); state = Player(25);
        Check(cue.Observe(state, 200), "New raid can alert again");
        state.Oxygen = 26; Check(!cue.Observe(state, 201), "Low threshold has refill hysteresis");
        state.Oxygen = 25; Check(!cue.Observe(state, 202), "Low threshold cannot chatter");
        state.Oxygen = 40; Check(!cue.Observe(state, 203), "Recovered reserve clears warning");
    }

    private static void Breathing()
    {
        var state = Player(100); var normal = MoonkovOxygenFeedback.Breath(state);
        state.Oxygen = 25; var low = MoonkovOxygenFeedback.Breath(state);
        state.Oxygen = 10; var critical = MoonkovOxygenFeedback.Breath(state);
        state.Oxygen = 0; var depleted = MoonkovOxygenFeedback.Breath(state);
        Check(normal.Audible && normal.Interval > low.Interval && low.Interval > critical.Interval && critical.Interval > depleted.Interval,
            "Breathing accelerates through all oxygen stages");
        Check(normal.Volume < low.Volume && low.Volume < critical.Volume && critical.Volume <= depleted.Volume,
            "Oxygen urgency increases breathing presence");
        state.BreathableAir = true;
        Check(!MoonkovOxygenFeedback.Breath(state).Audible, "Safe room removes low-oxygen breath without filling reserve");
        state.BreathableAir = false; state.OxygenSupplyPerSecond = state.OxygenDrainPerSecond;
        Check(MoonkovOxygenFeedback.Breath(state).Interval == normal.Interval, "Sufficient suit supply restores calm breathing");
        state.CurrentHealth = 0;
        Check(!MoonkovOxygenFeedback.Breath(state).Audible, "Death silences breathing");
    }

    private static void RefillAction()
    {
        var graph = InventoryGraph.Create(false, false);
        Check(graph.AddSupply("oxygen_canister", 3, InventoryCatalog.Pockets) == InventoryError.None, "Canister fixture fits pockets");
        var item = graph.Items.Find(i => i.Code == "oxygen_canister");
        var inventory = new RaidInventoryState { Graph = graph }; var state = Player(0);
        int version = graph.Version, quantity = item.Quantity;
        Check(inventory.BeginConsumable(state, item.Id, version, float3.zero, 0) == InventoryError.None,
            "Empty suit can start timed refill");
        Check(inventory.MedicalIsConsumable && inventory.MedicalUntil == RaidOxygenConsumables.UseSeconds,
            "Oxygen uses its own two-second duration");
        inventory.RefreshMedical(ref state, true, float3.zero, 1);
        Check(state.Oxygen == 0 && item.Quantity == quantity && graph.Version == version, "No air or consumption before completion");
        RaidHealth.TickOxygen(ref state, false, .5f, 600, 0, 0, 3, 1);
        inventory.RefreshMedical(ref state, true, new float3(1, 0, 0), 2);
        Check(state.Oxygen == 40 && item.Quantity == quantity - 1 && inventory.Error == InventoryError.None,
            "Refill completes through physiological damage and slow movement");
        inventory.RefreshMedical(ref state, true, float3.zero, 3);
        Check(item.Quantity == quantity - 1 && state.Oxygen == 40, "Repeated completion cannot duplicate refill");

        state = Player(20); version = graph.Version; quantity = item.Quantity;
        Check(inventory.BeginConsumable(state, item.Id, version, float3.zero, 10) == InventoryError.None, "Next refill starts");
        state.NonPhysiologicalDamageSequence++;
        inventory.RefreshMedical(ref state, true, float3.zero, 12);
        Check(inventory.Error == InventoryError.Inaccessible && state.Oxygen == 20 && item.Quantity == quantity,
            "Enemy attack interrupts without spending canister");

        state = Player(20);
        inventory.BeginConsumable(state, item.Id, graph.Version, float3.zero, 20);
        state.Oxygen = 100;
        inventory.RefreshMedical(ref state, true, float3.zero, 22);
        Check(inventory.Error == InventoryError.Invalid && item.Quantity == quantity, "Station filling suit during use preserves canister");

        state = Player(20);
        inventory.BeginConsumable(state, item.Id, graph.Version, float3.zero, 30);
        state.HeadDomeEquipped = false;
        inventory.RefreshMedical(ref state, true, float3.zero, 32);
        Check(inventory.Error == InventoryError.Invalid && item.Quantity == quantity, "Lost seal cannot receive refill");

        state = Player(20);
        inventory.BeginConsumable(state, item.Id, graph.Version, float3.zero, 40); graph.Version++;
        inventory.RefreshMedical(ref state, true, float3.zero, 42);
        Check(inventory.Error == InventoryError.Stale && item.Quantity == quantity, "Inventory mutation cancels without spending");
        inventory.BeginConsumable(state, item.Id, graph.Version, float3.zero, 50); inventory.CancelActions();
        inventory.RefreshMedical(ref state, true, float3.zero, 52);
        Check(state.Oxygen == 20 && item.Quantity == quantity, "Manual cancel preserves resource and item");
        inventory.BeginConsumable(state, item.Id, graph.Version, float3.zero, 60);
        inventory.RefreshMedical(ref state, false, float3.zero, 62);
        Check(inventory.Error == InventoryError.Inaccessible && item.Quantity == quantity, "Death or settlement cancels refill");

        var foodGraph = InventoryGraph.Create(false);
        Check(foodGraph.AddSupply("water", 1, foodGraph.Equipped("ChestRig").Id) == InventoryError.None, "Water regression fixture");
        var water = foodGraph.Items.Find(i => i.Code == "water");
        var foodAction = new RaidInventoryState { Graph = foodGraph }; state = Player(20); state.Hydration = 20;
        Check(foodAction.BeginConsumable(state, water.Id, foodGraph.Version, float3.zero, 70) == InventoryError.None,
            "Ordinary drink still starts through shared operation");
        foodAction.RefreshMedical(ref state, true, float3.zero, 72);
        Check(foodAction.Error == InventoryError.None && state.Hydration > 20 && state.Oxygen == 20,
            "Ordinary drink completes without granting suit oxygen");
    }

    private static void Monitor()
    {
        var state = Player(10);
        Check(RaidVitalSignal.Read(state).Warning == "CRITICAL OXYGEN", "Critical reserve has a distinct visible warning");
        state.Oxygen = 0; state.OxygenGraceRemaining = 6;
        Check(RaidVitalSignal.Read(state).Warning == "OXYGEN DEPLETED", "Empty reserve is distinct from damage after grace");
        var view = new RaidVitalsMonitor(); view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "OXYGEN DEPLETED / 6s", "Monitor exposes grace countdown");
        state.OxygenGraceRemaining = 5; view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "OXYGEN DEPLETED / 5s", "Countdown updates without changing stage");
        state.OxygenGraceRemaining = 0; view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "HYPOXIA", "Ongoing damage has a hypoxia warning");
        state.Oxygen = 40; state.BreathableAir = true; state.OxygenDrainPerSecond = 0; state.OxygenSupplyPerSecond = 5;
        view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "OXYGEN REFILLING", "External refill is visible in existing status slot");
        Check(view.Q("vitals-reserve-0").Q<Label>().text == "O₂  40%", "Reserve retains exact percentage");
        state.OxygenSupplyPerSecond = 0; view.Present(state);
        Check(view.Q<Label>("vitals-warning").style.display == DisplayStyle.None, "Safe low reserve produces no false danger");
    }

    private static void Receipts()
    {
        var start = ScriptableObject.CreateInstance<SoundDef>();
        var success = ScriptableObject.CreateInstance<SoundDef>();
        var error = ScriptableObject.CreateInstance<SoundDef>();
        try
        {
            var cue = new MoonkovActionAudio();
            Check(cue.Observe(1, 1, false, false, error) == null, "Passive reserve changes cannot emit item-use sound");
            cue.Track(1, 2, success, start);
            Check(cue.Observe(1, 1, true, false, error) == null, "Old snapshot cannot start refill connection sound");
            Check(cue.Observe(1, 2, true, false, error) == start && cue.Observe(1, 2, true, false, error) == null,
                "Confirmed action emits one connection cue");
            Check(cue.Observe(1, 2, false, false, error) == success && cue.Observe(1, 2, false, false, error) == null,
                "Confirmed completion emits one supply cue");
            cue.Track(1, 3, success, start);
            Check(cue.Observe(1, 3, false, true, error) == error && cue.Observe(1, 3, false, false, error) == null,
                "Rejected or interrupted operation cannot emit supply success");
            cue.Track(1, 4, success, start); cue.Reset();
            Check(cue.Observe(1, 4, false, false, error) == null, "Timeout or HUD teardown discards pending cue");
        }
        finally { UnityEngine.Object.DestroyImmediate(start); UnityEngine.Object.DestroyImmediate(success); UnityEngine.Object.DestroyImmediate(error); }
    }

    public static void SyncTranslations()
    {
        var source = JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var collection = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach (var table in collection.StringTables)
        {
            foreach (string key in new[] { "CRITICAL OXYGEN", "OXYGEN DEPLETED", "OXYGEN DEPLETED / {0}s", "OXYGEN REFILLING" })
                table.AddEntry(key, table.LocaleIdentifier.Code == "zh-Hans" ? source[key].Value<string>() : key);
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData); AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }

    public static void RunBatch()
    {
        try
        {
            SyncTranslations(); Debug.Log(Run()); Debug.Log(RaidVitalSignalChecks.Run());
            MoonkovOxygenChecks.Run();
            File.WriteAllText("Library/BuildValidation/OxygenFeedback-result.txt", "PASS");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
