using UnityEngine;

namespace Unity.MP_FPS
{
    public static class WeaponSpread
    {
        // Deterministic, evenly filled cone shared by prediction and server simulation.
        public static Vector3 Direction(Vector3 forward, WeaponData weapon, int pellet, uint tick)
        {
            int count = Mathf.Clamp(weapon.PelletCount, 1, 32);
            if (weapon.SpreadDegrees <= 0f) return forward.normalized;
            if (count == 1)
            {
                // A tick hash gives prediction and authority the same shot cone,
                // without consuming Unity's global random state.
                uint seed = unchecked(tick * 747796405u + 2891336453u);
                seed = unchecked(((seed >> ((int)(seed >> 28) + 4)) ^ seed) * 277803737u);
                seed = (seed >> 22) ^ seed;
                float r = Mathf.Sqrt((seed & 65535u) / 65536f) * Mathf.Tan(weapon.SpreadDegrees * Mathf.Deg2Rad);
                float theta = (seed >> 16) / 65536f * Mathf.PI * 2;
                return Quaternion.LookRotation(forward) * new Vector3(Mathf.Cos(theta) * r, Mathf.Sin(theta) * r, 1).normalized;
            }
            float radius = Mathf.Sqrt((pellet + .5f) / count) * Mathf.Tan(weapon.SpreadDegrees * Mathf.Deg2Rad);
            float angle = pellet * 2.39996323f + (tick % 360) * Mathf.Deg2Rad;
            return Quaternion.LookRotation(forward) * new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 1f).normalized;
        }
    }
}
