using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Shared planning/rendering two-link solution. A reachable toe alone is insufficient:
    /// both fixed-length segments and their contact collar must fit the environment.</summary>
    public static class SpiderLegKinematics
    {
        public static bool Solve(SurfaceMotor.Pose pose, int index, Vector3 point, Vector3 normal,
            int mask, out Vector3 knee, out Vector3 tip)
        {
            Vector3 hip = PredictiveFeet.Hip(pose, index), delta = point - hip;
            float distance = Mathf.Clamp(delta.magnitude,
                PredictiveFeet.LowerLength - PredictiveFeet.UpperLength + .01f, PredictiveFeet.MaxReach);
            Vector3 direction = delta.sqrMagnitude > .00001f ? delta.normalized : pose.Rotation * PredictiveFeet.Radial(index);
            Vector3 hint = pose.Up * .65f + normal * .75f + pose.Rotation * PredictiveFeet.Radial(index) * .35f;
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction).normalized;
            if (bend.sqrMagnitude < .01f) bend = Vector3.Cross(direction, pose.Right).normalized;
            float l1 = PredictiveFeet.UpperLength, l2 = PredictiveFeet.LowerLength;
            float along = (l1 * l1 - l2 * l2 + distance * distance) / (2 * distance);
            float lift = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - along * along));
            tip = hip + direction * distance;
            knee = hip + direction * along + bend * lift;
            for (int candidate = 0; candidate <= 16; candidate++)
            {
                float angle = candidate == 0 ? 0 : ((candidate + 1) / 2) * 22.5f * (candidate % 2 == 0 ? -1 : 1);
                Vector3 proposed = hip + direction * along + Quaternion.AngleAxis(angle, direction) * bend * lift;
                Vector3 collar = tip + (proposed - tip).normalized * .10f;
                if (Physics.CheckSphere(proposed, .058f, mask, QueryTriggerInteraction.Ignore) ||
                    Blocked(hip, proposed, .05f, mask) || Blocked(proposed, collar, .024f, mask)) continue;
                knee = proposed; return true;
            }
            return false;
        }

        private static bool Blocked(Vector3 from, Vector3 to, float radius, int mask)
        {
            var delta = to - from;
            return delta.sqrMagnitude > .001f && Physics.SphereCast(from, radius, delta.normalized,
                out _, delta.magnitude, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
