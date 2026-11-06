using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public partial class ServerPlayerMovementSystem
    {
        private readonly Dictionary<(int, uint), Entity> m_TransferGhosts = new();
        private readonly List<Entity> m_TransferActors = new();

        private void UpdateHaloTransfers(uint serverTick, float dt)
        {
            m_TransferGhosts.Clear();
            m_TransferActors.Clear();
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>()
                         .WithAll<PredictedPlayerGhost, GhostOwner, Simulate>().WithEntityAccess())
            {
                m_TransferGhosts[(ghost.ValueRO.ghostId, ghost.ValueRO.spawnTick.SerializedData)] = entity;
                m_TransferActors.Add(entity);
            }

            foreach (var actor in m_TransferActors)
            {
                var sender = EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
                bool fresh = TryReadTransferInput(actor, serverTick, out var input);
                bool held = fresh && input.HaloTransfer;
                bool start = held && (!sender.HaloTransferLatched || input.HaloGuard && !sender.HaloGuardLatched);
                bool wasLatched = sender.HaloTransferLatched;
                bool wasGuard = sender.HaloGuardLatched;
                bool wasAnchor = sender.HaloAnchorLatched, wasMode = sender.HaloModeLatched;
                if (fresh) { sender.HaloTransferLatched = held; sender.HaloGuardLatched = input.HaloGuard; }
                if (UpdateHaloAnchor(actor, ref sender, input, fresh, dt))
                { EntityManager.SetComponentData(actor, sender); continue; }
                if (!HaloTransferRules.Occupied(sender) && !start)
                {
                    if (wasLatched != sender.HaloTransferLatched || wasGuard != sender.HaloGuardLatched ||
                        wasAnchor != sender.HaloAnchorLatched || wasMode != sender.HaloModeLatched)
                        EntityManager.SetComponentData(actor, sender);
                    continue;
                }

                if (!EnergyShieldRules.Alive(sender) || !sender.HaloEquipped)
                    HaloTransferRules.Clear(ref sender);
                else if (sender.HaloTransferPhase == HaloTransferPhase.Returning)
                    HaloTransferRules.TickReturn(ref sender, dt);
                else
                {
                    bool occupied = HaloTransferRules.Occupied(sender);
                    Entity target = occupied ? sender.HaloTransferTarget : Entity.Null;
                    if (!occupied && start)
                        m_TransferGhosts.TryGetValue((input.HaloTransferGhostId, input.HaloTransferSpawnTick), out target);
                    bool valid = held && (occupied || start) && target != actor && target != Entity.Null &&
                        EntityManager.HasComponent<PredictedPlayerGhost>(target) && EntityManager.HasComponent<GhostInstance>(target) &&
                        HaloTransferRules.CanSend(sender) && ActiveTransferActor(actor, false) && ActiveTransferActor(target, true);
                    PredictedPlayerGhost receiver = default;
                    if (valid)
                    {
                        receiver = EntityManager.GetComponentData<PredictedPlayerGhost>(target);
                        var identity = EntityManager.GetComponentData<GhostInstance>(target);
                        float3 from = HaloTransferRules.Anchor(sender), to = HaloTransferRules.Anchor(receiver);
                        valid = identity.ghostId == input.HaloTransferGhostId && identity.spawnTick.SerializedData == input.HaloTransferSpawnTick &&
                            (!occupied || sender.HaloTransferWeapon == sender.EquippedWeaponID) &&
                            (input.HaloGuard ? HaloSupportRules.CanProtect(receiver) : HaloTransferRules.CanReceive(receiver)) &&
                            HaloTransferRules.Teammates(sender, receiver, IsHuman(actor), IsHuman(target)) &&
                            HaloTransferRules.InRange(from, to) && TransferVisible(actor, from, to);
                        if (valid && !occupied)
                        {
                            Vector3 forward = Quaternion.Euler(input.LookYawPitchDegrees.y, input.LookYawPitchDegrees.x, 0) * Vector3.forward;
                            valid = math.dot((float3)forward, math.normalizesafe(to - from)) >= .5f;
                            if (valid) HaloTransferRules.Begin(ref sender, target, to,
                                input.HaloGuard ? HaloDeploymentMode.AllyGuard : HaloDeploymentMode.Transfer);
                        }
                        if (valid)
                        {
                            sender.HaloTransferPoint = to;
                            sender.HaloDeploymentMode = input.HaloGuard ? HaloDeploymentMode.AllyGuard : HaloDeploymentMode.Transfer;
                            if (input.HaloGuard) HaloSupportRules.AdvanceDeployment(ref sender, dt);
                            else
                            {
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

        private bool TryReadTransferInput(Entity actor, uint tick, out PlayerInput input)
        {
            input = default;
            if (!IsHuman(actor) || !EntityManager.HasComponent<PlayerClientCommandInputLookup>(actor)) return false;
            var source = EntityManager.GetComponentData<PlayerClientCommandInputLookup>(actor).ClientCommandInputEntity;
            if (!EntityManager.HasBuffer<ClientCommandInput>(source)) return false;
            var now = new NetworkTick(tick);
            var commands = EntityManager.GetBuffer<ClientCommandInput>(source, true);
            if (!commands.GetDataAtTick(now, out var command) || !command.Tick.IsValid ||
                command.Tick.IsNewerThan(now) || now.TicksSince(command.Tick) > 12) return false;
            input = RaidHealth.RestrictInput(EntityManager.GetComponentData<PredictedPlayerGhost>(actor), command.PlayerInput).Sanitized();
            return true;
        }

        private bool IsHuman(Entity actor) => EntityManager.HasComponent<GhostOwner>(actor) &&
            EntityManager.GetComponentData<GhostOwner>(actor).NetworkId > 0;

        // Called by the authoritative damage boundary. Re-read each donor: several impacts
        // in one tick must spend its current pool, including when the donor is the victim.
        public float AbsorbHaloSupport(Entity victim, ref PredictedPlayerGhost health, float incoming,
            uint tick, RaidDamageKind kind, float3 point)
        {
            if (!EnergyShieldRules.Blocks(kind) || !HaloSupportRules.CanProtect(health) || incoming <= 0) return incoming;
            foreach (var actor in m_TransferActors)
            {
                if (!EntityManager.HasComponent<PredictedPlayerGhost>(actor)) continue;
                var donor = actor == victim ? health : EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
                bool guard = donor.HaloDeploymentMode == HaloDeploymentMode.AllyGuard && donor.HaloTransferTarget == victim;
                bool area = donor.HaloDeploymentMode == HaloDeploymentMode.Shield &&
                    HaloSupportRules.Within(donor.HaloTransferPoint - new float3(0, .6f, 0), HaloTransferRules.Anchor(health), HaloSupportRules.ShieldRadius);
                if (donor.HaloTransferPhase != HaloTransferPhase.Sending || (!guard && !area) || !HaloTransferRules.CanSend(donor) ||
                    donor.HaloTransferWeapon != donor.EquippedWeaponID || !ActiveTransferActor(actor, false) ||
                    !ActiveTransferActor(victim, true) || !HaloTransferRules.Teammates(donor, health, IsHuman(actor), IsHuman(victim)) ||
                    (guard ? !HaloTransferRules.InRange(HaloTransferRules.Anchor(donor), HaloTransferRules.Anchor(health)) :
                        !HaloSupportRules.Within(HaloTransferRules.Anchor(donor), donor.HaloTransferPoint, HaloSupportRules.LeashRange)) ||
                    !TransferVisible(actor, guard ? HaloTransferRules.Anchor(donor) : donor.HaloTransferPoint, HaloTransferRules.Anchor(health))) continue;
                float absorbed = HaloSupportRules.Absorb(ref donor, incoming, tick, kind, point);
                if (absorbed <= 0) continue;
                if (actor == victim) health = donor;
                else
                {
                    EntityManager.SetComponentData(actor, donor);
                    HaloSupportRules.RecordHit(ref health, absorbed, incoming, tick, point);
                }
                return incoming - absorbed;
            }
            return incoming;
        }

        private bool ActiveTransferActor(Entity actor, bool allowBot)
        {
            if (!IsHuman(actor)) return allowBot && EntityManager.HasComponent<DollSingerEnemyBrain>(actor);
            if (!SystemAPI.TryGetSingletonBuffer<ClientsMap>(out var clients)) return false;
            int owner = EntityManager.GetComponentData<GhostOwner>(actor).NetworkId;
            if (owner >= clients.Length) return false;
            Entity connection = clients[owner].ConnectionEntity;
            if (!EntityManager.Exists(connection) || !EntityManager.HasComponent<JoinedClient>(connection) ||
                !EntityManager.HasComponent<RaidSession>(connection) || !EntityManager.HasComponent<NetworkStreamInGame>(connection) ||
                EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection)) return false;
            return EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity == actor &&
                EntityManager.GetComponentData<RaidSession>(connection).Phase == RaidPhase.Active;
        }

        private bool TransferVisible(Entity actor, float3 from, float3 to)
        {
            if (!EntityManager.HasComponent<GhostGameObjectLink>(actor)) return false;
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(actor);
            if (!link.LinkedInstance) return false;
            var scene = link.LinkedInstance.gameObject.scene.GetPhysicsScene();
            float3 delta = to - from;
            float distance = math.length(delta);
            return scene.IsValid() && (distance < .01f || !scene.Raycast(from, delta / distance, out _, distance,
                s_ShootableLayerMask, QueryTriggerInteraction.Ignore));
        }
    }
}
