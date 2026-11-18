// 资源子场景在 Inspector 列出要烘焙的 Ghost 预制件。Baker 标记这些引用，让运行时能找到角色、管理器等所需的实体模板。
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class GhostPrefabsAuthoring : MonoBehaviour {
    [field: SerializeField] public List<AssetReferenceGameObject> GhostPrefabs { get; private set; }
}

public class GhostPrefabsBaker : Baker<GhostPrefabsAuthoring> {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() {
        s_BakedPrefabs.Clear();
        s_BakedPrefabs = null;
    }

    private static HashSet<EntityId> s_BakedPrefabs = new();

    public override void Bake(GhostPrefabsAuthoring authoring) {
        var ghosts = authoring.GhostPrefabs;
        s_BakedPrefabs.Clear();
#if UNITY_EDITOR
        for (int i = 0; i < ghosts.Count; i++) {
            var prefab = ghosts[i].editorAsset;
            if (prefab != null) {
                var hash = prefab.gameObject.GetEntityId();
                // 同一次烘焙里每份模板只登记一次。列表里配了重复资源会指出位置，便于回子场景核对。
                if (!s_BakedPrefabs.Contains(hash)) {
                    // required to get prefab to be baked
                    // 这里只需要把模板标记给烘焙器，并引用它的 Entity。实际生成位置由服务端生成器稍后写入。
                    GetEntity(prefab.gameObject, TransformUsageFlags.None);
                    s_BakedPrefabs.Add(hash);
                } else {
                    Debug.LogError(
                        $"[GHOSTPREFABSAUTHORING] Duplicate entity prefab detected `{prefab.gameObject.name}` index {i}");
                }
            } else { Debug.LogError($"[GHOSTPREFABSAUTHORING] Invalid entry in GhostPrefabs list at index {i}"); }
        }
#endif
    }
}