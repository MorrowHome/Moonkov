using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class MissionOfflineChecks
{
    public static string Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); checks++; }
        string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/BuildValidation/Missions"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "offline-fixture-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var seed = new OfflineProfileStore(file)) { }
            // The migration fixture omits fields absent from existing Format 1 profiles.
            var old = JObject.Parse(File.ReadAllText(file)); old.Remove("MissionArtifacts");
            ((JObject)old["Inventory"]).Remove("MissionFirsts"); File.WriteAllText(file, old.ToString());
            string player;
            RaidPersistenceContext.Settlement payload;
            InventoryItem artifact;
            int before;
            using (var store = new OfflineProfileStore(file))
            {
                var profile = store.Read(); player = profile.PlayerId; before = profile.Dust;
                Check(RaidInventoryTransport.Decode(profile.InventoryJson).MissionFirsts == 0, "old facts default");
                string deployment = Guid.NewGuid().ToString("D");
                var live = store.Deploy(new RaidPersistenceContext.Deployment { PlayerId = player, DeploymentId = deployment, Cells = 0 });
                var raid = RaidInventoryTransport.Decode(live.RaidInventoryJson);
                artifact = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "mooncore_fragment", Parent = "pockets", Region = "1",
                    FoundInRaid = true, MissionWorldId = Guid.NewGuid().ToString("D"), MissionOperationId = Guid.NewGuid().ToString("D"), MissionContentVersion = 1 };
                raid.Items.Add(artifact);
                payload = new RaidPersistenceContext.Settlement { PlayerId = player, SettlementId = deployment, DeploymentId = deployment, Outcome = "Extracted",
                    Dust = raid.Count("dust", true), Alloy = raid.Count("alloy", true), Cells = raid.Count("cells", true), InventoryJson = RaidInventoryTransport.Encode(raid) };
                var saved = store.Settle(payload);
                Check(saved.Dust == before + 40 && RaidInventoryTransport.Decode(saved.InventoryJson).MissionFirsts == 3, "offline automatic reward");
                Check(RaidInventoryTransport.Decode(saved.InventoryJson).Find(artifact.Id).MissionRecovered, "offline marked source");
                Check(store.Settle(payload).Dust == saved.Dust, "retry no duplicate reward");
            }
            using (var reopened = new OfflineProfileStore(file))
            {
                Check(reopened.Read().Dust == before + 40 && !reopened.RaidActive, "reopen saved result");
                Check(reopened.Settle(payload).Dust == before + 40, "receipt survives restart");
                string deployment = Guid.NewGuid().ToString("D");
                var live = reopened.Deploy(new RaidPersistenceContext.Deployment { PlayerId = player, DeploymentId = deployment, Cells = 0 });
                var raid = RaidInventoryTransport.Decode(live.RaidInventoryJson);
                raid.Items.RemoveAll(i => i.Id == artifact.Id); raid.Items.Add(artifact.Clone());
                bool rejected = false;
                try { reopened.Settle(new RaidPersistenceContext.Settlement { PlayerId = player, SettlementId = deployment, DeploymentId = deployment, Outcome = "Extracted",
                    Dust = raid.Count("dust", true), Alloy = raid.Count("alloy", true), Cells = raid.Count("cells", true), InventoryJson = RaidInventoryTransport.Encode(raid) }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected && reopened.RaidActive, "copied source rejected atomically");
                var dead = reopened.Settle(new RaidPersistenceContext.Settlement { PlayerId = player, SettlementId = deployment, DeploymentId = deployment, Outcome = "Dead" });
                Check(dead.Dust == before + 40 && RaidInventoryTransport.Decode(dead.InventoryJson).MissionFirsts == 3, "death preserves facts not loot");
            }
            return "PASS " + checks + " offline mission checks: old fields, atomic reward, retry, restart, duplicate source and death.";
        }
        finally
        {
            // Only the three known files owned by this unique fixture; no recursive cleanup.
            foreach (string suffix in new[] { "", ".bak", ".lock", ".tmp" }) if (File.Exists(file + suffix)) File.Delete(file + suffix);
        }
    }
}
