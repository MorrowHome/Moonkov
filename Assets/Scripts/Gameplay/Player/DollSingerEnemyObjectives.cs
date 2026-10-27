using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public static class PmcLootRules
    {
        public static bool CanSearch(in PredictedPlayerGhost health, double lastDamage, double now) =>
            !RaidHealth.HandsBusy(health) && now - lastDamage >= 1;

        // Keep the same container stable until this real search completes or is
        // interrupted, including the tick where its timer has just elapsed.
        public static bool SearchingCache(DollSingerEnemyBrain brain) => brain != null && !brain.IsClashBot &&
            brain.Action == PmcAction.Scavenge && brain.Cache >= 0 && brain.LootUntil > 0;

        public static void RecordSearch(DollSingerEnemyBrain brain, bool drawer, bool ground, int transferred)
        {
            if (ground)
            { if (transferred > 0 && ++brain.GroundItemsLooted % 3 == 0) brain.LootedCaches++; }
            else if (!drawer || ++brain.DrawersSearched % 3 == 0) brain.LootedCaches++;
        }

        public static bool IsHumanCorpse(RaidLootContainers caches, int id) => caches != null &&
            caches.DeathBags.TryGetValue(id, out var corpse) && corpse.CharacterIndex >= 0 && caches.Containers.ContainsKey(id);

        public static bool IsDroppedItem(RaidLootContainers caches, int id) => caches != null &&
            caches.DeathBags.TryGetValue(id, out var drop) && drop.CharacterIndex == RaidLootContainers.DroppedItemCharacterIndex &&
            caches.Containers.ContainsKey(id) && !caches.IsEmptyDrop(id);

        public static bool IsNearbyLoot(RaidLootContainers caches, int id) =>
            IsHumanCorpse(caches, id) || IsDroppedItem(caches, id);

        public static bool TryPosition(Vector3[] fixedPositions, RaidLootContainers caches, int id, out Vector3 position)
        {
            position = default;
            if (id >= 0 && fixedPositions != null && id < fixedPositions.Length)
            { position = fixedPositions[id]; return true; }
            if (IsNearbyLoot(caches, id))
            { position = (Vector3)caches.DeathBags[id].Position + Vector3.up * .25f; return true; }
            if (!RaidFurnitureLoot.IsFurniture(id) || caches == null || !caches.Containers.ContainsKey(id) ||
                !caches.FurnitureCaches.TryGetValue(id, out var drawer)) return false;
            position = drawer;
            return true;
        }

        // Only nearby, registered furniture is considered. Contents remain unknown until searched.
        public static int[] NearbyDrawers(RaidLootContainers caches, Vector3 position, HashSet<int> visited)
        {
            if (caches == null) return Array.Empty<int>();
            return caches.FurnitureCaches.Where(pair => RaidFurnitureLoot.IsFurniture(pair.Key) &&
                    caches.Containers.ContainsKey(pair.Key) && (visited == null || !visited.Contains(pair.Key)) &&
                    ((Vector3)pair.Value - position).sqrMagnitude <= 20 * 20)
                .OrderBy(pair => ((Vector3)pair.Value - position).sqrMagnitude).ThenBy(pair => pair.Key)
                .Take(12).Select(pair => pair.Key).ToArray();
        }

        // Examine at most four nearby bodies or nonempty ground drops for line of sight.
        public static int[] NearbyLoot(RaidLootContainers caches, Vector3 position, HashSet<int> visited)
        {
            if (caches == null) return Array.Empty<int>();
            return caches.DeathBags.Where(pair => IsNearbyLoot(caches, pair.Key) &&
                    (visited == null || !visited.Contains(pair.Key)) &&
                    (((Vector3)pair.Value.Position + Vector3.up * .25f) - position).sqrMagnitude <= RaidRules.PickupRange * RaidRules.PickupRange)
                .OrderBy(pair => (((Vector3)pair.Value.Position + Vector3.up * .25f) - position).sqrMagnitude)
                .ThenBy(pair => pair.Key).Take(4).Select(pair => pair.Key).ToArray();
        }

        private static float Value(InventoryItem item, bool needCells, bool needMedicine, bool needShield,
            bool needFood, bool needDrink, bool needOxygen)
        {
            var def = InventoryCatalog.Get(item?.Code);
            if (def == null || def.Kind == ItemKind.Root || MissionInventory.IsArtifact(item.Code)) return 0;
            int price = ShopCatalog.Find(item.Code)?.SellDust ?? (item.Code == "dust" ? 1 : item.Code == "alloy" ? 6 : 0);
            if (price <= 0) return 0;
            float priority = price * item.Quantity / (float)Math.Max(1, def.Width * def.Height);
            if (def.Kind == ItemKind.Cell && needCells) priority += 1000;
            if (def.Kind == ItemKind.Medical && needMedicine) priority += 1000;
            if (needShield && PmcProtectionLoadout.CanUseLootedShield(item)) priority += 900;
            var consumable = needFood || needDrink || needOxygen ? ConsumableCatalog.Get(item.Code) : null;
            if (consumable != null && (needOxygen && consumable.Oxygen > 0 ||
                needFood && consumable.Food || needDrink && !consumable.Food && consumable.Hydration > 0)) priority += 1000;
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
        public static int TakeAvailable(InventoryGraph inventory, InventoryGraph cache, bool includeNested = false,
            bool equipShield = false, bool seekSurvivalSupplies = false)
        {
            if (inventory == null || cache == null || cache.Find(LootInventoryExchange.Root) == null) return 0;
            bool needCells = inventory.CellEnergy(accessibleOnly: true) < BatteryEnergy.Capacity * 2;
            bool needMedicine = inventory.Items.Where(inventory.MedicalAccessible).Sum(i => i.Quantity) < 2;
            bool needShield = equipShield && inventory.Equipped("Shield") == null;
            bool needFood = seekSurvivalSupplies && !PmcNutrition.HasCarried(inventory, true);
            bool needDrink = seekSurvivalSupplies && !PmcNutrition.HasCarried(inventory, false);
            bool needOxygen = seekSurvivalSupplies && !PmcLifeSupport.HasCarried(inventory);
            var occupiedContainers = new HashSet<string>(cache.Items.Select(i => i.Parent));
            var byId = includeNested ? cache.Items.ToDictionary(i => i.Id) : null;
            bool InLootTree(InventoryItem item)
            {
                string parent = item.Parent;
                for (int depth = 0; parent != null && depth <= InventoryCatalog.MaxDepth; depth++)
                {
                    if (parent == LootInventoryExchange.Root) return true;
                    if (!includeNested || !byId.TryGetValue(parent, out var ancestor) || HaloModules.IsWeapon(ancestor.Code)) return false;
                    parent = ancestor.Parent;
                }
                return false;
            }
            var candidates = cache.Items.Where(i => InLootTree(i) && !occupiedContainers.Contains(i.Id))
                .Select(i => (item: i, value: Value(i, needCells, needMedicine, needShield, needFood, needDrink, needOxygen))).Where(candidate => candidate.value > 0)
                .OrderByDescending(candidate => candidate.value).Select(candidate => candidate.item.Id).ToArray();
            int transfers = 0;
            foreach (string id in candidates)
            {
                var source = cache.Find(id);
                if (source == null || cache.RootOf(source.Id) != LootInventoryExchange.Root) continue;
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
                if (equipShield && inventory.Equipped("Shield") == null && PmcProtectionLoadout.CanUseLootedShield(source))
                {
                    var equip = new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                        ItemId = id, Parent = InventoryCatalog.Equipment, Region = "Shield" };
                    if (LootInventoryExchange.TryApply(inventory, cache, equip, cache.Version) == InventoryError.None)
                    { transfers++; continue; }
                }
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
        private readonly List<RaidSpawnObserver> m_LocalExitObservers = new List<RaidSpawnObserver>();

        private bool LocalExitObserved(Vector3 position, PhysicsScene scene)
        {
            m_LocalExitObservers.Clear();
            foreach (var target in m_Targets)
            {
                if (target.Alien || !target.Root || !EntityManager.Exists(target.Entity) ||
                    !EntityManager.HasComponent<GhostOwner>(target.Entity) || !EntityManager.HasComponent<PredictedPlayerGhost>(target.Entity) ||
                    !EntityManager.HasComponent<LocalTransform>(target.Entity) || target.Root.gameObject.scene.GetPhysicsScene() != scene ||
                    EntityManager.GetComponentData<GhostOwner>(target.Entity).NetworkId <= 0) continue;
                var health = EntityManager.GetComponentData<PredictedPlayerGhost>(target.Entity);
                if (health.CurrentHealth <= 0) continue;
                var view = health.ControllerState;
                var look = new Unity.Mathematics.float2(view.YawDegrees, view.PitchDegrees) + view.FreeLookOffset;
                m_LocalExitObservers.Add(new RaidSpawnObserver { Position = EntityManager.GetComponentData<LocalTransform>(target.Entity).Position,
                    Forward = Quaternion.Euler(look.y, look.x, 0) * Vector3.forward, CheckVisibility = true });
            }
            return PmcLocalExitRules.Observed(scene, position, m_LocalExitObservers);
        }

        private static void AbandonCache(DollSingerEnemyBrain brain)
        {
            if (brain.Cache < 0) return;
            brain.VisitedCaches.Add(brain.Cache); brain.Cache = -1; brain.LootUntil = 0;
            brain.CacheApproachKnown = false; brain.CacheObstructedSince = -1;
            brain.NextDecision = brain.NextPath = 0;
        }

        private RaidLootContainers PmcLootContainers()
        {
            if (!SystemAPI.HasSingleton<RaidLootWorld>()) return null;
            var entity = SystemAPI.GetSingletonEntity<RaidLootWorld>();
            return EntityManager.HasComponent<RaidLootContainers>(entity) ? EntityManager.GetComponentObject<RaidLootContainers>(entity) : null;
        }

        private Vector3 SelectCache(DollSingerEnemyBrain brain, Vector3 position, MoonRaidMap map)
        {
            var loot = PmcLootContainers();
            if (brain.Cache >= 0)
            {
                if (PmcLootRules.TryPosition(map.LootPositions, loot, brain.Cache, out var current))
                    return brain.CacheApproachKnown ? brain.CacheApproach : current;
                brain.Cache = -1; brain.CacheApproachKnown = false; brain.CacheObstructedSince = -1;
                brain.LootUntil = 0; brain.NextPath = 0;
            }
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
                best = value; brain.Cache = i; brain.CacheApproachKnown = false;
            }
            foreach (int id in PmcLootRules.NearbyDrawers(loot, position, brain.VisitedCaches))
            {
                if (!PmcLootRules.TryPosition(map.LootPositions, loot, id, out var target) ||
                    !NavMesh.SamplePosition(target, out var sample, 3, m_Filter) ||
                    Vector3.Distance(sample.position, target) > RaidRules.PickupRange - .9f ||
                    WorldBlocked(sample.position + Vector3.up * 1.4f, target) ||
                    !CompletePath(brain, position, sample.position, out float length))
                { brain.VisitedCaches.Add(id); continue; }
                float value = length + (float)(((long)id * 7 + (long)brain.Seed * 11) % 13);
                if (brain.Target != Entity.Null && HorizontalDistance(sample.position, brain.LastSeen) < 12) value += 25;
                if (value >= best) continue;
                best = value; brain.Cache = id; brain.CacheApproach = sample.position; brain.CacheApproachKnown = true;
            }
            brain.CacheObstructedSince = -1;
            if (brain.Cache >= 0)
                return brain.CacheApproachKnown ? brain.CacheApproach : map.LootPositions[brain.Cache];
            brain.Leaving = true;
            brain.CommitUntil = 0;
            return brain.RaidExit;
        }

        private void AdvanceObjective(DollSingerEnemyBrain brain, ref PredictedPlayerGhost health, Vector3 position, Vector3 eye, MoonRaidMap map, double now, float dt)
        {
            if (brain.IsClashBot) { AdvanceClashObjective(brain, position, eye, now); return; }
            if (brain.Action == PmcAction.Scavenge && !PmcLootRules.CanSearch(health, brain.LastDamage, now))
            { brain.LootUntil = 0; brain.CacheObstructedSince = -1; return; }
            var loot = brain.Action == PmcAction.Scavenge ? PmcLootContainers() : null;
            if (brain.Action == PmcAction.Scavenge && brain.LootUntil <= 0 && now >= brain.NextNearbyLootScan &&
                !PmcLootRules.IsNearbyLoot(loot, brain.Cache))
            {
                brain.NextNearbyLootScan = now + 1;
                foreach (int id in PmcLootRules.NearbyLoot(loot, position, brain.VisitedCaches))
                {
                    if (!PmcLootRules.TryPosition(map.LootPositions, loot, id, out var target) || WorldBlocked(eye, target)) continue;
                    brain.Cache = id; brain.CacheApproach = position; brain.CacheApproachKnown = true;
                    brain.CacheObstructedSince = -1; brain.TacticalGoal = position; brain.NextPath = 0;
                    break;
                }
            }
            if (brain.Action == PmcAction.Scavenge && brain.Cache >= 0)
            {
                if (!PmcLootRules.TryPosition(map.LootPositions, loot, brain.Cache, out var cachePosition))
                { brain.Cache = -1; brain.CacheApproachKnown = false; brain.CacheObstructedSince = -1;
                    brain.LootUntil = 0; brain.NextDecision = brain.NextPath = 0; return; }
                bool drawer = RaidFurnitureLoot.IsFurniture(brain.Cache);
                bool corpse = PmcLootRules.IsHumanCorpse(loot, brain.Cache);
                bool ground = PmcLootRules.IsDroppedItem(loot, brain.Cache);
                bool localTarget = drawer || corpse || ground;
                bool reachable = localTarget ? Vector3.Distance(position, cachePosition) <= RaidRules.PickupRange && !WorldBlocked(eye, cachePosition) :
                    HorizontalDistance(position, cachePosition) <= RaidRules.PickupRange && Mathf.Abs(position.y - cachePosition.y) <= 3 &&
                    !WorldBlocked(eye, cachePosition + Vector3.up * .3f);
                if (!reachable)
                {
                    brain.LootUntil = 0;
                    if (localTarget && brain.CacheApproachKnown && Vector3.Distance(position, brain.CacheApproach) <= 1.5f)
                    {
                        if (brain.CacheObstructedSince < 0) brain.CacheObstructedSince = now;
                        if (now - brain.CacheObstructedSince >= 2)
                            AbandonCache(brain);
                    }
                    else brain.CacheObstructedSince = -1;
                    return;
                }
                brain.CacheObstructedSince = -1;
                if (brain.LootUntil == 0)
                {
                    brain.LootUntil = now + m_Tuning.LootSearchSeconds;
                    brain.LootLookPoint = cachePosition;
                    RaidLootAcoustics.Mark(ref health);
                }
                if (now < brain.LootUntil) return;
                int transferred = 0;
                if (loot != null && loot.Containers.TryGetValue(brain.Cache, out var cache))
                {
                    bool equipShield = !TutorialRuntime.Enabled(EntityManager);
                    transferred = PmcLootRules.TakeAvailable(brain.Inventory, cache, includeNested: corpse || ground,
                        equipShield: equipShield, seekSurvivalSupplies: equipShield);
                    if (equipShield) PmcProtectionLoadout.BindLootedShield(ref health, brain.Inventory);
                }
                PmcLootRules.RecordSearch(brain, drawer, ground, transferred);
                brain.VisitedCaches.Add(brain.Cache);
                brain.Cache = -1;
                brain.CacheApproachKnown = false;
                brain.LootUntil = 0;
                brain.NextDecision = brain.NextPath = 0;
            }
            bool extracting = brain.Action == PmcAction.Extract &&
                HorizontalDistance(position, brain.RaidExit) <= map.ExtractionRadius &&
                Mathf.Abs(position.y - brain.RaidExit.y) <= 3 && !brain.Visible &&
                now - brain.LastDamage > 5 && brain.Suppression < .25f;
            if (extracting && brain.LocalRaidExit && (now >= brain.NextLocalExitCheck ||
                brain.ExtractionProgress + dt >= map.ExtractionSeconds))
            {
                brain.LocalExitObserved = LocalExitObserved(position, map.gameObject.scene.GetPhysicsScene());
                brain.NextLocalExitCheck = now + .25;
            }
            if (PmcLocalExitRules.Advance(ref brain.ExtractionProgress, extracting, brain.LocalRaidExit,
                brain.LocalExitObserved, dt, map.ExtractionSeconds))
            {
                brain.Extracted = true;
                Debug.Log($"[DollSinger PMC {brain.Seed}] Extracted with {brain.Inventory.Count("dust")} dust, " +
                    $"{brain.Inventory.Count("alloy")} alloy and {brain.Inventory.Count("cells")} cells.");
            }
        }

    }
}
