using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public static class PmcLootRules
    {
        private static float Value(InventoryItem item, bool needCells, bool needMedicine)
        {
            var def = InventoryCatalog.Get(item?.Code);
            if (def == null || def.Kind == ItemKind.Root || MissionInventory.IsArtifact(item.Code)) return 0;
            int price = ShopCatalog.Find(item.Code)?.SellDust ?? (item.Code == "dust" ? 1 : item.Code == "alloy" ? 6 : 0);
            if (price <= 0) return 0;
            float priority = price * item.Quantity / (float)Math.Max(1, def.Width * def.Height);
            if (def.Kind == ItemKind.Cell && needCells) priority += 1000;
            if (def.Kind == ItemKind.Medical && needMedicine) priority += 1000;
            return priority;
        }

        private static IEnumerable<string> Destinations(InventoryGraph inventory, bool usable)
        {
            string rig = inventory.Equipped("ChestRig")?.Id, pack = inventory.Equipped("Backpack")?.Id;
            if (!usable && pack != null) yield return pack;
            yield return InventoryCatalog.Pockets;
            if (rig != null) yield return rig;
            if (usable && pack != null) yield return pack;
        }

        private static int WeightAllowance(InventoryGraph inventory, ItemDefinition definition, int desired)
        {
            if (definition.Weight <= 0) return desired;
            double free = Math.Max(0, InventoryCatalog.CarryWeightLimit - inventory.CarriedWeight);
            return Math.Min(desired, (int)Math.Floor((free + .0001) / definition.Weight));
        }

        // All moves use the same versioned transaction as a player. The candidate list
        // holds IDs because a successful transaction replaces both inventory trees.
        public static int TakeAvailable(InventoryGraph inventory, InventoryGraph cache)
        {
            if (inventory == null || cache == null || cache.Find(LootInventoryExchange.Root) == null) return 0;
            bool needCells = inventory.CellEnergy(accessibleOnly: true) < BatteryEnergy.Capacity * 2;
            bool needMedicine = inventory.Items.Where(inventory.MedicalAccessible).Sum(i => i.Quantity) < 2;
            var occupiedContainers = new HashSet<string>(cache.Items.Select(i => i.Parent));
            var candidates = cache.Items.Where(i => i.Parent == LootInventoryExchange.Root && !occupiedContainers.Contains(i.Id))
                .Select(i => (item: i, value: Value(i, needCells, needMedicine))).Where(candidate => candidate.value > 0)
                .OrderByDescending(candidate => candidate.value).Select(candidate => candidate.item.Id).ToArray();
            int transfers = 0;
            foreach (string id in candidates)
            {
                var source = cache.Find(id);
                if (source == null || source.Parent != LootInventoryExchange.Root) continue;
                var def = InventoryCatalog.Get(source.Code);
                bool usable = def.Kind == ItemKind.Cell || def.Kind == ItemKind.Medical;
                var destinations = Destinations(inventory, usable).ToArray();
                if (!def.Container)
                {
                    var targets = destinations.SelectMany(parent => inventory.Items.Where(i => i.Parent == parent && i.Code == source.Code))
                        .Select(i => i.Id).ToArray();
                    foreach (string targetId in targets)
                    {
                        source = cache.Find(id); var target = inventory.Find(targetId);
                        if (source == null) break;
                        int count = WeightAllowance(inventory, def, Math.Min(source.Quantity, def.MaxStack - target.Quantity));
                        if (count <= 0 || source.FoundInRaid != target.FoundInRaid || source.EmergencySupply != target.EmergencySupply ||
                            !BatteryEnergy.CanMerge(source, target, count)) continue;
                        var command = new InventoryCommand { Operation = InventoryOperation.Merge, ExpectedVersion = inventory.Version,
                            ItemId = id, TargetId = targetId, Quantity = count };
                        if (LootInventoryExchange.TryApply(inventory, cache, command, cache.Version) == InventoryError.None) transfers++;
                    }
                }
                source = cache.Find(id);
                if (source == null) continue;
                int amount = WeightAllowance(inventory, def, source.Quantity);
                if (amount <= 0) continue;
                foreach (string parent in destinations)
                {
                    bool moved = false;
                    for (int orientation = 0; orientation < (def.Width == def.Height ? 1 : 2); orientation++)
                    {
                        var probe = source.Clone(); if (orientation == 1) probe.Rotated = !probe.Rotated;
                        if (!inventory.FindSpace(probe, parent, out string region, out int x, out int y)) continue;
                        var command = new InventoryCommand { Operation = amount < source.Quantity ? InventoryOperation.Split : InventoryOperation.Move,
                            ExpectedVersion = inventory.Version, ItemId = id, Quantity = amount, Parent = parent,
                            Region = region, X = x, Y = y, Rotated = probe.Rotated };
                        if (LootInventoryExchange.TryApply(inventory, cache, command, cache.Version) != InventoryError.None) continue;
                        transfers++; moved = true; break;
                    }
                    if (moved) break;
                }
            }
            return transfers;
        }
    }

    public partial class DollSingerEnemySystem
    {
        private Vector3 SelectCache(DollSingerEnemyBrain brain, Vector3 position, MoonRaidMap map)
        {
            if (brain.Cache >= 0) return map.LootPositions[brain.Cache];
            float best = float.MaxValue;
            for (int i = 0; i < map.LootPositions.Length; i++)
            {
                if (brain.VisitedCaches.Contains(i)) continue;
                if (!NavMesh.SamplePosition(map.LootPositions[i], out var sample, 3, m_Filter) ||
                    !CompletePath(brain, position, sample.position, out float length))
                { brain.VisitedCaches.Add(i); continue; }
                // Different route preferences distribute independent raiders across the map.
                float value = length + (i * 7 + brain.Seed * 11) % 13;
                if (brain.Target != Unity.Entities.Entity.Null && HorizontalDistance(sample.position, brain.LastSeen) < 12) value += 25;
                if (value >= best) continue;
                best = value; brain.Cache = i;
            }
            if (brain.Cache >= 0) return map.LootPositions[brain.Cache];
            brain.Leaving = true;
            brain.CommitUntil = 0;
            return brain.RaidExit;
        }

        private void AdvanceObjective(DollSingerEnemyBrain brain, ref PredictedPlayerGhost health, Vector3 position, Vector3 eye, MoonRaidMap map, double now, float dt)
        {
            if (brain.IsClashBot) { AdvanceClashObjective(brain, position, eye, now); return; }
            if (brain.Action == PmcAction.Scavenge && brain.Cache >= 0)
            {
                Vector3 cachePosition = map.LootPositions[brain.Cache];
                if (HorizontalDistance(position, cachePosition) > RaidRules.PickupRange ||
                    Mathf.Abs(position.y - cachePosition.y) > 3 || WorldBlocked(eye, cachePosition + Vector3.up * .3f))
                { brain.LootUntil = 0; return; }
                if (brain.LootUntil == 0)
                {
                    brain.LootUntil = now + m_Tuning.LootSearchSeconds;
                    RaidLootAcoustics.Mark(ref health);
                }
                if (now < brain.LootUntil) return;
                var loot = EntityManager.GetComponentObject<RaidLootContainers>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
                if (loot.Containers.TryGetValue(brain.Cache, out var cache))
                    PmcLootRules.TakeAvailable(brain.Inventory, cache);
                brain.LootedCaches++;
                brain.VisitedCaches.Add(brain.Cache);
                brain.Cache = -1;
                brain.LootUntil = 0;
                brain.NextDecision = brain.NextPath = 0;
            }
            bool extracting = brain.Action == PmcAction.Extract &&
                HorizontalDistance(position, brain.RaidExit) <= map.ExtractionRadius &&
                Mathf.Abs(position.y - brain.RaidExit.y) <= 3 && !brain.Visible &&
                now - brain.LastDamage > 5 && brain.Suppression < .25f;
            brain.ExtractionProgress = extracting ? brain.ExtractionProgress + dt : 0;
            if (brain.ExtractionProgress >= map.ExtractionSeconds)
            {
                brain.Extracted = true;
                Debug.Log($"[DollSinger PMC {brain.Seed}] Extracted with {brain.Inventory.Count("dust")} dust, " +
                    $"{brain.Inventory.Count("alloy")} alloy and {brain.Inventory.Count("cells")} cells.");
            }
        }

    }
}
