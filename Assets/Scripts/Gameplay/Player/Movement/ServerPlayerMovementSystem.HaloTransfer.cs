// 服务器推进光环给队友输送能量、协防和召回。每拍验证目标的身份、队伍、距离、视线和装备。
// 伤害入口也会查协防光环，让供能者实际消耗自己的能量代为挡伤害。
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    public partial class ServerPlayerMovementSystem {
        private readonly Dictionary<(int, uint), Entity> m_TransferGhosts = new();
        private readonly List<Entity> m_TransferActors = new();

        private void UpdateHaloTransfers(uint serverTick, float dt) {
            // 重建这拍的候选角色索引。已经离场或还未生成的对象不进入传输候选。
            m_TransferGhosts.Clear();
            m_TransferActors.Clear();
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>()
                         .WithAll<PredictedPlayerGhost, GhostOwner, Simulate>().WithEntityAccess()) {
                m_TransferGhosts[(ghost.ValueRO.ghostId, ghost.ValueRO.spawnTick.SerializedData)] = entity;
                m_TransferActors.Add(entity);
            }
            foreach (var actor in m_TransferActors) {
                var sender = EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
                bool fresh = TryReadTransferInput(actor, serverTick, out var input);
                // 光环传输需要本拍有效的持续输入。发起时还要检测新的按下边沿，持续按住不会重建连接。
                bool held = fresh && input.HaloTransfer;
                bool start = held && (!sender.HaloTransferLatched || (input.HaloGuard && !sender.HaloGuardLatched));
                bool wasLatched = sender.HaloTransferLatched;
                bool wasGuard = sender.HaloGuardLatched;
                bool wasAnchor = sender.HaloAnchorLatched, wasMode = sender.HaloModeLatched;
                if (fresh) {
                    sender.HaloTransferLatched = held;
                    sender.HaloGuardLatched = input.HaloGuard;
                }
                if (UpdateHaloAnchor(actor, ref sender, input, fresh, dt)) {
                    EntityManager.SetComponentData(actor, sender);
                    continue;
                }
                if (!HaloTransferRules.Occupied(sender) && !start) {
                    if (wasLatched != sender.HaloTransferLatched || wasGuard != sender.HaloGuardLatched ||
                        wasAnchor != sender.HaloAnchorLatched || wasMode != sender.HaloModeLatched)
                        EntityManager.SetComponentData(actor, sender);
                    continue;
                }
                if (!EnergyShieldRules.Alive(sender) || !sender.HaloEquipped) {
                    HaloTransferRules.Clear(ref sender);
                } else if (sender.HaloTransferPhase == HaloTransferPhase.Returning) {
                    HaloTransferRules.TickReturn(ref sender, dt);
                } else {
                    bool occupied = HaloTransferRules.Occupied(sender);
                    var target = occupied ? sender.HaloTransferTarget : Entity.Null;
                    if (!occupied && start)
                        // 用 Ghost 编号加出生拍识别这次连接。角色销毁重生后，即使编号再用，也不应连到旧目标。
                        m_TransferGhosts.TryGetValue((input.HaloTransferGhostId, input.HaloTransferSpawnTick),
                            out target);
                    // 先核对当前目标还活着并在同一战局。随后按输入模式检查是否可接收补能或保护。
                    bool valid = held && (occupied || start) && target != actor && target != Entity.Null &&
                                 EntityManager.HasComponent<PredictedPlayerGhost>(target) &&
                                 EntityManager.HasComponent<GhostInstance>(target) &&
                                 HaloTransferRules.CanSend(sender) && ActiveTransferActor(actor, false) &&
                                 ActiveTransferActor(target, true);
                    PredictedPlayerGhost receiver = default;
                    if (valid) {
                        receiver = EntityManager.GetComponentData<PredictedPlayerGhost>(target);
                        var identity = EntityManager.GetComponentData<GhostInstance>(target);
                        float3 from = HaloTransferRules.Anchor(sender), to = HaloTransferRules.Anchor(receiver);
                        valid = identity.ghostId == input.HaloTransferGhostId &&
                                identity.spawnTick.SerializedData == input.HaloTransferSpawnTick &&
                                (!occupied || sender.HaloTransferWeapon == sender.EquippedWeaponID) &&
                                (input.HaloGuard
                                    ? HaloSupportRules.CanProtect(receiver)
                                    : HaloTransferRules.CanReceive(receiver)) &&
                                HaloTransferRules.Teammates(sender, receiver, IsHuman(actor), IsHuman(target)) &&
                                HaloTransferRules.InRange(from, to) && TransferVisible(actor, from, to);
                        // 建立一条新连接时还要求角色确实朝向队友。已有连接每拍重新查距离和遮挡。
                        if (valid && !occupied) {
                            var forward =
                                Quaternion.Euler(input.LookYawPitchDegrees.y, input.LookYawPitchDegrees.x, 0) *
                                Vector3.forward;
                            valid = math.dot((float3)forward, math.normalizesafe(to - from)) >= .5f;
                            if (valid)
                                HaloTransferRules.Begin(ref sender, target, to,
                                    input.HaloGuard ? HaloDeploymentMode.AllyGuard : HaloDeploymentMode.Transfer);
                        }
                        if (valid) {
                            sender.HaloTransferPoint = to;
                            sender.HaloDeploymentMode =
                                input.HaloGuard ? HaloDeploymentMode.AllyGuard : HaloDeploymentMode.Transfer;
                            // 协防推进的是部署时间。补能则真的同时修改发送者和接收者，避免队友只看到动作却没收到能量。
                            if (input.HaloGuard) { HaloSupportRules.AdvanceDeployment(ref sender, dt); } else {
                                HaloTransferRules.Transfer(ref sender, ref receiver, dt);
                                EntityManager.SetComponentData(target, receiver);
                            }
                        }
                    }
                    if (!valid) HaloTransferRules.Recall(ref sender);
                }
                EntityManager.SetComponentData(actor, sender);
            }
        }

        private bool TryReadTransferInput(Entity actor, uint tick, out PlayerInput input) {
            input = default;
            if (!IsHuman(actor) || !EntityManager.HasComponent<PlayerClientCommandInputLookup>(actor)) return false;
            var source = EntityManager.GetComponentData<PlayerClientCommandInputLookup>(actor).ClientCommandInputEntity;
            if (!EntityManager.HasBuffer<ClientCommandInput>(source)) return false;
            var now = new NetworkTick(tick);
            var commands = EntityManager.GetBuffer<ClientCommandInput>(source, true);
            if (!commands.GetDataAtTick(now, out var command) || !command.Tick.IsValid ||
                command.Tick.IsNewerThan(now) || now.TicksSince(command.Tick) > 12)
                return false;
            input = RaidHealth
                .RestrictInput(EntityManager.GetComponentData<PredictedPlayerGhost>(actor), command.PlayerInput)
                .Sanitized();
            return true;
        }

        private bool IsHuman(Entity actor) {
            return EntityManager.HasComponent<GhostOwner>(actor) &&
                   EntityManager.GetComponentData<GhostOwner>(actor).NetworkId > 0;
        }

        // 由权威伤害入口调用。每次都读捐助者当前的能量池，
        // 同一拍可以挨多发，每次都要消耗当时的余额。捐助者就是受害者时也一样按当前值算。
        // 角色被击中时按当前状态重新查协防者。前一发已经用掉的能量不能再给下一发吸收。
        public float AbsorbHaloSupport(
            Entity victim,
            ref PredictedPlayerGhost health,
            float incoming,
            uint tick,
            RaidDamageKind kind,
            float3 point) {
            if (!EnergyShieldRules.Blocks(kind) || !HaloSupportRules.CanProtect(health) || incoming <= 0)
                return incoming;
            foreach (var actor in m_TransferActors) {
                if (!EntityManager.HasComponent<PredictedPlayerGhost>(actor)) continue;
                var donor = actor == victim ? health : EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
                bool guard = donor.HaloDeploymentMode == HaloDeploymentMode.AllyGuard &&
                             donor.HaloTransferTarget == victim;
                bool area = donor.HaloDeploymentMode == HaloDeploymentMode.Shield && HaloSupportRules.Within(
                    donor.HaloTransferPoint - new float3(0, .6f, 0), HaloTransferRules.Anchor(health),
                    HaloSupportRules.ShieldRadius);
                if (donor.HaloTransferPhase != HaloTransferPhase.Sending || (!guard && !area) ||
                    !HaloTransferRules.CanSend(donor) || donor.HaloTransferWeapon != donor.EquippedWeaponID ||
                    !ActiveTransferActor(actor, false) || !ActiveTransferActor(victim, true) ||
                    !HaloTransferRules.Teammates(donor, health, IsHuman(actor), IsHuman(victim)) ||
                    (guard
                        ? !HaloTransferRules.InRange(HaloTransferRules.Anchor(donor), HaloTransferRules.Anchor(health))
                        : !HaloSupportRules.Within(HaloTransferRules.Anchor(donor), donor.HaloTransferPoint,
                            HaloSupportRules.LeashRange)) || !TransferVisible(actor,
                        guard ? HaloTransferRules.Anchor(donor) : donor.HaloTransferPoint,
                        HaloTransferRules.Anchor(health)))
                    continue;
                // 能抵掉多少就扣多少并记录撞击之后的状态，余下的 incoming 继续交给常规伤害流程。
                float absorbed = HaloSupportRules.Absorb(ref donor, incoming, tick, kind, point);
                if (absorbed <= 0) continue;
                if (actor == victim) { health = donor; } else {
                    EntityManager.SetComponentData(actor, donor);
                    HaloSupportRules.RecordHit(ref health, absorbed, incoming, tick, point);
                }
                return incoming - absorbed;
            }
            return incoming;
        }

        private bool ActiveTransferActor(Entity actor, bool allowBot) {
            if (!IsHuman(actor)) return allowBot && EntityManager.HasComponent<DollSingerEnemyBrain>(actor);
            if (!SystemAPI.TryGetSingletonBuffer<ClientsMap>(out var clients)) return false;
            int owner = EntityManager.GetComponentData<GhostOwner>(actor).NetworkId;
            if (owner >= clients.Length) return false;
            var connection = clients[owner].ConnectionEntity;
            if (!EntityManager.Exists(connection) || !EntityManager.HasComponent<JoinedClient>(connection) ||
                !EntityManager.HasComponent<RaidSession>(connection) ||
                !EntityManager.HasComponent<NetworkStreamInGame>(connection) ||
                EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection))
                return false;
            return EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity == actor &&
                   EntityManager.GetComponentData<RaidSession>(connection).Phase == RaidPhase.Active;
        }

        private bool TransferVisible(Entity actor, float3 from, float3 to) {
            if (!EntityManager.HasComponent<GhostGameObjectLink>(actor)) return false;
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(actor);
            if (!link.LinkedInstance) return false;
            var scene = link.LinkedInstance.gameObject.scene.GetPhysicsScene();
            var delta = to - from;
            float distance = math.length(delta);
            return scene.IsValid() && (distance < .01f || !scene.Raycast(from, delta / distance, out _, distance,
                s_ShootableLayerMask, QueryTriggerInteraction.Ignore));
        }
    }
}