using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
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
            return map.ExtractionPosition;
        }

        private void AdvanceObjective(DollSingerEnemyBrain brain, Vector3 position, Vector3 eye, MoonRaidMap map, double now, float dt)
        {
            if (brain.Action == PmcAction.Scavenge && brain.Cache >= 0)
            {
                Vector3 cachePosition = map.LootPositions[brain.Cache];
                if (HorizontalDistance(position, cachePosition) > RaidRules.PickupRange ||
                    Mathf.Abs(position.y - cachePosition.y) > 3 || WorldBlocked(eye, cachePosition + Vector3.up * .3f))
                { brain.LootUntil = 0; return; }
                if (brain.LootUntil == 0) brain.LootUntil = now + m_Tuning.LootSearchSeconds;
                if (now < brain.LootUntil) return;
                var loot = EntityManager.GetComponentObject<RaidLootContainers>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
                if (loot.Containers.TryGetValue(brain.Cache, out var cache))
                {
                    // Use the same versioned, atomic transfer as a human, taking real shared loot.
                    foreach (var item in cache.Items.Where(i => i.Parent == LootInventoryExchange.Root &&
                                 (i.Code == "cells" || i.Code == "alloy" || i.Code == "dust")).OrderBy(i => i.Code == "cells" ? 0 : 1).ToArray())
                    {
                        foreach (string parent in CarryContainers(brain.Inventory))
                        {
                            if (!brain.Inventory.FindSpace(item, parent, out var region, out int x, out int y)) continue;
                            var result = LootInventoryExchange.TryApply(brain.Inventory, cache, new InventoryCommand {
                                Operation = InventoryOperation.Move, ExpectedVersion = brain.Inventory.Version,
                                ItemId = item.Id, Parent = parent, Region = region, X = x, Y = y, Rotated = item.Rotated
                            }, cache.Version);
                            if (result == InventoryError.None) break;
                        }
                    }
                }
                brain.LootedCaches++;
                brain.VisitedCaches.Add(brain.Cache);
                brain.Cache = -1;
                brain.LootUntil = 0;
                brain.NextDecision = brain.NextPath = 0;
            }
            bool extracting = brain.Action == PmcAction.Extract &&
                HorizontalDistance(position, map.ExtractionPosition) <= map.ExtractionRadius &&
                Mathf.Abs(position.y - map.ExtractionPosition.y) <= 3 && !brain.Visible &&
                now - brain.LastDamage > 5 && brain.Suppression < .25f;
            brain.ExtractionProgress = extracting ? brain.ExtractionProgress + dt : 0;
            if (brain.ExtractionProgress >= map.ExtractionSeconds)
            {
                brain.Extracted = true;
                Debug.Log($"[DollSinger PMC {brain.Seed}] Extracted with {brain.Inventory.Count("dust")} dust, " +
                    $"{brain.Inventory.Count("alloy")} alloy and {brain.Inventory.Count("cells")} cells.");
            }
        }

        private static System.Collections.Generic.IEnumerable<string> CarryContainers(InventoryGraph inventory)
        {
            yield return InventoryCatalog.Pockets;
            var rig = inventory.Equipped("ChestRig");
            if (rig != null) yield return rig.Id;
            var backpack = inventory.Equipped("Backpack");
            if (backpack != null) yield return backpack.Id;
        }

        private static bool PrepareReloadCell(InventoryGraph inventory, int energyPerRound)
        {
            string rig = inventory.Equipped("ChestRig")?.Id;
            if (inventory.CellEnergy(accessibleOnly: true) >= energyPerRound) return true;
            // A cell stored in the backpack must be moved into an accessible slot before reloading.
            foreach (var source in inventory.Items.Where(i => i.Code == "cells" && inventory.Carried(i) && i.Parent != InventoryCatalog.Pockets && i.Parent != rig).ToArray())
            {
                var item = inventory.Find(source.Id);
                foreach (string parent in new[] { InventoryCatalog.Pockets, rig })
                    if (parent != null && inventory.FindSpace(item, parent, out var region, out int x, out int y) &&
                        inventory.TryApply(new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                            ItemId = item.Id, Parent = parent, Region = region, X = x, Y = y }) == InventoryError.None)
                    { if (inventory.CellEnergy(accessibleOnly: true) >= energyPerRound) return true; break; }
            }
            return false;
        }
    }
}
