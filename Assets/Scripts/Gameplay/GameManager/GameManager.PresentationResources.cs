// GameManager 在加载进度里提前等角色预制体到位，再为角色网格准备护盾所需的拟合数据。每处理一个网格让出一帧。
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    public partial class GameManager {
        private static async Task PrepareCharacterResourcesAsync(World world, CancellationToken cancellationToken) {
            // Let the prefab registry process the subscene entities loaded this frame.
            await Awaitable.NextFrameAsync(cancellationToken);
            var registry = world.GetExistingSystemManaged<GhostEntityPrefabSystem>();
            if (registry == null) return;
            GhostGameObjectPrefabReference[] references;
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
                await asset.OperationHandle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                var prefab = asset.Asset as GameObject;
                if (!prefab || !prefab.GetComponent<DollSingerNetworkPresentation>()) continue;
                foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                    if (!renderer.sharedMesh || renderer.name.Contains("Runtime")) continue;
                    EnergyShieldFittingData.For(renderer);
                    await Awaitable.NextFrameAsync(cancellationToken);
                }
            }
        }
    }
}