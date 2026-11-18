// GhostSpawner 可选的固定 GUID 来源。编辑时根据场景对象的层级信息算出 ID，生成器优先使用这个值。
using Unity.Entities;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public class GhostGuidSpawnerCustomiser : MonoBehaviour,
    IGhostSpawnerCustomiser
{
    [field: SerializeField] public Hash128 Guid { get; private set; }

    public void OnGhostPrefabSpawned(Entity ghostEntity, EntityCommandBuffer ecb)
    {
        // N/A
    }

#if UNITY_EDITOR
    public void OnValidate()
    {
        Guid = GhostGameObject.GenerateNewHashOnNameDepthAndSiblingIndex(gameObject);
    }
#endif
}
