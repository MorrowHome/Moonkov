using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    // Presentation geometry only. Never use these feet as authoritative hitboxes.
    public static class AlienLegMath
    {
        public static float Damping(float sharpness, float dt) => 1f - Mathf.Exp(-Mathf.Max(0f, sharpness) * Mathf.Max(0f, dt));

        public static Vector3 Swing(Vector3 from, Vector3 to, Vector3 up, float progress, float lift)
        {
            float t = Mathf.Clamp01(progress);
            float smooth = t * t * (3f - 2f * t);
            // Zero vertical and horizontal endpoint velocity; a fixed target prevents skating.
            float arc = 16f * t * t * (1f - t) * (1f - t);
            return Vector3.LerpUnclamped(from, to, smooth) + up.normalized * (arc * Mathf.Max(0f, lift));
        }

        public static void Solve(Vector3 hip, Vector3 requestedFoot, Vector3 bendHint,
            float upperLength, float lowerLength, out Vector3 knee, out Vector3 foot)
        {
            float upper = Mathf.Max(.001f, upperLength);
            float lower = Mathf.Max(.001f, lowerLength);
            Vector3 delta = requestedFoot - hip;
            Vector3 direction = delta.sqrMagnitude > .0000001f ? delta.normalized : Vector3.down;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(upper - lower) + .0001f, upper + lower - .0001f);
            foot = hip + direction * distance;
            Vector3 bend = Vector3.ProjectOnPlane(bendHint, direction);
            if (bend.sqrMagnitude < .00001f) bend = Vector3.Cross(direction, Vector3.forward);
            if (bend.sqrMagnitude < .00001f) bend = Vector3.Cross(direction, Vector3.right);
            float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            knee = hip + direction * along + bend.normalized * height;
        }

        public static Vector3 SafeUp(Vector3 normal, float maxSlopeDegrees)
        {
            if (normal.sqrMagnitude < .00001f) return Vector3.up;
            return Vector3.RotateTowards(Vector3.up, normal.normalized,
                Mathf.Clamp(maxSlopeDegrees, 0f, 80f) * Mathf.Deg2Rad, 0f).normalized;
        }
    }
}
