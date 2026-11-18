// 按 GUID 同时登记实体模板和可寻址的 GameObject 资源。服务器用前者生成实体，GhostGameObjectLifetimeSystem 等后者加载完成再建表现体。
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Hash128 = Unity.Entities.Hash128;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class GhostEntityPrefabSystem : ClientServerSingletonSystem<GhostEntityPrefabSystem> {
    public struct EntityPrefabProcessed : IComponentData {
    }

    private Dictionary<Hash128, Entity> m_EntityPrefabsByGuid = new();
    private Dictionary<Hash128, AssetReferenceGameObject> m_GameObjectPrefabsByGuid = new();
#if !NGS_SUBMISSION_BUILD
    private Dictionary<string, Entity> m_EntityPrefabsByName = new();
#endif
    private int m_LoadedPrefabs = 0;
    // 记录已完成的加载数量。生成器会等已登记的表现资源都完成，避免查到实体模板却拿不到对应的 GameObject。
    public bool PrefabsLoaded => m_LoadedPrefabs > 0 && m_LoadedPrefabs >= m_GameObjectPrefabsByGuid.Count;

    public AssetReferenceGameObject GetGameObjectPrefab(Hash128 hash) {
        if (m_GameObjectPrefabsByGuid.TryGetValue(hash, out var prefabReference)) return prefabReference;
        return null;
    }

#if !NGS_SUBMISSION_BUILD
    public bool TryFindPrefabByPartialName(string partialName, out Entity prefab) {
        // any complete matches?
        foreach (var entry in m_EntityPrefabsByName)
            if (string.Equals(entry.Key.ToString(), partialName, StringComparison.OrdinalIgnoreCase)) {
                Debug.Log($"Found complete match for ghost {entry.Key} and string {partialName}");
                prefab = entry.Value;
                return true;
            }

        // any partial matches?
        foreach (var entry in m_EntityPrefabsByName)
            if (entry.Key.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0) {
                Debug.Log($"Found a partial match for ghost {entry.Key} and string {partialName}");
                prefab = entry.Value;
                return true;
            }
        prefab = default;
        return false;
    }
#endif

    public Entity GetEntityPrefab(Hash128 entityPrefabGuid) {
        if (m_EntityPrefabsByGuid.TryGetValue(entityPrefabGuid, out var entity)) return entity;
        return Entity.Null;
    }

    protected override void OnDestroy() {
        // World 退场时释放这个登记器持有的 Addressables 资源操作。下次进局会从自己的模板表重新读。
        foreach (var entry in m_GameObjectPrefabsByGuid)
            if (entry.Value.IsValid())
                entry.Value.ReleaseAsset();
        base.OnDestroy();
    }

    protected override void OnUpdate() {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
        int numRegistered = 0;
        foreach (var (ghostPrefabReference, entity) in SystemAPI.Query<RefRO<GhostGameObjectPrefabReference>>()
                     .WithNone<EntityPrefabProcessed>().WithAll<GhostInstance, Prefab>()
                     .WithOptions(EntityQueryOptions.IncludePrefab).WithEntityAccess()) {
            // 每次处理一个新出现且还没登记的实体模板。用同一个 GUID 把 ECS 模板和异步加载的表现模板配对。
            var prefabGuid = ghostPrefabReference.ValueRO.PrefabGuid;
            if (!m_EntityPrefabsByGuid.ContainsKey(prefabGuid)) {
                var assetReference = new AssetReferenceGameObject(prefabGuid.ToString());
                var operation = assetReference.LoadAssetAsync();
                operation.Completed += OnPrefabLoaded;
                m_EntityPrefabsByGuid.Add(prefabGuid, entity);
                m_GameObjectPrefabsByGuid.Add(prefabGuid, assetReference);
#if !NGS_SUBMISSION_BUILD
                m_EntityPrefabsByName.Add(ghostPrefabReference.ValueRO.PrefabName.ToString(), entity);
#endif
            }
            // 登记结束时给模板加标记。以后 ECS 继续枚举这一批预制件，就不会重复创建加载操作。
            ecb.AddComponent<EntityPrefabProcessed>(entity);
            numRegistered++;
        }
        ecb.Playback(EntityManager);
        ecb.Dispose();
        if (numRegistered > 0)
            Debug.Log(
                $"[GhostEntityPrefabSystem] : Ghosts registered {numRegistered.ToString()} this frame, total ghosts {m_EntityPrefabsByGuid.Count.ToString()}.");
    }

    private void OnPrefabLoaded(AsyncOperationHandle<GameObject> operation) {
        Debug.Assert(operation.Result != null,
            "[GhostEntityPrefabSystem] OnPrefabLoaded : Asset is null, loading failed!");
        m_LoadedPrefabs++;
    }
}