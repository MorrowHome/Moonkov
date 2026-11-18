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
    // 可变战术道具状态按服务端 World 隔离，不能用进程静态单例共享。
    public sealed class TacticalServerWorld : IComponentData {
        // 飞行中、爆开后或摆在地上的状态独立存进这个对象，攻击者信息在创建时固定下来。客户端只收到它的显示快照。
        public sealed class Object {
            public uint Id;
            public int Owner;
            public Entity OwnerPlayer;
            public ClashDamageSource Source;
            public TacticalItemDefinition Definition;
            public TacticalWorldPhase Phase;
            public PhysicsScene Scene;
            public Transform Shooter;
            public Vector3 Position, Velocity;
            public bool Resting;
            public double Created, Until, NextSnapshot, ArmAt;
            public int Energy;
        }

        // 玩家读条时记录当时的角色、物品版本、受击与武器标记。完成前每个 tick 对照这些起始条件。
        public sealed class Use {
            public Entity Player;
            public int RaidId, Version;
            public uint Request, Target;
            public string ItemId;
            public TacticalItemAction Action;
            public TacticalItemDefinition Definition;
            public Vector3 Origin, Placement;
            public double Until;
            public uint Hit, DamageSequence, ShieldHit, Shot, Grenade, Reload, Weapon;
            public float Health;
        }

        // 燃烧、出血、刺激、疲劳等都按角色挂记录和截止时间。持续伤害沿用原始来源，攻击者掉线后也不会丢掉归属。
        public sealed class Status {
            public double Fire, Poison, Bleed, Stamina, Antidote, Wire, Cough;
            public double FlashUntil;
            public double Regeneration, RegenerationTick;
            public float FlashStrength;
            public TacticalSprintState Sprint;
            public float3 SprintPosition;
            public bool SprintPositionKnown;
            public int FireOwner, PoisonOwner, BleedOwner;
            public ClashDamageSource FireSource, PoisonSource, BleedSource;

            // 这个 NPC 自身的燃烧与出血扣血只在服务端记账，
            // 自我治疗可据此区分同次更新中的既有伤害与新攻击。
            public double PmcStatusHealthDamage;
            public ulong PmcStatusShieldHits;
        }

        public readonly List<Object> Objects = new();
        public readonly Dictionary<Entity, Use> Uses = new();
        public readonly Dictionary<Entity, TacticalItemFeedback> Feedbacks = new();
        public readonly Dictionary<Entity, Status> Statuses = new();
        public readonly List<Entity> Scratch = new();
        public readonly List<Entity> Victims = new();
        public uint Epoch = 1, NextId, Sequence;
        public object Map;
        public double NextOwnerSnapshot, Now;
        public int GrenadeReservations;
        public RaycastHit[] Hits = new RaycastHit[32];
        public readonly Collider[] Overlaps = new Collider[32];
    }

    // 报警只提供位置线索。障碍会缩短能有效听到的距离，
    // 听见声音也不会告诉 AI 是谁触发了装置。
    public static class TacticalAlarmHearing {
        public const float Range = 35;

        private static class PhysicsMask {
            public static readonly int World = LayerMask.GetMask("Default", "Ground");
        }

        public static string EventId(uint epoch, uint id) { return id == 0 ? null : $"trap/{epoch}/{id}"; }

        public static bool WithinRange(float squaredDistance, bool obstructed, float range = Range) {
            return MissionMachineSounds.WithinRange(squaredDistance, range, obstructed);
        }

        public static bool Audible(PhysicsScene scene, Vector3 source, Vector3 listener, float range = Range) {
            if (!scene.IsValid()) return false;
            var delta = listener - source;
            float distanceSquared = delta.sqrMagnitude;
            if (!WithinRange(distanceSquared, false, range)) return false;
            if (WithinRange(distanceSquared, true, range)) return true;
            float distance = Mathf.Sqrt(distanceSquared);
            return !scene.Raycast(source, delta / distance, out _, distance, PhysicsMask.World,
                QueryTriggerInteraction.Ignore);
        }
    }

    // 每次实际爆炸只发一轮限量的声音线索。附近空闲敌人只获得
    // 爆炸位置，不能直接识别投掷者或得到瞄准点。
    public static class RaidEnvironmentalNoise {
        public const float ExplosionRange = 50;

        public static string ExplosionId(bool energy, uint epoch, uint id) {
            return id == 0 ? null : energy ? $"blast/energy/{id}" : $"blast/tactical/{epoch}/{id}";
        }

        // 实体版本让回收复用的投掷体 ID 依然能区分，
        // 而声音线索里无需带射手身份。
        public static string ProjectileExplosionId(Entity projectile) {
            return projectile == Entity.Null ? null : $"blast/projectile/{projectile.Index}/{projectile.Version}";
        }

        public static void EmitExplosion(
            EntityManager manager,
            PhysicsScene scene,
            Vector3 position,
            string clue,
            double now) {
            if (!scene.IsValid() || string.IsNullOrEmpty(clue) || !double.IsFinite(now) ||
                BountyClashRuntime.Enabled(manager))
                return;
            var source = position + Vector3.up;
            using var humans = manager.CreateEntityQuery(new EntityQueryDesc {
                All = new[] {
                    ComponentType.ReadOnly<DollSingerEnemy>(), ComponentType.ReadOnly<Simulate>(),
                    ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostGameObjectLink>()
                },
                None = new[] { ComponentType.ReadOnly<ClashBot>() }
            });
            using var actors = humans.ToEntityArray(Allocator.Temp);
            foreach (var entity in actors) {
                if (manager.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth <= 0 ||
                    !manager.HasComponent<DollSingerEnemyBrain>(entity))
                    continue;
                var link = manager.GetComponentObject<GhostGameObjectLink>(entity);
                if (!link.LinkedInstance || link.LinkedInstance.gameObject.scene.GetPhysicsScene() != scene) continue;
                var brain = manager.GetComponentObject<DollSingerEnemyBrain>(entity);
                var listener = (Vector3)manager.GetComponentData<LocalTransform>(entity).Position + Vector3.up * 1.2f;
                if (brain.Target != Entity.Null || brain.Leaving ||
                    !TacticalAlarmHearing.Audible(scene, source, listener, ExplosionRange))
                    continue;
                DollSingerEnemySystem.HearMissionAlarm(brain, clue, position, now);
            }
            using var aliens = manager.CreateEntityQuery(new EntityQueryDesc {
                All = new[] {
                    ComponentType.ReadOnly<ProceduralAlienEnemy.State>(), ComponentType.ReadOnly<Simulate>(),
                    ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostGameObjectLink>()
                }
            });
            using var creatures = aliens.ToEntityArray(Allocator.Temp);
            foreach (var entity in creatures) {
                if (manager.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth <= 0 ||
                    !TacticalAlarmHearing.WithinRange(
                        ((Vector3)manager.GetComponentData<LocalTransform>(entity).Position - source).sqrMagnitude,
                        false, ExplosionRange))
                    continue;
                var link = manager.GetComponentObject<GhostGameObjectLink>(entity);
                if (!link.LinkedInstance || link.LinkedInstance.gameObject.scene.GetPhysicsScene() != scene) continue;
                var alien = link.LinkedInstance.GetComponent<ProceduralAlienEnemy>();
                if (alien && TacticalAlarmHearing.Audible(scene, source, alien.AimPoint, ExplosionRange))
                    alien.HearMissionAlarm(clue, position);
            }
        }
    }

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

        private void TacticalDetonate(
            ref SystemState state,
            EntityCommandBuffer ecb,
            TacticalServerWorld world,
            TacticalServerWorld.Object obj,
            double now,
            uint tick) {
            // 投掷物根据临界阶段变成闪光、烟雾、持续范围或一次性爆炸。状态转完再决定保留多久供同步。
            if (obj.Definition.Kind == TacticalItemKind.Flash) {
                TacticalFlash(state.EntityManager, world, obj, now);
                RaidEnvironmentalNoise.EmitExplosion(state.EntityManager, obj.Scene, obj.Position,
                    RaidEnvironmentalNoise.ExplosionId(false, world.Epoch, obj.Id), now);
                obj.Phase = TacticalWorldPhase.Explosion;
                obj.Velocity = Vector3.zero;
                obj.Until = now + .35;
                obj.NextSnapshot = 0;
                TacticalBroadcast(ecb, world, obj, false);
                return;
            }
            if (obj.Definition.Kind == TacticalItemKind.Smoke) {
                obj.Phase = TacticalWorldPhase.Smoke;
                obj.Velocity = Vector3.zero;
                obj.Until = now + obj.Definition.Lifetime;
                obj.NextSnapshot = 0;
                return;
            }
            if (obj.Definition.Kind == TacticalItemKind.Fire || obj.Definition.Kind == TacticalItemKind.Choke ||
                obj.Definition.Kind == TacticalItemKind.PoisonBomb) {
                if (obj.Definition.Kind == TacticalItemKind.Fire &&
                    TacticalChoked(world, obj.Position, obj.Scene, now)) {
                    obj.Until = now;
                    return;
                }
                obj.Phase = obj.Definition.Kind == TacticalItemKind.Fire ? TacticalWorldPhase.Fire :
                    obj.Definition.Kind == TacticalItemKind.PoisonBomb ? TacticalWorldPhase.Poison :
                    TacticalWorldPhase.Choke;
                obj.Velocity = Vector3.zero;
                obj.Until = now + obj.Definition.Lifetime;
                obj.NextSnapshot = 0;
                return;
            }
            if (obj.Definition.Kind != TacticalItemKind.Dynamite && obj.Definition.Kind != TacticalItemKind.Frag)
                return;
            foreach (var victim in world.Victims) {
                if (!TacticalCanBeHurt(ref state, victim)) continue;
                var link = state.EntityManager.GetComponentObject<GhostGameObjectLink>(victim).LinkedInstance;
                if (!link || link.gameObject.scene.GetPhysicsScene() != obj.Scene) continue;
                var point = link.transform.position + Vector3.up * .8f;
                float damage =
                    TacticalItemRules.BlastDamage(obj.Definition.Kind, Vector3.Distance(obj.Position, point));
                if (damage <= 0 || !EnergyGrenadePhysics.ClearSight(obj.Scene, obj.Position, point, link.transform))
                    continue;
                // 爆炸用真实距离与遮挡先筛目标，再交给共用伤害层。流血只由实际穿过护盾等保护而扣到血量的伤害触发。
                float actual = BountyClashDamage.Apply(state.World, victim, obj.Source, BodyPart.Auto, damage, tick,
                    RaidDamageKind.Explosion);
                if (actual > 0 && obj.Definition.Kind == TacticalItemKind.Frag)
                    TacticalRefreshBleed(TacticalStatus(world, victim), now + TacticalItemRules.BleedDuration,
                        obj.Owner, obj.Source);
            }
            if (obj.Definition.Kind == TacticalItemKind.Dynamite)
                foreach (var other in world.Objects)
                    if (other != obj &&
                        (other.Phase == TacticalWorldPhase.Wire || other.Phase == TacticalWorldPhase.Armed) &&
                        other.Scene == obj.Scene && Vector3.Distance(other.Position, obj.Position) <= 8 &&
                        EnergyGrenadePhysics.ClearSight(obj.Scene, obj.Position, other.Position + Vector3.up * .2f,
                            null))
                        other.Until = now;
            RaidEnvironmentalNoise.EmitExplosion(state.EntityManager, obj.Scene, obj.Position,
                RaidEnvironmentalNoise.ExplosionId(false, world.Epoch, obj.Id), now);
            obj.Phase = TacticalWorldPhase.Explosion;
            obj.Until = now + .6;
            obj.NextSnapshot = 0;
            TacticalBroadcast(ecb, world, obj, false);
        }

        // 重用常规受影响角色列表与物理场景，无需逐人新增重叠查询或网络消息。
        public static void TacticalFlash(
            EntityManager manager,
            TacticalServerWorld world,
            TacticalServerWorld.Object obj,
            double now) {
            foreach (var victim in world.Victims) {
                if (!manager.HasComponent<PredictedPlayerGhost>(victim) ||
                    !manager.HasComponent<GhostGameObjectLink>(victim))
                    continue;
                var health = manager.GetComponentData<PredictedPlayerGhost>(victim);
                if (health.CurrentHealth <= 0 || (health.ClashEnabled && health.ClashLife != ClashLife.Alive) ||
                    BountyClashDamage.TeamProtected(manager, victim, obj.Source))
                    continue;
                var root = manager.GetComponentObject<GhostGameObjectLink>(victim).LinkedInstance;
                if (!root || root.gameObject.scene.GetPhysicsScene() != obj.Scene) continue;
                var eye = TacticalEye(root);
                var forward = TacticalDirection(health);
                if (root.TryGetComponent<ProceduralAlienEnemy>(out var alien)) {
                    eye = alien.AimPoint;
                    forward = root.transform.forward;
                } else {
                    var look = health.ControllerState.FreeLookOffset;
                    forward = Quaternion.Euler(health.ControllerState.PitchDegrees + look.y,
                        health.ControllerState.YawDegrees + look.x, 0) * Vector3.forward;
                }
                var delta = obj.Position - eye;
                float distance = delta.magnitude;
                float strength = TacticalItemRules.FlashStrength(distance,
                    distance > .01f ? Vector3.Dot(forward, delta / distance) : 1);
                if (strength <= 0 || !EnergyGrenadePhysics.ClearSight(obj.Scene, obj.Position, eye, root.transform) ||
                    SmokeVisibility.Blocked(world, obj.Scene, obj.Position, eye))
                    continue;
                var status = TacticalStatus(world, victim);
                // 闪光强度会衰减。新的只在当时更强时覆盖旧状态，免得弱闪光反而把现有更强效果冲掉。
                float remaining = status.FlashStrength * Mathf.Clamp01((float)(status.FlashUntil - now) / 4);
                if (strength <= remaining) continue;
                status.FlashStrength = strength;
                status.FlashUntil = now + obj.Definition.Lifetime;
            }
        }

        private void TacticalArea(
            ref SystemState state,
            TacticalServerWorld world,
            TacticalServerWorld.Object obj,
            double now,
            uint tick) {
            if (obj.Phase != TacticalWorldPhase.Armed && obj.Phase != TacticalWorldPhase.Fire &&
                obj.Phase != TacticalWorldPhase.Poison && obj.Phase != TacticalWorldPhase.Wire &&
                obj.Phase != TacticalWorldPhase.Choke)
                return;
            foreach (var victim in world.Victims) {
                if (!TacticalCanBeHurt(ref state, victim)) continue;
                var root = state.EntityManager.GetComponentObject<GhostGameObjectLink>(victim).LinkedInstance;
                if (!root) continue;
                var pos = root.transform.position;
                if (!TacticalWithin(obj, pos, 0, root.gameObject.scene.GetPhysicsScene(), root.transform)) continue;
                // 机关先等布置计时结束，再判断人物真的进了有效区域。触发后改为报警、绊线或持续状态。
                if (obj.Phase == TacticalWorldPhase.Armed) {
                    if (now < obj.ArmAt || Mathf.Abs(pos.y - obj.Position.y) > TacticalItemRules.TrapVerticalTolerance)
                        continue;
                    obj.Phase = obj.Definition.Kind == TacticalItemKind.AlertTrap ? TacticalWorldPhase.Alarm :
                        obj.Definition.Kind == TacticalItemKind.WireTrap ? TacticalWorldPhase.Wire :
                        TacticalWorldPhase.Poison;
                    obj.Until = now + (obj.Phase == TacticalWorldPhase.Alarm ? 8 :
                        obj.Phase == TacticalWorldPhase.Wire ? 45 : 30);
                    obj.NextSnapshot = 0;
                    if (obj.Phase == TacticalWorldPhase.Alarm) {
                        EmitTacticalAlarm(ref state, world, obj, now);
                        break;
                    }
                }
                // 所有角色都能触发陷阱。同队保护只
                // 阻拦有害状态，保留抑制道具的通用用途和咳嗽效果。
                // 触发装置和受其伤害分开。队友可触发，但同队保护会屏蔽对应的有害状态。
                if (TacticalAreaBlockedByTeam(obj.Phase,
                        BountyClashDamage.TeamProtected(state.EntityManager, victim, obj.Source)))
                    continue;
                var status = TacticalStatus(world, victim);
                if (obj.Phase == TacticalWorldPhase.Choke) {
                    status.Fire = status.Poison = 0;
                    status.Cough = now + 1;
                } else if (obj.Phase == TacticalWorldPhase.Fire && !TacticalChoked(world, pos, obj.Scene, now)) {
                    status.Fire = now + TacticalItemRules.BurningDuration;
                    status.FireOwner = obj.Owner;
                    status.FireSource = obj.Source;
                } else if (obj.Phase == TacticalWorldPhase.Poison && status.Antidote <= now &&
                           !TacticalChoked(world, pos, obj.Scene, now)) {
                    status.Poison = now + 2;
                    status.PoisonOwner = obj.Owner;
                    status.PoisonSource = obj.Source;
                } else if (obj.Phase == TacticalWorldPhase.Wire) {
                    status.Wire = now + .5;
                    TacticalRefreshBleed(status, now + TacticalItemRules.BleedDuration, obj.Owner, obj.Source);
                }
            }
        }

        // 报警声也是 AI 可听的线索。敌人需处于同地图并符合听觉条件，忙于追敌或正离场的 PMC 不打断自己的决定。
        private void EmitTacticalAlarm(
            ref SystemState state,
            TacticalServerWorld world,
            TacticalServerWorld.Object alarm,
            double now) {
            if (ClashEnabled(ref state) || !alarm.Scene.IsValid()) return;
            string clue = TacticalAlarmHearing.EventId(world.Epoch, alarm.Id);
            if (clue == null) return;
            var source = alarm.Position + Vector3.up;
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, GhostGameObjectLink, Simulate>().WithNone<ClashBot>()
                         .WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0 ||
                    !state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity))
                    continue;
                var link = state.EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (!link.LinkedInstance || link.LinkedInstance.gameObject.scene.GetPhysicsScene() != alarm.Scene)
                    continue;
                var brain = state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                if (brain.Target != Entity.Null || brain.Leaving || !TacticalAlarmHearing.Audible(alarm.Scene, source,
                        (Vector3)pose.ValueRO.Position + Vector3.up * 1.2f))
                    continue;
                DollSingerEnemySystem.HearMissionAlarm(brain, clue, alarm.Position, now);
            }
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>>()
                         .WithAll<ProceduralAlienEnemy.State, GhostGameObjectLink, Simulate>().WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0 ||
                    !TacticalAlarmHearing.WithinRange(((Vector3)pose.ValueRO.Position - source).sqrMagnitude, false))
                    continue;
                var link = state.EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (!link.LinkedInstance || link.LinkedInstance.gameObject.scene.GetPhysicsScene() != alarm.Scene)
                    continue;
                var alien = link.LinkedInstance.GetComponent<ProceduralAlienEnemy>();
                if (alien && TacticalAlarmHearing.Audible(alarm.Scene, source, alien.AimPoint))
                    alien.HearMissionAlarm(clue, alarm.Position);
            }
        }

        private void TacticalTickStatuses(
            ref SystemState state,
            TacticalServerWorld world,
            double now,
            float dt,
            uint tick) {
            world.Scratch.Clear();
            foreach (var pair in world.Statuses) {
                var victim = pair.Key;
                var status = pair.Value;
                if (!SystemAPI.Exists(victim) || !SystemAPI.HasComponent<PredictedPlayerGhost>(victim)) {
                    world.Scratch.Add(victim);
                    continue;
                }
                var health = SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
                bool canHurt = health.CurrentHealth > 0 &&
                               (!health.ClashEnabled || health.ClashLife == ClashLife.Alive);
                if (!canHurt)
                    status.Fire = status.Poison = status.Bleed =
                        status.Wire = status.Cough = status.FlashUntil = status.Regeneration = 0;
                health.TacticalWireSeconds = TacticalRemaining(status.Wire, now);
                health.TacticalCoughSeconds = TacticalRemaining(status.Cough, now);
                health.TacticalPoisonSeconds = TacticalRemaining(status.Poison, now);
                health.TacticalBleeding = status.Bleed > now;
                SystemAPI.SetComponent(victim, health);
                if (!canHurt) continue;
                bool treating = false;
                foreach (var pending in world.Uses.Values)
                    if (pending.Player == victim && pending.Action == TacticalItemAction.Extinguish) {
                        treating = true;
                        break;
                    }
                bool monitorPmc = (status.Fire > now || status.Bleed > now) &&
                                  SystemAPI.HasComponent<DollSingerEnemy>(victim) &&
                                  !SystemAPI.HasComponent<ClashBot>(victim);
                if (status.Fire > now) {
                    var before = monitorPmc ? SystemAPI.GetComponent<PredictedPlayerGhost>(victim) : default;
                    BountyClashDamage.Apply(state.World, victim, status.FireSource, BodyPart.Auto, 8 * dt, tick,
                        RaidDamageKind.Fire);
                    if (monitorPmc)
                        PmcStatusDamage.Record(status, before, SystemAPI.GetComponent<PredictedPlayerGhost>(victim));
                }
                if (status.Poison > now && status.Antidote <= now)
                    BountyClashDamage.Apply(state.World, victim, status.PoisonSource, BodyPart.Auto, 6 * dt, tick,
                        RaidDamageKind.Poison);
                if (status.Bleed > now) {
                    var before = monitorPmc ? SystemAPI.GetComponent<PredictedPlayerGhost>(victim) : default;
                    BountyClashDamage.Apply(state.World, victim, status.BleedSource, BodyPart.Auto, 4 * dt, tick,
                        RaidDamageKind.Bleed);
                    if (monitorPmc)
                        PmcStatusDamage.Record(status, before, SystemAPI.GetComponent<PredictedPlayerGhost>(victim));
                    // 处理伤口的绷带不应该因该伤口本身继续流血被打断。新攻击、火和毒气仍会打断。
                    if (status.Fire <= now && status.Poison <= now) {
                        var connection = FindTacticalConnection(ref state, victim);
                        if (state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                            var inventory = GetRaidInventory(ref state, connection);
                            if (inventory.MedicalItemId != null && !inventory.MedicalIsConsumable &&
                                ConsumableCatalog.StopsBleeding(inventory.Graph.Find(inventory.MedicalItemId)?.Code) &&
                                RaidHealth.SelfTreatmentDamageUnchanged(health, inventory.MedicalDamageSequence,
                                    inventory.MedicalShieldHitSequence)) {
                                var after = SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
                                inventory.MedicalDamageSequence = after.NonPhysiologicalDamageSequence;
                                inventory.MedicalShieldHitSequence = after.ShieldHitSequence;
                            }
                        }
                    }
                }
                if (treating && status.Poison <= now) {
                    // 伤害正常发生。只把本次操作已经知道的状态伤害计入
                    // 新的基线，且本次更新之前不能有外部受击。
                    // 同一 tick 内即使伤害全被吸收，也会被护盾受击序号发现。
                    // 开始或取消动作都不会暂停伤害。
                    var after = SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
                    foreach (var pending in world.Uses.Values)
                        if (pending.Player == victim && pending.Action == TacticalItemAction.Extinguish &&
                            RaidHealth.SelfTreatmentDamageUnchanged(health, pending.DamageSequence,
                                pending.ShieldHit)) {
                            pending.Hit = after.LastHitTick;
                            pending.DamageSequence = after.NonPhysiologicalDamageSequence;
                            pending.ShieldHit = after.ShieldHitSequence;
                            pending.Health = after.CurrentHealth;
                        }
                }
                if (status.Regeneration > 0) {
                    // 只按实际有效时间回血，包括在该 tick 中途到期的部分。中毒或满血时不会累积以后再用的回复量。
                    float seconds = (float)Math.Max(0,
                        Math.Min(now, status.Regeneration) - Math.Max(status.RegenerationTick, now - dt));
                    status.RegenerationTick = now;
                    var after = SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
                    if (after.CurrentHealth <= 0 || (after.ClashEnabled && after.ClashLife != ClashLife.Alive))
                        status.Regeneration = 0;
                    else if (seconds > 0 &&
                             RaidHealth.Regenerate(ref after, seconds * TacticalItemRules.RegenerationPerSecond) > 0)
                        SystemAPI.SetComponent(victim, after);
                    if (now >= status.Regeneration) status.Regeneration = 0;
                }
                if (!status.Sprint.Initialized && status.FlashUntil <= now && status.Regeneration <= now && Math.Max(
                        Math.Max(Math.Max(status.Fire, status.Poison), Math.Max(status.Bleed, status.Stamina)),
                        Math.Max(status.Antidote, Math.Max(status.Wire, status.Cough))) <= now)
                    world.Scratch.Add(victim);
            }
            foreach (var entity in world.Scratch) world.Statuses.Remove(entity);
        }

        private bool TacticalCanBeHurt(ref SystemState state, Entity victim) {
            if (!SystemAPI.Exists(victim) || !SystemAPI.HasComponent<PredictedPlayerGhost>(victim)) return false;
            var health = SystemAPI.GetComponent<PredictedPlayerGhost>(victim);
            // 断开的活角色不能行动，仍可受伤。倒地角色
            // 沿用第一版不受燃烧伤害且不能处决的规则。
            return health.CurrentHealth > 0 && (!health.ClashEnabled || health.ClashLife == ClashLife.Alive);
        }

        // 从实际位置变化和控制状态判冲刺，更新耐力再同步给角色。被卡住或不能行动时不会算作正常连续奔跑。
        private void TacticalTickSprint(ref SystemState state, TacticalServerWorld world, double now, float dt) {
            if (!MoonRaidMap.Active) return;
            foreach (var (session, joined) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>()) {
                var player = joined.ValueRO.PlayerEntity;
                if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<PredictedPlayerGhost>(player)) continue;
                var health = SystemAPI.GetComponent<PredictedPlayerGhost>(player);
                var status = TacticalStatus(world, player);
                bool allowed = session.ValueRO.Phase == RaidPhase.Active && health.CurrentHealth > 0 &&
                               (!health.ClashEnabled || health.ClashLife == ClashLife.Alive);
                bool canRun = allowed && BountyClashDamage.CanAct(state.EntityManager, player);
                var position = SystemAPI.HasComponent<LocalTransform>(player)
                    ? SystemAPI.GetComponent<LocalTransform>(player).Position
                    : health.ControllerState.CurrentPosition;
                var travel = position - status.SprintPosition;
                float threshold = Mathf.Max(.005f, dt * .15f);
                bool moved = status.SprintPositionKnown &&
                             travel.x * travel.x + travel.z * travel.z > threshold * threshold;
                bool sprinting = canRun && health.ControllerState.Sprinting &&
                                 health.ControllerState.MovementSpeed > .1f && moved;
                status.SprintPosition = position;
                status.SprintPositionKnown = allowed;
                TacticalSprintRules.Tick(ref status.Sprint, dt, sprinting, status.Stamina > now, allowed,
                    health.ClashEnabled ? 0 : health.CarriedWeight);
                health.TacticalSprintStamina = status.Sprint.Value;
                health.TacticalSprintExhausted = status.Sprint.Exhausted;
                SystemAPI.SetComponent(player, health);
            }
            // PMC 没有连接或 RaidSession，但和真人远征玩家一样，
            // 依据实际移动、负重和恢复规则算体力。
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithNone<ClashBot>().WithEntityAccess()) {
                var status = TacticalStatus(world, entity);
                bool alive = health.ValueRO.CurrentHealth > 0;
                PmcSprintRules.Tick(ref status.Sprint, ref status.SprintPosition, ref status.SprintPositionKnown,
                    pose.ValueRO.Position, dt, alive, health.ValueRO.ControllerState.Sprinting,
                    health.ValueRO.ControllerState.MovementSpeed, health.ValueRO.CarriedWeight, status.Stamina > now);
                health.ValueRW.TacticalSprintStamina = status.Sprint.Value;
                health.ValueRW.TacticalSprintExhausted = status.Sprint.Exhausted;
            }
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