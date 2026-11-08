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
            check.CheckLanProfile();
            check.CheckField();
            check.CheckAssets();
            return $"{check.m_Passed} research quest persistence, field and asset checks passed.";
        }
        finally { DestroyImmediate(check); }
    }

    private void CheckLanProfile()
    {
        string root = Path.Combine(Output, "Online", "lan-" + Guid.NewGuid().ToString("N"));
        using var lan = new LanPersistence(root);
        string Open(string name) => lan.OpenPlayer(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), name);
        string first = Open("Research LAN A"), peer = Open("Research LAN B");
        var accept = JObject.FromObject(new ResearchQuestCommand { Action = ResearchQuestAction.Accept, Quest = ResearchQuest.Exposure });
        var accepted = lan.Mutate(first, "research", accept);
        Require(accepted.Profile.ResearchQuests.Active == 1 && lan.Mutate(first, "research", accept).Profile.ResearchQuests.Revision == 1,
            "LAN command retry is idempotent");
        Require(lan.Read(peer).Profile.ResearchQuests.Accepted == 0, "LAN profiles keep separate research state");
        var request = new RaidPersistenceContext.Deployment { PlayerId = first, DeploymentId = Guid.NewGuid().ToString("D") };
        var live = lan.Request("internal/deployments", request);
        Require(live.ResearchQuests.Active == 1, "Host deploys the member's research state");
        var raid = RaidInventoryTransport.Decode(live.RaidInventoryJson);
        var receipt = new RaidPersistenceContext.Settlement
        {
            PlayerId = first, DeploymentId = request.DeploymentId, SettlementId = request.DeploymentId, Outcome = "Extracted",
            InventoryJson = live.RaidInventoryJson, Dust = raid.Count("dust", true), Alloy = raid.Count("alloy", true), Cells = raid.Count("cells", true),
            ResearchQuests = new ResearchQuestResult { Active = 1, ExposureSeconds = ResearchQuestRules.ExposureSeconds }
        };
        var result = lan.Request("internal/settlements", receipt);
        Require(result.ResearchQuests.Ready == 1 && lan.Read(peer).Profile.ResearchQuests.Ready == 0,
            "Host credits the extracting member only");
        var claim = JObject.FromObject(new ResearchQuestCommand { Action = ResearchQuestAction.Claim, Quest = ResearchQuest.Exposure });
        Require(lan.Mutate(first, "research", claim).Profile.ResearchQuests.Completed == 1, "LAN member turns in the quest");
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
            Status(store, ResearchQuest.Exposure, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (_, result) => result.ExposureSeconds = 300);
            Claim(store, ResearchQuest.Exposure);
            Accept(store, ResearchQuest.HaloTrial);
            Settle(store, "TimedOut", (_, result) => result.HaloKills = 5);
            Require(store.Read().ResearchQuests.HaloKills == 0, "Failed deployment cannot credit combat kills");
            var receipt = Settle(store, "Extracted", (_, result) => result.HaloKills = 2);
            store.Settle(receipt);
            Require(store.Read().ResearchQuests.HaloKills == 2, "Settlement retry cannot count twice");
            Settle(store, "Extracted", (_, result) => result.HaloKills = 3);
            Claim(store, ResearchQuest.HaloTrial);
            Accept(store, ResearchQuest.Biology);
            Settle(store, "Extracted", (raid, result) =>
            {
                Issued(raid, ResearchQuestRules.SampleKit);
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
            Status(store, ResearchQuest.HardDrive, ResearchQuestStatus.Locked);
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
                result.PackageSecured = true;
            });
            Claim(store, ResearchQuest.HaloStorage);
            Accept(store, ResearchQuest.Sentry);
            Settle(store, "TimedOut", (_, result) => result.SentryKills = 3);
            Status(store, ResearchQuest.Sentry, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (_, result) => result.SentryKills = 3);
            Claim(store, ResearchQuest.Sentry);
            Require(!store.AdvanceResearchQuest(ResearchQuest.Sentry, true), "No duplicate delivery");
            Accept(store, ResearchQuest.HardDrive);
            Settle(store, "Extracted", (raid, result) =>
            {
                result.HardDriveId = Issued(raid, ResearchQuestRules.Drive);
                raid.Items.Remove(raid.Find(result.HardDriveId));
            });
            Status(store, ResearchQuest.HardDrive, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) => result.HardDriveId = Issued(raid, ResearchQuestRules.Drive));
            Claim(store, ResearchQuest.HardDrive);
            Require(!store.ChooseResearchEnding(ResearchEnding.Expose), "Ending remains locked before final extraction");
            Accept(store, ResearchQuest.NoWayBack);
            Settle(store, "Extracted", (raid, result) =>
            {
                Require(raid.AddLoot("small_pack") == InventoryError.None, "Equip a backpack for both Q9 records");
                result.NadirRecordsId = Add(raid, ResearchQuestRules.NadirRecords);
            });
            Status(store, ResearchQuest.NoWayBack, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) =>
            {
                result.NadirRecordsId = Add(raid, ResearchQuestRules.NadirRecords);
                result.CredentialsId = Add(raid, ResearchQuestRules.Credentials);
            });
            Claim(store, ResearchQuest.NoWayBack);
            Accept(store, ResearchQuest.EndOfTransmission);
            Settle(store, "Extracted", (raid, result) =>
            {
                result.CredentialsId = Issued(raid, ResearchQuestRules.Credentials);
                result.OriginalArchiveId = Add(raid, ResearchQuestRules.OriginalArchive);
            });
            Status(store, ResearchQuest.EndOfTransmission, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) =>
            {
                result.CredentialsId = Guid.NewGuid().ToString("D");
                result.OriginalArchiveId = Add(raid, ResearchQuestRules.OriginalArchive);
                result.SystemsDisabled = true;
            });
            Status(store, ResearchQuest.EndOfTransmission, ResearchQuestStatus.Active);
            Settle(store, "Extracted", (raid, result) =>
            {
                result.CredentialsId = Issued(raid, ResearchQuestRules.Credentials);
                Require(result.CredentialsId == store.Read().ResearchQuests.CredentialsId, "Q9 credentials issued by identity on Q10");
                result.OriginalArchiveId = Add(raid, ResearchQuestRules.OriginalArchive);
                result.SystemsDisabled = true;
            });
            Require(!store.ChooseResearchEnding(ResearchEnding.Corporate), "Ending waits for debrief");
            Claim(store, ResearchQuest.EndOfTransmission);
        }
        using (var reopened = new OfflineProfileStore(path))
        {
            var restored = reopened.Read();
            Require(restored.ResearchQuests.Completed == (1 << ResearchQuestRules.Count) - 1, "All deliveries survive reopening");
            Require(!RaidInventoryTransport.Decode(restored.InventoryJson).Items.Any(i => InventoryCatalog.IsResearchSupply(i.Code)),
                "Research supplies never enter the persistent inventory");
        }
        foreach (var ending in new[] { ResearchEnding.Expose, ResearchEnding.Corporate, ResearchEnding.Erase })
        {
            string fork = path + "." + ending + ".json";
            File.Copy(path, fork);
            using (var store = new OfflineProfileStore(fork))
            {
                var previous = store.Read();
                Require(store.ChooseResearchEnding(ending), "Commit " + ending);
                var profile = store.Read();
                Require(profile.ResearchQuests.Ending == ending && profile.ResearchQuests.EvidenceId == null,
                    "Decision closes private evidence custody");
                Require(!store.ChooseResearchEnding(ending) && !store.ChooseResearchEnding(ResearchEnding.Expose), "Decision cannot be repeated or replaced");
                int reward = ending == ResearchEnding.Corporate ? ResearchQuestRules.CorporateDust : 0;
                Require(profile.Dust == previous.Dust + reward, "Only corporate grants compensation");
                var graph = RaidInventoryTransport.Decode(profile.InventoryJson);
                var module = graph.Items.Find(i => i.Code == ResearchQuestRules.CorporateModule);
                Require(ending == ResearchEnding.Corporate
                    ? module != null && graph.Find(module.Parent)?.Code == ResearchQuestRules.CorporateHalo
                    : module == null, "Corporate issues an actual upgraded Halo");
            }
            using var check = new OfflineProfileStore(fork);
            Require(check.Read().ResearchQuests.Ending == ending, "Reopening preserves " + ending);
        }
        var legacyState = new ResearchQuestProgress { Accepted = 63, Completed = 63, HaloKills = ResearchQuestRules.HaloKillGoal };
        ResearchQuestRules.Migrate(legacyState);
        Require(ResearchQuestRules.Valid(legacyState) && ResearchQuestRules.Status(legacyState, ResearchQuest.HardDrive) == ResearchQuestStatus.Completed &&
            ResearchQuestRules.Status(legacyState, ResearchQuest.NoWayBack) == ResearchQuestStatus.Locked &&
            ResearchQuestRules.Status(legacyState, ResearchQuest.HaloStorage) == ResearchQuestStatus.Available,
            "Legacy database is preserved; qualification still gates Q9");
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
            var photo = Resources.Load<Texture2D>("Moonkov/UI/QuestExhibits/" + code);
            Require(photo && photo.width == 1600 && photo.height == 1160, "Camera framing preserved for " + code);
        }
        foreach (bool zh in new[] { false, true })
            foreach (var cue in new[] { "nadir", "kade", "kadeRefusal", "warning", "ending_1_voice", "ending_1_epilogue",
                "ending_2_voice", "ending_2_epilogue", "ending_3_voice", "ending_3_epilogue" })
            {
                var clip = Resources.Load<AudioClip>($"Moonkov/QuestRadio/{cue}_{(zh ? "zh" : "en")}");
                Require(clip && clip.channels == 1 && clip.length > 1 && clip.length < 165, $"Imported story track {cue}, zh={zh}");
            }
        foreach (bool zh in new[] { false, true })
            foreach (bool closing in new[] { false, true })
                for (int i = 0; i < ResearchQuestRules.Count; i++)
                {
                    var clip = Resources.Load<AudioClip>($"Moonkov/QuestRadio/{i + 1:00}_{(closing ? "close" : "brief")}_{(zh ? "zh" : "en")}");
                    Require(clip && clip.channels == 1 && clip.length > 8 && clip.length < 165, $"Imported radio {i}, close={closing}, zh={zh}");
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
            Require(ResearchQuestRuntime.Anchor(ResearchDevice.Administrator) != null &&
                ResearchQuestRuntime.Anchor(ResearchDevice.Server) != null && ResearchQuestRuntime.Anchor(ResearchDevice.Tablet) != null,
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
            Require(ResearchQuestRules.Issue(inventory.Graph, progress) == InventoryError.None, "Issue the field fixture drive");
            string issued = Issued(inventory.Graph, ResearchQuestRules.Flash);
            var health = new PredictedPlayerGhost { CurrentHealth = 100 };
            double now = 0;
            void Tick(Vector3 position)
            {
                now += .1; world.SetTime(new TimeData(now, .1f));
                ResearchQuestRuntime.Tick(world, connection, player, raid, inventory, progress, health, position, now, .1f);
            }
            Tick(Vector3.zero);
            ResearchQuestRuntime.Hold(world, connection, player, 1, ResearchDevice.Administrator, true);
            Tick(Vector3.zero);
            Require(ResearchQuestRuntime.TryRead(world, player, 1, out var frame) && frame.CanOperate, "Authoritative terminal hold starts");
            Tick(Vector3.right);
            Require(ResearchQuestRuntime.TryRead(world, player, 1, out frame) && !frame.CanOperate && frame.Notice.ToString() == "interrupted",
                "Moving interrupts the hold");
            ResearchQuestRuntime.Hold(world, connection, player, 1, ResearchDevice.Administrator, true);
            Tick(Vector3.zero);
            Require(ResearchQuestRuntime.TryRead(world, player, 1, out frame) && !frame.CanOperate, "Interrupted hold needs a fresh press");
            ResearchQuestRuntime.Hold(world, connection, player, 1, ResearchDevice.Administrator, false);
            for (int i = 0; i < 63; i++)
            {
                ResearchQuestRuntime.Hold(world, connection, player, 1, ResearchDevice.Administrator, true);
                Tick(Vector3.zero);
            }
            Require(ResearchQuestRuntime.TryRead(world, player, 1, out frame) && ResearchQuestRules.Has(frame.Recovered, ResearchQuest.FlashDrive),
                "A complete hold marks that exact carried drive");
            var manual = ResearchQuestRuntime.TagShot(world, player, true, false, default);
            var sentry = ResearchQuestRuntime.TagShot(world, player, true, true, default);
            ResearchQuestRuntime.RecordKill(world, specimen, manual);
            ResearchQuestRuntime.RecordKill(world, human, sentry);
            ResearchQuestRuntime.RecordKill(world, specimen, sentry);
            Require(ResearchQuestRuntime.TryRead(world, player, 1, out frame) && frame.SentryKills == 1, "Sentry credits only specimens from the actual sentry shot");
            var result = ResearchQuestRuntime.Finish(world, player, 1, true);
            Require(result.FlashId == issued && result.SentryKills == 1 && !ResearchQuestRuntime.TryRead(world, player, 1, out _),
                "Settlement freezes the proof and clears the run");
            CheckLateField(world, connection, player, now);
            CheckPeerField(world, connection, player, now + 90);
        }
        finally
        {
            ResearchQuestRuntime.Clear(world);
            if (site) typeof(RaidMissionSite).GetMethod("OnDisable", lifecycle).Invoke(site, null);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private void CheckLateField(World world, Entity connection, Entity player, double now)
    {
        var progress = new ResearchQuestProgress
        {
            SequenceVersion = 1, Accepted = 511, Completed = 255,
            HaloKills = ResearchQuestRules.HaloKillGoal, SentryKills = ResearchQuestRules.SentryKillGoal
        };
        var raid = new RaidSession { RaidId = 2, Phase = RaidPhase.Active };
        var inventory = new RaidInventoryState { Graph = InventoryGraph.Create(stash: false) };
        Require(inventory.Graph.AddLoot("small_pack") == InventoryError.None, "Field fixture can carry research records");
        var health = new PredictedPlayerGhost { CurrentHealth = 100 };

        void Tick(Vector3 position)
        {
            now += .1;
            world.SetTime(new TimeData(now, .1f));
            ResearchQuestRuntime.Tick(world, connection, player, raid, inventory, progress, health, position, now, .1f);
        }

        void Use(ResearchDevice device, int ticks, bool sentry = false)
        {
            var anchor = ResearchQuestRuntime.Anchor(device);
            var p = anchor.Approach;
            var aim = Quaternion.LookRotation(anchor.Position - (p + Vector3.up * 1.4f)).eulerAngles;
            health.ControllerState.YawDegrees = aim.y;
            health.ControllerState.PitchDegrees = aim.x;
            for (int i = 0; i < ticks; i++)
            {
                if (sentry && i % 7 == 0) health.LastShotTick = health.LastSentryShotTick = (uint)i + 100;
                ResearchQuestRuntime.Hold(world, connection, player, raid.RaidId, device, true);
                Tick(p);
            }
            ResearchQuestRuntime.Hold(world, connection, player, raid.RaidId, device, false);
        }

        bool Needs(int raidId, ResearchDevice device) =>
            ResearchQuestRuntime.TryRead(world, player, raidId, out var snapshot) && snapshot.Needs(device);

        Tick(Vector3.zero);
        Require(!Needs(2, ResearchDevice.NadirTerminal) &&
            !Needs(2, ResearchDevice.CredentialsLocker), "Q9 evidence is sealed until the sector is opened");
        Use(ResearchDevice.LowerSeal, 23);
        Require(Needs(2, ResearchDevice.NadirTerminal) &&
            Needs(2, ResearchDevice.CredentialsLocker), "Sector release exposes two independent objectives");
        Use(ResearchDevice.NadirTerminal, 44);
        Require(ResearchQuestRuntime.TryRead(world, player, 2, out var q9) && q9.RecordsHeld && q9.HeardNadir && !q9.CredentialsHeld,
            "Recovering Nadir's records triggers her recording without awarding credentials");
        Use(ResearchDevice.CredentialsLocker, 8);
        var evidence = ResearchQuestRuntime.Finish(world, player, 2, true);
        Require(ResearchQuestRules.Holds(inventory.Graph, evidence.NadirRecordsId, ResearchQuestRules.NadirRecords, true) &&
            ResearchQuestRules.Holds(inventory.Graph, evidence.CredentialsId, ResearchQuestRules.Credentials, true),
            "Both Q9 interactions award distinct real carried evidence");

        progress.Accepted = 1023; progress.Completed = 511; progress.CredentialsId = evidence.CredentialsId;
        raid.RaidId = 3;
        inventory.Graph = InventoryGraph.Create(stash: false);
        Tick(Vector3.zero);
        Require(!Needs(3, ResearchDevice.ControlAccess) &&
            !Needs(3, ResearchDevice.OriginalArchive) &&
            !Needs(3, ResearchDevice.Shutdown), "Q10 remains sealed without recovered credentials");
        Require(ResearchQuestRules.Issue(inventory.Graph, progress) == InventoryError.None &&
            Needs(3, ResearchDevice.ControlAccess), "Reissued credential enables the access reader");
        Use(ResearchDevice.ControlAccess, 23);
        Require(ResearchQuestRuntime.TryRead(world, player, 3, out var q10) && q10.ControlOpened && !q10.ArchiveHeld && !q10.SystemsDisabled,
            "Authenticating opens control but does not finish the objectives");
        health.HaloDeploymentMode = HaloDeploymentMode.Sentry;
        health.HaloTransferPhase = HaloTransferPhase.Sending;
        Use(ResearchDevice.OriginalArchive, 165, sentry: true);
        Require(ResearchQuestRuntime.TryRead(world, player, 3, out q10) && q10.ArchiveHeld && !q10.SystemsDisabled,
            "Stationary sentry fire does not interrupt the original archive transfer");
        Use(ResearchDevice.Shutdown, 54);
        Require(ResearchQuestRuntime.TryRead(world, player, 3, out q10) && q10.SystemsDisabled &&
            ResearchQuestRules.Has(q10.Recovered, ResearchQuest.EndOfTransmission), "Shutdown and archive together enable extraction");
        var final = ResearchQuestRuntime.Finish(world, player, 3, true);
        Require(final.CredentialsId == progress.CredentialsId && final.SystemsDisabled &&
            ResearchQuestRules.Holds(inventory.Graph, final.OriginalArchiveId, ResearchQuestRules.OriginalArchive, true),
            "Final server settlement freezes exact credentials, archive and shutdown proof");
    }

    private void CheckPeerField(World world, Entity connection, Entity player, double now)
    {
        var peer = world.EntityManager.CreateEntity();
        var peerConnection = world.EntityManager.CreateEntity();
        var rebound = world.EntityManager.CreateEntity();
        var state = new ResearchQuestProgress { Accepted = 31, Completed = 15, HaloKills = ResearchQuestRules.HaloKillGoal };
        var raid = new RaidSession { RaidId = 9, Phase = RaidPhase.Active };
        var first = new RaidInventoryState { Graph = InventoryGraph.Create(stash: false) };
        var second = new RaidInventoryState { Graph = InventoryGraph.Create(stash: false) };
        Require(ResearchQuestRules.Issue(first.Graph, state) == InventoryError.None &&
            ResearchQuestRules.Issue(second.Graph, state) == InventoryError.None, "Both teammates receive their own mission drive");
        string a = Issued(first.Graph, ResearchQuestRules.Flash), b = Issued(second.Graph, ResearchQuestRules.Flash);
        Require(a != b, "Mission drives are scoped to the player");
        var health = new PredictedPlayerGhost { CurrentHealth = 100 };
        void Tick(Entity connectionA)
        {
            now += .1;
            world.SetTime(new TimeData(now, .1f));
            ResearchQuestRuntime.Tick(world, connectionA, player, raid, first, state, health, Vector3.zero, now, .1f);
            ResearchQuestRuntime.Tick(world, peerConnection, peer, raid, second, state, health, Vector3.zero, now, .1f);
        }
        Tick(connection);
        ResearchQuestRuntime.Hold(world, connection, peer, raid.RaidId, ResearchDevice.Administrator, true);
        Tick(connection);
        Require(ResearchQuestRuntime.TryRead(world, peer, raid.RaidId, out var peerView) && !peerView.CanOperate,
            "A teammate cannot submit an intent for another owner");
        for (int i = 0; i < 64; i++)
        {
            ResearchQuestRuntime.Hold(world, connection, player, raid.RaidId, ResearchDevice.Administrator, true);
            Tick(connection);
        }
        Tick(rebound);
        Require(ResearchQuestRuntime.TryRead(world, player, raid.RaidId, out var resumed) &&
            ResearchQuestRules.Has(resumed.Recovered, ResearchQuest.FlashDrive) && !resumed.CanOperate,
            "Rebinding the connection preserves recovered evidence and cancels the hold");
        Require(ResearchQuestRuntime.TryRead(world, peer, raid.RaidId, out peerView) &&
            !ResearchQuestRules.Has(peerView.Recovered, ResearchQuest.FlashDrive), "Shared terminal does not advance the teammate");
        Require(ResearchQuestRuntime.Finish(world, player, raid.RaidId, true).FlashId == a &&
            ResearchQuestRuntime.TryRead(world, peer, raid.RaidId, out _), "Extracting one owner leaves the teammate's run alive");
        for (int i = 0; i < 64; i++)
        {
            ResearchQuestRuntime.Hold(world, peerConnection, peer, raid.RaidId, ResearchDevice.Administrator, true);
            now += .1;
            world.SetTime(new TimeData(now, .1f));
            ResearchQuestRuntime.Tick(world, peerConnection, peer, raid, second, state, health, Vector3.zero, now, .1f);
        }
        Require(ResearchQuestRuntime.Finish(world, peer, raid.RaidId, true).FlashId == b, "Teammate can copy using their original drive");
        state = new ResearchQuestProgress { Accepted = 7, Completed = 3, HaloKills = ResearchQuestRules.HaloKillGoal };
        raid.RaidId++;
        first.Graph = InventoryGraph.Create(stash: false);
        second.Graph = InventoryGraph.Create(stash: false);
        Require(ResearchQuestRules.Issue(first.Graph, state) == InventoryError.None &&
            ResearchQuestRules.Issue(second.Graph, state) == InventoryError.None, "Both players enter with a sample kit");
        Tick(rebound);
        ResearchQuestRuntime.CollectSample(world, rebound, player, raid.RaidId, 64, now);
        ResearchQuestRuntime.CollectSample(world, rebound, player, raid.RaidId, 64, now);
        ResearchQuestRuntime.CollectSample(world, peerConnection, peer, raid.RaidId, 64, now);
        var firstSamples = ResearchQuestRuntime.Finish(world, player, raid.RaidId, true).SampleIds;
        var peerSamples = ResearchQuestRuntime.Finish(world, peer, raid.RaidId, true).SampleIds;
        Require(firstSamples.Length == 1 && peerSamples.Length == 1 && firstSamples[0] != peerSamples[0],
            "A shared corpse yields one independently owned sample per eligible player");
        Require(LootInventoryExchange.PrepareDrop(first.Graph, firstSamples[0], first.Graph.Version, out _, out _) ==
            InventoryError.Inaccessible, "Quest supplies cannot be dropped for a teammate");
        Require(!LootInventoryExchange.DropOnDeath(first.Graph).Items.Any(i => InventoryCatalog.IsResearchSupply(i.Code)),
            "Death bags exclude personal evidence");
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
            bool closed = false;
            m_View = new ResearchQuestView(rootVisualElement, () => state, (quest, claim) =>
                Task.FromResult(claim ? ResearchQuestRules.Claim(state, quest) : ResearchQuestRules.Accept(state, quest)),
                () => { }, () => { }, () => closed = true, ending => Task.FromResult(ResearchQuestRules.ChooseEnding(state, ending)));
            await Wait(450);
            var matrix = m_View.Element.Q("questMatrix");
            Require(matrix.layout.width > 0 && m_View.Element.Q("questExhibit").layout.width > 0,
                "Selection and exhibit are visible on the same screen");
            Require(matrix.Query<Button>().ToList().Count == ResearchQuestRules.Count, "One native node per quest");
            var photo = m_View.Element.Q<Image>("questModelPhoto");
            foreach (var (index, code) in new[] { (3, ResearchQuestRules.Tablet), (4, ResearchQuestRules.Flash), (7, ResearchQuestRules.Drive) })
            {
                Click("questCard" + index);
                Require(photo.image && photo.image == Resources.Load<Texture2D>("Moonkov/UI/QuestExhibits/" + code) &&
                    photo.scaleMode == ScaleMode.ScaleToFit, "Selection shows the full model photograph for " + code);
                Require(!m_View.Element.Q<Button>("questAction").enabledSelf, "Locked quest cannot be accepted");
            }
            Click("questCard0");
            Require(m_View.Element.Q<Button>("questListen").enabledSelf, "Available briefing has a playback control");
            Click("questAction");
            Require(ResearchQuestRules.Status(state, ResearchQuest.Exposure) == ResearchQuestStatus.Active, "Accept button sends a real intent");
            state.Ready = ResearchQuestRules.Bit(ResearchQuest.Exposure);
            m_View.Refresh();
            Click("questAction");
            Require(ResearchQuestRules.Status(state, ResearchQuest.Exposure) == ResearchQuestStatus.Completed, "Turn in button sends a real intent");
            Click("questCard7");
            position = new Rect(56, 64, 800, 570);
            await Wait(320);
            Require(m_View.Element.ClassListContains("research-quest-compact") && photo.layout.height > 0 && matrix.layout.height > 0,
                "Compact layout preserves selection and the photograph");
            state.Accepted = state.Completed = (1 << ResearchQuestRules.Count) - 1;
            state.EvidenceId = Guid.NewGuid().ToString("D");
            m_View.Refresh();
            Click("questDecision");
            Require(m_View.Element.Q("researchEndingScreen") != null &&
                !m_View.Element.Q<Button>("questEndingConfirm").enabledSelf, "Separate event waits for an explicit choice");
            Click("questEnding2");
            Require(state.Ending == ResearchEnding.None, "Preview cannot commit an ending");
            Click("questEndingConfirm");
            Require(state.Ending == ResearchEnding.Corporate && m_View.Element.Q("researchEndingScreen") != null &&
                m_View.Element.Q<Button>("questEndingConfirm") == null, "Confirmation reveals only the saved epilogue");
            var backToQuests = m_View.Element.Q("researchEndingScreen").Q<Button>("questEndingBack");
            using (var backEvent = NavigationSubmitEvent.GetPooled())
            { backEvent.target = backToQuests; backToQuests.SendEvent(backEvent); }
            Click("questBack");
            Require(closed, "Back returns to the ship");
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

    private void Require(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        m_Passed++;
    }

    private void OnDisable() => m_View?.Dispose();
}
