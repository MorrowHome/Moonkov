using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class TutorialChecks
{
    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        string root = Path.GetFullPath("Library/BuildValidation/Tutorial/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string fresh = Path.Combine(root, "new.json");
        using (var store = new OfflineProfileStore(fresh))
        {
            Check(store.ReadTutorial().Status == TutorialStatus.Active, "new profile automatically teaches");
            store.UpdateTutorial(nextIntro: true, steps: TutorialRules.Moved);
            Check(store.ReadTutorial().IntroPage == 1, "intro advances");
            Check(store.ReadCampaign().NodeMasks.Count == 0, "learning never grants chapter facts");
        }
        using (var store = new OfflineProfileStore(fresh))
        {
            Check(store.ReadTutorial().IntroPage == 1 && store.ReadTutorial().CompletedSteps == TutorialRules.Moved, "resume tutorial across restart");
            store.UpdateTutorial(skip: true);
            Check(store.ReadTutorial().Status == TutorialStatus.Skipped, "skip is durable");
            Check(store.ReadCampaign().CompletedTasks.Count == 0, "skip never completes campaign");
            string before = store.Read().InventoryJson; store.ClaimTutorialSupplies();
            Check(store.Read().InventoryJson == before, "skipped tutorial cannot claim equipment");
        }
        var template = JObject.Parse(File.ReadAllText(fresh));
        var legacy = (JObject)template.DeepClone(); legacy.Remove("Tutorial"); ((JObject)legacy["Receipts"]).Remove("tutorial/schema-v1");
        string old = Path.Combine(root, "old.json"); File.WriteAllText(old, legacy.ToString());
        using (var store = new OfflineProfileStore(old)) Check(store.ReadTutorial().Status == TutorialStatus.Available, "existing profiles stay opt-in");
        foreach (string damage in new[] { "missing", "empty", "version", "status", "steps" })
        {
            var document = (JObject)template.DeepClone();
            if (damage == "missing") document.Remove("Tutorial");
            if (damage == "empty") document["Tutorial"] = new JObject();
            if (damage == "version") document["Tutorial"]["Version"] = 99;
            if (damage == "status") document["Tutorial"]["Status"] = 99;
            if (damage == "steps") document["Tutorial"]["CompletedSteps"] = 128;
            string file = Path.Combine(root, damage + ".json"); File.WriteAllText(file, document.ToString());
            string before = File.ReadAllText(file); bool rejected = false;
            try { using var store = new OfflineProfileStore(file); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllText(file) == before, "damaged tutorial preserved: " + damage);
        }
        string poor = Path.Combine(root, "poor.json");
        using (var store = new OfflineProfileStore(poor)) { }
        var seed = JObject.Parse(File.ReadAllText(poor));
        var graph = seed["Inventory"].ToObject<InventoryGraph>();
        graph.Items.RemoveAll(i => TutorialRules.IsWeapon(i.Code) || i.Code == "cells" || i.Code == "medkit");
        seed["Inventory"] = JToken.FromObject(graph); File.WriteAllText(poor, seed.ToString());
        using (var store = new OfflineProfileStore(poor))
        {
            Check(store.ClaimTutorialSupplies() == "教学补给已放入仓库。", "missing kit grants atomically");
            graph = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(graph.Count("cells") == 3 && graph.Count("medkit") == 1 && graph.Count("pistol") == 1, "minimal kit quantities");
            Check(graph.Items.Where(i => i.Code == "pistol" || i.Code == "cells" || i.Code == "medkit").All(i => i.EmergencySupply), "kit cannot be sold");
            string before = store.Read().InventoryJson; store.ClaimTutorialSupplies();
            Check(store.Read().InventoryJson == before, "duplicate claim does not add anything");
            using var loading = new LoadingFixture(store.BeginLoadingCheckpoint());
            Check(store.ClaimTutorialSupplies().Contains("等待"), "loading cannot grant equipment");
        }
        using (var store = new OfflineProfileStore(poor))
        {
            var deployment = (RaidPersistenceContext.Profile)Helper("Deploy", store);
            string before = store.Read().InventoryJson;
            Check(store.ClaimTutorialSupplies().Contains("等待") && store.Read().InventoryJson == before, "raid cannot claim equipment");
            store.UpdateTutorial(steps: TutorialRules.AllSteps);
            store.Abandon(new RaidPersistenceContext.Deployment { PlayerId = deployment.PlayerId, DeploymentId = deployment.DeploymentId });
            Check(store.ReadTutorial().SupplyCycle == 1 && (store.ReadTutorial().CompletedSteps & TutorialRules.Fired) == 0, "failure reopens supply and resets field teaching");
            store.Abandon(new RaidPersistenceContext.Deployment { PlayerId = deployment.PlayerId, DeploymentId = deployment.DeploymentId });
            Check(store.ReadTutorial().SupplyCycle == 1, "duplicate abandon never advances supply cycle");
        }
        string full = Path.Combine(root, "full.json");
        var crowded = (JObject)seed.DeepClone(); graph = crowded["Inventory"].ToObject<InventoryGraph>();
        graph.Items.RemoveAll(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root);
        for (int y = 0; y < 80; y++) for (int x = 0; x < 10; x++)
            graph.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "dust", Quantity = 20, Parent = "stash", Region = "main", X = x, Y = y });
        crowded["Inventory"] = JToken.FromObject(graph); File.WriteAllText(full, crowded.ToString());
        using (var store = new OfflineProfileStore(full))
        {
            string before = store.Read().InventoryJson;
            Check(store.ClaimTutorialSupplies().Contains("空间不足"), "full stash offers recovery action");
            Check(store.Read().InventoryJson == before, "failed grant rolls back entire kit");
        }
        string completed = Path.Combine(root, "success.json");
        using (var store = new OfflineProfileStore(completed))
        {
            var deployment = (RaidPersistenceContext.Profile)Helper("Deploy", store);
            var raid = RaidInventoryTransport.Decode(deployment.RaidInventoryJson);
            var fragment = (InventoryItem)Helper("Fragment"); Helper("PutInCarry", raid, fragment);
            var run = (CampaignProgress)Helper("FieldChapter", store.ReadCampaign(), fragment);
            var settlement = (RaidPersistenceContext.Settlement)Helper("Settlement", deployment, raid, run);
            store.Settle(settlement);
            Check(store.ReadTutorial().Status == TutorialStatus.Completed, "real saved sample return completes tutorial");
            string before = store.Read().InventoryJson; store.Settle(settlement);
            Check(store.Read().InventoryJson == before, "duplicate settlement never rewards twice");
        }
        using (var store = new OfflineProfileStore(completed)) Check(store.ReadTutorial().Status == TutorialStatus.Completed, "completion survives restart");
        string failedLoad = Path.Combine(root, "failed-load.json");
        using (var store = new OfflineProfileStore(failedLoad))
        {
            string before = store.Read().InventoryJson;
            var checkpoint = store.BeginLoadingCheckpoint();
            var deployed = (RaidPersistenceContext.Profile)Helper("Deploy", store);
            store.Abandon(new RaidPersistenceContext.Deployment { PlayerId = deployed.PlayerId, DeploymentId = deployed.DeploymentId });
            store.UpdateTutorial(skip: true); checkpoint.Refund();
            Check(store.ReadTutorial().Status == TutorialStatus.Skipped && store.ReadTutorial().SupplyCycle == 0, "failed loading preserves skip but refunds supply cycle");
            var restored = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(restored.Items.Count == RaidInventoryTransport.Decode(before).Items.Count, "failed loading restores kit without duplication");
        }
        File.WriteAllText(Path.Combine(root, "result.txt"), "PASS " + checks);
        return "PASS " + checks + " tutorial checks: " + root;
    }
    public static string RunInput()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        var previousPad = UnityEngine.InputSystem.Gamepad.current;
        var previousKeyboard = UnityEngine.InputSystem.Keyboard.current;
        var previousMouse = UnityEngine.InputSystem.Mouse.current;
        var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("Tutorial input check");
        var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("Tutorial keyboard check");
        var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("Tutorial mouse check");
        try
        {
            pad.MakeCurrent(); keyboard.MakeCurrent(); mouse.MakeCurrent();
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState { rightStick = Vector2.right });
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState());
            UnityEngine.InputSystem.InputSystem.Update(); TutorialRuntime.UpdateDevice();
            Check(TutorialRuntime.UsingGamepad, "controller activity selects controller prompts");
            Check(TutorialRuntime.ReloadHint.Contains(pad.buttonNorth.shortDisplayName ?? pad.buttonNorth.displayName), "reload prompt uses the actual controller button name");
            Check(TutorialRuntime.InteractHint.Contains(pad.buttonWest.shortDisplayName ?? pad.buttonWest.displayName), "device interaction names match actual binding");
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            UnityEngine.InputSystem.InputSystem.Update(); TutorialRuntime.UpdateDevice();
            Check(!TutorialRuntime.UsingGamepad && TutorialRuntime.ReloadHint.Contains("R"), "keyboard activity restores keyboard prompts");
            TutorialRuntime.Observe(new global::PlayerInput { InputFlags = (uint)global::PlayerInput.InputFlag.Aim });
            Check(TutorialRuntime.Aiming, "actual input intent can observe aiming");
            TutorialRuntime.Observe(default); Check(!TutorialRuntime.Aiming, "aim observation clears on release");
        }
        finally
        {
            UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
            UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            previousPad?.MakeCurrent(); previousKeyboard?.MakeCurrent(); previousMouse?.MakeCurrent(); TutorialRuntime.UpdateDevice();
        }
        return "PASS " + checks + " input checks";
    }
    private static object Helper(string name, params object[] args) => typeof(CampaignOfflineChecks).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    private sealed class LoadingFixture : IDisposable
    {
        private readonly OfflineProfileStore.LoadingCheckpoint m_Checkpoint;
        public LoadingFixture(OfflineProfileStore.LoadingCheckpoint checkpoint) { m_Checkpoint = checkpoint; }
        public void Dispose() => m_Checkpoint.Refund();
    }
}
