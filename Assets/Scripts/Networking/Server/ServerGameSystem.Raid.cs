// 远征阶段推进与结果收口。每局追踪倒计时和撤离条件，角色撤离、死亡或超时后更新掉落、结果物品清单和存档。
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS {
    public partial struct ServerGameSystem {
        private void HandleRaids(ref SystemState state, EntityCommandBuffer ecb) {
            var map = MoonRaidMap.Active;
            if (map == null) return;
            var caches = LootContainers(ref state);
            InitializeLootContainers(ref state);
            bool clash = ClashEnabled(ref state);
            var research = !clash ? Persistence(ref state) : null;
            if (!clash) HandleMissions(ref state, ecb);
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<ResearchQuestHoldRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                var intent = request.ValueRO;
                if (!clash && SystemAPI.HasComponent<RaidSession>(connection) &&
                    SystemAPI.HasComponent<JoinedClient>(connection) &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    var raid = SystemAPI.GetComponent<RaidSession>(connection);
                    if (raid.Phase == RaidPhase.Active && raid.RaidId == intent.RaidId)
                        ResearchQuestRuntime.Hold(state.World, connection,
                            SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity, raid.RaidId, intent.Device,
                            intent.Pressed);
                }
                ecb.DestroyEntity(entity);
            }
            HandleLootRequests(ref state, ecb);
            HandleInventoryRequests(ref state, ecb);
            RefreshLootContainers(ref state);
            float dt = SystemAPI.Time.DeltaTime;
            var loot = SystemAPI.GetSingletonRW<RaidLootWorld>();
            // Shells remain in the world; the server refills only safe, completely empty caches.
            loot.ValueRW.TakenMask = 0;
            foreach (var cache in caches.Containers)
                if (cache.Key < RaidLootContainers.FirstDeathBagId &&
                    !cache.Value.Items.Exists(i => i.Parent == Inventory.LootInventoryExchange.Root))
                    loot.ValueRW.TakenMask |= 1UL << cache.Key;
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidPickupRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
                // Consume obsolete instant-pickup intents without generating new items.
                ecb.DestroyEntity(entity);
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidDeployRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                if (clash) {
                    ecb.DestroyEntity(entity);
                    continue;
                }
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) &&
                    SystemAPI.HasComponent<JoinedClient>(connection) &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection);
                    int cells = request.ValueRO.CarryCells;
                    if (!OnlineInstanceServer.Managed &&
                        RaidRules.CanDeploy(session.ValueRO, request.ValueRO.SettledRaidId)) {
                        session.ValueRW.LoadoutRequestId = request.ValueRO.RequestId;
                        if (!RaidRules.ValidLoadout(cells)) {
                            session.ValueRW.LoadoutError = RaidLoadoutError.InvalidCount;
                            session.ValueRW.SnapshotTimer = 0;
                        } else if (Persistence(ref state).Enabled) {
                            // Mark immediately; repeated RPCs cannot enqueue a second debit.
                            session.ValueRW.DeployPending = true;
                            session.ValueRW.LoadoutError = RaidLoadoutError.None;
                            session.ValueRW.SnapshotTimer = 0;
                            Persistence(ref state).BeginDeploy(connection, cells);
                        } else if (RaidRules.TryDeploy(ref session.ValueRW, request.ValueRO.SettledRaidId,
                                       map.RaidDuration, cells)) {
                            var joined = SystemAPI.GetComponent<JoinedClient>(connection);
                            SpawnPlayerCharacter(ref state, ecb, connection, joined.PlayerName, joined.CharacterIndex);
                        } else {
                            session.ValueRW.LoadoutError = RaidLoadoutError.InsufficientCells;
                            session.ValueRW.SnapshotTimer = 0;
                        }
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRW<RaidSession>, RefRO<JoinedClient>>()
                         .WithEntityAccess()) {
                if (SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) continue;
                RefreshLootAccess(ref state, connection, session.ValueRO);
                SendInventory(ref state, ecb, connection, session.ValueRO.RaidId);
                if (clash) {
                    var match = ClashWorld(ref state).Match;
                    session.ValueRW.TimeLeft =
                        match != null ? (float)math.max(0, match.Deadline - SystemAPI.Time.ElapsedTime) : 0;
                    session.ValueRW.ExtractionProgress = 0;
                    session.ValueRW.ExtractionIndex = -1;
                }
                if (!clash && session.ValueRO.Phase == RaidPhase.Active &&
                    TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var position)) {
                    session.ValueRW.TimeLeft = math.max(0, session.ValueRO.TimeLeft - dt);
                    if (research != null && research.Profiles.TryGetValue(connection, out var profile) &&
                        profile.Mode == null && profile.ResearchQuests != null && profile.ResearchQuests.Active != 0) {
                        var player = joined.ValueRO.PlayerEntity;
                        ResearchQuestRuntime.Tick(state.World, connection, player, session.ValueRO,
                            GetRaidInventory(ref state, connection), profile.ResearchQuests,
                            SystemAPI.GetComponent<PredictedPlayerGhost>(player), position, SystemAPI.Time.ElapsedTime,
                            dt, SystemAPI.GetSingleton<NetworkTime>().ServerTick.TickIndexForValidTick);
                    }
                    // 用服务器已确认的玩家位置累计撤离进度。同一帧既走完整个撤离读条又到时间时，按超时结算。
                    map.AdvanceExtraction(ref session.ValueRW, (Vector3)position, dt);
                    // Expiry takes precedence over extraction; death is handled before this method.
                    if (session.ValueRO.TimeLeft <= 0)
                        FinishRaid(ref state, ecb, connection, RaidPhase.TimedOut);
                    else if (session.ValueRO.ExtractionProgress >= map.ExtractionSeconds)
                        FinishRaid(ref state, ecb, connection, RaidPhase.Extracted);
                }
                // 每帧都算倒计时和撤离。定时发送给界面的快照可以低频更新，需要立刻显示结算或错误时再把定时器清零。
                session.ValueRW.SnapshotTimer -= dt;
                if (session.ValueRO.SnapshotTimer <= 0) {
                    SendDeathBags(ref state, ecb, connection);
                    if (!clash && session.ValueRO.Phase == RaidPhase.Active && ResearchQuestRuntime.TryRead(state.World,
                            joined.ValueRO.PlayerEntity, session.ValueRO.RaidId, out var quest)) {
                        var questRpc = ecb.CreateEntity();
                        ecb.AddComponent(questRpc, quest);
                        ecb.AddComponent(questRpc, new SendRpcCommandRequest { TargetConnection = connection });
                    }
                    session.ValueRW.SnapshotTimer = 0.25f;
                    session.ValueRW.SnapshotSequence++;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new RaidSnapshotV2Rpc {
                        RaidId = session.ValueRO.RaidId, Sequence = session.ValueRO.SnapshotSequence,
                        Phase = session.ValueRO.Phase,
                        LoadoutRequestId = session.ValueRO.LoadoutRequestId,
                        SaveState = session.ValueRO.SaveState,
                        DeployPending = session.ValueRO.DeployPending, LoadoutError = session.ValueRO.LoadoutError,
                        CellStackId = session.ValueRO.CellStackId,
                        Dust = session.ValueRO.Dust, Alloy = session.ValueRO.Alloy, Cells = session.ValueRO.Cells,
                        StashDust = session.ValueRO.StashDust, StashAlloy = session.ValueRO.StashAlloy,
                        StashCells = session.ValueRO.StashCells,
                        TimeLeft = session.ValueRO.TimeLeft, TakenMask = loot.ValueRO.TakenMask,
                        ExtractionRemaining = session.ValueRO.ExtractionProgress > 0
                            ? math.max(0, map.ExtractionSeconds - session.ValueRO.ExtractionProgress)
                            : -1
                    });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }
        }

        private bool TryGetLivingPosition(ref SystemState state, Entity player, out float3 position) {
            position = default;
            if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<LocalTransform>(player) ||
                !SystemAPI.HasComponent<PredictedPlayerGhost>(player) ||
                SystemAPI.GetComponent<PredictedPlayerGhost>(player).CurrentHealth <= 0)
                return false;
            position = SystemAPI.GetComponent<LocalTransform>(player).Position;
            return true;
        }

        // 趁角色和背包还在，先保存结果清单及需要的现场数据。随后才做阶段结算、请求落盘和销毁角色。连接暂留给显示结果和后续操作。
        private void FinishRaid(ref SystemState state, EntityCommandBuffer ecb, Entity connection, RaidPhase outcome) {
            var session = SystemAPI.GetComponentRW<RaidSession>(connection);
            var persistence = Persistence(ref state);
            if (!ClashEnabled(ref state) && SystemAPI.HasSingleton<RaidLootWorld>())
                ObserveCampaign(ref state,
                    state.EntityManager.GetComponentObject<RaidMissionWorld>(
                        SystemAPI.GetSingletonEntity<RaidLootWorld>()));
            UpdateInventoryProtection(ref state, connection);
            if (!ClashEnabled(ref state) && session.ValueRO.Phase == RaidPhase.Active &&
                (outcome == RaidPhase.Extracted || outcome == RaidPhase.Dead || outcome == RaidPhase.TimedOut) &&
                state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                var inventory = GetRaidInventory(ref state, connection);
                if (outcome == RaidPhase.Extracted && SystemAPI.HasComponent<JoinedClient>(connection)) {
                    var extractingPlayer = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                    if (SystemAPI.Exists(extractingPlayer) &&
                        SystemAPI.HasComponent<PredictedPlayerGhost>(extractingPlayer)) {
                        var ghost = SystemAPI.GetComponentRW<PredictedPlayerGhost>(extractingPlayer);
                        var weapon =
                            WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(ghost.ValueRO.EquippedWeaponID,
                                ghost.ValueRO.EquippedModules);
                        if (weapon != null)
                            DollSingerWeapons.CompleteExtractedCharge(ref ghost.ValueRW, inventory.Graph,
                                weapon.MagazineSize);
                    }
                }
                inventory.Result = RaidResultManifest.Capture(inventory.Graph, session.ValueRO.RaidId, outcome);
                inventory.LastSentVersion = -1;
            }
            // Commit ownership transfer exactly once, while the dead entity still has a
            // transform. Keep loss totals in RaidSession for the result screen only.
            if (outcome == RaidPhase.Dead && session.ValueRO.Phase == RaidPhase.Active && MoonRaidMap.Active != null)
                DropDeathInventory(ref state, connection);
            // 同一场远征只允许从进行中结算一次。服务端得到这次阶段变化后，才向持久化上下文提交保存。
            if (!RaidRules.TrySettle(ref session.ValueRW, outcome, !persistence.Enabled)) return;
            if (persistence.Enabled) {
                session.ValueRW.SaveState = RaidSaveState.Saving;
                persistence.BeginSave(connection, session.ValueRO,
                    ResearchQuestRuntime.Finish(state.World,
                        SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity, session.ValueRO.RaidId,
                        outcome == RaidPhase.Extracted));
            }
            var joined = SystemAPI.GetComponentRW<JoinedClient>(connection);
            var player = joined.ValueRO.PlayerEntity;
            if (SystemAPI.Exists(player)) {
                if (SystemAPI.HasComponent<PlayerClientCommandInputLookup>(player)) {
                    var input = SystemAPI.GetComponent<PlayerClientCommandInputLookup>(player).ClientCommandInputEntity;
                    if (SystemAPI.Exists(input)) ecb.DestroyEntity(input);
                }
                // Remove the stale link before despawn; the connection stays alive for settlement/redeploy.
                if (state.EntityManager.HasBuffer<LinkedEntityGroup>(connection)) {
                    var linked = state.EntityManager.GetBuffer<LinkedEntityGroup>(connection);
                    for (int i = linked.Length - 1; i >= 0; i--)
                        if (linked[i].Value == player)
                            linked.RemoveAt(i);
                }
                ecb.DestroyEntity(player);
            }
            joined.ValueRW.PlayerEntity = Entity.Null;
            ecb.SetComponent(connection, new CommandTarget { targetEntity = Entity.Null });
            int networkId = SystemAPI.GetComponent<NetworkId>(connection).Value;
            var clients = SystemAPI.GetSingletonBuffer<ClientsMap>();
            if (networkId < clients.Length && clients[networkId].ConnectionEntity == connection)
                clients.ElementAt(networkId).PlayerEntity = Entity.Null;
        }
    }
}