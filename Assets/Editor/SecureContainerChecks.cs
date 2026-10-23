using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class SecureContainerChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        var raid = InventoryGraph.Create(false, false);
        var box = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "secure_case", Parent = "equipment", Region = "SecureContainer" };
        raid.Items.Add(box);
        Check(raid.AddSupply("dust", 3, box.Id, true) == InventoryError.None, "Secure case rejects ordinary loot.");
        Check(raid.AddSupply("medkit", 1, "pockets") == InventoryError.None, "Fixture pocket item.");
        var rifle = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "rifle" };
        Check(raid.CanPlace(rifle, box.Id, "main", 0, 1, false) == InventoryError.Incompatible, "Case accepts a weapon.");
        Check(!SecureInventory.Allowed("mooncore_fragment") && !SecureInventory.Allowed("mc00_archive_drive") && !SecureInventory.Allowed("backpack"), "Case policy bypass.");
        var allIds = raid.Items.Where(i => i.Parent != null).Select(i => i.Id).ToArray();
        var cache = LootInventoryExchange.DropOnDeath(raid);
        Check(raid.Validate() == InventoryError.None && cache.Validate() == InventoryError.None, "Death graphs invalid.");
        Check(raid.Equipped("SecureContainer")?.Id == box.Id && raid.Count("dust") == 3 && cache.Count("medkit") == 1 && cache.Count("dust") == 0, "Death transfers protected items.");
        Check(allIds.All(id => (raid.Find(id) != null) != (cache.Find(id) != null)), "Death duplicates or loses ownership.");
        var returned = SecureInventory.FailureReturn(raid);
        Check(returned.Count("dust") == 3 && returned.Items.All(i => !i.FoundInRaid), "Failed loot retains extraction flags.");
        var account = InventoryGraph.Create(true, false);
        Check(RecoveryInventory.Settle(account, new RecoveryInbox(), returned, out var stored, out var pending, false) == InventoryError.None &&
            stored.Count("dust") == 3 && stored.MissionFirsts == 0 && pending.ItemCount() == 0, "Failure settlement awards extraction rewards.");
        var nested = InventoryGraph.Create(false);
        box = box.Clone(); box.Parent = nested.Equipped("Backpack").Id; box.Region = "main"; nested.Items.Add(box);
        Check(SecureInventory.ProtectedIds(nested).Count == 0 && LootInventoryExchange.DropOnDeath(nested).Find(box.Id) != null, "Unequipped case still protects loot.");

        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string parent = Path.Combine(project, "Library", "SecureContainerChecks");
        string directory = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        if (!directory.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture path.");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (bool clash in new[] { false, true })
            foreach (string outcome in new[] { "Dead", "TimedOut", "Extracted", "Abandoned" })
            {
                string path = Path.Combine(directory, (clash ? "clash-" : "raid-") + outcome + ".json");
                string caseId, medicineId, player;
                int returnedCount;
                using (var store = new OfflineProfileStore(path))
                {
                    InventoryGraph Read() => RaidInventoryTransport.Decode(store.Read().InventoryJson);
                    player = store.Read().PlayerId;
                    store.Trade(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), Operation = ShopOperation.Buy, Code = "secure_case", Quantity = 1, ExpectedVersion = Read().Version });
                    var graph = Read(); caseId = graph.Items.Single(i => i.Code == "secure_case").Id;
                    store.Move(new InventoryCommand { ExpectedVersion = graph.Version, ItemId = caseId, Parent = "equipment", Region = "SecureContainer" });
                    graph = Read(); var med = graph.Items.First(i => i.Code == "medkit");
                    store.Move(new InventoryCommand { ExpectedVersion = graph.Version, ItemId = med.Id, Operation = InventoryOperation.Split, Quantity = 2, Parent = caseId, Region = "main" });
                    medicineId = Read().Children(caseId).Single().Id;
                    for (int n = 1; n <= 3; n++)
                    {
                        string code = n == 2 ? "laser_rangefinder" : "survey_compass";
                        store.Trade(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), Operation = ShopOperation.Buy, Code = code, Quantity = 1, ExpectedVersion = Read().Version });
                        graph = Read(); var tool = graph.Items.First(i => i.Code == code && i.Parent == "stash");
                        store.Move(new InventoryCommand { ExpectedVersion = graph.Version, ItemId = tool.Id, Parent = "equipment", Region = "Special" + n });
                    }
                    graph = Read();
                    Check(graph.CanPlace(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "dust" }, "equipment", "Special1", 0, 0, false) == InventoryError.Incompatible, "Special slot accepts ordinary loot.");
                    var deployment = new RaidPersistenceContext.Deployment { PlayerId = player, DeploymentId = Guid.NewGuid().ToString("D"), Cells = 0 };
                    if (clash)
                    {
                        deployment.Mode = "BountyClash"; deployment.MatchId = Guid.NewGuid().ToString("D"); deployment.RulesVersion = 1;
                        typeof(OfflineProfileStore).GetMethod("RegisterBountyMatch", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(store,
                            new object[] { deployment.MatchId, 1, Guid.NewGuid().ToString("D"), new[] { new RaidPersistenceContext.BountyParticipant { PlayerId = player, DeploymentId = deployment.DeploymentId, TeamId = 0 } }, false });
                    }
                    var live = store.Deploy(deployment);
                    var cargo = RaidInventoryTransport.Decode(live.RaidInventoryJson);
                    Check(cargo.AddSupply("data_drive", 1, caseId, true) == InventoryError.None, "Cannot secure raid loot.");
                    if (outcome == "Dead") LootInventoryExchange.DropOnDeath(cargo);
                    if (outcome == "Abandoned") { store.Abandon(deployment); store.Abandon(deployment); }
                    else
                    {
                        var receipt = new RaidPersistenceContext.Settlement { PlayerId = player, DeploymentId = deployment.DeploymentId, SettlementId = deployment.DeploymentId,
                            Mode = deployment.Mode, MatchId = deployment.MatchId, RulesVersion = deployment.RulesVersion, TeamId = deployment.TeamId,
                            Outcome = outcome, InventoryJson = RaidInventoryTransport.Encode(cargo), Dust = cargo.Count("dust", true), Alloy = cargo.Count("alloy", true), Cells = cargo.Count("cells", true) };
                        store.Settle(receipt); var once = store.Read().InventoryJson; store.Settle(receipt);
                        Check(store.Read().InventoryJson == once, "Settlement replay duplicates items.");
                    }
                    var result = Read();
                    Check(!store.RaidActive && result.Equipped("SecureContainer")?.Id == caseId && result.Find(medicineId)?.Quantity == 2, "Protected kit lost: " + clash + outcome);
                    Check(result.Equipped("Special1")?.Code == "survey_compass" && result.Equipped("Special2")?.Code == "laser_rangefinder" && result.Equipped("Special3")?.Code == "survey_compass", "Protected special tools lost.");
                    Check(result.Count("data_drive") == (outcome == "Abandoned" ? 0 : 1), "Protected loot lost or invented.");
                    if (outcome != "Extracted") Check(result.MissionFirsts == 0 && !result.Items.Any(i => i.FoundInRaid), "Failure grants extraction facts.");
                    returnedCount = result.Items.Count;
                }
                using var reopened = new OfflineProfileStore(path);
                Check(RaidInventoryTransport.Decode(reopened.Read().InventoryJson).Items.Count == returnedCount, "Protected return did not survive reload.");
            }
            File.WriteAllText(Path.Combine(project, "Temp", "secure-container-checks.txt"), "PASS: " + checks + " assertions; policy, ownership, death/timeout/extraction/abandon, expedition and Clash, receipt replay and disk reload.");
            Debug.Log("Secure container checks passed: " + checks);
        }
        finally { Directory.Delete(directory, true); }
    }
}
