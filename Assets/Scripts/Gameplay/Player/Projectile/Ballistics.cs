// 枪弹共用的弹道计算。按初速度、重力和时间找位置，并沿经过的路径做碰撞检查。
// 直接命中按距离衰减伤害，范围爆炸的衰减交给 ProjectileBlast。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    public static class Ballistics {
        // 玩家和 AI 共用发射起点规则。距离变远不会让伤害升高。
        // 旧武器若没配有效的衰减区间，仍保留原有行为。
        public static float DamageAtDistance(WeaponData weapon, float distance) {
            if (!weapon) return 0;
            // 范围伤害由爆点到目标的距离计算。直击武器没配置有效衰减区间时则用固定伤害。
            if (weapon.Behavior == ProjectileBehavior.AreaOfEffect ||
                weapon.DamageFalloffEnd <= weapon.DamageFalloffStart)
                return weapon.Damage;
            float fraction = Mathf.InverseLerp(weapon.DamageFalloffStart, weapon.DamageFalloffEnd,
                Mathf.Max(0, distance));
            return weapon.Damage * Mathf.Lerp(1, Mathf.Clamp01(weapon.MinimumDamageFraction), fraction);
        }

        // 弹体位置由初始位置、匀速项和重力项直接算。隔了几帧也能在指定时间重现同一条曲线。
        public static Vector3 Position(Vector3 origin, Vector3 velocity, float gravity, float age) {
            return origin + velocity * age + Vector3.down * (0.5f * gravity * age * age);
        }

        public static Vector3 Velocity(Vector3 initialVelocity, float gravity, float age) {
            return initialVelocity + Vector3.down * (gravity * age);
        }

        // 扫描这一拍已经飞过的路径，下一拍的路径留到下一次更新。
        // 检查所有命中候选，避免发射者自己的碰撞体遮掉实际的墙或其他玩家。
        public static bool Sweep(
            PhysicsScene scene,
            Vector3 from,
            Vector3 to,
            float radius,
            int mask,
            Transform ignoredRoot,
            ref RaycastHit[] results,
            out RaycastHit closest) {
            closest = default;
            var travel = to - from;
            float distance = travel.magnitude;
            if (distance < 0.00001f) return false;
            int count;
            while (true) {
                // 从这一段起点到终点扫出有半径的碰撞体。结果缓冲装满就加大再查，避免被垫在最前面的无效碰撞漏掉真正目标。
                count = scene.SphereCast(from, radius, travel / distance, results, distance, mask,
                    QueryTriggerInteraction.Ignore);
                if (count < results.Length) break;
                Array.Resize(ref results, results.Length * 2);
            }
            float nearest = float.MaxValue;
            for (int i = 0; i < count; ++i) {
                var candidate = results[i];
                // 跳过发射者自己的模型，剩下的候选按实际距离取第一个。
                if (ignoredRoot != null && candidate.collider.transform.IsChildOf(ignoredRoot)) continue;
                if (candidate.distance >= nearest) continue;
                nearest = candidate.distance;
                closest = candidate;
            }
            return nearest < float.MaxValue;
        }
    }
}