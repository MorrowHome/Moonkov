using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static class Ballistics
    {
        public static Vector3 Position(Vector3 origin, Vector3 velocity, float gravity, float age) =>
            origin + velocity * age + Vector3.down * (0.5f * gravity * age * age);

        public static Vector3 Velocity(Vector3 initialVelocity, float gravity, float age) =>
            initialVelocity + Vector3.down * (gravity * age);

        // Sweep the path we actually travelled, never the next frame's path.
        // Query all candidates so the shooter's collider cannot hide a wall or another player.
        public static bool Sweep(UnityEngine.PhysicsScene scene, Vector3 from, Vector3 to,
            float radius, int mask, Transform ignoredRoot, ref RaycastHit[] results, out RaycastHit closest)
        {
            closest = default;
            Vector3 travel = to - from;
            float distance = travel.magnitude;
            if (distance < 0.00001f) return false;
            int count;
            while (true)
            {
                count = scene.SphereCast(from, radius, travel / distance, results, distance, mask,
                    QueryTriggerInteraction.Ignore);
                if (count < results.Length) break;
                Array.Resize(ref results, results.Length * 2);
            }
            float nearest = float.MaxValue;
            for (int i = 0; i < count; ++i)
            {
                var candidate = results[i];
                if (ignoredRoot != null && candidate.collider.transform.IsChildOf(ignoredRoot)) continue;
                if (candidate.distance >= nearest) continue;
                nearest = candidate.distance;
                closest = candidate;
            }
            return nearest < float.MaxValue;
        }
    }
}
