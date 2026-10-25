using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;
using UnityEngine;

// Uses only unique synthetic profiles beneath Library. Never opens the player's save.
public static class CampaignOfflineChecks
{
    public static string RunShapeChecks()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        string root = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Library/BuildValidation/Campaign/Shape-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string seedFile = Path.Combine(root, "seed.json");
        using (var seed = new OfflineProfileStore(seedFile))
            Check(seed.ReadCampaign().CompletedTasks.Count == 0, "complete new format 4 shape remains valid");
        var template = JObject.Parse(File.ReadAllText(seedFile));
        foreach (string damage in new[] { "empty", "NodeMasks", "CompletedTasks", "missing-format-empty" })
        {
            var document = (JObject)template.DeepClone();
            if (damage == "empty" || damage == "missing-format-empty") document["Campaign"] = new JObject();
            else ((JObject)document["Campaign"]).Remove(damage);
            if (damage == "missing-format-empty") document.Remove("Format");
            string file = Path.Combine(root, damage + ".json"); File.WriteAllText(file, document.ToString());
            byte[] original = File.ReadAllBytes(file);
            bool rejected = false;
            try { using var invalid = new OfflineProfileStore(file); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "incomplete format 4 chapter shape is rejected: " + damage);
            Check(File.ReadAllBytes(file).SequenceEqual(original), "incomplete chapter save remains byte-identical: " + damage);
        }
        return "PASS " + checks + " isolated chapter shape checks. Fixtures: " + root;
    }

    public static string RunSchemaChecks()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        string root = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Library/BuildValidation/Campaign/Schema-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string seedFile = Path.Combine(root, "seed.json");
        using (var seed = new OfflineProfileStore(seedFile)) { }
        var template = JObject.Parse(File.ReadAllText(seedFile));

        // Preserve a real completed-task shape in the damage fixtures. Losing
        // this field must be diagnosed rather than resetting the chapter.
        var run = CampaignRules.Create();
        var task = CampaignCatalog.Find("CM01");
        foreach (var node in task.Nodes.Where(n => n.Kind != CampaignObjectiveKind.Extract))
            if (!CampaignRules.TryRecord(run, new CampaignFact { TaskId = task.Id, NodeId = node.Id,
                Kind = node.Kind, SourceId = node.SourceId, EventId = Guid.NewGuid().ToString("D") }, out var error))
                throw new InvalidOperationException("schema fixture: " + error);
        template["Campaign"] = JToken.FromObject(CampaignRules.MergeExtracted(CampaignRules.Create(), run));

        foreach (bool nullField in new[] { false, true })
        {
            var damaged = (JObject)template.DeepClone();
            if (nullField) damaged["Campaign"] = JValue.CreateNull(); else damaged.Remove("Campaign");
            string file = Path.Combine(root, nullField ? "format4-null.json" : "format4-missing.json");
            File.WriteAllText(file, damaged.ToString());
            byte[] original = File.ReadAllBytes(file);
            bool rejected = false;
            try { using var invalid = new OfflineProfileStore(file); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, nullField ? "format 4 null chapter is rejected" : "format 4 missing chapter is rejected");
            Check(File.ReadAllBytes(file).SequenceEqual(original), "damaged profile bytes remain unchanged");
        }

        var old = (JObject)template.DeepClone();
        old["Format"] = 1; old.Remove("Campaign"); old.Remove("ActiveCampaignVersion");
        old["Inventory"]["MissionFirsts"] = 7;
        string format1 = Path.Combine(root, "format1.json"); File.WriteAllText(format1, old.ToString());
        using (var migrated = new OfflineProfileStore(format1))
        {
            Check(migrated.ReadCampaign().CompletedTasks.Count == 0, "format 1 receives a fresh chapter");
            Check(RaidInventoryTransport.Decode(migrated.Read().InventoryJson).MissionFirsts == 7,
                "format 1 migration preserves historical mission facts");
        }
        Check((int)JObject.Parse(File.ReadAllText(format1))["Format"] == 4, "format 1 migration is saved as format 4");

        // Generate the legacy Clash block through its real registration path so
        // the match/token/deployment hash is valid, then omit the later chapter.
        string format3 = Path.Combine(root, "format3.json");
        using (var store = new OfflineProfileStore(format3))
        {
            var participant = new RaidPersistenceContext.BountyParticipant
            {
                PlayerId = store.Read().PlayerId, DeploymentId = Guid.NewGuid().ToString("D"), TeamId = 1
            };
            var register = typeof(OfflineProfileStore).GetMethod("RegisterBountyMatch", BindingFlags.Instance | BindingFlags.NonPublic);
            if (register == null) throw new InvalidOperationException("Clash fixture registration method is missing.");
            register.Invoke(store, new object[] { Guid.NewGuid().ToString("D"), 1, Guid.NewGuid().ToString("D"),
                new[] { participant }, false });
        }
        var oldClash = JObject.Parse(File.ReadAllText(format3));
        oldClash["Format"] = 3; oldClash.Remove("Campaign"); oldClash.Remove("ActiveCampaignVersion");
        JToken originalClash = oldClash["Clash"].DeepClone();
        File.WriteAllText(format3, oldClash.ToString());
        using (var migrated = new OfflineProfileStore(format3))
            Check(migrated.ReadCampaign().CompletedTasks.Count == 0, "valid format 3 Clash profile receives a fresh chapter");
        var saved = JObject.Parse(File.ReadAllText(format3));
        Check((int)saved["Format"] == 4 && JToken.DeepEquals(originalClash, saved["Clash"]),
            "format 3 migration preserves the real Clash registration ledger");
        return "PASS " + checks + " isolated chapter schema checks. Fixtures: " + root;
    }

    public static string Run()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        string root = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Library/BuildValidation/Campaign/Offline-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);

        string migrated = Path.Combine(root, "migration.json");
        using (var store = new OfflineProfileStore(migrated)) { }
        var legacy = JObject.Parse(File.ReadAllText(migrated));
        legacy["Format"] = 2; legacy.Remove("Campaign"); legacy.Remove("ActiveCampaignVersion");
        legacy["Inventory"]["MissionFirsts"] = 7;
        File.WriteAllText(migrated, legacy.ToString());
        using (var store = new OfflineProfileStore(migrated))
        {
            Check((int)JObject.Parse(File.ReadAllText(migrated))["Format"] == 4, "old profile migrates to format 4");
            Check(store.ReadCampaign().CompletedTasks.Count == 0, "old first milestones do not complete chapter");
            Check(RaidInventoryTransport.Decode(store.Read().InventoryJson).MissionFirsts == 7, "migration preserves old milestones");
            var copy = store.ReadCampaign(); copy.PrimaryTracked = null;
            Check(store.ReadCampaign().PrimaryTracked == "CM01", "campaign reads are isolated copies");
            store.SetCampaignTracking("CM01", remove: true);
        }
        using (var reopened = new OfflineProfileStore(migrated))
            Check(reopened.ReadCampaign().PrimaryTracked == null && reopened.ReadCampaign().TrackingSuppressed,
                "untracking survives restart");

        string success = Path.Combine(root, "success.json");
        RaidPersistenceContext.Settlement accepted;
        int savedDust;
        using (var store = new OfflineProfileStore(success))
        {
            var deployment = Deploy(store);
            var raid = RaidInventoryTransport.Decode(deployment.RaidInventoryJson);
            var fragment = Fragment(); PutInCarry(raid, fragment);
            var run = FieldChapter(store.ReadCampaign(), fragment);
            store.SetCampaignTracking("CM01", remove: true);
            accepted = Settlement(deployment, raid, run);
            var result = store.Settle(accepted); savedDust = result.Dust;
            Check(store.ReadCampaign().CompletedTasks.SetEquals(new[] { "CM01", "CM02", "CM03" }),
                "one real extraction atomically completes three tutorial tasks");
            Check(store.ReadCampaign().PrimaryTracked == null && store.ReadCampaign().TrackingSuppressed,
                "latest UI tracking survives older server-run settlement");
            Check(store.Settle(accepted).Dust == savedDust, "same receipt cannot duplicate extraction rewards");
            Check(!store.RaidActive, "chapter commits with deployment closure");
        }
        using (var reopened = new OfflineProfileStore(success))
        {
            Check(reopened.ReadCampaign().CompletedTasks.Count == 3, "completed chapter survives restart");
            Check(reopened.Settle(accepted).Dust == savedDust, "settlement receipt survives restart");
        }

        string dropped = Path.Combine(root, "dropped.json");
        using (var store = new OfflineProfileStore(dropped))
        {
            var deployment = Deploy(store);
            var raid = RaidInventoryTransport.Decode(deployment.RaidInventoryJson);
            var fragment = Fragment();
            var run = FieldChapter(store.ReadCampaign(), fragment); // Observed, then dropped before freezing return.
            store.Settle(Settlement(deployment, raid, run));
            Check(store.ReadCampaign().CompletedTasks.Contains("CM01") && store.ReadCampaign().CompletedTasks.Contains("CM02"),
                "ordinary return saves other completed teaching steps after sample loss");
            Check(!store.ReadCampaign().CompletedTasks.Contains("CM03") && !store.ReadCampaign().NodeItemIds.ContainsKey("CM03/recover"),
                "a once-seen sample cannot count when absent from the actual return inventory");
            Check(!store.RaidActive, "lost sample does not trap ordinary stash settlement in retry");
        }

        string oldItem = Path.Combine(root, "old-item.json");
        using (var store = new OfflineProfileStore(oldItem)) { }
        var seeded = JObject.Parse(File.ReadAllText(oldItem));
        var oldGraph = RaidInventoryTransport.Decode(seeded["Inventory"].ToString());
        var oldFragment = Fragment(); oldFragment.MissionRecovered = true;
        if (!oldGraph.FindSpace(oldFragment, "stash", out var region, out var x, out var y))
            throw new InvalidOperationException("old sample fixture capacity");
        oldFragment.Parent = "stash"; oldFragment.Region = region; oldFragment.X = x; oldFragment.Y = y;
        oldGraph.Items.Add(oldFragment); seeded["Inventory"] = JToken.FromObject(oldGraph);
        File.WriteAllText(oldItem, seeded.ToString());
        using (var store = new OfflineProfileStore(oldItem))
        {
            var graph = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            store.Move(new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = graph.Version,
                ItemId = oldFragment.Id, Parent = "pockets", Region = "1", Quantity = 1 });
            var deployment = Deploy(store); var raid = RaidInventoryTransport.Decode(deployment.RaidInventoryJson);
            store.Settle(Settlement(deployment, raid, FieldChapter(store.ReadCampaign(), oldFragment)));
            Check(!store.ReadCampaign().CompletedTasks.Contains("CM03"), "old stash fragment does not impersonate a new operation output");
        }

        string failure = Path.Combine(root, "failure.json");
        using (var store = new OfflineProfileStore(failure))
        {
            var deployment = Deploy(store); var raid = RaidInventoryTransport.Decode(deployment.RaidInventoryJson);
            var payload = Settlement(deployment, raid, FieldChapter(store.ReadCampaign(), Fragment())); payload.Outcome = "Dead";
            store.Settle(payload);
            Check(store.ReadCampaign().CompletedTasks.Count == 0 && store.ReadCampaign().NodeMasks.Count == 0,
                "failed field operation does not commit ordinary chapter facts");
        }

        string loading = Path.Combine(root, "loading.json");
        using (var store = new OfflineProfileStore(loading))
        {
            var before = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            var checkpoint = store.BeginLoadingCheckpoint();
            store.SetCampaignTracking("CM01", remove: true);
            var deployment = Deploy(store);
            store.Abandon(new RaidPersistenceContext.Deployment { PlayerId = deployment.PlayerId,
                DeploymentId = deployment.DeploymentId, Cells = 0 });
            checkpoint.Refund();
            Check(store.ReadCampaign().PrimaryTracked == null && store.ReadCampaign().TrackingSuppressed,
                "failed-load refund keeps newest tracking preferences");
            Check(RaidInventoryTransport.Decode(store.Read().InventoryJson).Items.Count == before.Items.Count,
                "failed-load refund restores loadout without duplicating it");
            Check(store.ReadCampaign().CompletedTasks.Count == 0, "failed-load refund cannot create story progress");
        }
        return "PASS " + checks + " isolated chapter persistence checks. Fixtures: " + root;
    }

    private static RaidPersistenceContext.Profile Deploy(OfflineProfileStore store)
    {
        var profile = store.Read();
        return store.Deploy(new RaidPersistenceContext.Deployment { PlayerId = profile.PlayerId,
            DeploymentId = Guid.NewGuid().ToString("D"), Cells = 0 });
    }

    private static InventoryItem Fragment() => new InventoryItem
    {
        Id = Guid.NewGuid().ToString("D"), Code = "mooncore_fragment", FoundInRaid = true,
        MissionWorldId = Guid.NewGuid().ToString("D"), MissionOperationId = Guid.NewGuid().ToString("D"), MissionContentVersion = 1
    };

    private static void PutInCarry(InventoryGraph graph, InventoryItem item)
    {
        foreach (string container in graph.CarryContainers())
        {
            if (!graph.FindSpace(item, container, out var region, out var x, out var y)) continue;
            item.Parent = container; item.Region = region; item.X = x; item.Y = y; graph.Items.Add(item); return;
        }
        throw new InvalidOperationException("sample fixture capacity");
    }

    private static CampaignProgress FieldChapter(CampaignProgress saved, InventoryItem sample)
    {
        var run = CampaignRules.Clone(saved);
        foreach (var task in CampaignCatalog.Tasks)
            foreach (var node in task.Nodes.Where(n => n.Kind != CampaignObjectiveKind.Extract))
                if (!CampaignRules.TryRecord(run, new CampaignFact { TaskId = task.Id, NodeId = node.Id,
                    SourceId = node.SourceId, Kind = node.Kind, EventId = Guid.NewGuid().ToString("D"),
                    ItemId = node.Kind == CampaignObjectiveKind.RecoverItem ? sample.Id : null,
                    ItemCode = node.Kind == CampaignObjectiveKind.RecoverItem ? sample.Code : null }, out var error))
                    throw new InvalidOperationException("field fixture: " + error);
        return run;
    }

    private static RaidPersistenceContext.Settlement Settlement(RaidPersistenceContext.Profile deployment,
        InventoryGraph raid, CampaignProgress run) => new RaidPersistenceContext.Settlement
    {
        PlayerId = deployment.PlayerId, DeploymentId = deployment.DeploymentId, SettlementId = deployment.DeploymentId,
        Outcome = "Extracted", Dust = raid.Count("dust", true), Alloy = raid.Count("alloy", true), Cells = raid.Count("cells", true),
        InventoryJson = RaidInventoryTransport.Encode(raid), CampaignProgress = run
    };
}
