// 战术物品在服务端的完整流程。由服务器检查使用条件，处理手上计时、背包扣除、投掷体和区域效果，再向客户端同步状态。
using System;
using Math = System.Math;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Missions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    public partial struct ServerGameSystem {
#if UNITY_EDITOR
        // 仅用于可销毁的隔离 World 验证服务端裁决，不会自动运行或触碰玩家档案。
        public void TacticalTestTick(ref SystemState state, EntityCommandBuffer ecb) {
            UpdateTacticalItems(ref state, ecb);
        }

        public void TacticalTestClear(ref SystemState state, EntityCommandBuffer ecb) {
            ClearTacticalItems(ref state, ecb);
        }

        public void TacticalTestRefreshMedical(ref SystemState state, Entity connection) {
            RefreshLootAccess(ref state, connection, state.EntityManager.GetComponentData<RaidSession>(connection));
        }
#endif
        private TacticalServerWorld TacticalWorld(ref SystemState state) {
            if (!state.EntityManager.HasComponent<TacticalServerWorld>(m_PersistenceEntity))
                state.EntityManager.AddComponentObject(m_PersistenceEntity, new TacticalServerWorld());
            return state.EntityManager.GetComponentObject<TacticalServerWorld>(m_PersistenceEntity);
        }

        private void ClearTacticalItems(ref SystemState state, EntityCommandBuffer ecb) {
            var world = TacticalWorld(ref state);
            foreach (var pair in world.Uses)
                if (state.EntityManager.HasComponent<RaidInventoryState>(pair.Key)) {
                    var inventory = GetRaidInventory(ref state, pair.Key);
                    inventory.TacticalPending = false;
                    inventory.LastSentVersion = -1;
                }
            world.Uses.Clear();
            world.Feedbacks.Clear();
            world.Objects.Clear();
            world.Statuses.Clear();
            world.Epoch++;
            foreach (var health in SystemAPI.Query<RefRW<PredictedPlayerGhost>>()) {
                health.ValueRW.TacticalWireSeconds = 0;
                health.ValueRW.TacticalCoughSeconds = 0;
                health.ValueRW.TacticalPoisonSeconds = 0;
                health.ValueRW.TacticalBleeding = false;
                health.ValueRW.TacticalSprintStamina = 100;
                health.ValueRW.TacticalSprintExhausted = false;
                health.ValueRW.PendingBulletWound = 0;
                health.ValueRW.PendingBulletWoundSource = default;
            }
            TacticalBroadcast(ecb, world, null, true);
        }

        private void UpdateTacticalItems(ref SystemState state, EntityCommandBuffer ecb) {
            var world = TacticalWorld(ref state);
            var map = MoonRaidMap.Active;
            // 切换地图后清掉旧对象、读条和角色状态，再把新的世界标为当前场景。
            if (!ReferenceEquals(world.Map, map)) {
                ClearTacticalItems(ref state, ecb);
                world.Map = map;
            }
            double now = SystemAPI.Time.ElapsedTime;
            world.Now = now;
            // 将射击系统留下的伤口记录接到持续流血状态，然后才处理本帧的新道具使用请求。
            TacticalConsumeBulletWounds(ref state, world, now);
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidTacticalItemActionRpc>, RefRO<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection) &&
                    SystemAPI.HasComponent<RaidSession>(connection)) {
                    var inventory = GetRaidInventory(ref state, connection);
                    // 与局内拖格共用编号。旧请求不会覆盖最近一次操作，当前 RaidId 不符也不会建立新读条。
                    if (r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        inventory.Error = InventoryError.Inaccessible;
                        world.Feedbacks[connection] = TacticalItemFeedback.Unavailable;
                        if (r.RaidId == inventory.RaidId &&
                            SystemAPI.GetComponent<RaidSession>(connection).RaidId == r.RaidId) {
                            // 取消时释放关联的道具和手部状态。尚未提交的道具不会扣背包。
                            if (r.Action == TacticalItemAction.Cancel) {
                                inventory.CancelActions();
                                world.Uses.Remove(connection);
                                inventory.Error = InventoryError.None;
                                world.Feedbacks[connection] = TacticalItemFeedback.Cancelled;
                            } else if (TacticalActor(ref state, connection, out var player, out var health,
                                           out var root)) {
                                if (!inventory.ActionPending)
                                    TacticalBegin(ref state, world, inventory, connection, player, health, root, r,
                                        now);
                            }
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            world.Scratch.Clear();
            foreach (var pair in world.Uses) world.Scratch.Add(pair.Key);
            foreach (var connection in world.Scratch) {
                var use = world.Uses[connection];
                if (!state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                    world.Uses.Remove(connection);
                    continue;
                }
                var inventory = GetRaidInventory(ref state, connection);
                // 逐帧重新核对角色、背包版本和物品操作仍属于本局。受击、开火、换弹或角色切换时判中止。
                bool actorValid = TacticalActor(ref state, connection, out var player, out var health, out var root);
                bool valid = actorValid && inventory.TacticalPending && inventory.RaidId == use.RaidId &&
                             inventory.Graph.Version == use.Version;
                valid = valid && player == use.Player && !RaidHealth.HandsBusy(health) &&
                        (use.Action == TacticalItemAction.Extinguish
                            ? RaidHealth.SelfTreatmentDamageUnchanged(health, use.DamageSequence, use.ShieldHit)
                            : health.LastHitTick == use.Hit &&
                              health.NonPhysiologicalDamageSequence == use.DamageSequence &&
                              health.ShieldHitSequence == use.ShieldHit &&
                              health.CurrentHealth >= use.Health - .001f) && health.LastShotTick == use.Shot &&
                        health.LastGrenadeShotTick == use.Grenade && health.LastReloadTick == use.Reload &&
                        health.EquippedWeaponID == use.Weapon;
                // 状态合法且还没读完就继续等。到点后移除等待记录，服务端调用一次实际生效，再把原因发回 HUD。
                if (valid && now < use.Until) continue;
                inventory.TacticalPending = false;
                world.Uses.Remove(connection);
                inventory.Error = valid
                    ? TacticalComplete(ref state, ecb, world, inventory, use, health, root, now)
                    : InventoryError.Inaccessible;
                world.Feedbacks[connection] = !valid ? TacticalItemFeedback.Interrupted :
                    inventory.Error == InventoryError.None ? TacticalItemFeedback.Completed :
                    inventory.Error == InventoryError.Full ? TacticalItemFeedback.NoRoom :
                    inventory.Error == InventoryError.Stale ? TacticalItemFeedback.Stale :
                    TacticalItemFeedback.Unavailable;
                inventory.LastSentVersion = -1;
            }
            world.Victims.Clear();
            foreach (var (health, link, entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>, GhostGameObjectLink>()
                         .WithEntityAccess())
                if (health.ValueRO.CurrentHealth > 0 &&
                    (!health.ValueRO.ClashEnabled || health.ValueRO.ClashLife == ClashLife.Alive) &&
                    link.LinkedInstance)
                    world.Victims.Add(entity);
            world.Scratch.Clear();
            foreach (var pair in world.Feedbacks)
                if (!state.EntityManager.Exists(pair.Key))
                    world.Scratch.Add(pair.Key);
            foreach (var gone in world.Scratch) world.Feedbacks.Remove(gone);
            float dt = Mathf.Clamp(SystemAPI.Time.DeltaTime, 0, .25f);
            uint tick = SystemAPI.TryGetSingleton<NetworkTime>(out var time) && time.ServerTick.IsValid
                ? time.ServerTick.TickIndexForValidTick
                : 0;
            // 先处理能抑制燃烧与毒气的区域。场上对象加入顺序不同也应得出同样效果。
            // 压制效果先于燃烧和毒气计算，与对象插入顺序无关。
            foreach (var obj in world.Objects)
                if (obj.Phase == TacticalWorldPhase.Choke && now < obj.Until)
                    foreach (var other in world.Objects)
                        if (other != obj && TacticalWithin(obj, other.Position, .25f, other.Scene, null) &&
                            (other.Phase == TacticalWorldPhase.Fire || other.Phase == TacticalWorldPhase.Poison ||
                             (other.Phase == TacticalWorldPhase.Flying &&
                              other.Definition.Kind == TacticalItemKind.Dynamite)))
                            other.Until = now;
            for (int i = world.Objects.Count - 1; i >= 0; i--) {
                var obj = world.Objects[i];
                // 对象到期时可能从飞行转到爆炸或持续区域。处理完若仍是已过期状态，给客户端发删除并清服务端记录。
                if (!obj.Scene.IsValid() || now >= obj.Until) {
                    if (obj.Scene.IsValid() && obj.Phase == TacticalWorldPhase.Flying &&
                        (obj.Definition.Kind == TacticalItemKind.Smoke ||
                         !TacticalChoked(world, obj.Position, obj.Scene, now)))
                        TacticalDetonate(ref state, ecb, world, obj, now, tick);
                    if (obj.Phase == TacticalWorldPhase.Flying || now >= obj.Until || !obj.Scene.IsValid()) {
                        TacticalBroadcast(ecb, world, obj, true);
                        world.Objects.RemoveAt(i);
                        continue;
                    }
                }
                if (obj.Phase == TacticalWorldPhase.Flying) {
                    var previous = obj.Velocity;
                    // 投掷后用该地图真实物理场景更新位置和速度。不同道具是碰撞或到期变为范围效果，不由客户端画面触发。
                    EnergyGrenadePhysics.Step(obj.Scene, ref obj.Position, ref obj.Velocity, ref obj.Resting, dt,
                        now - obj.Created < .25 ? obj.Shooter : null, ref world.Hits);
                    bool hit =
                        (obj.Velocity - (previous + Vector3.down * EnergyGrenadeRules.Gravity * dt)).sqrMagnitude >
                        .02f;
                    if (obj.Definition.Kind == TacticalItemKind.Fire && (hit || obj.Resting))
                        TacticalDetonate(ref state, ecb, world, obj, now, tick);
                    if (TacticalChoked(world, obj.Position, obj.Scene, now) &&
                        obj.Definition.Kind == TacticalItemKind.Dynamite)
                        obj.Until = now;
                } else { TacticalArea(ref state, world, obj, now, tick); }
                // 飞行较快，需要较频繁给客户端状态。放下的器具与区域更新稀一些，创建和删除时另发消息。
                if (now >= obj.NextSnapshot) {
                    obj.NextSnapshot = now + (obj.Phase == TacticalWorldPhase.Flying ? .2 : .75);
                    TacticalBroadcast(ecb, world, obj, false);
                }
            }
            // 场上对象处理完，才统一计算这些状态的持续伤害、恢复和体力。避免同帧更新顺序使个别角色漏算。
            TacticalTickStatuses(ref state, world, now, dt, tick);
            TacticalTickSprint(ref state, world, now, SystemAPI.Time.DeltaTime);
            // 每个人的读条结果、当前体力和剩余状态只回本人。多人共享的是先前发的场上物体消息。
            if (now >= world.NextOwnerSnapshot) {
                world.NextOwnerSnapshot = now + .2;
                foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>()
                             .WithEntityAccess()) {
                    if (!state.EntityManager.HasComponent<RaidInventoryState>(connection)) continue;
                    var inv = GetRaidInventory(ref state, connection);
                    world.Uses.TryGetValue(connection, out var use);
                    world.Statuses.TryGetValue(joined.ValueRO.PlayerEntity, out var status);
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new TacticalItemOwnerRpc {
                        RaidId = inv.RaidId, RequestId = inv.RequestId, Error = inv.Error,
                        Feedback = world.Feedbacks.TryGetValue(connection, out var feedback)
                            ? feedback
                            : TacticalItemFeedback.None,
                        SprintStamina = status?.Sprint.Value ?? 100,
                        SprintExhausted = status?.Sprint.Exhausted ?? false,
                        SprintTracked = status?.Sprint.Initialized ?? false,
                        ItemId = use?.ItemId ?? "", Remaining = use == null ? 0 : (float)Math.Max(0, use.Until - now),
                        FireSeconds = TacticalRemaining(status?.Fire ?? 0, now),
                        PoisonSeconds = TacticalRemaining(status?.Poison ?? 0, now),
                        BleedSeconds = TacticalRemaining(status?.Bleed ?? 0, now),
                        StaminaSeconds = TacticalRemaining(status?.Stamina ?? 0, now),
                        AntidoteSeconds = TacticalRemaining(status?.Antidote ?? 0, now),
                        WireSeconds = TacticalRemaining(status?.Wire ?? 0, now),
                        CoughSeconds = TacticalRemaining(status?.Cough ?? 0, now),
                        RegenerationSeconds = TacticalRemaining(status?.Regeneration ?? 0, now)
                    });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }
        }

        private void TacticalConsumeBulletWounds(ref SystemState state, TacticalServerWorld world, double now) {
            foreach (var (health, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithEntityAccess()) {
                float wound = health.ValueRO.PendingBulletWound;
                if (wound <= 0) continue;
                var source = health.ValueRO.PendingBulletWoundSource;
                health.ValueRW.PendingBulletWound = 0;
                health.ValueRW.PendingBulletWoundSource = default;
                if (wound < TacticalItemRules.BulletWoundThreshold || health.ValueRO.CurrentHealth <= 0 ||
                    health.ValueRO.ClashEnabled)
                    continue;
                var status = TacticalStatus(world, entity);
                TacticalRefreshBleed(status, now + TacticalItemRules.BulletWoundDuration, source.NetworkId, source);
            }
        }

        private static float TacticalRemaining(double until, double now) { return (float)Math.Max(0, until - now); }

        private bool TacticalActor(
            ref SystemState state,
            Entity connection,
            out Entity player,
            out PredictedPlayerGhost health,
            out GhostGameObject root) {
            player = Entity.Null;
            health = default;
            root = null;
            if (!MoonRaidMap.Active) return false;
            if (!SystemAPI.HasComponent<RaidSession>(connection) || !SystemAPI.HasComponent<JoinedClient>(connection) ||
                SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                SystemAPI.GetComponent<RaidSession>(connection).Phase != RaidPhase.Active)
                return false;
            player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<PredictedPlayerGhost>(player) ||
                !state.EntityManager.HasComponent<GhostGameObjectLink>(player) ||
                !BountyClashDamage.CanAct(state.EntityManager, player))
                return false;
            health = SystemAPI.GetComponent<PredictedPlayerGhost>(player);
            root = state.EntityManager.GetComponentObject<GhostGameObjectLink>(player).LinkedInstance;
            return health.CurrentHealth > 0 && root;
        }

        // 开始使用时记下当前人物、物品和版本，先做可用性检查并建立计时。这一步为之后真正生效保留核对依据。
        private void TacticalBegin(
            ref SystemState state,
            TacticalServerWorld world,
            RaidInventoryState inventory,
            Entity connection,
            Entity player,
            in PredictedPlayerGhost health,
            GhostGameObject root,
            in RaidTacticalItemActionRpc r,
            double now) {
            if (r.ExpectedVersion != inventory.Graph.Version) {
                inventory.Error = InventoryError.Stale;
                world.Feedbacks[connection] = TacticalItemFeedback.Stale;
                return;
            }
            if (RaidHealth.HandsBusy(health)) {
                inventory.Error = InventoryError.Inaccessible;
                world.Feedbacks[connection] = TacticalItemFeedback.Unavailable;
                return;
            }
            TacticalItemDefinition definition = null;
            Vector3 placement = default;
            float seconds = 3;
            if (r.Action == TacticalItemAction.Use) {
                inventory.Error = TacticalItemRules.Validate(inventory.Graph, r.ItemId.ToString(), r.ExpectedVersion,
                    out definition);
                if (inventory.Error != InventoryError.None) return;
                // 所有能投掷的物品共用服务器裁决的持有、启用和释放流程。
                if (definition.Throwable) {
                    inventory.Error = InventoryError.Inaccessible;
                    return;
                }
                seconds = definition.UseSeconds;
                if (definition.Deployable && !TacticalPlacement(root, health, world, out placement)) {
                    inventory.Error = InventoryError.Inaccessible;
                    world.Feedbacks[connection] = TacticalItemFeedback.NeedGround;
                    return;
                }
                if ((definition.Throwable || definition.Deployable) &&
                    !TacticalCapacity(world, definition, player, placement)) {
                    inventory.Error = InventoryError.Full;
                    world.Feedbacks[connection] = TacticalItemFeedback.Limit;
                    return;
                }
                if (definition.Kind == TacticalItemKind.Vitality && !TacticalCanHeal(health)) {
                    inventory.Error = InventoryError.Invalid;
                    world.Feedbacks[connection] = health.TacticalPoisonSeconds > 0
                        ? TacticalItemFeedback.Poisoned
                        : TacticalItemFeedback.NoWound;
                    return;
                }
            } else if (r.Action == TacticalItemAction.Disarm || r.Action == TacticalItemAction.Resupply) {
                uint targetId = r.TargetId;
                var target = world.Objects.Find(o => o.Id == targetId);
                if (target == null || now >= target.Until || !TacticalInteract(root, health, target, r.Action)) {
                    inventory.Error = InventoryError.Inaccessible;
                    world.Feedbacks[connection] = TacticalItemFeedback.NoTarget;
                    return;
                }
                seconds = r.Action == TacticalItemAction.Resupply ? 2 : 3;
            } else if (r.Action == TacticalItemAction.Extinguish) {
                if (!world.Statuses.TryGetValue(player, out var status) ||
                    (status.Fire <= now && status.Bleed <= now)) {
                    inventory.Error = InventoryError.Invalid;
                    return;
                }
                seconds = TacticalItemRules.StopBleedSeconds;
            } else {
                inventory.Error = InventoryError.Invalid;
                return;
            }
            // 用已有的通用取消规则结束任务、搜索或医疗。
            inventory.CancelActions();
            inventory.TacticalPending = true;
            CancelManualMission(ref state, connection, "道具操作开始。");
            world.Uses[connection] = new TacticalServerWorld.Use {
                Player = player, RaidId = r.RaidId, Version = r.ExpectedVersion, Request = r.RequestId,
                Target = r.TargetId, ItemId = r.ItemId.ToString(), Action = r.Action, Definition = definition,
                Origin = root.transform.position, Placement = placement,
                Until = now + seconds, Hit = health.LastHitTick, DamageSequence = health.NonPhysiologicalDamageSequence,
                ShieldHit = health.ShieldHitSequence, Shot = health.LastShotTick, Grenade = health.LastGrenadeShotTick,
                Reload = health.LastReloadTick, Weapon = health.EquippedWeaponID, Health = health.CurrentHealth
            };
            if (r.Action == TacticalItemAction.Extinguish ||
                (r.Action == TacticalItemAction.Use && definition != null && definition.Injectable)) {
                var actionHealth = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                RaidConsumableAcoustics.Mark(ref actionHealth.ValueRW);
            }
            inventory.Error = InventoryError.None;
            world.Feedbacks[connection] = TacticalItemFeedback.Started;
        }

        private static bool TacticalCapacity(
            TacticalServerWorld world,
            TacticalItemDefinition definition,
            Entity player,
            Vector3 position) {
            if (world.Objects.Count + world.GrenadeReservations >= TacticalItemRules.MaxWorldObjects) return false;
            if (definition.Trap &&
                world.Objects.FindAll(o =>
                    o.OwnerPlayer == player && o.Definition.Trap && o.Phase == TacticalWorldPhase.Armed).Count >=
                TacticalItemRules.MaxOwnerTraps)
                return false;
            return !definition.Deployable || !world.Objects.Exists(o =>
                o.Phase != TacticalWorldPhase.Flying && Vector3.Distance(o.Position, position) < .65f);
        }

        // 动作读条结束后要重新检查角色、版本和目标。只有通过后才消耗物品并产生效果，防止状态已经变化还按旧条件执行。
        private InventoryError TacticalComplete(
            ref SystemState state,
            EntityCommandBuffer ecb,
            TacticalServerWorld world,
            RaidInventoryState inventory,
            TacticalServerWorld.Use use,
            in PredictedPlayerGhost health,
            GhostGameObject root,
            double now) {
            var status = TacticalStatus(world, use.Player);
            if (use.Action == TacticalItemAction.Extinguish) {
                status.Fire = status.Bleed = 0;
                return InventoryError.None;
            }
            if (use.Action == TacticalItemAction.Disarm || use.Action == TacticalItemAction.Resupply) {
                var target = world.Objects.Find(o => o.Id == use.Target);
                if (target == null || now >= target.Until || !TacticalInteract(root, health, target, use.Action))
                    return InventoryError.Inaccessible;
                if (use.Action == TacticalItemAction.Resupply) {
                    // 补给箱能量和人物背包同时检查。新背包装得下这批电池才扣补给能源，不能先清箱子再发现没地方。
                    var error = TacticalItemRules.PrepareResupply(inventory.Graph, use.Version, target.Energy,
                        out var graph, out int energy);
                    if (error != InventoryError.None) return error;
                    inventory.Graph.Items = graph.Items;
                    inventory.Graph.Version = graph.Version;
                    target.Energy -= energy;
                    if (target.Energy <= 0) {
                        target.Until = now;
                        target.Phase = TacticalWorldPhase.Removed;
                    }
                    target.NextSnapshot = 0;
                    var session = SystemAPI.GetComponentRW<RaidSession>(FindTacticalConnection(ref state, use.Player));
                    RaidInventoryState.UpdateTotals(inventory.Graph, ref session.ValueRW);
                    return InventoryError.None;
                }
                // 拆除会销毁已设下的装置，不会在背包里重新生成一件。
                target.Until = now;
                target.Phase = TacticalWorldPhase.Removed;
                target.NextSnapshot = 0;
                return InventoryError.None;
            }
            var definition = use.Definition;
            var validation = TacticalItemRules.Validate(inventory.Graph, use.ItemId, use.Version, out _);
            if (validation != InventoryError.None) return validation;
            Vector3 position = root.transform.position, velocity = default;
            if (definition.Deployable && !TacticalPlacement(root, health, world, out position))
                return InventoryError.Inaccessible;
            if (definition.Throwable && !TacticalThrow(root, health, world, out position, out velocity))
                return InventoryError.Inaccessible;
            // 目标点仍可用、世界容量够才扣物品。已经开始的放置不会抢占后来已被占用的位置。
            if ((definition.Deployable || definition.Throwable) &&
                !TacticalCapacity(world, definition, use.Player, position))
                return InventoryError.Full;
            if (definition.Kind == TacticalItemKind.Vitality && !TacticalCanHeal(health)) return InventoryError.Invalid;
            var errorTake = TacticalItemRules.Take(inventory.Graph, use.ItemId, use.Version);
            if (errorTake != InventoryError.None) return errorTake;
            if (definition.Kind == TacticalItemKind.Vitality) {
                var next = health;
                for (int p = 0; p < RaidHealth.PartCount; p++) {
                    var part = (BodyPart)p;
                    float before = RaidHealth.Get(next, part);
                    if (before <= 0) continue;
                    RaidHealth.Set(ref next, part, RaidHealth.Maximum(next, part));
                }
                SystemAPI.SetComponent(use.Player, next);
            } else if (definition.Kind == TacticalItemKind.Stamina) {
                status.Stamina = now + TacticalItemRules.StaminaDuration;
            } else if (definition.Kind == TacticalItemKind.Antidote) {
                status.Antidote = now + TacticalItemRules.AntidoteDuration;
                status.Poison = 0;
            } else if (definition.Kind == TacticalItemKind.Regeneration) {
                status.Regeneration = now + TacticalItemRules.RegenerationDuration;
                status.RegenerationTick = now;
            } else {
                var obj = new TacticalServerWorld.Object {
                    Id = ++world.NextId,
                    Owner = SystemAPI.HasComponent<GhostOwner>(use.Player)
                        ? SystemAPI.GetComponent<GhostOwner>(use.Player).NetworkId
                        : 0,
                    OwnerPlayer = use.Player, Source = BountyClashDamage.Capture(state.World, use.Player),
                    Definition = definition,
                    Phase = definition.Throwable ? TacticalWorldPhase.Flying :
                        definition.Trap ? TacticalWorldPhase.Armed : TacticalWorldPhase.Supply,
                    Scene = root.gameObject.scene.GetPhysicsScene(), Shooter = root.transform, Position = position,
                    Velocity = velocity, Created = now, ArmAt = now + 1,
                    Until = now + (definition.Throwable ? definition.Fuse : definition.Lifetime),
                    Energy = definition.Kind == TacticalItemKind.AmmoBox ? TacticalItemRules.SupplyEnergy : 0
                };
                world.Objects.Add(obj);
                TacticalBroadcast(ecb, world, obj, false);
            }
            return InventoryError.None;
        }

        private Entity FindTacticalConnection(ref SystemState state, Entity player) {
            foreach (var (joined, connection) in SystemAPI.Query<RefRO<JoinedClient>>().WithEntityAccess())
                if (joined.ValueRO.PlayerEntity == player)
                    return connection;
            return Entity.Null;
        }

        private static bool TacticalCanHeal(in PredictedPlayerGhost health) {
            if (health.TacticalPoisonSeconds > 0 || !health.BodyHealthInitialized) return false;
            for (int p = 0; p < RaidHealth.PartCount; p++) {
                float value = RaidHealth.Get(health, (BodyPart)p);
                if (value > 0 && value < RaidHealth.Maximum(health, (BodyPart)p) - .01f) return true;
            }
            return false;
        }

        private static TacticalServerWorld.Status TacticalStatus(TacticalServerWorld world, Entity entity) {
            if (!world.Statuses.TryGetValue(entity, out var status))
                world.Statuses[entity] = status = new TacticalServerWorld.Status();
            return status;
        }

        private static void TacticalRefreshBleed(
            TacticalServerWorld.Status status,
            double until,
            int owner,
            ClashDamageSource source) {
            // 持续更短的新伤不能缩短既有伤口，也不能抢走其伤害归属。
            if (until <= status.Bleed) return;
            status.Bleed = until;
            status.BleedOwner = owner;
            status.BleedSource = source;
        }

        private static Vector3 TacticalEye(GhostGameObject root) {
            var controller = root.GetComponent<PlayerGhost>()?.Controller?.CharacterController;
            return root.transform.position + Vector3.up * Mathf.Max(.3f, (controller ? controller.height : 1.8f) - .2f);
        }

        private static Vector3 TacticalDirection(in PredictedPlayerGhost health) {
            return Quaternion.Euler(health.ControllerState.PitchDegrees, health.ControllerState.YawDegrees, 0) *
                   Vector3.forward;
        }

        private static bool TacticalThrow(
            GhostGameObject root,
            in PredictedPlayerGhost health,
            TacticalServerWorld world,
            out Vector3 position,
            out Vector3 velocity) {
            var eye = TacticalEye(root);
            var direction = TacticalDirection(health);
            var scene = root.gameObject.scene.GetPhysicsScene();
            position = eye;
            velocity = direction * 14 + Vector3.up * 2;
            if (!float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(velocity.z)) return false;
            if (Ballistics.Sweep(scene, eye, eye + direction * .55f, .13f, EnergyGrenadePhysics.HitMask, root.transform,
                    ref world.Hits, out var hit))
                position = eye + direction * Mathf.Max(0, hit.distance - .02f);
            else
                position += direction * .55f;
            int count = scene.OverlapSphere(position, .13f, world.Overlaps, EnergyGrenadePhysics.HitMask,
                QueryTriggerInteraction.Ignore);
            if (count == world.Overlaps.Length) return false;
            for (int i = 0; i < count; i++)
                if (!world.Overlaps[i].transform.IsChildOf(root.transform))
                    return false;
            return true;
        }

        private static bool TacticalPlacement(
            GhostGameObject root,
            in PredictedPlayerGhost health,
            TacticalServerWorld world,
            out Vector3 position) {
            position = default;
            var scene = root.gameObject.scene.GetPhysicsScene();
            var eye = TacticalEye(root);
            var direction = TacticalDirection(health);
            int mask = LayerMask.GetMask("Default", "Ground");
            if (!scene.Raycast(eye, direction, out var hit, 3, mask, QueryTriggerInteraction.Ignore) ||
                hit.normal.y < .7f || Mathf.Abs(hit.point.y - root.transform.position.y) > .6f ||
                Vector3.Distance(root.transform.position, hit.point) > 2.2f)
                return false;
            var center = hit.point;
            for (int i = 0; i < 4; i++) {
                var offset = i == 0 ? Vector3.right : i == 1 ? Vector3.left : i == 2 ? Vector3.forward : Vector3.back;
                if (!scene.Raycast(center + offset * .25f + Vector3.up * .2f, Vector3.down, out var support, .4f, mask,
                        QueryTriggerInteraction.Ignore) || Mathf.Abs(support.point.y - center.y) > .12f)
                    return false;
            }
            position = center + Vector3.up * .05f;
            int count = scene.OverlapSphere(position + Vector3.up * .15f, .12f, world.Overlaps, mask,
                QueryTriggerInteraction.Ignore);
            return count == 0 &&
                   EnergyGrenadePhysics.ClearSight(scene, eye, position + Vector3.up * .15f, root.transform);
        }

        private static bool TacticalInteract(
            GhostGameObject root,
            in PredictedPlayerGhost health,
            TacticalServerWorld.Object target,
            TacticalItemAction action) {
            if (target == null || target.Scene != root.gameObject.scene.GetPhysicsScene() ||
                (action == TacticalItemAction.Disarm && target.Phase != TacticalWorldPhase.Armed) ||
                (action == TacticalItemAction.Resupply &&
                 (target.Phase != TacticalWorldPhase.Supply || target.Energy <= 0)))
                return false;
            var eye = TacticalEye(root);
            var delta = target.Position + Vector3.up * .15f - eye;
            return delta.magnitude <= TacticalItemRules.InteractionRange &&
                   Mathf.Abs(target.Position.y - root.transform.position.y) < .7f &&
                   Vector3.Dot(delta.normalized, TacticalDirection(health)) > .6f &&
                   EnergyGrenadePhysics.ClearSight(target.Scene, eye, target.Position + Vector3.up * .15f,
                       root.transform);
        }

        public static bool TacticalWithin(
            TacticalServerWorld.Object area,
            Vector3 point,
            float targetRadius,
            PhysicsScene scene,
            Transform target) {
            var delta = point - area.Position;
            float radius = area.Definition.Radius + targetRadius;
            if (scene != area.Scene || Mathf.Abs(delta.y) > 1.5f ||
                new Vector2(delta.x, delta.z).sqrMagnitude > radius * radius)
                return false;
            // 在较低高度查遮挡，避免绊线或区域效果穿过墙和楼板。
            var origin = area.Position + Vector3.up * .2f;
            var end = point + Vector3.up * .2f;
            return EnergyGrenadePhysics.ClearSight(scene, origin, end, target);
        }

        private static bool TacticalChoked(TacticalServerWorld world, Vector3 point, PhysicsScene scene, double now) {
            foreach (var area in world.Objects)
                if (area.Phase == TacticalWorldPhase.Choke && now < area.Until &&
                    TacticalWithin(area, point, 0, scene, null))
                    return true;
            return false;
        }

        public static bool TacticalAreaBlockedByTeam(TacticalWorldPhase phase, bool protectedByTeam) {
            return protectedByTeam && (phase == TacticalWorldPhase.Fire || phase == TacticalWorldPhase.Poison ||
                                       phase == TacticalWorldPhase.Wire);
        }

        internal static void TacticalBroadcast(
            EntityCommandBuffer ecb,
            TacticalServerWorld world,
            TacticalServerWorld.Object obj,
            bool removed) {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new TacticalWorldStateRpc {
                Epoch = world.Epoch, Id = obj?.Id ?? 0, Sequence = ++world.Sequence,
                Kind = obj?.Definition.Kind ?? default, Phase = removed ? TacticalWorldPhase.Removed : obj.Phase,
                Position = obj?.Position ?? default, Velocity = obj?.Velocity ?? default,
                Radius = obj?.Definition.Radius ?? 0, Energy = obj?.Energy ?? 0,
                Remaining = obj == null ? 0 : (float)Math.Max(0, obj.Until - world.Now)
            });
            ecb.AddComponent(entity, new SendRpcCommandRequest());
        }
    }
}