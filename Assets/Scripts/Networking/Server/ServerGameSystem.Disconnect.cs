// 在线远征断线后保留原来的角色和背包一小段时间。新连接认证成同一人就接回原角色，角色死亡、超时或过期则对原战局结算。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public struct RaidDetached : IComponentData {
        public double Deadline;
    }

    public partial struct ServerGameSystem {
        public void RaidDisconnectBeforeCleanup(ref SystemState state) {
            if (MoonRaidMap.Active == null || ClashEnabled(ref state) ||
                SystemAPI.ManagedAPI.HasSingleton<ArenaMatch>())
                return;
            // 这里只看真的带了远征会话的连接。断线时先尝试保留，条件不满足就按失败交给结算。
            using var query =
                state.EntityManager.CreateEntityQuery(typeof(NetworkStreamConnection), typeof(RaidSession));
            using var connections = query.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections)
                if (state.EntityManager.GetComponentData<NetworkStreamConnection>(connection).CurrentState ==
                    ConnectionState.State.Disconnected)
                    if (!RetainDisconnectedRaid(ref state, connection))
                        SettleDisconnectedRaid(ref state, connection);
        }

        // 把原连接上的战局状态转给临时实体，再从准备删除的连接上解绑角色。角色留在服务端地图上，原来的输入连接会被清掉。
        private bool RetainDisconnectedRaid(ref SystemState state, Entity connection) {
            var manager = state.EntityManager;
            var context = Persistence(ref state);
            // 在线断线才走这一套暂存。也要确认已认证、正在行动且地图上还有活角色，才有东西可恢复。
            if (SystemAPI.HasSingleton<OfflineRaidMode>() || SystemAPI.HasSingleton<LanRaidMode>() ||
                !manager.HasComponent<JoinedClient>(connection) ||
                !context.Profiles.TryGetValue(connection, out var profile))
                return false;
            var session = manager.GetComponentData<RaidSession>(connection);
            var joined = manager.GetComponentData<JoinedClient>(connection);
            if (session.Phase != RaidPhase.Active || !manager.Exists(joined.PlayerEntity) ||
                !manager.HasComponent<PredictedPlayerGhost>(joined.PlayerEntity) ||
                manager.GetComponentData<PredictedPlayerGhost>(joined.PlayerEntity).CurrentHealth <= 0)
                return false;
            var holder = manager.CreateEntity();
            // 替身保存原会话、角色引用和重连期限。它没有活的网络流，只让 ECS 还能找到这批出战数据。
            manager.AddComponentData(holder, new RaidDetached { Deadline = SystemAPI.Time.ElapsedTime + 90 });
            manager.AddComponentData(holder, session);
            manager.AddComponentData(holder, joined);
            manager.AddComponentData(holder, manager.GetComponentData<NetworkId>(connection));
            manager.AddComponentData(holder, new CommandTarget());
            manager.AddComponentData(holder, new NetworkStreamRequestDisconnect());
            if (!context.RebindBountyOwner(connection, holder, profile.PlayerId, session.SettlementId.ToString())) {
                manager.DestroyEntity(holder);
                return false;
            }
            if (context.Inventories.TryGetValue(holder, out var inventory)) {
                // 断线取消手上仍在读条的动作，关闭搜刮窗口。恢复时保留已确认库存，但不能继续占着箱子或消耗尚未用完的道具。
                inventory.CancelActions();
                inventory.OpenedLootId = -1;
                manager.AddComponentObject(holder, inventory);
            }
            if (manager.HasBuffer<LinkedEntityGroup>(connection)) {
                var links = manager.GetBuffer<LinkedEntityGroup>(connection);
                // 从旧连接的关联组移除角色。否则 NetCode 清理它时会连正想保留的角色一并销毁。
                for (int i = links.Length - 1; i >= 0; i--)
                    if (links[i].Value == joined.PlayerEntity)
                        links.RemoveAt(i);
            }
            var player = joined.PlayerEntity;
            if (manager.HasComponent<PlayerClientCommandInputLookup>(player)) {
                var input = manager.GetComponentData<PlayerClientCommandInputLookup>(player).ClientCommandInputEntity;
                if (manager.Exists(input)) manager.DestroyEntity(input);
                manager.SetComponentData(player,
                    new PlayerClientCommandInputLookup { ClientCommandInputEntity = Entity.Null });
            }
            // 没玩家连接时把角色暂交给服务器。移除旧连接的 RaidSession 后，随后那次常规断线回调不会又把同一局结算为死亡。
            manager.SetComponentData(player, new GhostOwner { NetworkId = 0 });
            // Prevent the later disconnect event from settling the transferred raid again.
            manager.RemoveComponent<RaidSession>(connection);
            return true;
        }

        // 先按已认证的玩家身份找记录，同时检查时间与角色存活。通过后才给原角色绑定新连接和输入实体，并要求库存重新同步。
        private bool TryResumeRetainedRaid(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity connection,
            RaidPersistenceContext.Profile authenticated,
            RaidPersistenceContext.Join join,
            bool notifyLeaderboard = true) {
            var manager = state.EntityManager;
            using var query =
                manager.CreateEntityQuery(typeof(RaidDetached), typeof(RaidSession), typeof(JoinedClient));
            using var holders = query.ToEntityArray(Allocator.Temp);
            var context = Persistence(ref state);
            foreach (var holder in holders) {
                if (!context.Profiles.TryGetValue(holder, out var original) ||
                    original.PlayerId != authenticated.PlayerId)
                    continue;
                var session = manager.GetComponentData<RaidSession>(holder);
                var joined = manager.GetComponentData<JoinedClient>(holder);
                bool living = manager.Exists(joined.PlayerEntity) &&
                              manager.HasComponent<PredictedPlayerGhost>(joined.PlayerEntity) && manager
                                  .GetComponentData<PredictedPlayerGhost>(joined.PlayerEntity).CurrentHealth > 0;
                // 光凭玩家身份还不能恢复。重连期限、本局倒计时、活动阶段和角色存活都要通过。
                if (manager.GetComponentData<RaidDetached>(holder).Deadline <= SystemAPI.Time.ElapsedTime ||
                    session.Phase != RaidPhase.Active || !living || session.TimeLeft <= 0)
                    return false;
                if (!context.RebindBountyOwner(holder, connection, authenticated.PlayerId,
                        session.SettlementId.ToString()))
                    return false;
                session.ExtractionProgress = 0;
                session.ExtractionIndex = -1;
                session.SnapshotTimer = 0;
                ecb.AddComponent(connection, session);
                ecb.AddComponent(connection, joined);
                if (context.Inventories.TryGetValue(connection, out var inventory)) {
                    // 新客户端没有旧连接的库存和遗体标记，强制从完整快照补起。
                    inventory.LastSentVersion = -1;
                    inventory.LastSentLootVersion = -1;
                    inventory.NextDeathBagResync = 0;
                    manager.AddComponentObject(connection, inventory);
                }
                int owner = manager.GetComponentData<NetworkId>(connection).Value;
                // 用新连接的 NetworkId 新建输入 Ghost 并改角色归属，角色本身和档案仍是原局的。
                var input = ecb.Instantiate(SystemAPI.GetSingleton<PlayerEntityPrefabs>().ClientInputEntityPrefab);
                ecb.SetComponent(input, new GhostOwner { NetworkId = owner });
                ecb.AddBuffer<ClientCommandInput>(input);
                ecb.SetComponent(input, new PlayerCommandTarget { NetworkId = owner });
                ecb.SetComponent(joined.PlayerEntity, new GhostOwner { NetworkId = owner });
                ecb.SetComponent(joined.PlayerEntity,
                    new PlayerClientCommandInputLookup { ClientCommandInputEntity = input });
                ecb.SetComponent(connection, new CommandTarget { targetEntity = input });
                ecb.AddComponent(connection, new NetworkStreamInGame());
                ecb.AppendToBuffer(connection, new LinkedEntityGroup { Value = joined.PlayerEntity });
                SystemAPI.GetSingletonBuffer<ClientsMap>().ElementAt(owner).PlayerEntity = joined.PlayerEntity;
                ecb.DestroyEntity(holder);
                if (notifyLeaderboard) AddPlayerToLeaderboard(owner, joined.PlayerName);
                return true;
            }
            return false;
        }

        // 掉线期间本局时间照走，撤离进度清零。到期或角色已经死亡时，从这里保证同一套远征结算流程仍能走完。
        private void PollRetainedRaids(ref SystemState state, EntityCommandBuffer ecb) {
            foreach (var (detached, session, joined, holder) in SystemAPI
                         .Query<RefRO<RaidDetached>, RefRW<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess()) {
                session.ValueRW.TimeLeft = Mathematics.math.max(0, session.ValueRO.TimeLeft - SystemAPI.Time.DeltaTime);
                session.ValueRW.ExtractionProgress = 0;
                session.ValueRW.ExtractionIndex = -1;
                bool dead = !SystemAPI.Exists(joined.ValueRO.PlayerEntity) ||
                            !SystemAPI.HasComponent<PredictedPlayerGhost>(joined.ValueRO.PlayerEntity) ||
                            SystemAPI.GetComponent<PredictedPlayerGhost>(joined.ValueRO.PlayerEntity).CurrentHealth <=
                            0;
                if (!dead && session.ValueRO.TimeLeft > 0 && detached.ValueRO.Deadline > SystemAPI.Time.ElapsedTime)
                    continue;
                // 离线期限先到了且该局还有剩余时间，也收作失败；本局时间用尽则收作超时。两者共用远征结算。
                FinishRaid(ref state, ecb, holder,
                    dead || session.ValueRO.TimeLeft > 0 ? RaidPhase.Dead : RaidPhase.TimedOut);
                ecb.DestroyEntity(holder);
            }
        }

        private void SettleDisconnectedRaid(ref SystemState state, Entity connection) {
            if (SystemAPI.ManagedAPI.HasSingleton<ArenaMatch>()) return;
            var manager = state.EntityManager;
            if (!manager.Exists(connection) || !manager.HasComponent<RaidSession>(connection)) return;
            var session = manager.GetComponentData<RaidSession>(connection);
            if (session.Phase != RaidPhase.Active) return;
            var persistence = Persistence(ref state);
            // Transfer the current carried inventory while JoinedClient and the
            // player's transform still exist. BeginSave snapshots the remainder,
            // including items moved into/out of the secure case during this raid.
            // 无法保留时先用服务器此刻的背包和角色位置生成尸体物资。扣掉掉落后只将余下受保护的物品写回档案。
            DropDeathInventory(ref state, connection);
            if (manager.HasComponent<JoinedClient>(connection))
                ResearchQuestRuntime.Finish(state.World,
                    manager.GetComponentData<JoinedClient>(connection).PlayerEntity, session.RaidId, false);
            if (!RaidRules.TrySettle(ref session, RaidPhase.Dead, !persistence.Enabled)) return;
            if (persistence.Enabled) session.SaveState = RaidSaveState.Saving;
            manager.SetComponentData(connection, session);
            if (persistence.Enabled) persistence.BeginSave(connection, session);
        }
    }
}