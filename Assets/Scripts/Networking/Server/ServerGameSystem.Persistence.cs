// 把异步档案结果接回服务端每帧更新。认证和出战装备确认后才生成玩家，结算成功或失败时把结果反映到本局状态。
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS {
    public partial struct ServerGameSystem {
        private Entity m_PersistenceEntity;

        private RaidPersistenceContext Persistence(ref SystemState state) {
            if (!state.EntityManager.HasComponent<RaidPersistenceContext>(m_PersistenceEntity)) {
                // 持久化由服务器自己的模式标记决定。竞技场临时装备不落盘，单人读本机、好友房读房主档，其他服务器调用账号服务。
                var context = SystemAPI.ManagedAPI.HasSingleton<ArenaMatch>()
                    ? RaidPersistenceContext.ForArena()
                    :
                    SystemAPI.HasSingleton<OfflineRaidMode>()
                        ?
                        new RaidPersistenceContext(PlayerProfileClient.OfflineStore)
                        : SystemAPI.HasSingleton<LanRaidMode>()
                            ? new RaidPersistenceContext(LanRoomClient.Server.Persistence)
                            : new RaidPersistenceContext();
                state.EntityManager.AddComponentObject(m_PersistenceEntity, context);
            }
            var persistence = state.EntityManager.GetComponentObject<RaidPersistenceContext>(m_PersistenceEntity);
            persistence.SetClashSoloEnabled(ClashSoloRuntime.Enabled(state.EntityManager));
            return persistence;
        }

        public void OnDestroy(ref SystemState state) {
            // 结束 World 时先清本局研究状态，再关闭档案上下文。仍在等待的结算会进入持久化自己的关闭任务。
            ResearchQuestRuntime.Clear(state.World);
            if (state.EntityManager.Exists(m_PersistenceEntity) &&
                state.EntityManager.HasComponent<RaidPersistenceContext>(m_PersistenceEntity))
                Persistence(ref state).Dispose();
        }

        private void PollPersistence(ref SystemState state, EntityCommandBuffer ecb) {
            var context = Persistence(ref state);
            if (!context.Enabled) return;
            var removeJoins = new List<Entity>();
            foreach (var pair in context.Joins) {
                var connection = pair.Key;
                var join = pair.Value;
                // 主线程每帧只查结果，不等磁盘或 HTTP。连接没了就移除入场等待，认证失败则通知断连。
                if (!join.Task.IsCompleted) continue;
                if (!SystemAPI.Exists(connection) || !SystemAPI.HasComponent<NetworkId>(connection) ||
                    SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    removeJoins.Add(connection);
                    continue;
                }
                if (join.Task.IsFaulted || join.Task.IsCanceled) {
                    removeJoins.Add(connection);
                    Debug.LogError("[Raid] Cannot load persistent profile; connection rejected. " +
                                   join.Task.Exception?.GetBaseException().GetType().Name);
                    ecb.AddComponent(connection,
                        new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    continue;
                }
                var profile = join.Task.Result;
                // 官方调度的一场有固定实例号。票据已验证但属于另一场时也不能入场。
                if (OnlineInstanceServer.Managed && profile.OnlineInstanceId != OnlineInstanceServer.InstanceId) {
                    removeJoins.Add(connection);
                    ecb.AddComponent(connection,
                        new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    continue;
                }
                // 重连前等待之前的待发回执确认，然后重读已提交的仓库。
                // 同一账号还有未结算回执时暂缓接收资格。等结算成功再刷新账号仓库，否则会拿到扣出战装前后的旧库存。
                bool pendingReceipt = false;
                foreach (var save in context.Saves.Values)
                    if (save.Payload.PlayerId == profile.PlayerId)
                        pendingReceipt = true;
                if (pendingReceipt) {
                    join.NeedsRefresh = true;
                    continue;
                }
                if (join.NeedsRefresh) {
                    context.RefreshJoin(join);
                    continue;
                }
                removeJoins.Add(connection);
                // 即使复制了相同访客凭据，也不能同时以同一身份进这个 World 两次。
                // 同一个档案不能同时被两个活连接使用。掉线保留用的替身实体可重绑，不算第二个在线玩家。
                bool alreadyPlaying = false;
                foreach (var existing in context.Profiles)
                    if (existing.Key != connection && SystemAPI.Exists(existing.Key) &&
                        existing.Value.PlayerId == profile.PlayerId &&
                        !SystemAPI.HasComponent<RaidDetached>(existing.Key) &&
                        (!BountyClashRuntime.Enabled(state.EntityManager) ||
                         (SystemAPI.HasComponent<NetworkStreamConnection>(existing.Key) &&
                          !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(existing.Key))))
                        alreadyPlaying = true;
                if (alreadyPlaying) {
                    Debug.LogWarning("[Raid] This profile is already connected to this server.");
                    ecb.AddComponent(connection,
                        new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    continue;
                }
                // 优先尝试接回已经在地图上的角色。重连专用请求恢复失败就直接拒绝，不能改成新局再扣一套装备。
                if (!BountyClashRuntime.Enabled(state.EntityManager) &&
                    TryResumeRetainedRaid(ref state, ecb, connection, profile, join))
                    continue;
                if (join.ResumeOnly && !BountyClashRuntime.Enabled(state.EntityManager)) {
                    var rejected = ecb.CreateEntity();
                    ecb.AddComponent(rejected, new ClashJoinAcceptedRpc {
                        Mode = RaidGameMode.Expedition, Accepted = false,
                        Reason = "无法恢复原战局：角色已死亡、服务器已重启或重连窗口已过。不会自动开始新战局。"
                    });
                    ecb.AddComponent(rejected, new SendRpcCommandRequest { TargetConnection = connection });
                    ecb.AddComponent(connection,
                        new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    continue;
                }
                // 官方实例记录已经正式入场的账号。原出战已结算后，不让同一账号拿这场资格再生一个角色。
                if (OnlineInstanceServer.Managed && !state.EntityManager
                        .GetComponentObject<OnlineRaidWorld>(SystemAPI.GetSingletonEntity<RaidLootWorld>())
                        .DeployedAccounts.Add(profile.PlayerId)) {
                    ecb.AddComponent(connection,
                        new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    continue;
                }
                context.Profiles[connection] = profile;
                // 认证完成使用档案里的名字和归属。玩家提交的显示名能用于等待，不是服务端确认的身份。
                join.Name.CopyFromTruncated(profile.DisplayName);
                if (BountyClashRuntime.Enabled(state.EntityManager)) {
                    CompleteClashJoin(ref state, ecb, connection, join);
                    continue;
                }
                // 有远征地图才需要划正式出战装备。先启动异步扣装，回执里有完整可用的库存才生成玩家。
                if (MoonRaidMap.Active != null) { context.BeginDeploy(connection, join.CarryCells, join); } else {
                    SpawnPlayerCharacter(ref state, ecb, connection, join.Name, join.CharacterIndex);
                    AddPlayerToLeaderboard(SystemAPI.GetComponent<NetworkId>(connection).Value, join.Name);
                }
            }
            foreach (var connection in removeJoins) context.Joins.Remove(connection);
            // 出战请求发出后连接也可能断掉。即使没生成角色，已扣的装备也要按原出战记录走退款或放弃路线，不能把未完成的写入忘掉。
            var removeDeploys = new List<Entity>();
            foreach (var pair in context.Deployments) {
                var deploy = pair.Value;
                var connection = pair.Key;
                if (MoonRaidMap.Active == null || !SystemAPI.Exists(connection) ||
                    !SystemAPI.HasComponent<NetworkId>(connection) ||
                    SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    // 连接已经消失时，也要接住可能已扣装的结果。
                    if (deploy.Payload.Mode == "BountyClash") {
                        if (deploy.Task != null && !deploy.Task.IsCompleted) continue;
                        context.RefundBountyDeployment(deploy.Payload);
                    } else { context.Abandon(deploy.Payload); }
                    removeDeploys.Add(connection);
                    continue;
                }
                if (deploy.Task == null) {
                    if (SystemAPI.Time.ElapsedTime >= deploy.RetryAt) deploy.Task = context.DeployAsync(deploy.Payload);
                    continue;
                }
                if (!deploy.Task.IsCompleted) continue;
                if (deploy.Task.IsFaulted || deploy.Task.IsCanceled) {
                    if (deploy.Payload.Mode == "BountyClash" &&
                        !ClashDeploymentAllowed(ref state, connection, deploy.Payload.DeploymentId)) {
                        context.RefundBountyDeployment(deploy.Payload);
                        removeDeploys.Add(connection);
                        continue;
                    }
                    // 明确的出装拒绝要结束等待并把具体原因带回。网络或暂时读写失败还可能成功，所以保留同次出战编号定时重试。
                    if (deploy.Task.Exception?.GetBaseException() is RaidPersistenceContext.LoadoutRejectedException
                        rejected) {
                        if (deploy.Payload.Mode == "BountyClash")
                            context.RefundBountyDeployment(deploy.Payload);
                        else
                            context.Abandon(deploy.Payload);
                        removeDeploys.Add(connection);
                        if (deploy.Join != null) {
                            if (rejected.Error == RaidLoadoutError.ResearchSuppliesNoSpace) {
                                var reason = ecb.CreateEntity();
                                ecb.AddComponent(reason, new ClashJoinAcceptedRpc {
                                    Mode = RaidGameMode.Expedition, Accepted = false,
                                    Reason = "任务配发物品放不下。请回仓库腾出空间后再出战。"
                                });
                                ecb.AddComponent(reason, new SendRpcCommandRequest { TargetConnection = connection });
                            }
                            ecb.AddComponent(connection,
                                new NetworkStreamRequestDisconnect
                                    { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                        } else {
                            var raid = SystemAPI.GetComponentRW<RaidSession>(connection);
                            raid.ValueRW.DeployPending = false;
                            raid.ValueRW.LoadoutError = rejected.Error;
                            raid.ValueRW.SnapshotTimer = 0;
                        }
                    } else {
                        deploy.Task = null;
                        deploy.RetryAt = SystemAPI.Time.ElapsedTime + 3;
                    }
                    continue;
                }
                // 扣装回包里的库存来自正式档案。入场时建立的在线队伍归属从认证回执带过来，不能被普通库存响应清空。
                var profile = deploy.Task.Result;
                if (context.Profiles.TryGetValue(connection, out var admission)) {
                    profile.OnlineInstanceId = admission.OnlineInstanceId;
                    profile.OnlineTeam = admission.OnlineTeam;
                }
                context.Profiles[connection] = profile;
                // 下一场共用比赛仍需保留此连接的个人出战代数
                // 和快照序号，防止旧库存 RPC 改到新的出战装备。
                // 新一轮赏金还沿用该连接的出战计数。旧局延迟送达的背包 RPC 要靠个人出战号和快照序号挡在外面。
                bool continueClashSession = deploy.Payload.Mode == "BountyClash" &&
                                            SystemAPI.HasComponent<RaidSession>(connection);
                var session = continueClashSession || deploy.Join == null
                    ? SystemAPI.GetComponent<RaidSession>(connection)
                    : default;
                RaidRules.BeginNext(ref session,
                    deploy.Payload.Mode == "BountyClash" ? 0 : MoonRaidMap.Active.RaidDuration);
                if (deploy.Join != null) session.RequestedCompanions = deploy.Join.ExpeditionCompanions;
                session.SettlementId = deploy.Payload.DeploymentId;
                // 会话携带后端确认的唯一出战编号。以后不管撤离、掉线还是失败都沿这条记录结算，不能凭当前余额猜。
                session.PersistentDeployment = true;
                session.SaveState = RaidSaveState.Saved;
                session.Cells = deploy.Payload.Cells;
                session.CellStackId = profile.CellStackId ?? "";
                session.StashDust = profile.Dust;
                session.StashAlloy = profile.Alloy;
                session.StashCells = profile.Cells;
                // DeployAsync 已验证完整的权威出装，不可用测试装备代替。
                var graph = RaidInventoryTransport.Decode(profile.RaidInventoryJson);
                SetRaidInventory(ref state, connection, graph, session.RaidId);
                RaidInventoryState.UpdateTotals(graph, ref session);
                if (deploy.Payload.Mode == "BountyClash" &&
                    !ClashDeploymentAllowed(ref state, connection, deploy.Payload.DeploymentId)) {
                    session.Phase = RaidPhase.Extracted;
                    session.SaveState = RaidSaveState.Saving;
                    context.BeginSave(connection, session);
                    removeDeploys.Add(connection);
                    continue;
                }
                if (deploy.Join != null) {
                    // 首次生成时还没有 RaidSession 组件，先把这次确认出战放进暂存，生成角色时再安装到连接。
                    context.ReadyRaids[connection] = session;
                    SpawnPlayerCharacter(ref state, ecb, connection, deploy.Join.Name, deploy.Join.CharacterIndex);
                    AddPlayerToLeaderboard(SystemAPI.GetComponent<NetworkId>(connection).Value, deploy.Join.Name);
                } else {
                    SystemAPI.SetComponent(connection, session);
                    var joined = SystemAPI.GetComponent<JoinedClient>(connection);
                    SpawnPlayerCharacter(ref state, ecb, connection, joined.PlayerName, joined.CharacterIndex);
                }
                removeDeploys.Add(connection);
            }
            foreach (var connection in removeDeploys) context.Deployments.Remove(connection);
            // 保存失败会留待发记录继续重试。只在回执上的结算编号仍属于当前战局时更新客户端状态，避免迟到结果写到下一场。
            var removeSaves = new List<string>();
            foreach (var pair in context.Saves) {
                var save = pair.Value;
                // 同一个结算任务暂时失败会停一会重试同一回执，不能再做一次新结算。
                if (save.Task == null) {
                    if (SystemAPI.Time.ElapsedTime >= save.RetryAt) save.Task = context.RetrySave(save);
                    continue;
                }
                if (!save.Task.IsCompleted) continue;
                // 异步回执也可能比连接活得更久。只有当前连接还在等待这个结算号才改其保存状态，磁盘任务自始至终属于原记录。
                bool matching = SystemAPI.Exists(save.Connection) &&
                                SystemAPI.HasComponent<RaidSession>(save.Connection) &&
                                SystemAPI.GetComponent<RaidSession>(save.Connection).SettlementId.ToString() ==
                                pair.Key;
                if (save.Task.IsFaulted || save.Task.IsCanceled) {
                    // 重试失败会把 HUD 标成待重试并只记一次警告。整个回执保留在本机待发目录，重开服务还可恢复。
                    if (!save.Warned) {
                        Debug.LogWarning("[Raid] Settlement pending; receipt retained and retrying. " +
                                         save.Task.Exception?.GetBaseException().GetType().Name);
                        save.Warned = true;
                    }
                    save.Task = null;
                    save.RetryAt = SystemAPI.Time.ElapsedTime + 3;
                    if (matching) {
                        var session = SystemAPI.GetComponentRW<RaidSession>(save.Connection);
                        session.ValueRW.SaveState = RaidSaveState.Retrying;
                        session.ValueRW.SnapshotTimer = 0;
                    }
                    continue;
                }
                var profile = save.Task.Result;
                if (matching) {
                    var session = SystemAPI.GetComponentRW<RaidSession>(save.Connection);
                    session.ValueRW.StashDust = profile.Dust;
                    session.ValueRW.StashAlloy = profile.Alloy;
                    session.ValueRW.StashCells = profile.Cells;
                    session.ValueRW.SaveState = RaidSaveState.Saved;
                    session.ValueRW.SnapshotTimer = 0;
                    context.Profiles[save.Connection] = profile;
                    if (state.EntityManager.HasComponent<RaidInventoryState>(save.Connection) &&
                        !string.IsNullOrEmpty(profile.InventoryJson))
                        GetRaidInventory(ref state, save.Connection).MissionFirstsSaved =
                            RaidInventoryTransport.Decode(profile.InventoryJson).MissionFirsts;
                }
                removeSaves.Add(pair.Key);
            }
            foreach (string key in removeSaves) context.Saves.Remove(key);
            if (removeSaves.Count > 0) context.CleanBountyJournals();
            // 正常断线钩子没赶上时，也要找已经消失的连接。其活动出战交给放弃回执，账户和库存缓存随后释放。
            var disconnected = new List<Entity>();
            foreach (var connection in context.Profiles.Keys)
                if (!SystemAPI.Exists(connection))
                    disconnected.Add(connection);
            foreach (var connection in disconnected) {
                var profile = context.Profiles[connection];
                if (!string.IsNullOrEmpty(profile.DeploymentId))
                    context.Abandon(RaidPersistenceContext.DeploymentOf(profile));
                context.Profiles.Remove(connection);
                context.Inventories.Remove(connection);
            }
        }
    }
}