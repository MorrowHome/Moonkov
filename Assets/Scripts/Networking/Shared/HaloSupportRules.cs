using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS
{
    public enum HaloDeploymentMode : byte { Transfer, AllyGuard, Light, Shield, Sentry }

    public static class HaloSupportRules
    {
        public const float PlacementRange = 8, LeashRange = 18, ShieldRadius = 2.5f, SentryRange = 18;
        public const float SentryInterval = .65f;
        public static bool Anchored(in PredictedPlayerGhost state) => state.HaloDeploymentMode >= HaloDeploymentMode.Light;
        public static bool CanProtect(in PredictedPlayerGhost target) => EnergyShieldRules.Alive(target) &&
            (!target.ClashEnabled || target.ClashCanAct);

        public static bool Within(float3 a, float3 b, float range) => math.all(math.isfinite(a)) &&
            math.all(math.isfinite(b)) && math.distancesq(a, b) <= range * range;

        public static HaloDeploymentMode NextMode(HaloDeploymentMode mode) => mode == HaloDeploymentMode.Light
            ? HaloDeploymentMode.Shield : mode == HaloDeploymentMode.Shield ? HaloDeploymentMode.Sentry : HaloDeploymentMode.Light;

        public static bool ReadySentry(ref PredictedPlayerGhost state, WeaponData weapon)
        {
            HaloEnergyRules.RefreshAmmo(ref state, weapon);
            return state.HaloDeploymentMode == HaloDeploymentMode.Sentry && state.HaloTransferPhase == HaloTransferPhase.Sending &&
                HaloTransferRules.CanSend(state) && weapon != null && state.CurrentAmmo > 0 &&
                state.WeaponCooldown >= Mathf.Max(SentryInterval, weapon.BurstCount > 1 ? weapon.BurstCooldown : weapon.CooldownInMs);
        }

        // Deliberately broad aim error. Weapon pellet spread is applied afterwards as usual.
        public static Vector3 SentryDirection(Vector3 forward, uint tick)
        {
            uint hash = unchecked(tick * 747796405u + 2891336453u);
            hash = unchecked(((hash >> ((int)(hash >> 28) + 4)) ^ hash) * 277803737u);
            hash ^= hash >> 22;
            float radius = Mathf.Sqrt((hash & 65535u) / 65536f) * Mathf.Tan(9 * Mathf.Deg2Rad);
            float angle = (hash >> 16) / 65536f * Mathf.PI * 2;
            return Quaternion.LookRotation(forward) * new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 1).normalized;
        }

        public static void AdvanceDeployment(ref PredictedPlayerGhost state, float dt)
        {
            if (state.HaloTransferPhase != HaloTransferPhase.Extending || !float.IsFinite(dt) || dt <= 0) return;
            state.HaloTransferRemaining = Mathf.Max(0, state.HaloTransferRemaining - dt);
            if (state.HaloTransferRemaining <= 0) state.HaloTransferPhase = HaloTransferPhase.Sending;
        }

        public static float Absorb(ref PredictedPlayerGhost provider, float incoming, uint tick,
            RaidDamageKind kind, float3 point)
        {
            if (!EnergyShieldRules.Blocks(kind) || !float.IsFinite(incoming) || incoming <= 0) return 0;
            float absorbed = HaloEnergyRules.Spend(ref provider, incoming);
            if (absorbed > 0) RecordHit(ref provider, absorbed, incoming, tick, point);
            return absorbed;
        }

        public static void RecordHit(ref PredictedPlayerGhost state, float absorbed, float incoming, uint tick, float3 point)
        {
            state.ShieldHitSequence++;
            state.ShieldHitStrength = Mathf.Clamp01(absorbed / HaloEnergyRules.Maximum(state) * 3);
            state.ShieldHitPoint = math.all(math.isfinite(point)) ? point : HaloTransferRules.Anchor(state);
            state.LastHitTick = tick;
            state.ControllerState.IsHit = true;
            if (absorbed >= incoming) { state.LastDamageAmount = 0; state.LastHitPart = BodyPart.Auto; }
        }
    }
}
