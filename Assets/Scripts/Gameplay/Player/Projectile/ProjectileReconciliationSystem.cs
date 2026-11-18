// 防止自己开火时出现两枚一模一样的弹体。收到自己的服务端弹体后，用出手 tick、武器编号和弹丸序号找到对应的预测对象。
// 匹配时把服务器实体链到已有 GameObject。等不到确认的临时预测对象会尽快清掉。
using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS {
    internal struct NeedsLinking : IComponentData {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateBefore(typeof(GhostGameObjectLifetimeSystem))]
    public partial class ProjectileReconciliationSystem : SystemBase {
        protected override void OnUpdate() {
            bool networkTimeExists = SystemAPI.TryGetSingleton(out NetworkTime networkTime);
            if (!networkTimeExists) return;
            if (!networkTime.ServerTick.IsValid) return;
            if (!SystemAPI.TryGetSingleton(out NetworkId networkIdComponent)) return;
            var ecb = new EntityCommandBuffer(Collections.Allocator.Temp);
            int localPlayerNetworkId = networkIdComponent.Value;
            foreach (var (projectileData, entity) in SystemAPI.Query<RefRO<Projectile.ProjectileData>>()
                         .WithNone<GhostGameObjectLink>().WithEntityAccess()) {
                // 需要接管本机预测对象的只有自己发射的弹体。看到别人的弹体由正常 Ghost 生命周期生成它的模型。
                if (projectileData.ValueRO.OwnerNetworkId != localPlayerNetworkId) continue;
                uint serverInputTick = projectileData.ValueRO.SpawnTick;
                Projectile.PredictedProjectileInfo bestMatchInfo = null;

                // 在当前匹配窗口找最合适的预测弹体。
                foreach (var prediction in Projectile.PredictedProjectiles)
                    // 同时对拍、武器和弹丸序号。多弹丸同拍出手时，仍能找到每一颗对应的预测对象。
                    if (prediction.Instance != null && prediction.SpawnTick == serverInputTick &&
                        prediction.WeaponID == projectileData.ValueRO.WeaponID &&
                        prediction.PelletIndex == projectileData.ValueRO.PelletIndex) {
                        bestMatchInfo = prediction;
                        break;
                    }
                if (bestMatchInfo != null) {
                    var predictedGameObject = bestMatchInfo.Instance;
                    var ghostComponent = predictedGameObject.GetComponent<GhostGameObject>();
                    if (ghostComponent != null) {
                        // 先用 ECB 排队为实体写入场景对象链接。
                        ecb.AddComponent(entity, new GhostGameObjectLink { LinkedInstance = ghostComponent });

                        // 再排队写临时标记，说明后面需要调用 LinkGhost。
                        //    等 ECB 实际写完组件后再调用 LinkGhost。
                        // 先登记已有对象要被接管。等 EntityCommandBuffer 写进链接组件后，才调用正式的 Ghost 绑定。
                        ecb.AddComponent<NeedsLinking>(entity); // NeedsLinking 是空内容的 IComponentData 结构体，用作临时标志。

                        // 从 Projectile 的静态预测列表移除这条已经接管的记录。
                        Projectile.PredictedProjectiles.Remove(bestMatchInfo);
                    } else {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.LogError(
                            $"Reconciliation match found for entity {entity.Index.ToString()}, but predicted GameObject {predictedGameObject.name} is missing GhostGameObject component!");
#endif
                        // 预测对象缺少必要的 Ghost 组件时立即释放。
                        Object.Destroy(predictedGameObject);
                        Projectile.PredictedProjectiles.Remove(bestMatchInfo);
                    }
                }
            }

            // 先执行 ECB，再尝试 LinkGhost，确保链接用的组件已经存在。
            ecb.Playback(EntityManager);
            ecb.Dispose();

            // 现在给刚写入链接的实体执行 LinkGhost。
            foreach (var (needsLinking, entity) in SystemAPI.Query<RefRO<NeedsLinking>>().WithAll<GhostGameObjectLink>()
                         .WithEntityAccess()) {
                var link = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (link.LinkedInstance != null && link.LinkedInstance.gameObject != null) // 确认链接指向的实例仍然有效。
                {
                    // 需要时先激活现有 GameObject。
                    if (!link.LinkedInstance.gameObject.activeSelf) link.LinkedInstance.gameObject.SetActive(true);

                    // 组件已经存在，可以调用 LinkGhost。
                    // 让生命周期系统接管预测时创建的 GameObject，后续它跟服务器实体一起更新和释放。
                    GhostGameObjectLifetimeSystem.Instance(World).AdoptPredictedGhost(entity, link.LinkedInstance);
                    link.LinkedInstance.LinkGhost(World, entity, MultiplayerRole.ClientOwned);
                } else {
                    Debug.LogWarning(
                        $"[{entity.Index.ToString()}] Entity marked for linking, but LinkedInstance is null or destroyed.");
                }
            }

            // 清理作为接管信号的临时组件，可另用 ECB 或直接调用 EntityManager。
            // 若上面的循环使用 ECB，也可在那个循环里排队移除。
            // 此处集中用 EntityManager 清理，少走一个缓冲。
            var cleanupQuery = SystemAPI.QueryBuilder().WithAll<NeedsLinking>().Build();
            EntityManager.RemoveComponent<NeedsLinking>(cleanupQuery);
            for (int i = Projectile.PredictedProjectiles.Count - 1; i >= 0; i--) {
                var predictedProjectileInfo = Projectile.PredictedProjectiles[i];
                // 按实际时间过期也能处理 tick 频率变化和预测暂停。
                // 等不到服务端确认的弹体通过时间超时释放，即使网络时钟停住也不会常驻。
                if (UnityEngine.Time.time - predictedProjectileInfo.CreatedAt > 2f) {
                    if (predictedProjectileInfo.Instance != null) Object.Destroy(predictedProjectileInfo.Instance);
                    Projectile.PredictedProjectiles.RemoveAt(i);
                }
            }
        }
    }
}