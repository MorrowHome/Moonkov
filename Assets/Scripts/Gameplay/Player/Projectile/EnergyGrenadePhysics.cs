// 计算战术手雷的飞行、碰撞、反弹和停住。每次用短步长扫过实际路径，减少高速穿墙。
// ClearSight 也给范围伤害检查爆炸点与目标之间有没有遮挡。
using UnityEngine;

namespace Unity.MP_FPS {
    public static class EnergyGrenadePhysics {
        public static int HitMask => LayerMask.GetMask("Default", "Ground", "ServerPlayer");

        public static void Step(
            PhysicsScene scene,
            ref Vector3 position,
            ref Vector3 velocity,
            ref bool resting,
            float duration,
            Transform ignoredRoot,
            ref RaycastHit[] hits) {
            // 手雷已经停止滚动就保留位置。尚在运动时，把本次时间切成多个小段检查碰撞。
            if (resting) return;
            // Small swept steps prevent fast throws and stalled frames crossing thin walls.
            while (duration > .00001f) {
                float dt = Mathf.Min(duration, 1f / 120f);
                duration -= dt;
                var next = Ballistics.Position(position, velocity, EnergyGrenadeRules.Gravity, dt);
                velocity += Vector3.down * (EnergyGrenadeRules.Gravity * dt);
                if (!Ballistics.Sweep(scene, position, next, EnergyGrenadeRules.BodyRadius, HitMask, ignoredRoot,
                        ref hits, out var hit)) {
                    position = next;
                    continue;
                }
                // 命中后沿表面法线稍微移开手雷半径，然后丢掉一部分速度反弹，避免下一步还卡在表面里。
                position = hit.point + hit.normal * (EnergyGrenadeRules.BodyRadius + .006f);
                velocity = Vector3.Reflect(velocity, hit.normal) * .48f;
                // 速度很低并且碰到可落地的表面才停止。碰到墙的那种慢反弹随后仍受重力。
                if (hit.normal.y > .55f && velocity.sqrMagnitude < 1.2f) {
                    velocity = Vector3.zero;
                    resting = true;
                    return;
                }
            }
        }

        // 发射点到目标之间若先撞到目标自身，判为可见。提前撞到别的东西就当成遮挡。
        public static bool ClearSight(PhysicsScene scene, Vector3 origin, Vector3 point, Transform target) {
            var travel = point - origin;
            return travel.sqrMagnitude < .001f ||
                   !scene.Raycast(origin, travel.normalized, out var hit, travel.magnitude, HitMask,
                       QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(target);
        }
    }
}