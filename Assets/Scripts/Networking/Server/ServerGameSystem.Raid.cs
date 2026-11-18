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
            // 远征地图还供极速赏金复用，但地图任务和研究只在远征推进。
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
            // 先处理玩家本帧的取放请求，再推进箱子补货并更新各容器是否为空。地图标记看的是实际剩余内容。
            HandleLootRequests(ref state, ecb);
            HandleInventoryRequests(ref state, ecb);
            RefreshLootContainers(ref state);
            float dt = SystemAPI.Time.DeltaTime;
            var loot = SystemAPI.GetSingletonRW<RaidLootWorld>();
            // 箱体仍保留在场景中。服务器只为确认安全且完全空掉的箱补货。
            loot.ValueRW.TakenMask = 0;
            foreach (var cache in caches.Containers)
                if (cache.Key < RaidLootContainers.FirstDeathBagId &&
                    !cache.Value.Items.Exists(i => i.Parent == Inventory.LootInventoryExchange.Root))
                    loot.ValueRW.TakenMask |= 1UL << cache.Key;
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidPickupRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
                // 直接消费旧版瞬时拾取请求，不据此生成新物品。
                ecb.DestroyEntity(entity);
            // 旧局返回后想再开，需要确认出战号已结束、存档不再写入。官方调度的局还要走新的大厅分配。
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
                            // 立即标记待处理，反复到达的 RPC 不能再排第二次扣装。
                            // 先锁定这次再部署，再启动异步扣装。相同 RPC 在任务还未完成时不能重复进入。
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
                // 每个仍有效的连接先推进治疗和搜索等个人动作，再根据是否真的活着计算倒计时及撤离。
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
                    // 档案明确有进行中的研究时才为这局计算任务事件。只把对应本次行动的角色、库存和 tick 交进去。
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
                    // 即使同一帧满足撤离，也优先判超时。死亡在进入本方法前已处理。
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
                // 撤离前把当前武器已经开始的充能状态结算好，最后保存的是带出那一刻的真实装备余量。
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
                // 结算清单趁背包完整时生成。稍后尸体会接管部分物品，但结果页仍可以展示哪件带回、哪件遗失。
                inventory.Result = RaidResultManifest.Capture(inventory.Graph, session.ValueRO.RaidId, outcome);
                inventory.LastSentVersion = -1;
            }
            // 趁死亡实体还有位置时完成一次物品归属转移。
            // 遗失数量仍记在 RaidSession 上给结算页展示。
            if (outcome == RaidPhase.Dead && session.ValueRO.Phase == RaidPhase.Active && MoonRaidMap.Active != null)
                DropDeathInventory(ref state, connection);
            // 同一场远征只允许从进行中结算一次。服务端得到这次阶段变化后，才向持久化上下文提交保存。
            if (!RaidRules.TrySettle(ref session.ValueRW, outcome, !persistence.Enabled)) return;
            // 支持档案写入时标记 Saving 并提交结算。同一份快照会带这个状态，客户端能等后端确认再继续。
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
                // 销毁角色前解除旧关联，连接留下用于结算与下次出战。
                // 结束角色和命令实体时保留连接，用它接结果页、档案回执与可能的下次出战。
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