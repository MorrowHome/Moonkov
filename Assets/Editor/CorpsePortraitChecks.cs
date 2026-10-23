using System;
using System.Linq;
using Unity.Collections;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEngine;

public static class CorpsePortraitChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        var dead = InventoryGraph.Create(false);
        var primary = dead.Equipped("Primary"); var secondary = dead.Equipped("Secondary");
        primary.Code = "shotgun"; secondary.Code = "rifle";
        dead.AddSupply("helmet", 1, "equipment");
        var retained = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "secure_case", Parent = "equipment", Region = "SecureContainer" };
        dead.Items.Add(retained);
        var corpses = new RaidLootContainers();
        int id = corpses.Drop(dead, default, Unity.Mathematics.quaternion.identity, GameSettings.DollSingerCharacterIndex);
        var record = corpses.DeathBags[id]; var cache = corpses.Containers[id];
        Check(record.CharacterIndex == GameSettings.DollSingerCharacterIndex, "Death record loses opponent identity.");
        Check(record.EquipmentSlots["Primary"] == primary.Id && record.EquipmentSlots["Secondary"] == secondary.Id, "Death record swaps weapon slots.");
        Check(!record.EquipmentSlots.ContainsKey("SecureContainer") && cache.Find(retained.Id) == null, "Protected kit leaks into corpse portrait metadata.");
        var own = InventoryGraph.Create(false, false); var joined = LootInventoryExchange.Snapshot(own, cache);
        var client = new RaidInventoryClientState();
        void Receive(string json, uint sequence)
        {
            int count = (json.Length + RaidInventoryTransport.ChunkCharacters - 1) / RaidInventoryTransport.ChunkCharacters;
            bool accepted = false;
            for (int n = 0; n < count; n++)
            {
                int offset = n * RaidInventoryTransport.ChunkCharacters;
                accepted = client.Receive(new RaidInventoryChunkV2Rpc { RaidId = 7, Sequence = sequence, Count = count, Index = n,
                    Json = new FixedString512Bytes(json.Substring(offset, Math.Min(RaidInventoryTransport.ChunkCharacters, json.Length - offset))) }, RaidInventoryTransport.Decode);
                if (n < count - 1) Check(!accepted, "Partial metadata published before full snapshot.");
            }
            Check(accepted, "Snapshot did not assemble.");
        }
        Receive(RaidInventoryTransport.EncodeSnapshot(joined, id, cache.Version, corpseCharacterIndex: record.CharacterIndex, corpseEquipmentSlots: record.EquipmentSlots), 1);
        Check(client.CorpseCharacterIndex == record.CharacterIndex && client.CorpseEquipmentSlots["Primary"] == primary.Id, "Snapshot transport loses opponent metadata.");
        Check(client.Graph.Find(client.CorpseEquipmentSlots["Primary"]).Code == "shotgun", "Portrait identifies a weapon by type order instead of original slot.");
        var helmet = cache.Children("loot").First(i => InventoryCatalog.Get(i.Code).Kind == ItemKind.Helmet);
        Check(LootInventoryExchange.TryApply(own, cache, new InventoryCommand { ExpectedVersion = own.Version, ItemId = helmet.Id, Parent = "equipment", Region = "Helmet" }, cache.Version) == InventoryError.None, "Corpse transfer failed.");
        Check(cache.Find(record.EquipmentSlots["Helmet"]) == null && own.Equipped("Helmet")?.Id == helmet.Id, "Looted slot still resolves on corpse.");
        Receive(RaidInventoryTransport.Encode(InventoryGraph.Create(false, false)), 2);
        Check(client.CorpseCharacterIndex == -1 && client.CorpseEquipmentSlots == null && client.LootId == -1, "Legacy/closed target retains previous opponent identity.");
        System.IO.File.WriteAllText("Temp/corpse-portrait-checks.txt", "PASS: " + checks + " assertions; corpse identity, original equipment slots, protected exclusion, snapshot fragments, real loot transfer and legacy reset.");
        Debug.Log("Corpse portrait checks passed: " + checks);
    }
}
