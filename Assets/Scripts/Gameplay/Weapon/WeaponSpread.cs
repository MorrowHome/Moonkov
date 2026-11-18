// 计算武器每颗弹丸的方向。散布角受瞄准状态影响，多弹丸在锥形范围内分布，网络 tick 让同一次射击能复算出同样的方向。
using UnityEngine;

namespace Unity.MP_FPS {
    public static class WeaponSpread {
        // 预测和服务器使用同一个可复算且均匀分布的散布锥。
        // 瞄准读取基础散布。腰射额外扩大，且有下限和上限。
        public static float ConeDegrees(WeaponData weapon, bool aiming) {
            return aiming ? weapon.SpreadDegrees : Mathf.Min(30f, Mathf.Max(6f, weapon.SpreadDegrees * 2.5f));
        }

        public static Vector3 Direction(Vector3 forward, WeaponData weapon, int pellet, uint tick, bool aiming = true) {
            int count = Mathf.Clamp(weapon.PelletCount, 1, 32);
            float spread = ConeDegrees(weapon, aiming);
            if (spread <= 0f) return forward.normalized;
            if (count == 1) {
                // 从 tick 计算散布方向，使预测与服务器结果一致。
                // 这一计算不改变 Unity 的全局随机状态。
                // 预测与服务器从相同 tick 得到同样的单发偏移。无需同步随机数，也不影响别的系统随机结果。
                uint seed = unchecked(tick * 747796405u + 2891336453u);
                seed = unchecked(((seed >> ((int)(seed >> 28) + 4)) ^ seed) * 277803737u);
                seed = (seed >> 22) ^ seed;
                float r = Mathf.Sqrt((seed & 65535u) / 65536f) * Mathf.Tan(spread * Mathf.Deg2Rad);
                float theta = (seed >> 16) / 65536f * Mathf.PI * 2;
                return Quaternion.LookRotation(forward) *
                       new Vector3(Mathf.Cos(theta) * r, Mathf.Sin(theta) * r, 1).normalized;
            }
            // 多弹丸用按序号变化的半径和角度铺满锥截面。每发再随 tick 整体转一点。
            float radius = Mathf.Sqrt((pellet + .5f) / count) * Mathf.Tan(spread * Mathf.Deg2Rad);
            float angle = pellet * 2.39996323f + tick % 360 * Mathf.Deg2Rad;
            return Quaternion.LookRotation(forward) *
                   new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 1f).normalized;
        }
    }
}