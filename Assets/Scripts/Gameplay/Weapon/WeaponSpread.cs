using UnityEngine;

namespace Unity.MP_FPS
{
    public static class WeaponSpread
    {
        // Deterministic, evenly filled cone shared by prediction and server simulation.
        public static Vector3 Direction(Vector3 forward, WeaponData weapon, int pellet, uint tick)
        {
            int count = Mathf.Clamp(weapon.PelletCount, 1, 32);
            if (count == 1 || weapon.SpreadDegrees <= 0f) return forward.normalized;
            float radius = Mathf.Sqrt((pellet + .5f) / count) * Mathf.Tan(weapon.SpreadDegrees * Mathf.Deg2Rad);
            float angle = pellet * 2.39996323f + (tick % 360) * Mathf.Deg2Rad;
            return Quaternion.LookRotation(forward) * new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 1f).normalized;
        }
    }
}
