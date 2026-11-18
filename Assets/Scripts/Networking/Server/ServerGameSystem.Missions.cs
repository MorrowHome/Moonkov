// 服务端运转月核和档案点等地图任务。收到操作请求后核对锚点与角色状态，推进机器计时、产出任务物品，最后给每个玩家发自己的任务进度。
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Missions;

namespace Unity.MP_FPS {
    public sealed class RaidMissionWorld : IComponentData {
        public sealed class Actor {
            public int RaidId, Node = -1;
            public string Key;
            public uint Request;
            public uint AcknowledgedRequest;

            public uint LastHitTick,
                DamageSequence,
                ShieldHitSequence,
                LastShotTick,
                LastGrenadeTick,
                LastReloadTick,
                Weapon;

            public float Health;
            public float3 Origin;
            public MissionActionResult Result;

            // 开始手动交互先记下人物位置、生命和攻击动作编号。续租时和这份起点对比，受击离开或换武器会打断读条。
            public void Capture(in PredictedPlayerGhost health, float3 position) {
                Origin = position;
                Health = health.CurrentHealth;
                LastHitTick = health.LastHitTick;
                DamageSequence = health.NonPhysiologicalDamageSequence;
                ShieldHitSequence = health.ShieldHitSequence;
                LastShotTick = health.LastShotTick;
                LastGrenadeTick = health.LastGrenadeShotTick;
                LastReloadTick = health.LastReloadTick;
                Weapon = health.EquippedWeaponID;
            }

            public bool Interrupted(in PredictedPlayerGhost health, float3 position, bool allowAnchoredHalo = false) {
                return health.CurrentHealth < Health || health.LastHitTick != LastHitTick ||
                       health.NonPhysiologicalDamageSequence != DamageSequence ||
                       health.ShieldHitSequence != ShieldHitSequence ||
                       (health.LastShotTick != LastShotTick && !(allowAnchoredHalo &&
                                                                 HaloSupportRules.Anchored(health) &&
                                                                 health.LastShotTick == health.LastSentryShotTick)) ||
                       health.LastGrenadeShotTick != LastGrenadeTick || health.LastReloadTick != LastReloadTick ||
                       health.EquippedWeaponID != Weapon || RaidHealth.HandsBusy(health, allowAnchoredHalo);
            }
        }

        public readonly Dictionary<Entity, Actor> Actors = new();
        public readonly HashSet<Entity> Known = new();
        public RaidMissionSite Site;
        public string WorldId;
        public MissionDirector Director;
        public MissionOperation Operation;
        public uint Sequence;
        public uint Seed;
        public int Epoch, DefinitionIndex, OutputLootId = -1;
        public double NextSend, EndedAt;
        public bool OutputProduced;
        public bool BackendCompatible = true;
        public int CampaignChapter;
        public double CampaignNextCheck, NextMachineSound;
        public readonly Dictionary<Entity, int> CampaignRaidIds = new();
    }

    // Gameplay hearing is deliberately local and attenuated by world geometry.
    public static class MissionAlarm {
        public const float HearingRange = MissionMachineSounds.CutterRange;
        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");

        public static bool Audible(Vector3 source, Vector3 listener, float range = HearingRange) {
            float squared = (listener - source).sqrMagnitude;
            if (!MissionMachineSounds.WithinRange(squared, range, false)) return false;
            return MissionMachineSounds.WithinRange(squared, range, true) ||
                   !UnityEngine.Physics.Linecast(source, listener, s_WorldMask, QueryTriggerInteraction.Ignore);
        }
    }

    public partial struct ServerGameSystem {
        // 服用补给、搜刮等动作共用双手。切换它们时显式撤掉还在进行的任务交互，让进度和取消原因一起更新。
        private void CancelManualMission(ref SystemState state, Entity connection, string reason) {
            var context =
                state.EntityManager.GetComponentObject<RaidMissionWorld>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
            if (context.Operation == null || !context.Actors.TryGetValue(connection, out var actor) || actor.Node < 0)
                return;
            context.Operation.Cancel(actor.Key);
            actor.Node = -1;
            actor.Result = MissionActionResult.Invalid;
            context.NextSend = 0;
            if (state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                var inventory = GetRaidInventory(ref state, connection);
                inventory.ActionFeedback = reason;
                inventory.LastSentVersion = -1;
            }
        }

        // 地图任务同时处理一份世界状态和每名玩家的局内进度。模式、站点资源或服务能力不符时，不会凭空开一个任务。
        private void HandleMissions(ref SystemState state, EntityCommandBuffer ecb) {
            var site = RaidMissionSite.Active;
            if (!site) return;
            var context =
                state.EntityManager.GetComponentObject<RaidMissionWorld>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
            double now = SystemAPI.Time.ElapsedTime;
            if (context.Site != site) {
                context.Site = site;
                context.WorldId = Guid.NewGuid().ToString("D");
                context.Seed = site.Seed == 0 ? unchecked((uint)Guid.NewGuid().GetHashCode()) : site.Seed;
                context.Director = new MissionDirector(context.Seed, now);
                context.Operation = null;
                Debug.Log("[Missions] Director seed " + context.Seed +
                          ". Fixed author seed is available for reproduction.");
                context.Known.Clear();
                context.Actors.Clear();
                context.Epoch = 0;
                context.Sequence = 0;
                context.CampaignChapter = 0;
                context.CampaignRaidIds.Clear();
            }
            PrepareCampaign(ref state, context, site, now);
            float audience = 0;
            foreach (var (session, joined) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>())
                if (session.ValueRO.Phase == RaidPhase.Active &&
                    TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var p) &&
                    math.distancesq(p, site.Center) <= 300 * 300)
                    audience = math.max(audience, session.ValueRO.TimeLeft);
            if (context.Operation == null && context.BackendCompatible && context.CampaignChapter == 0) {
                var chosen = context.Director.TryOffer(site.Definitions, now, audience, false,
                    d => MissionAnchorsReady(site, d));
                if (chosen != null) {
                    context.Operation = new MissionOperation(context.WorldId, Guid.NewGuid().ToString("D"),
                        ++context.Epoch, chosen, now);
                    context.DefinitionIndex = Array.FindIndex(site.Definitions, d => d.Id == chosen.Id);
                    context.OutputLootId = -1;
                    context.OutputProduced = false;
                    context.EndedAt = 0;
                    context.Known.Clear();
                    context.Actors.Clear();
                }
            }
            var operation = context.Operation;
            if (operation != null) operation.Tick(now);
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidMissionActionRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (operation != null && r.OperationId.ToString() == operation.Id && r.Epoch == operation.Epoch &&
                    SystemAPI.HasComponent<RaidSession>(connection) &&
                    SystemAPI.HasComponent<JoinedClient>(connection) &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    if (session.RaidId == r.RaidId && session.Phase == RaidPhase.Active) {
                        if (!context.Actors.TryGetValue(connection, out var actor) || actor.RaidId != session.RaidId) {
                            if (actor != null) operation.Cancel(actor.Key);
                            actor = new RaidMissionWorld.Actor
                                { RaidId = session.RaidId, Key = session.SettlementId.ToString() };
                            context.Actors[connection] = actor;
                        }
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        bool accessible = MissionAccessible(ref state, player, site, operation, r.Node);
                        var ghost = accessible ? SystemAPI.GetComponent<PredictedPlayerGhost>(player) : default;
                        var position = accessible ? SystemAPI.GetComponent<LocalTransform>(player).Position : default;
                        if (r.Intent == MissionIntent.Begin || r.Intent == MissionIntent.Configure)
                            actor.AcknowledgedRequest = r.RequestId;
                        if (r.Intent == MissionIntent.Cancel) {
                            if (actor.Request == r.RequestId && actor.Node == r.Node) {
                                operation.Cancel(actor.Key);
                                actor.Node = -1;
                            }
                        } else if (!accessible) {
                            // An invalid new target must not interrupt an existing legal action.
                            if (r.Intent == MissionIntent.Renew && actor.Node == r.Node &&
                                actor.Request == r.RequestId) {
                                operation.Cancel(actor.Key);
                                actor.Node = -1;
                            }
                            actor.Result = MissionActionResult.Invalid;
                        } else if (r.Intent == MissionIntent.Renew && actor.Node >= 0 &&
                                   actor.Interrupted(ghost, position)) {
                            operation.Cancel(actor.Key);
                            actor.Node = -1;
                            actor.Result = MissionActionResult.Invalid;
                        } else if (r.Intent == MissionIntent.Begin) {
                            actor.AcknowledgedRequest = r.RequestId;
                            var inventory = GetRaidInventory(ref state, connection);
                            actor.Result = r.RequestId <= inventory.RequestId
                                ? MissionActionResult.Stale
                                : operation.Begin(actor.Key, r.Epoch, r.Node, r.RequestId, now);
                            if (actor.Result == MissionActionResult.Started ||
                                actor.Result == MissionActionResult.Running) {
                                inventory.RequestId = r.RequestId;
                                inventory.CancelActions(inventory.ActionPending ? "原操作已取消：开始任务设备操作；未消耗物品。" : null);
                                inventory.Error = InventoryError.None;
                                actor.Node = r.Node;
                                actor.Request = r.RequestId;
                                actor.Capture(ghost, position);
                                context.Known.Add(connection);
                            }
                        } else if (r.Intent == MissionIntent.Configure) {
                            var inventory = GetRaidInventory(ref state, connection);
                            actor.Result = r.RequestId <= inventory.RequestId
                                ? MissionActionResult.Stale
                                : operation.Configure(actor.Key, r.Epoch, r.Node, r.RequestId, r.Choice, now);
                            if (actor.Result == MissionActionResult.Completed) {
                                inventory.RequestId = r.RequestId;
                                inventory.CancelActions(inventory.ActionPending ? "原操作已取消：配置任务设备；未消耗物品。" : null);
                                operation.Cancel(actor.Key);
                                actor.Node = -1;
                                context.Known.Add(connection);
                            }
                        } else if (r.Intent == MissionIntent.Renew && actor.Node == r.Node &&
                                   actor.Request == r.RequestId) {
                            actor.Result = operation.Renew(actor.Key, r.RequestId, now);
                            if (actor.Result != MissionActionResult.Running) actor.Node = -1;
                        }
                    }
                }
                ecb.DestroyEntity(entity);
            }
            if (operation != null) {
                foreach (var pair in context.Actors) {
                    if (pair.Value.Node < 0) continue;
                    bool valid = SystemAPI.Exists(pair.Key) && SystemAPI.HasComponent<JoinedClient>(pair.Key) &&
                                 SystemAPI.HasComponent<RaidSession>(pair.Key) &&
                                 !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(pair.Key) &&
                                 SystemAPI.GetComponent<RaidSession>(pair.Key).Phase == RaidPhase.Active;
                    if (valid)
                        valid =
                            MissionAccessible(ref state, SystemAPI.GetComponent<JoinedClient>(pair.Key).PlayerEntity,
                                site, operation, pair.Value.Node) && !pair.Value.Interrupted(
                                SystemAPI.GetComponent<PredictedPlayerGhost>(SystemAPI
                                    .GetComponent<JoinedClient>(pair.Key).PlayerEntity),
                                SystemAPI.GetComponent<LocalTransform>(SystemAPI.GetComponent<JoinedClient>(pair.Key)
                                    .PlayerEntity).Position);
                    if (!valid) {
                        operation.Cancel(pair.Value.Key);
                        pair.Value.Node = -1;
                        pair.Value.Result = MissionActionResult.Invalid;
                        if (SystemAPI.Exists(pair.Key) &&
                            state.EntityManager.HasComponent<RaidInventoryState>(pair.Key)) {
                            var inventory = GetRaidInventory(ref state, pair.Key);
                            inventory.ActionFeedback = "任务操作中断：受击、使用武器、转开视线、离开范围或战局结束。";
                            inventory.LastSentVersion = -1;
                        }
                    }
                }
                if (operation.Phase == MissionPhase.OutputReady && !context.OutputProduced)
                    CreateMissionOutput(ref state, context);
                if (operation.Phase == MissionPhase.OutputReady && context.OutputProduced &&
                    !MissionArtifactsRemain(ref state, context, false))
                    operation.Resolve();
                if (operation.Phase == MissionPhase.Expired) MissionArtifactsRemain(ref state, context, true);
                bool ended = operation.Phase == MissionPhase.Expired || operation.Phase == MissionPhase.Resolved;
                if (ended && context.EndedAt == 0) context.EndedAt = now;
                if (ended && now - context.EndedAt >= 120 && !MissionPlayersNear(ref state, site.Center, 40)) {
                    if (context.OutputLootId >= 0) {
                        LootContainers(ref state).Containers.Remove(context.OutputLootId);
                        LootContainers(ref state).MissionCaches.Remove(context.OutputLootId);
                    }
                    context.Operation = null;
                    context.OutputLootId = -1;
                    context.Known.Clear();
                    context.Actors.Clear();
                }
            }
            EmitMachineSound(ref state, context, now);
            ObserveCampaign(ref state, context);
            if (now < context.NextSend) return;
            context.NextSend = now + .25;
            context.Sequence++;
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>()
                         .WithEntityAccess()) {
                if (SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) continue;
                if (context.Operation != null &&
                    TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var p) &&
                    math.distancesq(p, site.Center) <= site.DiscoveryRadius * site.DiscoveryRadius)
                    context.Known.Add(connection);
                var snapshot = new RaidMissionSnapshotRpc {
                    RaidId = session.ValueRO.RaidId, Sequence = context.Sequence,
                    WorldId = context.WorldId, OutputLootId = -1, DefinitionIndex = -1,
                    SaveState = session.ValueRO.SaveState, BackendSupported = context.BackendCompatible
                };
                AddCampaignSnapshot(ref state, context, connection, session.ValueRO, ref snapshot);
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                    var inventory = GetRaidInventory(ref state, connection);
                    snapshot.MissionFirstsBefore = inventory.MissionFirstsBefore;
                    snapshot.MissionFirstsSaved = inventory.MissionFirstsSaved;
                    if (session.ValueRO.Phase == RaidPhase.Extracted)
                        foreach (var item in inventory.Graph.Items)
                            if (MissionInventory.IsArtifact(item.Code) && !item.MissionRecovered &&
                                inventory.Graph.Carried(item))
                                snapshot.RecoveredArtifacts++;
                }
                if (context.Operation != null && context.Known.Contains(connection)) {
                    var op = context.Operation;
                    snapshot.OperationId = op.Id;
                    snapshot.Epoch = op.Epoch;
                    snapshot.Phase = op.Phase;
                    snapshot.CompletedMask = op.CompletedMask;
                    snapshot.DefinitionIndex = context.DefinitionIndex;
                    snapshot.Remaining = (float)Math.Max(0, op.Deadline - now);
                    snapshot.OutputLootId = context.OutputLootId;
                    if (context.OutputLootId >= 0 && LootContainers(ref state).Containers
                            .TryGetValue(context.OutputLootId, out var output))
                        foreach (var item in output.Items)
                            if (MissionInventory.IsArtifact(item.Code))
                                snapshot.OutputRemaining++;
                    for (int i = 0; i < op.ObjectiveCount; i++)
                        if (i < 4)
                            snapshot.ProgressA[i] = op.Progress(i, now);
                        else
                            snapshot.ProgressB[i - 4] = op.Progress(i, now);
                    if (context.Actors.TryGetValue(connection, out var actor)) {
                        snapshot.RequestId = actor.AcknowledgedRequest;
                        snapshot.Result = actor.Result;
                    }
                }
                var entity = ecb.CreateEntity();
                ecb.AddComponent(entity, snapshot);
                ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        // 机器还在运行时按频率发布声音。现场的 AI 能像听见普通战局声响一样用它，任务 HUD 则另从快照读进度。
        private void EmitMachineSound(ref SystemState state, RaidMissionWorld context, double now) {
            var operation = context.Operation;
            if (!MissionMachineSounds.TryActiveSource(operation, out string anchorId, out float range)) {
                context.NextMachineSound = 0;
                return;
            }
            if (now < context.NextMachineSound) return;
            context.NextMachineSound = now + 2;
            var anchor = context.Site ? context.Site.Anchor(anchorId) : null;
            if (anchor == null) return;
            Vector3 position = anchor.Approach, source = position + Vector3.up;
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithNone<ClashBot>().WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0 ||
                    !state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity))
                    continue;
                var brain = state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                if (brain.MissionAlarmId == operation.Id || brain.Target != Entity.Null || brain.Leaving) continue;
                if (MissionAlarm.Audible(source, (Vector3)pose.ValueRO.Position + Vector3.up * 1.2f, range))
                    DollSingerEnemySystem.HearMissionAlarm(brain, operation.Id, position, now);
            }
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>>()
                         .WithAll<ProceduralAlienEnemy.State, GhostGameObjectLink, Simulate>().WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0 ||
                    ((Vector3)pose.ValueRO.Position - source).sqrMagnitude > range * range)
                    continue;
                var link = state.EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                var alien = link.LinkedInstance ? link.LinkedInstance.GetComponent<ProceduralAlienEnemy>() : null;
                if (alien && MissionAlarm.Audible(source, alien.AimPoint, range))
                    alien.HearMissionAlarm(operation.Id, position);
            }
        }

        // 任务规则只知道节点编号。此处核对正式地图有没有全部实际挂点和可用设备，未摆齐就暂不启用该任务。
        private static bool MissionAnchorsReady(RaidMissionSite site, MissionDefinition definition) {
            var entrance = site.Anchor("entry");
            if (entrance == null || !NavMesh.SamplePosition(entrance.Approach, out var start, 3, NavMesh.AllAreas))
                return false;
            var path = new NavMeshPath();
            foreach (var n in definition.Objectives) {
                var a = site.Anchor(n.Anchor);
                if (a == null) return false;
                if (!UnityEngine.Physics.Raycast(a.Approach + Vector3.up, Vector3.down, out _, 2.5f,
                        LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
                    return false;
                if (!NavMesh.SamplePosition(a.Approach, out var end, 3, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    return false;
            }
            return true;
        }

        private bool MissionPlayersNear(ref SystemState state, Vector3 center, float radius) {
            foreach (var (session, joined) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>())
                if (session.ValueRO.Phase == RaidPhase.Active &&
                    TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var p) &&
                    math.distancesq(p, center) < radius * radius)
                    return true;
            return false;
        }

        // 有资格操作还得看人物在地图上的真实位置、方向与遮挡，这些条件不会由共享任务模型自己猜。
        private bool MissionAccessible(
            ref SystemState state,
            Entity player,
            RaidMissionSite site,
            MissionOperation operation,
            int node) {
            var definition = operation.Objective(node);
            if (definition == null || definition.Kind == MissionObjectiveKind.Process) return false;
            var anchor = site.Anchor(definition.Anchor);
            if (anchor == null || !TryGetLivingPosition(ref state, player, out var position)) return false;
            var ghost = SystemAPI.GetComponent<PredictedPlayerGhost>(player);
            var eye = (Vector3)position + Vector3.up * 1.4f;
            var facing = Quaternion.Euler(ghost.ControllerState.PitchDegrees, ghost.ControllerState.YawDegrees, 0) *
                         Vector3.forward;
            return !RaidHealth.HandsBusy(ghost) && math.distancesq(position, anchor.Position) <= 3 * 3 &&
                   Vector3.Dot(facing, (anchor.Position - eye).normalized) >= .65f && !UnityEngine.Physics.Linecast(eye,
                       anchor.Position, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
        }

        // 加工或调查完成后，把产物放进这个操作的任务取物点，并记录出处。结算用编号检查真实来源。
        private void CreateMissionOutput(ref SystemState state, RaidMissionWorld context) {
            var definition = context.Operation.Definition;
            var cache = InventoryGraph.Create(false, false);
            cache.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
            for (int i = 0; i < definition.OutputCount; i++)
                cache.Items.Add(new InventoryItem {
                    Id = Guid.NewGuid().ToString("D"), Code = definition.ArtifactCode,
                    Parent = LootInventoryExchange.Root, Region = "main", X = i % 6, Y = i / 6, FoundInRaid = true,
                    MissionWorldId = context.WorldId, MissionOperationId = context.Operation.Id,
                    MissionContentVersion = definition.Version
                });
            if (cache.Validate() != InventoryError.None)
                throw new InvalidOperationException("Mission output failed inventory validation.");
            context.OutputLootId = RaidLootContainers.FirstMissionCacheId + context.Epoch;
            var anchor = context.Site.Anchor(definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive");
            var loot = LootContainers(ref state);
            loot.Containers.Add(context.OutputLootId, cache);
            loot.MissionCaches.Add(context.OutputLootId, anchor.Position);
            context.OutputProduced = true;
        }

        // 先查世界容器与随身库存中这个操作的序号，决定是否仍有产物需要保留或回收，避免彼此重复。
        private bool MissionArtifactsRemain(ref SystemState state, RaidMissionWorld context, bool destroy) {
            bool remains = false;

            bool Match(InventoryItem item) {
                return MissionInventory.IsArtifact(item.Code) && item.MissionOperationId == context.Operation.Id &&
                       !item.MissionRecovered;
            }

            foreach (var graph in LootContainers(ref state).Containers.Values) {
                bool found = graph.Items.Exists(Match);
                remains |= found;
                if (destroy && found) {
                    graph.Items.RemoveAll(item => Match(item));
                    graph.Version++;
                }
            }
            foreach (var (session, connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess()) {
                if (session.ValueRO.Phase != RaidPhase.Active ||
                    !state.EntityManager.HasComponent<RaidInventoryState>(connection))
                    continue;
                var inventory = GetRaidInventory(ref state, connection);
                bool found = inventory.Graph.Items.Exists(Match);
                remains |= found;
                if (destroy && found) {
                    inventory.Graph.Items.RemoveAll(item => Match(item));
                    inventory.Graph.Version++;
                    inventory.LastSentVersion = -1;
                }
            }
            return remains;
        }
    }
}