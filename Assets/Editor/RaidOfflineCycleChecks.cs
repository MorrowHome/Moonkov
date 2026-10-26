using System;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class RaidOfflineCycleChecks
{
    static int checks;
    static void Check(bool ok, string message)
    { checks++; if (!ok) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Offline Raid Data Cycle")]
    public static void RunFromEditor() => Debug.Log(Run(Path.GetDirectoryName(Application.dataPath)));

    public static string Run(string projectPath)
    {
        checks = 0;
        string path = Path.GetFullPath(Path.Combine(projectPath, "Library", "BuildValidation", "OfflineCycle", Guid.NewGuid().ToString("N") + ".json"));
        string foundId, rifleId, medId, playerId;
        using (var store = new OfflineProfileStore(path))
        {
            var account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            playerId = store.Read().PlayerId;
            var rifle = account.Items.Single(i => i.Code == "rifle"); rifleId = rifle.Id;
            store.Move(new InventoryCommand { ExpectedVersion = account.Version, ItemId = rifleId,
                Parent = InventoryCatalog.Equipment, Region = "Primary" });
            account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            var med = account.Items.First(i => i.Code == "medkit" && i.Parent == InventoryCatalog.Stash); medId = med.Id;
            store.Move(new InventoryCommand { ExpectedVersion = account.Version, ItemId = medId,
                Parent = InventoryCatalog.Pockets, Region = "1" });
            account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(account.Equipped("Primary")?.Id == rifleId && account.Find(medId)?.Parent == "pockets", "outfitting failed");
            var deployment = new RaidPersistenceContext.Deployment { PlayerId = playerId,
                DeploymentId = Guid.NewGuid().ToString("D"), Cells = 3 };
            var live = store.Deploy(deployment);
            var inventory = new RaidInventoryState { Graph = RaidInventoryTransport.Decode(live.RaidInventoryJson), RaidId = 1 };
            Check(inventory.Graph.Validate() == InventoryError.None && inventory.Graph.Count("cells", true) == 3 &&
                inventory.Graph.Find(rifleId) != null && store.RaidActive, "deployment lost its real loadout");

            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
            var caches = new RaidLootContainers(); caches.Initialize(1);
            var cache = RaidLootTable.Create(RaidLootProfile.Industrial, new System.Random(42));
            caches.Containers[0] = cache;
            var source = cache.Items.First(i => i.Parent == LootInventoryExchange.Root &&
                !InventoryCatalog.Get(i.Code).Container && InventoryCatalog.Get(i.Code).Kind != ItemKind.Root);
            foundId = source.Id;
            Check(source.FoundInRaid && inventory.BeginDissection(0, 10, float3.zero, health, 2), "search did not start");
            inventory.RefreshDissection(caches, true, float3.zero, health, 11.9);
            Check(inventory.DissectingLootId == 0 && inventory.OpenedLootId < 0 && !inventory.SearchedLoot.Contains(0),
                "loot became available before the search ended");
            inventory.RefreshDissection(caches, true, float3.zero, health, 12);
            Check(inventory.OpenedLootId == 0 && inventory.SearchedLoot.Contains(0), "search did not open the real cache");
            var backpack = inventory.Graph.Equipped("Backpack");
            Check(backpack != null, "backpack missing");
            Check(inventory.Graph.FindSpace(source, backpack.Id, out var region, out var x, out var y), "loot did not fit");
            var move = new InventoryCommand { Operation = InventoryOperation.Move, ItemId = foundId, Parent = backpack.Id,
                Region = region, X = x, Y = y, ExpectedVersion = inventory.Graph.Version };
            Check(LootInventoryExchange.TryApply(inventory.Graph, cache, move, cache.Version) == InventoryError.None &&
                inventory.Graph.Find(foundId) != null && cache.Find(foundId) == null, "loot transfer duplicated or lost the item");

            RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 25, 1, RaidDamageKind.Bullet);
            float wounded = health.LeftArmHealth; int medicines = inventory.Graph.Find(medId).Quantity;
            Check(wounded < RaidHealth.Maximum(BodyPart.LeftArm) && inventory.BeginMedical(health, medId,
                inventory.Graph.Version, BodyPart.LeftArm, float3.zero, 20, 3) == InventoryError.None, "injury or treatment failed to start");
            inventory.RefreshMedical(ref health, true, float3.zero, 22.9);
            Check(health.LeftArmHealth == wounded && inventory.Graph.Find(medId).Quantity == medicines, "treatment consumed resources early");
            inventory.RefreshMedical(ref health, true, float3.zero, 23);
            Check(health.LeftArmHealth > wounded && inventory.Graph.Find(medId).Quantity == medicines - 1,
                "completed treatment did not heal and consume one item");

            var result = RaidResultManifest.Capture(inventory.Graph, 1, RaidPhase.Extracted);
            Check(result.Outcome == RaidPhase.Extracted && result.LostCount == 0 && result.ReturnedCount > 0,
                "successful extraction manifest is inconsistent");
            var receipt = new RaidPersistenceContext.Settlement { PlayerId = playerId, DeploymentId = deployment.DeploymentId,
                SettlementId = deployment.DeploymentId, Outcome = "Extracted", InventoryJson = RaidInventoryTransport.Encode(inventory.Graph),
                Dust = inventory.Graph.Count("dust", true), Alloy = inventory.Graph.Count("alloy", true), Cells = inventory.Graph.Count("cells", true) };
            store.Settle(receipt); string once = store.Read().InventoryJson;
            store.Settle(receipt);
            var returned = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(store.Read().InventoryJson == once && returned.Find(foundId) != null && returned.Find(rifleId) != null &&
                returned.Find(medId).Quantity == medicines - 1 && !store.RaidActive, "settlement lost, duplicated, or failed to persist items");
        }
        using (var reopened = new OfflineProfileStore(path))
        {
            var saved = RaidInventoryTransport.Decode(reopened.Read().InventoryJson);
            Check(reopened.Read().PlayerId == playerId && saved.Find(foundId) != null && saved.Find(rifleId) != null &&
                saved.Find(medId) != null && !reopened.RaidActive, "reopening the isolated save lost the extracted items");
        }
        CheckDeathAndSecureContainer(projectPath);
        return "PASS " + checks + " offline raid data-cycle checks: loadout, real loot search and transfer, timed healing, extraction, death, secure return, replay and save reload. No Unity scene or multiplayer executed.";
    }

    private static void CheckDeathAndSecureContainer(string projectPath)
    {
        string path = Path.GetFullPath(Path.Combine(projectPath, "Library", "BuildValidation", "OfflineCycle", Guid.NewGuid().ToString("N") + ".json"));
        string rifleId, caseId, lootId, playerId;
        using (var store = new OfflineProfileStore(path))
        {
            var account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            playerId = store.Read().PlayerId;
            rifleId = account.Items.Single(i => i.Code == "rifle").Id;
            store.Move(new InventoryCommand { ExpectedVersion = account.Version, ItemId = rifleId,
                Parent = InventoryCatalog.Equipment, Region = "Primary" });
            account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            store.Trade(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), Operation = ShopOperation.Buy,
                Code = "secure_case", Quantity = 1, ExpectedVersion = account.Version });
            account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            caseId = account.Items.Single(i => i.Code == "secure_case").Id;
            store.Move(new InventoryCommand { ExpectedVersion = account.Version, ItemId = caseId,
                Parent = InventoryCatalog.Equipment, Region = "SecureContainer" });
            var deployment = new RaidPersistenceContext.Deployment { PlayerId = playerId,
                DeploymentId = Guid.NewGuid().ToString("D"), Cells = 0 };
            var raid = RaidInventoryTransport.Decode(store.Deploy(deployment).RaidInventoryJson);
            Check(raid.Equipped("SecureContainer")?.Id == caseId && raid.Find(rifleId) != null,
                "death fixture did not deploy its equipment");
            var caches = new RaidLootContainers(); caches.Initialize(1);
            var cache = RaidLootTable.Create(RaidLootProfile.Industrial, new System.Random(42));
            caches.Containers[0] = cache;
            var item = cache.Items.First(i => i.Parent == LootInventoryExchange.Root && SecureInventory.Allowed(i.Code));
            lootId = item.Id;
            Check(raid.FindSpace(item, caseId, out var region, out var x, out var y), "secure loot did not fit");
            Check(LootInventoryExchange.TryApply(raid, cache, new InventoryCommand { Operation = InventoryOperation.Move,
                ItemId = lootId, Parent = caseId, Region = region, X = x, Y = y, ExpectedVersion = raid.Version }, cache.Version) == InventoryError.None,
                "secure loot transfer failed");
            var manifest = RaidResultManifest.Capture(raid, 1, RaidPhase.Dead);
            Check(manifest.ReturnedCount > 0 && manifest.LostCount > 0, "death manifest missed protected or dropped equipment");
            int corpseId = caches.Drop(raid, float3.zero);
            Check(raid.Find(lootId) != null && raid.Find(rifleId) == null && caches.Containers[corpseId].Find(rifleId) != null &&
                caches.Containers[corpseId].Find(lootId) == null, "death duplicated or lost secure and dropped items");
            var receipt = new RaidPersistenceContext.Settlement { PlayerId = playerId, DeploymentId = deployment.DeploymentId,
                SettlementId = deployment.DeploymentId, Outcome = "Dead", InventoryJson = RaidInventoryTransport.Encode(raid) };
            store.Settle(receipt); string once = store.Read().InventoryJson;
            store.Settle(receipt);
            var saved = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(store.Read().InventoryJson == once && saved.Find(caseId) != null && saved.Find(lootId) != null &&
                !saved.Find(lootId).FoundInRaid && saved.Find(rifleId) == null && !store.RaidActive,
                "death settlement did not preserve only the protected contents once");
        }
        using (var reopened = new OfflineProfileStore(path))
        {
            var saved = RaidInventoryTransport.Decode(reopened.Read().InventoryJson);
            Check(reopened.Read().PlayerId == playerId && saved.Find(caseId) != null && saved.Find(lootId) != null &&
                saved.Find(rifleId) == null && !reopened.RaidActive, "death settlement did not survive reopening the save");
        }
    }
}
