using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    struct NeedsLinking : IComponentData
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(GhostGameObjectLifetimeSystem))]
    public partial class ProjectileReconciliationSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var networkTimeExists = SystemAPI.TryGetSingleton(out NetworkTime networkTime);
            if (!networkTimeExists)
                return;

            if (!networkTime.ServerTick.IsValid)
                return;


            if (!SystemAPI.TryGetSingleton(out NetworkId networkIdComponent))
                return;
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            var localPlayerNetworkId = networkIdComponent.Value;

            foreach (var (projectileData, entity)
                     in SystemAPI.Query<RefRO<Projectile.ProjectileData>>()
                         .WithNone<GhostGameObjectLink>()
                         .WithEntityAccess())
            {
                if (projectileData.ValueRO.OwnerNetworkId != localPlayerNetworkId)
                    continue;

                uint serverInputTick = projectileData.ValueRO.SpawnTick;
                Projectile.PredictedProjectileInfo bestMatchInfo = null;

                // Find the closest predicted projectile within the window.
                foreach (var prediction in Projectile.PredictedProjectiles)
                {
                    if (prediction.Instance != null && prediction.SpawnTick == serverInputTick &&
                        prediction.WeaponID == projectileData.ValueRO.WeaponID)
                    {
                        bestMatchInfo = prediction;
                        break;
                    }
                }

                if (bestMatchInfo != null)
                {
                    var predictedGameObject = bestMatchInfo.Instance;
                    var ghostComponent = predictedGameObject.GetComponent<GhostGameObject>();

                    if (ghostComponent != null)
                    {
                        // 1. Schedule adding the link component via ECB
                        ecb.AddComponent(entity, new GhostGameObjectLink { LinkedInstance = ghostComponent });

                        // 2. Schedule adding a temporary component to signal LinkGhost call needed
                        //    (We'll handle the actual LinkGhost call after ECB playback)
                        ecb.AddComponent<NeedsLinking>(entity); // NeedsLinking is a simple struct IComponentData {}

                        // Remove from the prediction list (this uses the *static* list from Projectile)
                        Projectile.PredictedProjectiles.Remove(bestMatchInfo);
                    }
                    else
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogError(
                            $"Reconciliation match found for entity {entity.Index.ToString()}, but predicted GameObject {predictedGameObject.name} is missing GhostGameObject component!");
#endif
                        // Destroy the problematic predicted object immediately
                        Object.Destroy(predictedGameObject);
                        Projectile.PredictedProjectiles.Remove(bestMatchInfo);
                    }
                }
            }

            // IMPORTANT: Play back the ECB *before* attempting to call LinkGhost
            ecb.Playback(EntityManager);
            ecb.Dispose();

            // Now, handle the LinkGhost calls for entities that were just linked
            foreach (var (needsLinking, entity) in SystemAPI.Query<RefRO<NeedsLinking>>().WithAll<GhostGameObjectLink>()
                         .WithEntityAccess())
            {
                var link = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (link.LinkedInstance != null && link.LinkedInstance.gameObject != null) // Check if instance is valid
                {
                    // Ensure the GameObject is active if needed
                    if (!link.LinkedInstance.gameObject.activeSelf)
                    {
                        link.LinkedInstance.gameObject.SetActive(true);
                    }

                    // Call LinkGhost now that the component exists
                    GhostGameObjectLifetimeSystem.Instance(World).AdoptPredictedGhost(entity, link.LinkedInstance);
                    link.LinkedInstance.LinkGhost(World, entity, MultiplayerRole.ClientOwned);
                }
                else
                {
                    Debug.LogWarning(
                        $"[{entity.Index.ToString()}] Entity marked for linking, but LinkedInstance is null or destroyed.");
                }
            }

            // Clean up the temporary component (using another ECB or direct EntityManager)
            // This could be combined with the above ForEach if using ECB there.
            // For simplicity with direct EntityManager:
            var cleanupQuery = SystemAPI.QueryBuilder().WithAll<NeedsLinking>().Build();
            EntityManager.RemoveComponent<NeedsLinking>(cleanupQuery);

            for (int i = Projectile.PredictedProjectiles.Count - 1; i >= 0; i--)
            {
                var predictedProjectileInfo = Projectile.PredictedProjectiles[i];
                // Time-based expiry also handles tick-rate changes and stalled prediction.
                if (UnityEngine.Time.time - predictedProjectileInfo.CreatedAt > 2f)
                {
                    if (predictedProjectileInfo.Instance != null)
                        Object.Destroy(predictedProjectileInfo.Instance);
                    Projectile.PredictedProjectiles.RemoveAt(i);
                }
            }
        }
    }
}
