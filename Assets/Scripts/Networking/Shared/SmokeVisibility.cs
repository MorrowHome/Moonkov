using UnityEngine;

namespace Unity.MP_FPS
{
    // The same growing ellipsoid and fade drive client rendering and server perception.
    // Smoke never participates in collision, damage, hearing or navigation queries.
    public static class SmokeVisibility
    {
        public const float Duration = 20, BloomSeconds = 1.2f, FadeSeconds = 3;
        public static float Growth(float remaining) => Mathf.Lerp(.12f, 1, Mathf.SmoothStep(0, 1, (Duration - remaining) / BloomSeconds));
        public static float Density(float remaining) => Mathf.Clamp01(remaining / FadeSeconds);
        public static Vector3 Radii(float radius, float remaining) => new Vector3(radius, radius * .6f, radius) * Growth(remaining);
        public static Vector3 Center(Vector3 origin) => origin + Vector3.up * 1.4f;

        public static bool Blocked(TacticalServerWorld world, PhysicsScene scene, Vector3 from, Vector3 to)
        {
            if (world == null) return false;
            float depth = 0;
            foreach (var cloud in world.Objects)
            {
                if (cloud.Phase != TacticalWorldPhase.Smoke || cloud.Scene != scene || cloud.Until <= world.Now) continue;
                float remaining = (float)(cloud.Until - world.Now);
                depth += OpticalDepth(from, to, Center(cloud.Position), Radii(cloud.Definition.Radius, remaining), Density(remaining));
                if (depth >= 1.6f) return true;
            }
            return false;
        }

        public static float OpticalDepth(Vector3 from, Vector3 to, Vector3 center, Vector3 radii, float density)
        {
            if (density <= 0 || radii.x <= 0 || radii.y <= 0 || radii.z <= 0) return 0;
            Vector3 segment = to - from;
            float length = segment.magnitude;
            if (length < .0001f) return 0;
            Vector3 origin = Divide(from - center, radii), direction = Divide(segment / length, radii);
            float a = Vector3.Dot(direction, direction), b = Vector3.Dot(origin, direction);
            float discriminant = b * b - a * (Vector3.Dot(origin, origin) - 1);
            if (discriminant <= 0) return 0;
            float root = Mathf.Sqrt(discriminant);
            float enter = Mathf.Max(0, (-b - root) / a), exit = Mathf.Min(length, (-b + root) / a);
            if (exit <= enter) return 0;
            float step = (exit - enter) / 12, depth = 0;
            for (int i = 0; i < 12; i++)
            {
                float radius = (origin + direction * (enter + (i + .5f) * step)).magnitude;
                depth += Mathf.Clamp01((1 - radius) * 5) * step * density * 2;
            }
            return depth;
        }
        private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
    }
}
