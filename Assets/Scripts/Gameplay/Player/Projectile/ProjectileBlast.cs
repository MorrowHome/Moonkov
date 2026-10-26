using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Gameplay.Leaderboard;

namespace Unity.MP_FPS
{
    public static class ProjectileBlast
    {
        // Health entities are the victim identity. Alien rigs and PMC limbs can
        // have many colliders, on different layers; none can multiply the blast.
        public static int Apply(World world, PhysicsScene scene, Vector3 origin, float radius,
            float damage, int shooter, uint tick, out byte flags)
            =>Apply(world,scene,origin,radius,damage,BountyClashDamage.Capture(world,shooter),tick,out flags);
        public static int Apply(World world, PhysicsScene scene, Vector3 origin, float radius,
            float damage, ClashDamageSource source, uint tick, out byte flags)
        {
            flags = 0;
            if (world == null || !world.IsCreated || radius <= 0 || damage <= 0) return 0;
            var manager = world.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(PredictedPlayerGhost), typeof(GhostGameObjectLink));
            using var entities = query.ToEntityArray(Allocator.Temp);
            int victims = 0;
            foreach (var entity in entities)
            {
                var root = manager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
                if (!root || root.gameObject.scene.GetPhysicsScene() != scene) continue;
                bool owned = manager.HasComponent<GhostOwner>(entity);
                int owner = owned ? manager.GetComponentData<GhostOwner>(entity).NetworkId : int.MinValue;
                if (BountyClashDamage.IsSelf(world,entity,source)) continue; // Preserve projectile shooter immunity.
                var health = manager.GetComponentData<PredictedPlayerGhost>(entity);
                if (health.CurrentHealth <= 0) continue;
                var collider = root.GetComponentInChildren<Collider>();
                Vector3 point = collider ? collider.bounds.center : root.transform.position + Vector3.up * .8f;
                if (Vector3.Distance(origin, point) > radius || !EnergyGrenadePhysics.ClearSight(scene, origin, point, root.transform)) continue;
                var before = health;
                // A blast is one combat impact; the unified damage entry splits residual tissue damage.
                BountyClashDamage.Apply(world, entity, source, BodyPart.Auto, damage, tick, RaidDamageKind.Explosion, (Unity.Mathematics.float3)origin);
                health=manager.GetComponentData<PredictedPlayerGhost>(entity);
                byte impact = RaidHealth.ImpactFlags(before, health);
                if (impact == 0) continue;
                victims++; flags |= impact;
            }
            return victims;
        }
    }
}
