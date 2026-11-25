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
    }
}
