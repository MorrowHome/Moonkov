using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static class HaloLightGlareRules
    {
        public const float SampleInterval = .1f;
        public static bool Emits(in PredictedPlayerGhost source) => source.CurrentHealth > 0 &&
            (!source.ClashEnabled || source.ClashCanAct) && source.HaloEquipped &&
            DollSingerWeapons.IsHalo(source.EquippedWeaponID) && source.ControllerState.Aiming &&
            source.ControllerState.HaloLightEnabled && HaloLighting.AimPercent(source.ControllerState.HaloLightingSettings) > 0 &&
            source.WeaponEquipRemaining <= 0 && !source.HaloDefenseHeld && !source.ControllerState.IsReloadingState && !source.ConsumingSupply &&
            source.HandAction == 0 && source.MeleePhase == 0 && !GrenadeHandling.Occupied(source) &&
            !HaloTransferRules.Occupied(source) && !source.ClashDarkSight;

        public static float Strength(in PredictedPlayerGhost source, Vector3 origin, Vector3 beamForward,
            Vector3 eye, Vector3 viewForward)
        {
            if (!Emits(source)) return 0;
            Vector3 offset = eye - origin;
            float distance = offset.magnitude;
            if (!float.IsFinite(distance) || !Finite(beamForward) || !Finite(viewForward) || distance < .05f ||
                beamForward.sqrMagnitude < .01f || viewForward.sqrMagnitude < .01f) return 0;
            float range = HaloLighting.AimRange(source.ControllerState.HaloLightingSettings);
            if (distance >= range) return 0;
            Vector3 direction = offset / distance;
            // Only the aimed beam dazzles, and only while the target faces its source.
            float cone = Mathf.InverseLerp(Mathf.Cos(24 * Mathf.Deg2Rad), Mathf.Cos(12 * Mathf.Deg2Rad), Vector3.Dot(beamForward.normalized, direction));
            float facing = Mathf.InverseLerp(.1f, .85f, Vector3.Dot(viewForward.normalized, -direction));
            float brightness = Mathf.Clamp(HaloLighting.AimPercent(source.ControllerState.HaloLightingSettings) / 100f, 0, 2);
            float raised = float.IsFinite(source.WeaponRaiseProgress) ? Mathf.Clamp01(source.WeaponRaiseProgress) : 0;
            return Mathf.Clamp01(cone * facing * brightness * raised * (1 - distance / range));
        }

        public static float SightScale(float strength) => Mathf.Lerp(1, .25f, Mathf.Clamp01(strength));
        public static float AimErrorDegrees(float strength) => 12 * Mathf.Clamp01(strength);

        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        public static bool ClearPath(PhysicsScene scene, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            return scene.IsValid() && Finite(delta) && delta.sqrMagnitude > .0025f && !scene.Raycast(from, delta.normalized, out _,
                Mathf.Max(0, delta.magnitude - .05f), s_WorldMask, QueryTriggerInteraction.Ignore);
        }
    }
}
