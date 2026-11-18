// 光环灯是否造成眩光以及眩光多强的计算。检查瞄准、灯是否开启和动作状态，再按光束方向、对手朝向及距离衰减。
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS {
    public static class HaloLightGlareRules {
        public const float SampleInterval = .1f;

        // 近战、举盾、换武器和灯没打开时不发定向眩光。视觉亮点不必都具备影响 AI 的有效光束。
        public static bool Emits(in PredictedPlayerGhost source) {
            return source.CurrentHealth > 0 && (!source.ClashEnabled || source.ClashCanAct) && source.HaloEquipped &&
                   DollSingerWeapons.IsHalo(source.EquippedWeaponID) && source.ControllerState.Aiming &&
                   source.ControllerState.HaloLightEnabled &&
                   HaloLighting.AimPercent(source.ControllerState.HaloLightingSettings) > 0 &&
                   source.WeaponEquipRemaining <= 0 && !source.HaloDefenseHeld &&
                   !source.ControllerState.IsReloadingState && !source.ConsumingSupply && source.HandAction == 0 &&
                   source.MeleePhase == 0 && !GrenadeHandling.Occupied(source) && !HaloTransferRules.Occupied(source) &&
                   !source.ClashDarkSight;
        }

        public static float Strength(
            in PredictedPlayerGhost source,
            Vector3 origin,
            Vector3 beamForward,
            Vector3 eye,
            Vector3 viewForward) {
            if (!Emits(source)) return 0;
            var offset = eye - origin;
            float distance = offset.magnitude;
            if (!float.IsFinite(distance) || !Finite(beamForward) || !Finite(viewForward) || distance < .05f ||
                beamForward.sqrMagnitude < .01f || viewForward.sqrMagnitude < .01f)
                return 0;
            float range = HaloLighting.AimRange(source.ControllerState.HaloLightingSettings);
            if (distance >= range) return 0;
            var direction = offset / distance;
            // Only the aimed beam dazzles, and only while the target faces its source.
            // 眩光叠乘光束内角度、目标是否正看着来光、距离和抬枪进度。没在有效锥体内时强度降零。
            float cone = Mathf.InverseLerp(Mathf.Cos(24 * Mathf.Deg2Rad), Mathf.Cos(12 * Mathf.Deg2Rad),
                Vector3.Dot(beamForward.normalized, direction));
            float facing = Mathf.InverseLerp(.1f, .85f, Vector3.Dot(viewForward.normalized, -direction));
            float brightness = Mathf.Clamp(HaloLighting.AimPercent(source.ControllerState.HaloLightingSettings) / 100f,
                0, 2);
            float raised = float.IsFinite(source.WeaponRaiseProgress) ? Mathf.Clamp01(source.WeaponRaiseProgress) : 0;
            return Mathf.Clamp01(cone * facing * brightness * raised * (1 - distance / range));
        }

        // 同一个强度供 AI 缩短有效视距、增加瞄准误差。服务器采样系统决定实际受光人物。
        public static float SightScale(float strength) { return Mathf.Lerp(1, .25f, Mathf.Clamp01(strength)); }
        public static float AimErrorDegrees(float strength) { return 12 * Mathf.Clamp01(strength); }
        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");

        private static bool Finite(Vector3 value) {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        // 用物理场景查灯和目标之间的静态遮挡。略缩终点距离，避免把目标贴着的表面误当作中间障碍。
        public static bool ClearPath(PhysicsScene scene, Vector3 from, Vector3 to) {
            var delta = to - from;
            return scene.IsValid() && Finite(delta) && delta.sqrMagnitude > .0025f && !scene.Raycast(from,
                delta.normalized, out _, Mathf.Max(0, delta.magnitude - .05f), s_WorldMask,
                QueryTriggerInteraction.Ignore);
        }
    }
}