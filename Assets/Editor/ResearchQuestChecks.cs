using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ResearchQuestChecks : EditorWindow
{
    private const string Output = "LocalData/ResearchQuest";
    private int m_Passed;
    private ResearchQuestView m_View;

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || RaidMissionSite.Active)
            throw new InvalidOperationException("Review needs an idle Editor outside an active mission scene.");
        var check = CreateInstance<ResearchQuestChecks>();
        try
        {
            Directory.CreateDirectory(Output);
            check.CheckProfile();
            check.CheckField();
            check.CheckAssets();
            return $"{check.m_Passed} research quest persistence, field and asset checks passed.";
        }
        finally { DestroyImmediate(check); }
    }

    private void CheckProfile()
    {
        string path = Path.Combine(Output, Guid.NewGuid().ToString("N") + ".json");
        using (var store = new OfflineProfileStore(path))
        {
            Require(!store.AdvanceResearchQuest(ResearchQuest.HaloTrial, false), "Prerequisite blocks acceptance");
            Accept(store, ResearchQuest.Exposure);
            Require(!store.AdvanceResearchQuest(ResearchQuest.Exposure, false), "Accept is idempotent");
            Settle(store, "Extracted", (_, result) => result.ExposureSeconds = 299);
            Status(store, ResearchQuest.Exposure, ResearchQuestStatus.Active);
            Settle(store, "Dead", (_, result) => result.ExposureSeconds = 300);
            Claim(store, ResearchQuest.Exposure);
            Accept(store, ResearchQuest.HaloTrial);
            var receipt = Settle(store, "TimedOut", (_, result) => result.HaloKills = 2);
            store.Settle(receipt);
            Require(store.Read().ResearchQuests.HaloKills == 2, "Settlement retry cannot count twice");
            Settle(store, "Extracted", (_, result) => result.HaloKills = 3);
            Claim(store, ResearchQuest.HaloTrial);
            Accept(store, ResearchQuest.Biology);
            Settle(store, "Extracted", (raid, result) =>
            {
                string one = Add(raid, ResearchQuestRules.Sample);
                result.SampleIds = new[] { one, one, one };
            });
            Status(store, ResearchQuest.Biology, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) =>
                result.SampleIds = Enumerable.Range(0, 3).Select(_ => Add(raid, ResearchQuestRules.Sample)).ToArray());
            Claim(store, ResearchQuest.Biology);
            Accept(store, ResearchQuest.Tablet);
            Settle(store, "Extracted", (raid, result) =>
            {
                Require(raid.AddLoot("small_pack") == InventoryError.None, "Equip a pack after lost gear");
                result.TabletId = Add(raid, ResearchQuestRules.Tablet, false);
            });
            Status(store, ResearchQuest.Tablet, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) => result.TabletId = Add(raid, ResearchQuestRules.Tablet));
            Claim(store, ResearchQuest.Tablet);
            Accept(store, ResearchQuest.FlashDrive);
            Settle(store, "Extracted", (raid, _) => Issued(raid, ResearchQuestRules.Flash));
            Status(store, ResearchQuest.FlashDrive, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) => result.FlashId = Add(raid, ResearchQuestRules.Flash));
            Status(store, ResearchQuest.FlashDrive, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) => result.FlashId = Issued(raid, ResearchQuestRules.Flash));
            Claim(store, ResearchQuest.FlashDrive);
            Accept(store, ResearchQuest.HardDrive);
            Settle(store, "Extracted", (raid, result) =>
            {
                result.HardDriveId = Issued(raid, ResearchQuestRules.Drive);
                raid.Items.Remove(raid.Find(result.HardDriveId));
            });
            Status(store, ResearchQuest.HardDrive, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) => result.HardDriveId = Issued(raid, ResearchQuestRules.Drive));
            Claim(store, ResearchQuest.HardDrive);
            var graph = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            var rifle = graph.Items.First(i => i.Code == "rifle");
            store.Move(new InventoryCommand { ExpectedVersion = graph.Version, ItemId = rifle.Id, Parent = "equipment", Region = "Primary" });
            Accept(store, ResearchQuest.HaloStorage);
            Settle(store, "Extracted", (raid, result) => result.PackageId = Issued(raid, ResearchQuestRules.Package));
            Status(store, ResearchQuest.HaloStorage, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) =>
            {
                var parent = raid.Equipped("Primary").Id;
                var move = new InventoryCommand { ExpectedVersion = raid.Version, Parent = parent, Region = InventoryCatalog.HaloCargoRegion.Id };
                move.ItemId = Add(raid, "cells", false); move.ExpectedVersion = raid.Version;
                Require(raid.TryApply(move) != InventoryError.None, "Cargo slot rejects regular items");
                move.ItemId = result.PackageId = Issued(raid, ResearchQuestRules.Package);
                Require(raid.TryApply(move) == InventoryError.None, "Package moves into the real halo inventory");
            });
            Claim(store, ResearchQuest.HaloStorage);
            Accept(store, ResearchQuest.Sentry);
            Settle(store, "TimedOut", (_, result) => result.HaloKills = 5);
            Status(store, ResearchQuest.Sentry, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (_, result) => result.SentryKills = 3);
            Claim(store, ResearchQuest.Sentry);
            Require(!store.AdvanceResearchQuest(ResearchQuest.Sentry, true), "No duplicate delivery");
        }
        using (var reopened = new OfflineProfileStore(path))
        {
            var restored = reopened.Read();
            Require(restored.ResearchQuests.Completed == (1 << ResearchQuestRules.Count) - 1, "All deliveries survive reopening");
            Require(!RaidInventoryTransport.Decode(restored.InventoryJson).Items.Any(i => InventoryCatalog.IsResearchSupply(i.Code)),
                "Research supplies never enter the persistent inventory");
        }
        string oldPath = path + ".legacy.json";
        var old = JObject.Parse(File.ReadAllText(path));
        old.Remove("ResearchQuests");
        File.WriteAllText(oldPath, old.ToString());
        using var legacy = new OfflineProfileStore(oldPath);
        Status(legacy, ResearchQuest.Exposure, ResearchQuestStatus.Available);
    }

    private void Accept(OfflineProfileStore store, ResearchQuest quest) =>
        Require(store.AdvanceResearchQuest(quest, false), "Accept " + quest);

    private void Claim(OfflineProfileStore store, ResearchQuest quest)
    {
        Status(store, quest, ResearchQuestStatus.Ready);
        Require(store.AdvanceResearchQuest(quest, true), "Deliver " + quest);
        Status(store, quest, ResearchQuestStatus.Completed);
    }

    private void Status(OfflineProfileStore store, ResearchQuest quest, ResearchQuestStatus expected) =>
        Require(ResearchQuestRules.Status(store.Read().ResearchQuests, quest) == expected, $"{quest} is {expected}");

    private string Issued(InventoryGraph raid, string code)
    {
        var id = ResearchQuestRules.IssuedId(raid, code);
        Require(id != null, "Deployment issued " + code);
        return id;
    }

    private string Add(InventoryGraph raid, string code, bool found = true)
    {
        string id = Guid.NewGuid().ToString("D");
        Require(raid.AddSupply(code, 1, foundInRaid: found, id: id) == InventoryError.None, "Add " + code);
        return id;
    }

    private RaidPersistenceContext.Settlement Settle(OfflineProfileStore store, string outcome, Action<InventoryGraph, ResearchQuestResult> exercise)
    {
        var request = new RaidPersistenceContext.Deployment { PlayerId = store.Read().PlayerId, DeploymentId = Guid.NewGuid().ToString("D") };
        var deployment = store.Deploy(request);
        var raid = RaidInventoryTransport.Decode(deployment.RaidInventoryJson);
        var result = new ResearchQuestResult { Active = deployment.ResearchQuests.Active };
        exercise(raid, result);
        var payload = new RaidPersistenceContext.Settlement
        {
            PlayerId = request.PlayerId, DeploymentId = request.DeploymentId, SettlementId = request.DeploymentId,
            Outcome = outcome, InventoryJson = RaidInventoryTransport.Encode(raid),
            Cells = raid.Count("cells", true), Alloy = raid.Count("alloy", true), Dust = raid.Count("dust", true), ResearchQuests = result
        };
        store.Settle(payload);
        Require(!store.RaidActive, "Settlement closes deployment");
        return payload;
    }

    private void CheckAssets()
    {
        foreach (var (code, triangles) in new[] { (ResearchQuestRules.Tablet, 3640), (ResearchQuestRules.Flash, 708), (ResearchQuestRules.Drive, 3556) })
        {
            var prefab = PhysicalItemVisuals.Prefab(code);
            Require(prefab && PhysicalItemVisuals.Icon(code), "Real model and icon for " + code);
            var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
            Require(Enumerable.Range(0, mesh.subMeshCount).Sum(s => (int)mesh.GetIndexCount(s) / 3) == triangles, "Preserved source triangles for " + code);
        }
        foreach (bool zh in new[] { false, true })
            foreach (bool closing in new[] { false, true })
                for (int i = 0; i < ResearchQuestRules.Count; i++)
                {
                    var clip = Resources.Load<AudioClip>($"Moonkov/QuestRadio/{i + 1:00}_{(closing ? "close" : "brief")}_{(zh ? "zh" : "en")}");
                    Require(clip && clip.channels == 1 && clip.length > 8 && clip.length < 90, $"Imported radio {i}, close={closing}, zh={zh}");
                }
    }

    private void CheckField()
    {
        using var world = new World("Research quest authority fixture", WorldFlags.GameServer);
        var preview = EditorSceneManager.NewPreviewScene();
        RaidMissionSite site = null;
        var lifecycle = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MoonEnvironment/Variants/TheophilusBase/Missions/MC00MissionSite.prefab");
            var hardware = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
            site = hardware.GetComponent<RaidMissionSite>();
            site.Anchor("power_b").Position = new Vector3(0, 1.4f, 1);
            typeof(RaidMissionSite).GetMethod("OnEnable", lifecycle).Invoke(site, null);
            Require(ResearchQuestRuntime.Anchor(ResearchQuest.FlashDrive) != null &&
                ResearchQuestRuntime.Anchor(ResearchQuest.HardDrive) != null && ResearchQuestRuntime.Anchor(ResearchQuest.Tablet) != null,
                "All three quest endpoints resolve on the authored hardware");
            var player = world.EntityManager.CreateEntity();
            var connection = world.EntityManager.CreateEntity();
            var specimen = world.EntityManager.CreateEntity(typeof(ProceduralAlienEnemy.State));
            var human = world.EntityManager.CreateEntity();
            var progress = new ResearchQuestProgress
            {
                Accepted = 31 | ResearchQuestRules.Bit(ResearchQuest.HaloStorage) | ResearchQuestRules.Bit(ResearchQuest.Sentry),
                Completed = 15 | ResearchQuestRules.Bit(ResearchQuest.HaloStorage), HaloKills = ResearchQuestRules.HaloKillGoal
            };
            var raid = new RaidSession { RaidId = 1, Phase = RaidPhase.Active };
            var inventory = new RaidInventoryState { Graph = InventoryGraph.Create(stash: false) };
            Require(ResearchQuestRules.Issue(inventory.Graph, progress.Active) == InventoryError.None, "Issue the field fixture drive");
            string issued = Issued(inventory.Graph, ResearchQuestRules.Flash);
            var health = new PredictedPlayerGhost { CurrentHealth = 100 };
            double now = 0;
            void Tick(Vector3 position)
            {
                now += .1; world.SetTime(new TimeData(now, .1f));
                ResearchQuestRuntime.Tick(world, connection, player, raid, inventory, progress, health, position, now, .1f);
            }
            Tick(Vector3.zero);
            ResearchQuestRuntime.Hold(1, ResearchQuest.FlashDrive, true);
            Tick(Vector3.zero);
            Require(ResearchQuestRuntime.TryRead(1, out var frame) && frame.CanOperate, "Authoritative terminal hold starts");
            Tick(Vector3.right);
            Require(ResearchQuestRuntime.TryRead(1, out frame) && !frame.CanOperate && frame.Notice == "interrupted",
                "Moving interrupts the hold");
            ResearchQuestRuntime.Hold(1, ResearchQuest.FlashDrive, true);
            Tick(Vector3.zero);
            Require(ResearchQuestRuntime.TryRead(1, out frame) && !frame.CanOperate, "Interrupted hold needs a fresh press");
            ResearchQuestRuntime.Hold(1, ResearchQuest.FlashDrive, false);
            for (int i = 0; i < 63; i++)
            {
                ResearchQuestRuntime.Hold(1, ResearchQuest.FlashDrive, true);
                Tick(Vector3.zero);
            }
            Require(ResearchQuestRuntime.TryRead(1, out frame) && ResearchQuestRules.Has(frame.Recovered, ResearchQuest.FlashDrive),
                "A complete hold marks that exact carried drive");
            var manual = ResearchQuestRuntime.TagShot(world, player, true, false, default);
            var sentry = ResearchQuestRuntime.TagShot(world, player, true, true, default);
            ResearchQuestRuntime.RecordKill(world, specimen, manual);
            ResearchQuestRuntime.RecordKill(world, human, sentry);
            ResearchQuestRuntime.RecordKill(world, specimen, sentry);
            Require(ResearchQuestRuntime.TryRead(1, out frame) && frame.SentryKills == 1, "Sentry credits only specimens from the actual sentry shot");
            var result = ResearchQuestRuntime.Finish(world, connection, 1, true);
            Require(result.FlashId == issued && result.SentryKills == 1 && !ResearchQuestRuntime.TryRead(1, out _),
                "Settlement freezes the proof and clears the run");
        }
        finally
        {
            ResearchQuestRuntime.Clear(world);
            if (site) typeof(RaidMissionSite).GetMethod("OnDisable", lifecycle).Invoke(site, null);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("The quest preview needs an idle Editor.");
        var window = CreateInstance<ResearchQuestChecks>();
        window.titleContent = new GUIContent("Moonkov / Research quest review");
        window.position = new Rect(56, 64, 1360, 760);
        window.ShowUtility();
        window.Focus();
        window.Preview();
    }

    private async void Preview()
    {
        try
        {
            var state = new ResearchQuestProgress();
            m_View = new ResearchQuestView(rootVisualElement, () => state, (quest, claim) =>
                Task.FromResult(claim ? ResearchQuestRules.Claim(state, quest) : ResearchQuestRules.Accept(state, quest)),
                () => { }, () => { }, () => Close());
            await Wait(450);
            Require(m_View.Element.Q("questWall").resolvedStyle.display == DisplayStyle.Flex, "Wall is the initial screen");
            Require(m_View.Element.Query<Button>(className: "research-quest-card").ToList().Count == 8, "Eight native cards");
            SaveImage("QuestWall.png");
            Click("questCard0");
            await Wait(180);
            Require(m_View.Element.Q("questWall").resolvedStyle.display == DisplayStyle.None, "Card opens the independent detail");
            Click("questAction");
            Require(ResearchQuestRules.Status(state, ResearchQuest.Exposure) == ResearchQuestStatus.Active, "Accept button sends a real intent");
            state.Ready = ResearchQuestRules.Bit(ResearchQuest.Exposure);
            m_View.Refresh();
            Click("questAction");
            Require(ResearchQuestRules.Status(state, ResearchQuest.Exposure) == ResearchQuestStatus.Completed, "Turn in button sends a real intent");
            Click("questBack");
            Require(rootVisualElement.panel.focusController.focusedElement == m_View.Element.Q<Button>("questCard0"), "Return restores card focus");
            for (int i = 1; i < 5; i++) state.Accepted |= 1 << i;
            state.Completed = state.Accepted;
            state.HaloKills = ResearchQuestRules.HaloKillGoal;
            Click("questCard5");
            await Wait(250);
            SaveImage("QuestDetail.png");
            position = new Rect(56, 64, 800, 570);
            await Wait(320);
            SaveImage("QuestDetailCompact.png");
            Click("questBack");
            await Wait(180);
            SaveImage("QuestWallCompact.png");
            File.WriteAllText(Path.Combine(Output, "UI-checks.txt"), $"{m_Passed} native UI checks passed.");
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(Output, "UI-checks.txt"), exception.ToString());
            Debug.LogException(exception);
        }
        finally { Close(); }
    }

    private void Click(string name)
    {
        var button = m_View.Element.Q<Button>(name);
        using var evt = NavigationSubmitEvent.GetPooled();
        evt.target = button;
        button.SendEvent(evt);
    }

    private Task Wait(int milliseconds)
    {
        var task = new TaskCompletionSource<bool>();
        rootVisualElement.schedule.Execute(() => task.TrySetResult(true)).StartingIn(milliseconds);
        return task.Task;
    }

    private void SaveImage(string filename)
    {
        var rect = position;
        float scale = EditorGUIUtility.pixelsPerPoint;
        var image = new Texture2D(Mathf.RoundToInt(rect.width * scale), Mathf.RoundToInt(rect.height * scale), TextureFormat.RGB24, false);
        try
        {
            image.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position * scale, image.width, image.height));
            image.Apply();
            File.WriteAllBytes(Path.Combine(Output, filename), image.EncodeToPNG());
        }
        finally { DestroyImmediate(image); }
    }

    private void Require(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        m_Passed++;
    }

    private void OnDisable() => m_View?.Dispose();
}
