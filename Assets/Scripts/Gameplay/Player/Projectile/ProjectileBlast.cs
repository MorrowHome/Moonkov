using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public static class ProjectileBlast
    {
        private const float InnerRadiusFraction = .25f;
        // Authoritative projectile impacts run serially on the main thread.
        private static readonly List<Collider> s_Colliders = new List<Collider>(32);

        public static float DamageAt(float damage, float radius, float distance)
        {
            if (!float.IsFinite(damage) || !float.IsFinite(radius) || !float.IsFinite(distance) ||
                damage <= 0 || radius <= 0 || distance < 0 || distance >= radius) return 0;
            return damage * Mathf.Min(1, (1 - distance / radius) / (1 - InnerRadiusFraction));
        }

        private static Vector3 ClosestBodyPoint(Transform root, Vector3 origin)
        {
            Vector3 point = root.position + Vector3.up * .8f;
            float nearest = float.PositiveInfinity;
            root.GetComponentsInChildren(false, s_Colliders);
            try
            {
                foreach (var collider in s_Colliders)
                {
                    if (!collider || !collider.enabled || collider.isTrigger) continue;
                    Vector3 candidate = collider is MeshCollider mesh && !mesh.convex
                        ? collider.bounds.ClosestPoint(origin) : collider.ClosestPoint(origin);
                    float distance = (candidate - origin).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    point = candidate;
                }
            }
            finally { s_Colliders.Clear(); }
            return point;
        }

        // Health entities are the victim identity. Alien rigs and PMC limbs can
        // have many colliders, on different layers; none can multiply the blast.
        public static int Apply(World world, PhysicsScene scene, Vector3 origin, float radius,
            float damage, int shooter, uint tick, out byte flags)
            =>Apply(world,scene,origin,radius,damage,BountyClashDamage.Capture(world,shooter),tick,out flags);
        public static int Apply(World world, PhysicsScene scene, Vector3 origin, float radius,
            float damage, ClashDamageSource source, uint tick, out byte flags)
        {
            flags = 0;
            if (world == null || !world.IsCreated || !scene.IsValid() ||
                !float.IsFinite(radius) || !float.IsFinite(damage) || radius <= 0 || damage <= 0 ||
                !float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(origin.z)) return 0;
            var manager = world.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(PredictedPlayerGhost), typeof(GhostGameObjectLink));
            using var entities = query.ToEntityArray(Allocator.Temp);
            int victims = 0;
            foreach (var entity in entities)
            {
                var root = manager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
                if (!root || root.gameObject.scene.GetPhysicsScene() != scene) continue;
                var health = manager.GetComponentData<PredictedPlayerGhost>(entity);
                if (health.CurrentHealth <= 0) continue;
                Vector3 point = ClosestBodyPoint(root.transform, origin);
                float amount = DamageAt(damage, radius, Vector3.Distance(origin, point));
                if (amount <= 0 || !EnergyGrenadePhysics.ClearSight(scene, origin, point, root.transform)) continue;
                var before = health;
                // A blast is one combat impact; the unified damage entry splits residual tissue damage.
                BountyClashDamage.Apply(world, entity, source, BodyPart.Auto, amount, tick, RaidDamageKind.Explosion, (Unity.Mathematics.float3)origin);
                health=manager.GetComponentData<PredictedPlayerGhost>(entity);
                byte impact = RaidHealth.ImpactFlags(before, health);
                if (impact == 0) continue;
                victims++;
                // Self damage is real, but cannot confirm an opponent hit or kill.
                if (!BountyClashDamage.IsSelf(world, entity, source)) flags |= impact;
            }
            return victims;
        }
    }
}
