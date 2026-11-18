// 管理普通远征 AI 的搜刮和撤离目标。找能到、能看到的容器，完成搜索后按需求和容量挑物资，也会判断有无安全路线离场。
// 拿取物品走与玩家相同的库存版本事务。
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

namespace Unity.MP_FPS {
    public static class PmcLootRules {
        // 先看角色是否真的活着、健康到可以搜、没有刚受伤或手忙。不能只因为到了箱子旁就开始计时。
        public static bool CanSearch(in PredictedPlayerGhost health, double lastDamage, double now) {
            return !RaidHealth.HandsBusy(health) && now - lastDamage >= 1;
        }

        // Keep the same container stable until this real search completes or is
        // interrupted, including the tick where its timer has just elapsed.
        public static bool SearchingCache(DollSingerEnemyBrain brain) {
            return brain != null && !brain.IsClashBot && brain.Action == PmcAction.Scavenge && brain.Cache >= 0 &&
                   brain.LootUntil > 0;
        }

        public static void RecordSearch(DollSingerEnemyBrain brain, bool drawer, bool ground, int transferred) {
            if (ground) {
                if (transferred > 0 && ++brain.GroundItemsLooted % 3 == 0) brain.LootedCaches++;
            } else if (!drawer || ++brain.DrawersSearched % 3 == 0) { brain.LootedCaches++; }
        }

        public static bool IsHumanCorpse(RaidLootContainers caches, int id) {
            return caches != null && caches.DeathBags.TryGetValue(id, out var corpse) && corpse.CharacterIndex >= 0 &&
                   caches.Containers.ContainsKey(id);
        }

        public static bool IsDroppedItem(RaidLootContainers caches, int id) {
            return caches != null && caches.DeathBags.TryGetValue(id, out var drop) &&
                   drop.CharacterIndex == RaidLootContainers.DroppedItemCharacterIndex &&
                   caches.Containers.ContainsKey(id) && !caches.IsEmptyDrop(id);
        }

        public static bool IsNearbyLoot(RaidLootContainers caches, int id) {
            return IsHumanCorpse(caches, id) || IsDroppedItem(caches, id);
        }

        public static bool TryPosition(
            Vector3[] fixedPositions,
            RaidLootContainers caches,
            int id,
            out Vector3 position) {
            position = default;
            if (id >= 0 && fixedPositions != null && id < fixedPositions.Length) {
                position = fixedPositions[id];
                return true;
            }
            if (IsNearbyLoot(caches, id)) {
                position = (Vector3)caches.DeathBags[id].Position + Vector3.up * .25f;
                return true;
            }
            if (!RaidFurnitureLoot.IsFurniture(id) || caches == null || !caches.Containers.ContainsKey(id) ||
                !caches.FurnitureCaches.TryGetValue(id, out var drawer))
                return false;
            position = drawer;
            return true;
        }

        // Only nearby, registered furniture is considered. Contents remain unknown until searched.
        public static int[] NearbyDrawers(RaidLootContainers caches, Vector3 position, HashSet<int> visited) {
            if (caches == null) return Array.Empty<int>();
            return caches.FurnitureCaches.Where(pair => RaidFurnitureLoot.IsFurniture(pair.Key) &&
                                                        caches.Containers.ContainsKey(pair.Key) &&
                                                        (visited == null || !visited.Contains(pair.Key)) &&
                                                        ((Vector3)pair.Value - position).sqrMagnitude <= 20 * 20)
                .OrderBy(pair => ((Vector3)pair.Value - position).sqrMagnitude).ThenBy(pair => pair.Key).Take(12)
                .Select(pair => pair.Key).ToArray();
        }

        // Examine at most four nearby bodies or nonempty ground drops for line of sight.
        public static int[] NearbyLoot(RaidLootContainers caches, Vector3 position, HashSet<int> visited) {
            if (caches == null) return Array.Empty<int>();
            return caches.DeathBags.Where(pair => IsNearbyLoot(caches, pair.Key) &&
                                                  (visited == null || !visited.Contains(pair.Key)) &&
                                                  ((Vector3)pair.Value.Position + Vector3.up * .25f - position)
                                                  .sqrMagnitude <= RaidRules.PickupRange * RaidRules.PickupRange)
                .OrderBy(pair => ((Vector3)pair.Value.Position + Vector3.up * .25f - position).sqrMagnitude)
                .ThenBy(pair => pair.Key).Take(4).Select(pair => pair.Key).ToArray();
        }

        // 拾取优先级按当前缺什么算。电池、护盾、医药和生存品有各自用途，容量也会约束能拿多少。
        private static float Value(
            InventoryItem item,
            bool needCells,
            bool needMedicine,
            bool needShield,
            bool needFood,
            bool needDrink,
            bool needOxygen) {
            var def = InventoryCatalog.Get(item?.Code);
            if (def == null || def.Kind == ItemKind.Root || MissionInventory.IsArtifact(item.Code)) return 0;
            int price = ShopCatalog.Find(item.Code)?.SellDust ??
                        (item.Code == "dust" ? 1 : item.Code == "alloy" ? 6 : 0);
            if (price <= 0) return 0;
            float priority = price * item.Quantity / (float)Math.Max(1, def.Width * def.Height);
            if (def.Kind == ItemKind.Cell && needCells) priority += 1000;
            if (def.Kind == ItemKind.Medical && needMedicine) priority += 1000;
            if (needShield && PmcProtectionLoadout.CanUseLootedShield(item)) priority += 900;
            var consumable = needFood || needDrink || needOxygen ? ConsumableCatalog.Get(item.Code) : null;
            if (consumable != null && ((needOxygen && consumable.Oxygen > 0) || (needFood && consumable.Food) ||
                                       (needDrink && !consumable.Food && consumable.Hydration > 0)))
                priority += 1000;
            return priority;
        }

        private static IEnumerable<string> Destinations(InventoryGraph inventory, bool usable) {
            string rig = inventory.Equipped("ChestRig")?.Id, pack = inventory.Equipped("Backpack")?.Id;
            if (!usable && pack != null) yield return pack;
            yield return InventoryCatalog.Pockets;
            if (rig != null) yield return rig;
            if (usable && pack != null) yield return pack;
        }

        private static int WeightAllowance(InventoryGraph inventory, ItemDefinition definition, int desired) {
            if (definition.Weight <= 0) return desired;
            double free = Math.Max(0, InventoryCatalog.CarryWeightLimit - inventory.CarriedWeight);
            return Math.Min(desired, (int)Math.Floor((free + .0001) / definition.Weight));
        }

        // All moves use the same versioned transaction as a player. The candidate list
        // holds IDs because a successful transaction replaces both inventory trees.
        // 准备搜刮事务时先记物品实例 ID。成功交换可能换掉两边的库存树，继续遍历旧物品引用会读到过期数据。
        public static int TakeAvailable(
            InventoryGraph inventory,
            InventoryGraph cache,
            bool includeNested = false,
            bool equipShield = false,
            bool seekSurvivalSupplies = false) {
            if (inventory == null || cache == null || cache.Find(LootInventoryExchange.Root) == null) return 0;
            bool needCells = inventory.CellEnergy(accessibleOnly: true) < BatteryEnergy.Capacity * 2;
            bool needMedicine = inventory.Items.Where(inventory.MedicalAccessible).Sum(i => i.Quantity) < 2;
            bool needShield = equipShield && inventory.Equipped("Shield") == null;
            bool needFood = seekSurvivalSupplies && !PmcNutrition.HasCarried(inventory, true);
            bool needDrink = seekSurvivalSupplies && !PmcNutrition.HasCarried(inventory, false);
            bool needOxygen = seekSurvivalSupplies && !PmcLifeSupport.HasCarried(inventory);
            var occupiedContainers = new HashSet<string>(cache.Items.Select(i => i.Parent));
            var byId = includeNested ? cache.Items.ToDictionary(i => i.Id) : null;

            bool InLootTree(InventoryItem item) {
                string parent = item.Parent;
                for (int depth = 0; parent != null && depth <= InventoryCatalog.MaxDepth; depth++) {
                    if (parent == LootInventoryExchange.Root) return true;
                    if (!includeNested || !byId.TryGetValue(parent, out var ancestor) ||
                        HaloModules.IsWeapon(ancestor.Code))
                        return false;
                    parent = ancestor.Parent;
                }
                return false;
            }

            string[] candidates = cache.Items.Where(i => InLootTree(i) && !occupiedContainers.Contains(i.Id))
                .Select(i => (item: i,
                    value: Value(i, needCells, needMedicine, needShield, needFood, needDrink, needOxygen)))
                .Where(candidate => candidate.value > 0).OrderByDescending(candidate => candidate.value)
                .Select(candidate => candidate.item.Id).ToArray();
            int transfers = 0;
            foreach (string id in candidates) {
                var source = cache.Find(id);
                if (source == null || cache.RootOf(source.Id) != LootInventoryExchange.Root) continue;
                var def = InventoryCatalog.Get(source.Code);
                bool usable = def.Kind == ItemKind.Cell || def.Kind == ItemKind.Medical;
                string[] destinations = Destinations(inventory, usable).ToArray();
                if (!def.Container) {
                    string[] targets = destinations
                        .SelectMany(parent => inventory.Items.Where(i => i.Parent == parent && i.Code == source.Code))
                        .Select(i => i.Id).ToArray();
                    foreach (string targetId in targets) {
                        source = cache.Find(id);
                        var target = inventory.Find(targetId);
                        if (source == null) break;
                        int count = WeightAllowance(inventory, def,
                            Math.Min(source.Quantity, def.MaxStack - target.Quantity));
                        if (count <= 0 || source.FoundInRaid != target.FoundInRaid ||
                            source.EmergencySupply != target.EmergencySupply ||
                            !BatteryEnergy.CanMerge(source, target, count))
                            continue;
                        var command = new InventoryCommand {
                            Operation = InventoryOperation.Merge, ExpectedVersion = inventory.Version,
                            ItemId = id, TargetId = targetId, Quantity = count
                        };
                        if (LootInventoryExchange.TryApply(inventory, cache, command, cache.Version) ==
                            InventoryError.None)
                            transfers++;
                    }
                }
                source = cache.Find(id);
                if (source == null) continue;
                int amount = WeightAllowance(inventory, def, source.Quantity);
                if (amount <= 0) continue;
                if (equipShield && inventory.Equipped("Shield") == null &&
                    PmcProtectionLoadout.CanUseLootedShield(source)) {
                    var equip = new InventoryCommand {
                        Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                        ItemId = id, Parent = InventoryCatalog.Equipment, Region = "Shield"
                    };
                    if (LootInventoryExchange.TryApply(inventory, cache, equip, cache.Version) == InventoryError.None) {
                        transfers++;
                        continue;
                    }
                }
                foreach (string parent in destinations) {
                    bool moved = false;
                    for (int orientation = 0; orientation < (def.Width == def.Height ? 1 : 2); orientation++) {
                        var probe = source.Clone();
                        if (orientation == 1) probe.Rotated = !probe.Rotated;
                        if (!inventory.FindSpace(probe, parent, out string region, out int x, out int y)) continue;
                        var command = new InventoryCommand {
                            Operation = amount < source.Quantity ? InventoryOperation.Split : InventoryOperation.Move,
                            ExpectedVersion = inventory.Version, ItemId = id, Quantity = amount, Parent = parent,
                            Region = region, X = x, Y = y, Rotated = probe.Rotated
                        };
                        if (LootInventoryExchange.TryApply(inventory, cache, command, cache.Version) !=
                            InventoryError.None)
                            continue;
                        transfers++;
                        moved = true;
                        break;
                    }
                    if (moved) break;
                }
            }
            return transfers;
        }
    }

    public partial class DollSingerEnemySystem {
        private readonly List<RaidSpawnObserver> m_LocalExitObservers = new();
        private readonly List<CachePathCandidate> m_CachePaths = new(64);

        private struct CachePathCandidate : IComparable<CachePathCandidate> {
            public int Index;
            public Vector3 Position;
            public float LowerBound;

            public int CompareTo(CachePathCandidate other) {
                int order = LowerBound.CompareTo(other.LowerBound);
                return order != 0 ? order : Index.CompareTo(other.Index);
            }
        }

        // 回入口消失前，检查是否有玩家很近或从更远处直接看着它。要在可能被目击的情况下保留角色。
        private bool LocalExitObserved(Vector3 position, PhysicsScene scene) {
            m_LocalExitObservers.Clear();
            foreach (var target in m_Targets) {
                if (target.Alien || !target.Root || !EntityManager.Exists(target.Entity) ||
                    !EntityManager.HasComponent<GhostOwner>(target.Entity) ||
                    !EntityManager.HasComponent<PredictedPlayerGhost>(target.Entity) ||
                    !EntityManager.HasComponent<LocalTransform>(target.Entity) ||
                    target.Root.gameObject.scene.GetPhysicsScene() != scene ||
                    EntityManager.GetComponentData<GhostOwner>(target.Entity).NetworkId <= 0)
                    continue;
                var health = EntityManager.GetComponentData<PredictedPlayerGhost>(target.Entity);
                if (health.CurrentHealth <= 0) continue;
                var view = health.ControllerState;
                var look = new Mathematics.float2(view.YawDegrees, view.PitchDegrees) + view.FreeLookOffset;
                m_LocalExitObservers.Add(new RaidSpawnObserver {
                    Position = EntityManager.GetComponentData<LocalTransform>(target.Entity).Position,
                    Forward = Quaternion.Euler(look.y, look.x, 0) * Vector3.forward, CheckVisibility = true,
                    Aiming = view.Aiming
                });
            }
            return PmcLocalExitRules.Observed(scene, position, m_LocalExitObservers);
        }

        // 选定的物资箱走不通或操作中断时收掉这次搜索，让下一轮决策重新选目标。
        private static void AbandonCache(DollSingerEnemyBrain brain) {
            if (brain.Cache < 0) return;
            brain.VisitedCaches.Add(brain.Cache);
            brain.Cache = -1;
            brain.LootUntil = 0;
            brain.CacheApproachKnown = false;
            brain.CacheObstructedSince = -1;
            brain.NextDecision = brain.NextPath = 0;
        }

        private RaidLootContainers PmcLootContainers() {
            if (!SystemAPI.HasSingleton<RaidLootWorld>()) return null;
            var entity = SystemAPI.GetSingletonEntity<RaidLootWorld>();
            return EntityManager.HasComponent<RaidLootContainers>(entity)
                ? EntityManager.GetComponentObject<RaidLootContainers>(entity)
                : null;
        }

        private static readonly Profiling.ProfilerMarker s_SelectCacheProfile = new("Moonkov.Pmc.SelectCache");

        // 空闲时通过距离和可走路线挑箱子。附近抽屉、尸体等按另一组条件找到，不会预先知道里面都有什么。
        private Vector3 SelectCache(DollSingerEnemyBrain brain, Vector3 position, MoonRaidMap map) {
            using var profile = s_SelectCacheProfile.Auto();
            var loot = PmcLootContainers();
            if (brain.Cache >= 0) {
                if (PmcLootRules.TryPosition(map.LootPositions, loot, brain.Cache, out var current))
                    return brain.CacheApproachKnown ? brain.CacheApproach : current;
                brain.Cache = -1;
                brain.CacheApproachKnown = false;
                brain.CacheObstructedSince = -1;
                brain.LootUntil = 0;
                brain.NextPath = 0;
            }
            float best = float.MaxValue;
            m_CachePaths.Clear();
            bool hasStart = NavMesh.SamplePosition(position, out var start, 3, m_Filter);
            for (int i = 0; i < map.LootPositions.Length; i++) {
                if (brain.VisitedCaches.Contains(i)) continue;
                if (!NavMesh.SamplePosition(map.LootPositions[i], out var sample, 3, m_Filter)) {
                    brain.VisitedCaches.Add(i);
                    continue;
                }
                float bound = (i * 7 + brain.Seed * 11) % 13;
                if (hasStart) bound += Vector3.Distance(start.position, sample.position);
                if (brain.Target != Entity.Null && HorizontalDistance(sample.position, brain.LastSeen) < 12)
                    bound += 25;
                m_CachePaths.Add(new CachePathCandidate { Index = i, Position = sample.position, LowerBound = bound });
            }
            m_CachePaths.Sort();
            foreach (var candidate in m_CachePaths) {
                // A route cannot be shorter than the straight line between its snapped
                // endpoints. Visit promising caches first, then stop once even that lower
                // bound loses. Keep padding for large-world floating-point roundoff.
                if (candidate.LowerBound > best + .05f) break;
                int i = candidate.Index;
                if (!CompletePath(brain, position, candidate.Position, out float length)) {
                    brain.VisitedCaches.Add(i);
                    continue;
                }
                // Different route preferences distribute independent raiders across the map.
                float value = length + (i * 7 + brain.Seed * 11) % 13;
                if (brain.Target != Entity.Null && HorizontalDistance(candidate.Position, brain.LastSeen) < 12)
                    value += 25;
                // Sorting must retain the original index-order tie break.
                if (value > best || (value == best && i > brain.Cache)) continue;
                best = value;
                brain.Cache = i;
                brain.CacheApproachKnown = false;
            }
            foreach (int id in PmcLootRules.NearbyDrawers(loot, position, brain.VisitedCaches)) {
                if (!PmcLootRules.TryPosition(map.LootPositions, loot, id, out var target) ||
                    !NavMesh.SamplePosition(target, out var sample, 3, m_Filter) ||
                    Vector3.Distance(sample.position, target) > RaidRules.PickupRange - .9f ||
                    WorldBlocked(sample.position + Vector3.up * 1.4f, target) ||
                    !CompletePath(brain, position, sample.position, out float length)) {
                    brain.VisitedCaches.Add(id);
                    continue;
                }
                float value = length + (float)(((long)id * 7 + (long)brain.Seed * 11) % 13);
                if (brain.Target != Entity.Null && HorizontalDistance(sample.position, brain.LastSeen) < 12)
                    value += 25;
                if (value >= best) continue;
                best = value;
                brain.Cache = id;
                brain.CacheApproach = sample.position;
                brain.CacheApproachKnown = true;
            }
            brain.CacheObstructedSince = -1;
            if (brain.Cache >= 0)
                return brain.CacheApproachKnown ? brain.CacheApproach : map.LootPositions[brain.Cache];
            brain.Leaving = true;
            brain.CommitUntil = 0;
            return brain.RaidExit;
        }

        private void AdvanceObjective(
            DollSingerEnemyBrain brain,
            ref PredictedPlayerGhost health,
            Vector3 position,
            Vector3 eye,
            MoonRaidMap map,
            double now,
            float dt) {
            if (brain.IsClashBot) {
                AdvanceClashObjective(brain, position, eye, now);
                return;
            }
            // 动作条件失效就清掉进度。稍后恢复时必须按新观察重新开始。
            if (brain.Action == PmcAction.Scavenge && !PmcLootRules.CanSearch(health, brain.LastDamage, now)) {
                brain.LootUntil = 0;
                brain.CacheObstructedSince = -1;
                return;
            }
            var loot = brain.Action == PmcAction.Scavenge ? PmcLootContainers() : null;
            if (brain.Action == PmcAction.Scavenge && brain.LootUntil <= 0 && now >= brain.NextNearbyLootScan &&
                !PmcLootRules.IsNearbyLoot(loot, brain.Cache)) {
                brain.NextNearbyLootScan = now + 1;
                // 走路时隔一会留意未搜的尸体和地上物品，通过近距离视线后才切目标。
                foreach (int id in PmcLootRules.NearbyLoot(loot, position, brain.VisitedCaches)) {
                    if (!PmcLootRules.TryPosition(map.LootPositions, loot, id, out var target) ||
                        WorldBlocked(eye, target))
                        continue;
                    brain.Cache = id;
                    brain.CacheApproach = position;
                    brain.CacheApproachKnown = true;
                    brain.CacheObstructedSince = -1;
                    brain.TacticalGoal = position;
                    brain.NextPath = 0;
                    break;
                }
            }
            if (brain.Action == PmcAction.Scavenge && brain.Cache >= 0) {
                if (!PmcLootRules.TryPosition(map.LootPositions, loot, brain.Cache, out var cachePosition)) {
                    brain.Cache = -1;
                    brain.CacheApproachKnown = false;
                    brain.CacheObstructedSince = -1;
                    brain.LootUntil = 0;
                    brain.NextDecision = brain.NextPath = 0;
                    return;
                }
                bool drawer = RaidFurnitureLoot.IsFurniture(brain.Cache);
                bool corpse = PmcLootRules.IsHumanCorpse(loot, brain.Cache);
                bool ground = PmcLootRules.IsDroppedItem(loot, brain.Cache);
                bool localTarget = drawer || corpse || ground;
                // 近处对象按三维交互距离与遮挡检查，固定箱还限制楼层高度。隔着墙或楼板站近不算到达。
                bool reachable = localTarget
                    ? Vector3.Distance(position, cachePosition) <= RaidRules.PickupRange &&
                      !WorldBlocked(eye, cachePosition)
                    : HorizontalDistance(position, cachePosition) <= RaidRules.PickupRange &&
                      Mathf.Abs(position.y - cachePosition.y) <= 3 &&
                      !WorldBlocked(eye, cachePosition + Vector3.up * .3f);
                if (!reachable) {
                    brain.LootUntil = 0;
                    if (localTarget && brain.CacheApproachKnown &&
                        Vector3.Distance(position, brain.CacheApproach) <= 1.5f) {
                        if (brain.CacheObstructedSince < 0) brain.CacheObstructedSince = now;
                        if (now - brain.CacheObstructedSince >= 2) AbandonCache(brain);
                    } else { brain.CacheObstructedSince = -1; }
                    return;
                }
                brain.CacheObstructedSince = -1;
                if (brain.LootUntil == 0) {
                    // 开始搜索记录结束时间和面向点，同时向场内发布可被听到的搜刮事件。
                    brain.LootUntil = now + m_Tuning.LootSearchSeconds;
                    brain.LootLookPoint = cachePosition;
                    RaidLootAcoustics.Mark(ref health);
                }
                if (now < brain.LootUntil) return;
                // 读条结束这次才允许向容器拿取，并按是否允许携带装备或补给走共用库存规则。
                int transferred = 0;
                if (loot != null && loot.Containers.TryGetValue(brain.Cache, out var cache)) {
                    bool equipShield = !TutorialRuntime.Enabled(EntityManager);
                    transferred = PmcLootRules.TakeAvailable(brain.Inventory, cache, corpse || ground, equipShield,
                        equipShield);
                    if (equipShield) PmcProtectionLoadout.BindLootedShield(ref health, brain.Inventory);
                }
                PmcLootRules.RecordSearch(brain, drawer, ground, transferred);
                brain.VisitedCaches.Add(brain.Cache);
                brain.Cache = -1;
                brain.CacheApproachKnown = false;
                brain.LootUntil = 0;
                brain.NextDecision = brain.NextPath = 0;
            }
            // 撤离要求到出口范围且脱离战斗，暂时受压或正在被看到要等。入口回退还要查是否会被观察到消失。
            bool extracting = brain.Action == PmcAction.Extract &&
                              HorizontalDistance(position, brain.RaidExit) <= map.ExtractionRadius &&
                              Mathf.Abs(position.y - brain.RaidExit.y) <= 3 && !brain.Visible &&
                              now - brain.LastDamage > 5 && brain.Suppression < .25f;
            if (extracting && brain.LocalRaidExit && (now >= brain.NextLocalExitCheck ||
                                                      brain.ExtractionProgress + dt >= map.ExtractionSeconds)) {
                brain.LocalExitObserved = LocalExitObserved(position, map.gameObject.scene.GetPhysicsScene());
                brain.NextLocalExitCheck = now + .25;
            }
            if (PmcLocalExitRules.Advance(ref brain.ExtractionProgress, extracting, brain.LocalRaidExit,
                    brain.LocalExitObserved, dt, map.ExtractionSeconds)) {
                brain.Extracted = true;
                Debug.Log($"[DollSinger PMC {brain.Seed}] Extracted with {brain.Inventory.Count("dust")} dust, " +
                          $"{brain.Inventory.Count("alloy")} alloy and {brain.Inventory.Count("cells")} cells.");
            }
        }
    }
}