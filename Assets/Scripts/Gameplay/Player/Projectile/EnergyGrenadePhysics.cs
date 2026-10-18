using UnityEngine;

namespace Unity.MP_FPS
{
    public static class EnergyGrenadePhysics
    {
        public static int HitMask => LayerMask.GetMask("Default", "Ground", "ServerPlayer");

        public static void Step(PhysicsScene scene, ref Vector3 position, ref Vector3 velocity,
            ref bool resting, float duration, Transform ignoredRoot, ref RaycastHit[] hits)
        {
            if (resting) return;
            // Small swept steps prevent fast throws and stalled frames crossing thin walls.
            while (duration > .00001f)
            {
                float dt = Mathf.Min(duration, 1f / 120f);
                duration -= dt;
                Vector3 next = Ballistics.Position(position, velocity, EnergyGrenadeRules.Gravity, dt);
                velocity += Vector3.down * (EnergyGrenadeRules.Gravity * dt);
                if (!Ballistics.Sweep(scene, position, next, EnergyGrenadeRules.BodyRadius,
                    HitMask, ignoredRoot, ref hits, out var hit))
                { position = next; continue; }
                position = hit.point + hit.normal * (EnergyGrenadeRules.BodyRadius + .006f);
                velocity = Vector3.Reflect(velocity, hit.normal) * .48f;
                if (hit.normal.y > .55f && velocity.sqrMagnitude < 1.2f)
                { velocity = Vector3.zero; resting = true; return; }
            }
        }

        public static bool ClearSight(PhysicsScene scene, Vector3 origin, Vector3 point, Transform target)
        {
            Vector3 travel = point - origin;
            return travel.sqrMagnitude < .001f ||
                !scene.Raycast(origin, travel.normalized, out var hit, travel.magnitude,
                    HitMask, QueryTriggerInteraction.Ignore) || hit.collider.transform.IsChildOf(target);
        }
    }
}
