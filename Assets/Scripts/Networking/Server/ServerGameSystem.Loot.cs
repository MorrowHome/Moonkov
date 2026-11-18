// 服务端管理箱子、敌人尸体和地面丢弃物。开箱与搬东西时从服务端角色的位置和动作判断可访问性，客户端收到的只是在当前搜索阶段能看到的内容。
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public partial struct ServerGameSystem {
        private RaidLootContainers LootContainers(ref SystemState state) {
            return state.EntityManager.GetComponentObject<RaidLootContainers>(
                SystemAPI.GetSingletonEntity<RaidLootWorld>());
        }

        // 第一次按地图点位创建各处容器。后续每帧都用同一套容器记录，而不是打开箱子时从头抽一份。
        private void InitializeLootContainers(ref SystemState state) {
            var map = MoonRaidMap.Active;
            if (map == null) return;
            var caches = LootContainers(ref state);
            if (caches.Initialized && ReferenceEquals(caches.MapIdentity, map)) return;
            caches.Initialize(map.LootPositions.Length, map.GetLootProfile, map);
            if (RaidFurnitureLoot.Active && RaidFurnitureLoot.Active.gameObject.scene == map.gameObject.scene)
                RaidFurnitureLoot.Active.Initialize(caches);
            foreach (var (session, connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess()) {
                if (!state.EntityManager.HasComponent<RaidInventoryState>(connection)) continue;
                var inventory = GetRaidInventory(ref state, connection);
                inventory.OpenedLootId = -1;
                inventory.CancelActions();
                inventory.ClearSearchRecords();
                inventory.LastSentVersion = -1;
                inventory.LastSentLootVersion = -1;
                inventory.NextDeathBagResync = 0;
            }
        }

        // 箱子的补货状态按服务器时间推。没人进行远征、有人在看这个箱子或空箱附近有人时，会挡住补货。
        private void RefreshLootContainers(ref SystemState state) {
            var map = MoonRaidMap.Active;
            if (map == null) return;
            var caches = LootContainers(ref state);
            double now = SystemAPI.Time.ElapsedTime;
            if (now < caches.NextRefillCheck) return;
            caches.NextRefillCheck = now + 1;
            // 先收集玩家与敌人当前查看或搜索中的箱子，补货和回收都跳过这些目标，免得正在看时内容被刷新。
            var protectedLoot = new System.Collections.Generic.HashSet<int>();
            foreach (var (session, connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess()) {
                if (session.ValueRO.Phase != RaidPhase.Active ||
                    !state.EntityManager.HasComponent<RaidInventoryState>(connection) ||
                    SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                    continue;
                var inventory = GetRaidInventory(ref state, connection);
                if (inventory.OpenedLootId >= 0) protectedLoot.Add(inventory.OpenedLootId);
                if (inventory.DissectingLootId >= 0) protectedLoot.Add(inventory.DissectingLootId);
            }
            foreach (var (health, entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithNone<ClashBot>().WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0 ||
                    !state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity))
                    continue;
                var brain = state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                if (PmcLootRules.SearchingCache(brain)) protectedLoot.Add(brain.Cache);
            }
            // 过期空掉落可被回收。极速赏金虽然也回收空包，但不会沿用远征箱子的补货流程。
            caches.RecycleEmptyDrops(now, protectedLoot);
            if (ClashEnabled(ref state)) return; // Keep empty-drop cleanup without replenishing Clash supplies.
            bool hasActivePlayer = false;
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>()
                         .WithEntityAccess())
                if (session.ValueRO.Phase == RaidPhase.Active &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) &&
                    SystemAPI.HasComponent<NetworkId>(connection) &&
                    SystemAPI.GetComponent<NetworkId>(connection).Value > 0 &&
                    TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out _)) {
                    hasActivePlayer = true;
                    break;
                }
            float radius = math.max(RaidRules.PickupRange, map.LootRefillPlayerRadius);
            foreach (var refill in caches.RefillStates) {
                if (!caches.Containers.TryGetValue(refill.Key, out var cache) || refill.Key >= map.LootPositions.Length)
                    continue;
                bool blocked = !hasActivePlayer || protectedLoot.Contains(refill.Key);
                if (!blocked && !RaidLootRefillState.HasContents(cache)) {
                    float3 target = map.LootPositions[refill.Key];
                    foreach (var (session, joined, connection) in SystemAPI
                                 .Query<RefRO<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess()) {
                        if (session.ValueRO.Phase != RaidPhase.Active ||
                            SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                            !TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var position))
                            continue;
                        if (math.distancesq(position, target) <= radius * radius ||
                            (state.EntityManager.HasComponent<RaidInventoryState>(connection) &&
                             GetRaidInventory(ref state, connection).OpenedLootId == refill.Key)) {
                            blocked = true;
                            break;
                        }
                    }
                }
                refill.Value.TryRefill(cache, now, map.LootRespawnSeconds, map.LootRefillLimit, blocked,
                    caches.LootRandom);
            }
        }

        private void DropDeathInventory(ref SystemState state, Entity connection, bool initialize = true) {
            if (MoonRaidMap.Active == null || !state.EntityManager.HasComponent<RaidInventoryState>(connection) ||
                !SystemAPI.HasComponent<JoinedClient>(connection))
                return;
            if (initialize) InitializeLootContainers(ref state);
            var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<LocalTransform>(player)) return;
            var inventory = GetRaidInventory(ref state, connection);
            UpdateInventoryProtection(ref state, connection);
            // 死亡前先将角色的稀有装备余量写进物品，再按当前姿态建掉落。要确保拾到的是角色死前实际用过的那件。
            StoreDeathLoot(LootContainers(ref state), inventory.Graph, SystemAPI.GetComponent<LocalTransform>(player),
                SystemAPI.GetComponent<JoinedClient>(connection).CharacterIndex, SystemAPI.Time.ElapsedTime);
            inventory.CloseLoot();
        }

        // 尸体位置用角色最后的姿态往附近地板对齐。命中斜面时调整朝向，生成的容器与尸体表现共用这个位置。
        private static void StoreDeathLoot(
            RaidLootContainers loot,
            InventoryGraph graph,
            LocalTransform pose,
            int character,
            double now) {
            var position = pose.Position;
            var rotation = pose.Rotation;
            if (UnityEngine.Physics.Raycast((Vector3)position + Vector3.up, Vector3.down, out var ground, 4,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) {
                position = ground.point;
                rotation = Quaternion.FromToRotation(Vector3.up, ground.normal) * (Quaternion)pose.Rotation;
            }
            loot.Drop(graph, position, rotation, character, now);
        }

        private bool CanAccessLoot(
            ref SystemState state,
            Entity connection,
            int lootId,
            RaidSession session,
            ClassicMatch classic = null) {
            var map = MoonRaidMap.Active;
            if (map == null || session.Phase != RaidPhase.Active ||
                !TryGetLootPosition(ref state, lootId, out var target) ||
                SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                !SystemAPI.HasComponent<JoinedClient>(connection))
                return false;
            var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            // 访问箱子需要活角色靠近真实交互点，后续还要过玩法边界和视线。客户端提交坐标不参与判定。
            if (!TryGetLivingPosition(ref state, player, out var position) || math.distancesq(position, target) >
                RaidRules.PickupRange * RaidRules.PickupRange)
                return false;
            if (classic != null) {
                // 经典爆破只在允许搜刮的回合阶段开放，并要求这人属于当前回合。其他模式按角色和目标是否在合法区域判断。
                if (!classic.LootOpen || !SystemAPI.HasComponent<ClashPlayer>(player) ||
                    SystemAPI.GetComponent<ClashPlayer>(player).Epoch != classic.Round)
                    return false;
            } else if (!BountyClashRuntime.CanAct(state.EntityManager, player) ||
                       !BountyClashRuntime.InArena(state.EntityManager, position) ||
                       !BountyClashRuntime.InArena(state.EntityManager, target)) { return false; }
            return !UnityEngine.Physics.Linecast((Vector3)position + Vector3.up * 1.4f, (Vector3)target,
                LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
        }

        // 同一个打开请求支持地图资源箱、抽屉、任务缓存和尸体。服务端按容器 ID 到这些实际来源里取坐标。
        private bool TryGetLootPosition(ref SystemState state, int id, out float3 position) {
            position = default;
            var map = MoonRaidMap.Active;
            if (map != null && id >= 0 && id < map.LootPositions.Length) {
                position = map.LootPositions[id];
                return true;
            }
            if (LootContainers(ref state).Containers.ContainsKey(id) &&
                LootContainers(ref state).FurnitureCaches.TryGetValue(id, out position))
                return true;
            if (LootContainers(ref state).MissionCaches.TryGetValue(id, out position)) return true;
            if (!LootContainers(ref state).DeathBags.TryGetValue(id, out var corpse)) return false;
            position = corpse.Position + new float3(0, .25f, 0);
            return true;
        }

        private void SendDeathBags(ref SystemState state, EntityCommandBuffer ecb, Entity connection) {
            var loot = LootContainers(ref state);
            var inventory = GetRaidInventory(ref state, connection);
            // 遗体和地面物每个连接单独维护已发版本。只发变化，隔一会再补发实际仍在的标记给客户端校正。
            bool resync = SystemAPI.Time.ElapsedTime >= inventory.NextDeathBagResync;
            if (resync) inventory.NextDeathBagResync = SystemAPI.Time.ElapsedTime + 5;
            float3 position = default;
            bool living = SystemAPI.HasComponent<JoinedClient>(connection) && TryGetLivingPosition(ref state,
                SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity, out position);
            inventory.HiddenDeathBags.Clear();
            foreach (var sent in inventory.SentDeathBags)
                if (!loot.DeathBags.TryGetValue(sent.Key, out var bag) ||
                    (bag.CharacterIndex == RaidLootContainers.DroppedItemCharacterIndex &&
                     (!living || math.distancesq(position, bag.Position) > RaidLootContainers.DropInterestRadius *
                         RaidLootContainers.DropInterestRadius) && inventory.OpenedLootId != sent.Key &&
                     inventory.DissectingLootId != sent.Key))
                    inventory.HiddenDeathBags.Add(sent.Key);
            // 离开视野和真正销毁是两回事。回包带 Deleted，让客户端决定只是暂隐，还是废除这个 ID 和搜索记录。
            foreach (int id in inventory.HiddenDeathBags) {
                var removed = ecb.CreateEntity();
                ecb.AddComponent(removed,
                    new RaidGroundItemMarkerRpc {
                        LootId = id, Sequence = ++inventory.DeathBagSequence, Removed = true,
                        Deleted = !loot.DeathBags.ContainsKey(id)
                    });
                ecb.AddComponent(removed, new SendRpcCommandRequest { TargetConnection = connection });
                inventory.SentDeathBags.Remove(id);
                if (!loot.DeathBags.ContainsKey(id)) inventory.SearchedLoot.Remove(id);
            }
            inventory.RemovedSearches.Clear();
            foreach (int id in inventory.SearchedLoot)
                if (id >= RaidLootContainers.FirstDeathBagId && id < RaidLootContainers.FirstMissionCacheId &&
                    !loot.DeathBags.ContainsKey(id))
                    inventory.RemovedSearches.Add(id);
            foreach (int id in inventory.RemovedSearches) inventory.SearchedLoot.Remove(id);
            foreach (var bag in loot.DeathBags) {
                if (bag.Value.CharacterIndex == RaidLootContainers.DroppedItemCharacterIndex &&
                    (!living || math.distancesq(position, bag.Value.Position) > RaidLootContainers.DropInterestRadius *
                        RaidLootContainers.DropInterestRadius) && inventory.OpenedLootId != bag.Key &&
                    inventory.DissectingLootId != bag.Key)
                    continue;
                var graph = loot.Containers[bag.Key];
                if (!resync && inventory.SentDeathBags.TryGetValue(bag.Key, out int sent) && sent == graph.Version)
                    continue;
                inventory.SentDeathBags[bag.Key] = graph.Version;
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new RaidGroundItemMarkerRpc {
                    Sequence = ++inventory.DeathBagSequence, LootId = bag.Key, Version = graph.Version,
                    Position = bag.Value.Position,
                    Rotation = bag.Value.Rotation, CharacterIndex = bag.Value.CharacterIndex,
                    Age = (float)math.max(0, SystemAPI.Time.ElapsedTime - bag.Value.CreatedAt),
                    Empty = !RaidLootRefillState.HasContents(graph),
                    ItemCode = bag.Value.CharacterIndex == RaidLootContainers.DroppedItemCharacterIndex
                        ? new FixedString64Bytes(graph.Items.Find(item => item.Parent == LootInventoryExchange.Root)
                            ?.Code ?? "")
                        : default
                });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        private void HandleLootRequests(ref SystemState state, EntityCommandBuffer ecb, ClassicMatch classic = null) {
            var caches = LootContainers(ref state);
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidLootOpenRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (SystemAPI.HasComponent<RaidSession>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection);
                    if (r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        bool eligible = r.RaidId == session.RaidId && inventory.RaidId == r.RaidId &&
                                        session.Phase == RaidPhase.Active &&
                                        !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) &&
                                        (r.LootId == -1 || (!caches.IsEmptyDrop(r.LootId) &&
                                                            CanAccessLoot(ref state, connection, r.LootId, session,
                                                                classic)));
                        bool weaponBusy = eligible && r.LootId >= 0 &&
                                          RaidHealth.SearchHandsBusy(
                                              SystemAPI.GetComponent<PredictedPlayerGhost>(SystemAPI
                                                  .GetComponent<JoinedClient>(connection).PlayerEntity));
                        bool accepted = eligible && !weaponBusy;
                        if (accepted) {
                            if (r.LootId < 0) { inventory.CloseLoot(); } else {
                                if (classic != null && r.LootId < ClassicDust2Map.SupplyCacheCount &&
                                    classic.RestockCache(r.LootId, caches.Containers[r.LootId]))
                                    inventory.SearchedLoot.Remove(r.LootId);
                                // 开始找新容器会中止之前仍在读条的物品或任务动作，避免一双手同时完成多件事。
                                inventory.CancelActions(inventory.ActionPending ? "原操作已取消：切换搜刮目标；未消耗物品。" : null);
                                CancelManualMission(ref state, connection, "任务操作中断：开始搜索。");
                                inventory.OpenedLootId = r.LootId;
                            }
                            double now = SystemAPI.Time.ElapsedTime;
                            if (r.LootId >= 0 && now >= inventory.NextLootNoiseAt) {
                                var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                                var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                                RaidLootAcoustics.Mark(ref health.ValueRW);
                                inventory.NextLootNoiseAt = now + RaidLootAcoustics.MinimumInterval;
                            }
                        }
                        // 允许打开后仍要判断是否需要重新搜索。首次发现或箱子已补货，按这轮物品数量与规则重新计时。
                        if (accepted && r.LootId >= 0 && inventory.NeedsSearch(caches, r.LootId)) {
                            var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                            inventory.BeginDissection(r.LootId, SystemAPI.Time.ElapsedTime,
                                SystemAPI.GetComponent<LocalTransform>(player).Position,
                                SystemAPI.GetComponent<PredictedPlayerGhost>(player),
                                caches.RequiresDissection(r.LootId)
                                    ? AlienHarvest.DissectionSeconds
                                    : MoonRaidMap.Active.SearchSeconds, caches.RefillGeneration(r.LootId),
                                caches.Containers[r.LootId].Children(LootInventoryExchange.Root).Count(),
                                caches.Containers[r.LootId]);
                            inventory.NextLootNoiseAt = System.Math.Max(inventory.NextLootNoiseAt,
                                SystemAPI.Time.ElapsedTime + RaidLootAcoustics.SearchInterval);
                        } else if (accepted && r.LootId >= 0) {
                            CollectResearchSample(ref state, caches, connection, session.RaidId, r.LootId);
                        }
                        if (weaponBusy) inventory.ActionFeedback = "操作未开始：正在换弹或切换武器。";
                        inventory.Error = accepted ? InventoryError.None : InventoryError.Inaccessible;
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            // 即使刚才已经开过箱，搬物品时仍要重新确认够不够近和是否合法。背包、箱子两个版本要一起匹配，防止多人同时拿到同一件东西。
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidLootMoveRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (SystemAPI.HasComponent<RaidSession>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection);
                    if (r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        inventory.ActionFeedback = null;
                        UpdateInventoryProtection(ref state, connection);
                        bool chargeBlocked = ReloadWeaponMoveBlocked(ref state, connection, r.Operation,
                            r.ItemId.ToString(), r.Parent.ToString(), r.Region.ToString());
                        inventory.Error =
                            r.RaidId != session.ValueRO.RaidId || inventory.RaidId != r.RaidId || chargeBlocked ||
                            !CanAccessLoot(ref state, connection, r.LootId, session.ValueRO, classic)
                                ? InventoryError.Inaccessible
                                : inventory.TryLootMove(caches, r.LootId, new InventoryCommand {
                                    ExpectedVersion = r.ExpectedVersion, Operation = r.Operation,
                                    ItemId = r.ItemId.ToString(), Parent = r.Parent.ToString(),
                                    Region = r.Region.ToString(), TargetId = r.TargetId.ToString(),
                                    X = r.X, Y = r.Y, Rotated = r.Rotated, Quantity = r.Quantity
                                }, r.ExpectedLootVersion);
                        if (chargeBlocked) inventory.ActionFeedback = "充能中不能移动当前武器。";
                        if (inventory.Error == InventoryError.None) {
                            ResearchQuestRuntime.InventoryChanged(state.World, connection, session.ValueRO.RaidId);
                            UpdateInventoryProtection(ref state, connection, true);
                        }
                        inventory.LastSentVersion = -1;
                        if (session.ValueRO.Phase == RaidPhase.Active)
                            RaidInventoryState.UpdateTotals(inventory.Graph, ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
        }

        private void RefreshLootAccess(
            ref SystemState state,
            Entity connection,
            RaidSession session,
            ClassicMatch classic = null) {
            var inventory = GetRaidInventory(ref state, connection);
            // 每帧根据存活、模式和人物动作继续检查治疗或吃喝。最终成功时写回血量、止血等字段，中止则保留道具。
            if (inventory.MedicalItemId != null) {
                var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                bool accessible = session.Phase == RaidPhase.Active && inventory.RaidId == session.RaidId &&
                                  BountyClashRuntime.CanAct(state.EntityManager, player) &&
                                  TryGetLivingPosition(ref state, player, out _);
                var health = accessible ? SystemAPI.GetComponent<PredictedPlayerGhost>(player) : default;
                var position = accessible
                    ? SystemAPI.GetComponent<LocalTransform>(player).Position
                    : inventory.MedicalOrigin;
                bool wasBleeding = health.TacticalBleeding;
                inventory.RefreshMedical(ref health, accessible, position, SystemAPI.Time.ElapsedTime);
                if (accessible && wasBleeding && !health.TacticalBleeding)
                    TacticalStatus(TacticalWorld(ref state), player).Bleed = 0;
                if (accessible) SystemAPI.SetComponent(player, health);
            }
            var actionPlayer = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if (SystemAPI.Exists(actionPlayer) && SystemAPI.HasComponent<PredictedPlayerGhost>(actionPlayer))
                SystemAPI.GetComponentRW<PredictedPlayerGhost>(actionPlayer).ValueRW.ConsumingSupply =
                    session.Phase == RaidPhase.Active && inventory.MedicalItemId != null &&
                    inventory.MedicalIsConsumable;
            // 搜索中再次验证位置和可访问性。解剖或搜索完成才登记箱子已搜，符合条件的异形尸体还会记一条研究采样。
            if (inventory.DissectingLootId >= 0) {
                int id = inventory.DissectingLootId;
                bool accessible = CanAccessLoot(ref state, connection, id, session, classic) &&
                                  inventory.RaidId == session.RaidId;
                var position = inventory.DissectionOrigin;
                var health = default(PredictedPlayerGhost);
                if (accessible) {
                    var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                    position = SystemAPI.GetComponent<LocalTransform>(player).Position;
                    health = SystemAPI.GetComponent<PredictedPlayerGhost>(player);
                }
                var caches = LootContainers(ref state);
                inventory.RefreshDissection(caches, accessible, position, health, SystemAPI.Time.ElapsedTime);
                if (accessible && inventory.DissectingLootId < 0 && inventory.OpenedLootId == id &&
                    !inventory.NeedsSearch(caches, id))
                    CollectResearchSample(ref state, caches, connection, session.RaidId, id);
            }
            if (SystemAPI.Exists(actionPlayer) && SystemAPI.HasComponent<PredictedPlayerGhost>(actionPlayer)) {
                var audioState = SystemAPI.GetComponentRW<PredictedPlayerGhost>(actionPlayer);
                RaidLootAcoustics.RefreshSearch(ref audioState.ValueRW, inventory.DissectingLootId >= 0,
                    SystemAPI.Time.ElapsedTime, ref inventory.NextLootNoiseAt);
            }
            if (inventory.OpenedLootId < 0 ||
                (classic == null && session.Phase == RaidPhase.Active && session.SnapshotTimer > 0))
                return;
            // 玩家走远或被墙挡住后，即使旧箱曾经开过也要强制关掉。用重新发送的库存回执让客户端同步关闭。
            if (CanAccessLoot(ref state, connection, inventory.OpenedLootId, session, classic)) return;
            inventory.OpenedLootId = -1;
            inventory.Error = InventoryError.Inaccessible;
            inventory.LastSentVersion = -1;
        }

        private void CollectResearchSample(
            ref SystemState state,
            RaidLootContainers caches,
            Entity connection,
            int raidId,
            int lootId) {
            if (!caches.DeathBags.TryGetValue(lootId, out var corpse) || corpse.CharacterIndex != -1) return;
            var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            ResearchQuestRuntime.CollectSample(state.World, connection, player, raidId, lootId,
                SystemAPI.Time.ElapsedTime);
        }
    }
}