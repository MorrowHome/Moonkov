// GameManager 在加载进度里提前等角色预制体到位，再为角色网格准备护盾所需的拟合数据。每处理一个网格让出一帧。
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    public partial class GameManager {
        private static async Task PrepareCharacterResourcesAsync(World world, CancellationToken cancellationToken) {
            // 让预制体注册系统先处理这一帧已经载入的子场景实体。
            // 子场景刚加载时，实体模板登记系统还可能没运行过。先等一帧，再按 GUID 找得到对应的预制件。
            await Awaitable.NextFrameAsync(cancellationToken);
            var registry = world.GetExistingSystemManaged<GhostEntityPrefabSystem>();
            if (registry == null) return;
            GhostGameObjectPrefabReference[] references;
            // 这里明确把 Prefab 包含在查询里。先把结果拷成普通数组，后面异步等待每个资源时就不需要一直持有 ECS 的临时数据。
            using (var query = world.EntityManager.CreateEntityQuery(new EntityQueryDesc {
                       All = new[] {
                           ComponentType.ReadOnly<GhostGameObjectPrefabReference>(), ComponentType.ReadOnly<Prefab>()
                       },
                       Options = EntityQueryOptions.IncludePrefab
                   }))
            using (var source = query.ToComponentDataArray<GhostGameObjectPrefabReference>(Allocator.Temp)) {
                references = source.ToArray();
            }
            foreach (var reference in references) {
                cancellationToken.ThrowIfCancellationRequested();
                var asset = registry.GetGameObjectPrefab(reference.PrefabGuid);
                if (asset == null || !asset.IsValid()) continue;
                // Addressables 可能已经在后台读这个对象。复用同一操作并等待完成，随时接受取消，以便加载失败后能回菜单。
                await asset.OperationHandle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                var prefab = asset.Asset as GameObject;
                if (!prefab || !prefab.GetComponent<DollSingerNetworkPresentation>()) continue;
                foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                    // 这里预热参与装备贴合的角色网格。每处理一个网格便让出一帧，加载动画还能继续更新。
                    if (!renderer.sharedMesh || renderer.name.Contains("Runtime")) continue;
                    EnergyShieldFittingData.For(renderer);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
            }
        }
    }
}