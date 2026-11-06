using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public partial class ServerPlayerMovementSystem
    {
        private readonly Collider[] m_AnchorOverlap = new Collider[1];
        private bool UpdateHaloAnchor(Entity actor, ref PredictedPlayerGhost state, in PlayerInput input, bool fresh, float dt)
        {
            bool place = fresh && input.HaloAnchor && !state.HaloAnchorLatched;
            bool cycle = fresh && input.HaloMode && !state.HaloModeLatched;
            if (fresh) { state.HaloAnchorLatched = input.HaloAnchor; state.HaloModeLatched = input.HaloMode; }
            if (!HaloSupportRules.Anchored(state))
            {
                if (!place) return false;
                if (HaloTransferRules.Occupied(state)) HaloTransferRules.Recall(ref state);
                else if (HaloTransferRules.CanSend(state) && ActiveTransferActor(actor, false) &&
                         TryAnchorPoint(actor, state, input, out var point))
                    HaloTransferRules.Begin(ref state, Entity.Null, point, HaloDeploymentMode.Light);
                return true;
            }
            if (!EnergyShieldRules.Alive(state) || !state.HaloEquipped) HaloTransferRules.Clear(ref state);
            else if (state.HaloTransferPhase == HaloTransferPhase.Returning) HaloTransferRules.TickReturn(ref state, dt);
            else if (place || !HaloTransferRules.CanSend(state) || !ActiveTransferActor(actor, false) ||
                     state.EquippedWeaponID != state.HaloTransferWeapon ||
                     !HaloSupportRules.Within(HaloTransferRules.Anchor(state), state.HaloTransferPoint, HaloSupportRules.LeashRange))
                HaloTransferRules.Recall(ref state);
            else
            {
                if (cycle)
                {
                    state.HaloDeploymentMode = HaloSupportRules.NextMode(state.HaloDeploymentMode);
                    state.HaloSentryTarget = Entity.Null;
                    state.HaloSentrySearchRemaining = 0;
                    HaloModuleFire.Reset(ref state);
                    state.WeaponCooldown = 0;
                }
                HaloSupportRules.AdvanceDeployment(ref state, dt);
                if (state.HaloDeploymentMode == HaloDeploymentMode.Sentry && state.HaloTransferPhase == HaloTransferPhase.Sending)
                {
                    state.HaloSentrySearchRemaining = Mathf.Max(0, state.HaloSentrySearchRemaining - dt);
                    if (state.HaloSentrySearchRemaining <= 0)
                    {
                        state.HaloSentrySearchRemaining = .25f;
                        state.HaloSentryTarget = FindSentryTarget(actor, state);
                    }
                }
            }
            return true;
        }

        private bool TryAnchorPoint(Entity actor, in PredictedPlayerGhost state, in PlayerInput input, out float3 point)
        {
            point = default;
            if (!EntityManager.HasComponent<GhostGameObjectLink>(actor)) return false;
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(actor);
            if (!link.LinkedInstance) return false;
            var physics = link.LinkedInstance.gameObject.scene.GetPhysicsScene();
            Vector3 from = HaloTransferRules.Anchor(state), delta = (Vector3)input.AimPoint - from;
            if (!physics.IsValid() || delta.sqrMagnitude < .01f) return false;
            var forward = Quaternion.Euler(input.LookYawPitchDegrees.y, input.LookYawPitchDegrees.x, 0) * Vector3.forward;
            if (Vector3.Dot(forward, delta.normalized) < .5f) return false;
            if (!physics.Raycast(from, delta.normalized, out var hit, HaloSupportRules.PlacementRange,
                    s_ShootableLayerMask, QueryTriggerInteraction.Ignore) || hit.normal.y < .35f) return false;
            point = hit.point + Vector3.up * 1.6f;
            return TransferVisible(actor, from, point) &&
                physics.OverlapSphere(point, .25f, m_AnchorOverlap, s_ShootableLayerMask, QueryTriggerInteraction.Ignore) == 0;
        }

        private Entity FindSentryTarget(Entity actor, in PredictedPlayerGhost state)
        {
            Entity best = Entity.Null;
            float nearest = HaloSupportRules.SentryRange * HaloSupportRules.SentryRange;
            foreach (var (other, pose, target) in SystemAPI.Query<RefRO<PredictedPlayerGhost>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                if (!HostileHaloTarget(actor, state, target, other.ValueRO)) continue;
                float3 point = pose.ValueRO.Position + new float3(0, .9f, 0);
                float distance = math.distancesq(point, state.HaloTransferPoint);
                if (distance >= nearest || !TransferVisible(actor, state.HaloTransferPoint, point)) continue;
                best = target; nearest = distance;
            }
            return best;
        }

        private bool HostileHaloTarget(Entity actor, in PredictedPlayerGhost state, Entity target, in PredictedPlayerGhost other) =>
            target != actor && HaloSupportRules.CanProtect(other) &&
            !HaloTransferRules.Teammates(state, other, IsHuman(actor), IsHuman(target));

        private float3 SentryLookPoint(in PredictedPlayerGhost state) => EntityManager.HasComponent<LocalTransform>(state.HaloSentryTarget)
            ? EntityManager.GetComponentData<LocalTransform>(state.HaloSentryTarget).Position + new float3(0, .9f, 0)
            : state.HaloTransferPoint;

        private bool TrySentryShot(Entity actor, ref PredictedPlayerGhost state, WeaponData weapon, uint tick, out Ray ray)
        {
            ray = default;
            Entity target = state.HaloSentryTarget;
            if (!HaloSupportRules.ReadySentry(ref state, weapon) || !ActiveTransferActor(actor, false) ||
                !HaloSupportRules.Within(HaloTransferRules.Anchor(state), state.HaloTransferPoint, HaloSupportRules.LeashRange) ||
                !EntityManager.HasComponent<PredictedPlayerGhost>(target) || !EntityManager.HasComponent<LocalTransform>(target) ||
                !HostileHaloTarget(actor, state, target, EntityManager.GetComponentData<PredictedPlayerGhost>(target))) return false;
            float3 to = EntityManager.GetComponentData<LocalTransform>(target).Position + new float3(0, .9f, 0);
            if (!HaloSupportRules.Within(state.HaloTransferPoint, to, HaloSupportRules.SentryRange) ||
                !TransferVisible(actor, state.HaloTransferPoint, to)) return false;
            ray = new Ray(state.HaloTransferPoint, HaloSupportRules.SentryDirection(math.normalizesafe(to - state.HaloTransferPoint), tick));
            return true;
        }
    }
}
