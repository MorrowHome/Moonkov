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
        string foundId, rifleId, medId, freePistolId, playerId;
        using (var store = new OfflineProfileStore(path))
        {
            var account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            playerId = store.Read().PlayerId;
            store.Trade(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = account.Version,
                Operation = ShopOperation.Buy, Code = "pistol", Quantity = 1 });
            account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            var freePistol = account.Items.Single(i => i.Code == "pistol" && i.EmergencySupply); freePistolId = freePistol.Id;
            store.Move(new InventoryCommand { ExpectedVersion = account.Version, ItemId = freePistolId,
                Parent = InventoryCatalog.Equipment, Region = "Pistol" });
            account = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(account.Equipped("Pistol")?.EmergencySupply == true && account.Count("dust") == 100,
                "free purchase was not restricted or changed the account balance");
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
                inventory.Graph.Find(rifleId) != null && inventory.Graph.Find(freePistolId)?.EmergencySupply == true && store.RaidActive,
                "deployment lost its real loadout or free-supply restriction");

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

            var extraction = new RaidSession { Phase = RaidPhase.Active, TimeLeft = 100 };
            var exits = new[] { new Vector3(100, 0, 0) };
            RaidExtractionRules.Advance(ref extraction, Vector3.zero, exits, Vector3.zero, 5, 7);
            RaidExtractionRules.Advance(ref extraction, Vector3.zero, exits, exits[0], 5, 1);
            Check(extraction.ExtractionIndex == 1 && extraction.ExtractionProgress == 1,
                "moving to another exit carried over the old progress");
            RaidExtractionRules.Advance(ref extraction, Vector3.zero, exits, exits[0], 5, 7);
            Check(extraction.ExtractionProgress == 8 && RaidRules.TrySettle(ref extraction, RaidPhase.Extracted, false),
                "the alternate exit could not complete the existing settlement rule");
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
                returned.Find(medId).Quantity == medicines - 1 && returned.Find(freePistolId)?.EmergencySupply == true && !store.RaidActive,
                "settlement lost, duplicated, or failed to persist items");
        }
        using (var reopened = new OfflineProfileStore(path))
        {
            var saved = RaidInventoryTransport.Decode(reopened.Read().InventoryJson);
            Check(reopened.Read().PlayerId == playerId && saved.Find(foundId) != null && saved.Find(rifleId) != null &&
                saved.Find(medId) != null && saved.Find(freePistolId)?.EmergencySupply == true && !reopened.RaidActive,
                "reopening the isolated save lost the extracted items or restriction");
            var freePistol = saved.Find(freePistolId);
            Check(saved.FindSpace(freePistol, "stash", out var region, out int x, out int y), "free pistol could not return to storage");
            reopened.Move(new InventoryCommand { ExpectedVersion = saved.Version, ItemId = freePistolId,
                Parent = "stash", Region = region, X = x, Y = y });
            saved = RaidInventoryTransport.Decode(reopened.Read().InventoryJson);
            bool restricted = false;
            try
            {
                reopened.Trade(new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), ExpectedVersion = saved.Version,
                    Operation = ShopOperation.Sell, Code = "pistol", ItemId = freePistolId, Quantity = 1 });
            }
            catch (AccountClient.InventoryRequestException e) when (e.ErrorCode == "shop_emergency_supply") { restricted = true; }
            Check(restricted && RaidInventoryTransport.Decode(reopened.Read().InventoryJson).Find(freePistolId) != null,
                "reopened free purchase was sold after extraction");
        }
        CheckFailureAndSecureContainer(projectPath, RaidPhase.Dead);
        CheckFailureAndSecureContainer(projectPath, RaidPhase.TimedOut);
        CheckEnemyLootRecovery(projectPath);
        return "PASS " + checks + " offline raid data-cycle checks: free purchase, loadout, competing enemy loot and corpse recovery, timed healing, extraction, death, timeout, secure return, replay and save reload. No Unity scene or multiplayer executed.";
    }

    private static void CheckEnemyLootRecovery(string projectPath)
    {
        string path = Path.GetFullPath(Path.Combine(projectPath, "Library", "BuildValidation", "OfflineCycle", Guid.NewGuid().ToString("N") + ".json"));
        string lootId, shieldId, playerId;
        using (var store = new OfflineProfileStore(path))
        {
            playerId = store.Read().PlayerId;
            var deployment = new RaidPersistenceContext.Deployment { PlayerId = playerId,
                DeploymentId = Guid.NewGuid().ToString("D"), Cells = 0 };
            var inventory = new RaidInventoryState { Graph = RaidInventoryTransport.Decode(store.Deploy(deployment).RaidInventoryJson), RaidId = 1 };
            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
            var caches = new RaidLootContainers(); caches.Initialize(1);
            InventoryGraph cache = null;
            for (int seed = 0; seed < 256; seed++)
            {
                var candidate = RaidLootTable.Create(RaidLootProfile.Industrial, new System.Random(seed));
                if (candidate.Items.Any(i => i.Code == "data_drive" && i.Quantity == 1)) { cache = candidate; break; }
            }
            Check(cache != null, "industrial cache fixture did not contain a data drive");
            caches.Containers[0] = cache;
            lootId = cache.Items.Single(i => i.Code == "data_drive").Id;
            Check(inventory.BeginDissection(0, 10, float3.zero, health, 2), "player could not search shared cache");
            inventory.RefreshDissection(caches, true, float3.zero, health, 12);
            int oldCacheVersion = cache.Version;
            Check(inventory.OpenedLootId == 0 && inventory.SearchedLoot.Contains(0), "shared cache did not open after search");
            var enemy = InventoryGraph.Create(stash: false);
            Check(PmcProtectionLoadout.TryEquip(enemy, false, false, 0), "enemy shield did not fit its real loadout");
            shieldId = enemy.Equipped("Shield").Id;
            Check(PmcLootRules.TakeAvailable(enemy, cache) > 0 && enemy.Find(lootId)?.FoundInRaid == true && cache.Find(lootId) == null,
                "enemy did not take the unique data drive from the shared cache");
            var pack = inventory.Graph.Equipped("Backpack");
            var taken = enemy.Find(lootId);
            Check(pack != null, "player backpack missing");
            Check(inventory.Graph.FindSpace(taken, pack.Id, out var region, out int x, out int y), "player backpack could not receive the data drive");
            var stale = new InventoryCommand { Operation = InventoryOperation.Move, ItemId = lootId, Parent = pack.Id,
                Region = region, X = x, Y = y, ExpectedVersion = inventory.Graph.Version };
            Check(LootInventoryExchange.TryApply(inventory.Graph, cache, stale, oldCacheVersion) == InventoryError.Stale &&
                inventory.Graph.Find(lootId) == null && enemy.Find(lootId) != null,
                "a stale player request duplicated the enemy's loot");
            var enemyHealth = new PredictedPlayerGhost(); RaidHealth.Initialize(ref enemyHealth);
            PmcProtectionLoadout.Initialize(ref enemyHealth, enemy);
            RaidHealth.CombatDamage(ref enemyHealth, BodyPart.Chest, 60, 1, RaidDamageKind.Bullet);
            EnergyShieldEquipment.Tick(ref enemyHealth, enemy, 7);
            RaidHealth.CombatDamage(ref enemyHealth, BodyPart.Head, 100, 2, RaidDamageKind.Bullet);
            Check(enemyHealth.CurrentHealth <= 0 && enemyHealth.ShieldCharge == 0 && enemyHealth.ShieldEnergy == 96,
                "shielded enemy damage or finite recovery did not use the real combat rules");
            EnergyShieldEquipment.Flush(ref enemyHealth, enemy);
            int corpseId = caches.Drop(enemy, float3.zero);
            var corpse = caches.Containers[corpseId];
            Check(PmcLootRules.IsHumanCorpse(caches, corpseId) && corpse.Find(lootId) != null && enemy.Find(lootId) == null,
                "enemy death did not leave the original loot in a human corpse");
            Check(corpse.Find(shieldId)?.FoundInRaid == true && corpse.Find(shieldId).ShieldCharge == 0 &&
                corpse.Find(shieldId).ShieldEnergy == 96 && enemy.Find(shieldId) == null,
                "the enemy's damaged shield was refilled, duplicated, or lost on death");
            Check(inventory.BeginDissection(corpseId, 20, float3.zero, health, 2), "player could not begin searching corpse");
            inventory.RefreshDissection(caches, true, float3.zero, health, 22);
            Check(inventory.OpenedLootId == corpseId && inventory.SearchedLoot.Contains(corpseId), "corpse search did not finish");
            var recover = new InventoryCommand { Operation = InventoryOperation.Move, ItemId = lootId, Parent = pack.Id,
                Region = region, X = x, Y = y, ExpectedVersion = inventory.Graph.Version };
            Check(LootInventoryExchange.TryApply(inventory.Graph, corpse, recover, corpse.Version) == InventoryError.None &&
                inventory.Graph.Find(lootId)?.FoundInRaid == true && corpse.Find(lootId) == null,
                "player did not recover the same found-in-raid item from the corpse");
            var shield = corpse.Find(shieldId);
            Check(inventory.Graph.FindSpace(shield, pack.Id, out var shieldRegion, out int shieldX, out int shieldY),
                "player backpack could not receive the enemy shield");
            var recoverShield = new InventoryCommand { Operation = InventoryOperation.Move, ItemId = shieldId, Parent = pack.Id,
                Region = shieldRegion, X = shieldX, Y = shieldY, ExpectedVersion = inventory.Graph.Version };
            Check(LootInventoryExchange.TryApply(inventory.Graph, corpse, recoverShield, corpse.Version) == InventoryError.None &&
                corpse.Find(shieldId) == null && inventory.Graph.Find(shieldId)?.ShieldEnergy == 96,
                "player did not recover the same depleted shield from the corpse");
            var receipt = new RaidPersistenceContext.Settlement { PlayerId = playerId, DeploymentId = deployment.DeploymentId,
                SettlementId = deployment.DeploymentId, Outcome = "Extracted", InventoryJson = RaidInventoryTransport.Encode(inventory.Graph),
                Dust = inventory.Graph.Count("dust", true), Alloy = inventory.Graph.Count("alloy", true), Cells = inventory.Graph.Count("cells", true) };
            store.Settle(receipt);
            var saved = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(saved.Find(lootId)?.FoundInRaid == true && saved.Find(shieldId)?.ShieldCharge == 0 &&
                saved.Find(shieldId).ShieldEnergy == 96 && (saved.MissionFirsts & MissionInventory.FirstRecovery) != 0 &&
                saved.Count("dust") == 110 && !store.RaidActive, "corpse loot or first recovery reward was not settled");
        }
        using (var reopened = new OfflineProfileStore(path))
        {
            var saved = RaidInventoryTransport.Decode(reopened.Read().InventoryJson);
            Check(reopened.Read().PlayerId == playerId && saved.Find(lootId)?.FoundInRaid == true &&
                saved.Find(shieldId)?.ShieldCharge == 0 && saved.Find(shieldId).ShieldEnergy == 96 &&
                saved.Count("dust") == 110 && !reopened.RaidActive, "enemy loot did not survive reopening the profile");
        }
    }

    private static void CheckFailureAndSecureContainer(string projectPath, RaidPhase outcome)
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
                "failure fixture did not deploy its equipment");
            var caches = new RaidLootContainers(); caches.Initialize(1);
            var cache = RaidLootTable.Create(RaidLootProfile.Industrial, new System.Random(42));
            caches.Containers[0] = cache;
            var item = cache.Items.First(i => i.Parent == LootInventoryExchange.Root && SecureInventory.Allowed(i.Code));
            lootId = item.Id;
            Check(raid.FindSpace(item, caseId, out var region, out var x, out var y), "secure loot did not fit");
            Check(LootInventoryExchange.TryApply(raid, cache, new InventoryCommand { Operation = InventoryOperation.Move,
                ItemId = lootId, Parent = caseId, Region = region, X = x, Y = y, ExpectedVersion = raid.Version }, cache.Version) == InventoryError.None,
                "secure loot transfer failed");
            var manifest = RaidResultManifest.Capture(raid, 1, outcome);
            Check(manifest.Valid(1) && manifest.ReturnedCount > 0 && manifest.LostCount > 0,
                "failure manifest missed protected or lost equipment");
            if (outcome == RaidPhase.Dead)
            {
                int corpseId = caches.Drop(raid, float3.zero);
                Check(raid.Find(lootId) != null && raid.Find(rifleId) == null && caches.Containers[corpseId].Find(rifleId) != null &&
                    caches.Containers[corpseId].Find(lootId) == null, "death duplicated or lost secure and dropped items");
            }
            else
                Check(raid.Find(lootId) != null && raid.Find(rifleId) != null && caches.DeathBags.Count == 0,
                    "timeout fixture must settle the full inventory without creating a death bag");
            var receipt = new RaidPersistenceContext.Settlement { PlayerId = playerId, DeploymentId = deployment.DeploymentId,
                SettlementId = deployment.DeploymentId, Outcome = outcome.ToString(), InventoryJson = RaidInventoryTransport.Encode(raid) };
            store.Settle(receipt); string once = store.Read().InventoryJson;
            store.Settle(receipt);
            var saved = RaidInventoryTransport.Decode(store.Read().InventoryJson);
            Check(store.Read().InventoryJson == once && saved.Find(caseId) != null && saved.Find(lootId) != null &&
                !saved.Find(lootId).FoundInRaid && saved.Find(rifleId) == null && !store.RaidActive,
                "failure settlement did not preserve only the protected contents once");
            receipt.Outcome = outcome == RaidPhase.Dead ? "TimedOut" : "Dead";
            bool rejected = false;
            try { store.Settle(receipt); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && store.Read().InventoryJson == once,
                "a conflicting replay changed a completed failure settlement");
        }
        using (var reopened = new OfflineProfileStore(path))
        {
            var saved = RaidInventoryTransport.Decode(reopened.Read().InventoryJson);
            Check(reopened.Read().PlayerId == playerId && saved.Find(caseId) != null && saved.Find(lootId) != null &&
                saved.Find(rifleId) == null && !reopened.RaidActive, "failure settlement did not survive reopening the save");
        }
    }
}
