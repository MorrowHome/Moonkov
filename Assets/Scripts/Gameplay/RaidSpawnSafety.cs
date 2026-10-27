using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    public struct RaidSpawnObserver
    {
        public Vector3 Position;
        public Vector3 Forward;
        public bool CheckVisibility;
        public bool Aiming;
    }

    /// <summary>Bounded physical checks shared by server encounter placement and authoring.</summary>
    public static class RaidSpawnSafety
    {
        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");

        public static bool TryGroundPosition(Vector3 candidate, float radius, float height, float maxSlope,
            out Vector3 groundPoint, out Vector3 groundNormal)
        {
            groundPoint = default;
            groundNormal = Vector3.up;
            radius = Mathf.Max(.05f, radius);
            height = Mathf.Max(radius * 2, height);
            if (!UnityEngine.Physics.Raycast(candidate + Vector3.up * 2, Vector3.down, out var hit, 6,
                    s_WorldMask, QueryTriggerInteraction.Ignore) || Vector3.Angle(hit.normal, Vector3.up) > maxSlope)
                return false;
            // Keep the lower capsule clear of sloping ground; do not sample a different floor.
            float clearance = .06f + radius * Mathf.Tan(Vector3.Angle(hit.normal, Vector3.up) * Mathf.Deg2Rad);
            Vector3 bottom = hit.point + Vector3.up * (radius + clearance);
            Vector3 top = hit.point + Vector3.up * (height - radius + clearance);
            if (UnityEngine.Physics.CheckCapsule(bottom, top, radius, s_WorldMask, QueryTriggerInteraction.Ignore))
                return false;
            groundPoint = hit.point;
            groundNormal = hit.normal;
            return true;
        }

        public static bool IsSafeFromObservers(Vector3 position, IReadOnlyList<RaidSpawnObserver> observers,
            float minimumDistance, float visibilityDistance = 65, float minimumSpacing = 3)
        {
            foreach (var observer in observers)
            {
                float distance = Vector3.Distance(position, observer.Position);
                if (distance < (observer.CheckVisibility ? minimumDistance : minimumSpacing)) return false;
                if (!observer.CheckVisibility || distance > visibilityDistance) continue;
                Vector3 eye = observer.Position + Vector3.up * 1.6f;
                Vector3 direction = position + Vector3.up * .7f - eye;
                // Conservatively cover a 120-degree cone. An unknown direction checks all sides.
                if (observer.Forward.sqrMagnitude > .01f &&
                    Vector3.Dot(observer.Forward.normalized, direction.normalized) < .5f) continue;
                if (!UnityEngine.Physics.Linecast(eye, position + Vector3.up * .7f, s_WorldMask,
                        QueryTriggerInteraction.Ignore) ||
                    !UnityEngine.Physics.Linecast(eye, position + Vector3.up * 1.5f, s_WorldMask,
                        QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        public static bool TryPlaceCache(Vector3 candidate, float yaw, out Vector3 position)
            => TryPlaceCache(candidate, yaw, out position, out _);

        public static bool TryPlaceCache(Vector3 candidate, float yaw, out Vector3 position, out Vector3 normal)
        {
            position = default;
            normal = Vector3.up;
            if (!UnityEngine.Physics.Raycast(candidate + Vector3.up * 3, Vector3.down, out var floor, 7,
                    s_WorldMask, QueryTriggerInteraction.Ignore) || Vector3.Angle(floor.normal, Vector3.up) > 18)
                return false;
            normal = floor.normal;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, yaw, 0);
            // All four skid corners must have support on the same floor, away from ledges.
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = floor.point + rotation * new Vector3(x * .51f, 0, z * .37f);
                    if (!UnityEngine.Physics.Raycast(corner + Vector3.up, Vector3.down, out var support, 2,
                            s_WorldMask, QueryTriggerInteraction.Ignore)) return false;
                    if (Mathf.Abs(Vector3.Dot(support.point - corner, normal)) > .12f) return false;
                }
            position = floor.point + normal * .35f;
            if (UnityEngine.Physics.CheckBox(position + normal * .035f, new Vector3(.58f, .31f, .43f),
                    rotation, s_WorldMask, QueryTriggerInteraction.Ignore)) return false;
            // At least one long side must offer a standing interaction spot.
            for (int side = -1; side <= 1; side += 2)
                if (TryGroundPosition(position + rotation * new Vector3(0, 0, side * 1.25f), .35f, 1.8f, 40,
                        out var access, out _) && Mathf.Abs(access.y - floor.point.y) < .6f) return true;
            return false;
        }
    }
}
