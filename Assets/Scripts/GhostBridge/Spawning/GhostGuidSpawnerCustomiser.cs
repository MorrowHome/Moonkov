// GhostSpawner 可选的固定 GUID 来源。编辑时根据场景对象的层级信息算出 ID，生成器优先使用这个值。
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public class GhostGuidSpawnerCustomiser : MonoBehaviour, IGhostSpawnerCustomiser {
    [field: SerializeField] public Hash128 Guid { get; private set; }

    public void OnGhostPrefabSpawned(Entity ghostEntity, EntityCommandBuffer ecb) {
        // N/A
    }

#if UNITY_EDITOR
    // 指定使用固定标识时，由编辑器按层级位置等信息求 GUID 并序列化。运行中的生成器直接读取保存的值。
    public void OnValidate() { Guid = GhostGameObject.GenerateNewHashOnNameDepthAndSiblingIndex(gameObject); }
#endif
}