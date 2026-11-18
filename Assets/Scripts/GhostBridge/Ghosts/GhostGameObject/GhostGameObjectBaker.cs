// 烘焙时把表现体预制件的 GUID 写进 ECS 实体供运行时查找，还会给实体添加 Ghost 脚本声明的嵌套数据组件和缓冲。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public struct GhostGameObjectPrefabReference : IComponentData {
    public Hash128 PrefabGuid;
    public Hash128 PrefabRootGuid;
    public FixedString128Bytes PrefabName;
}

public class GhostGameObjectBaker : Baker<GhostGameObject> {
    public override void Bake(GhostGameObject authoring) {
#if UNITY_EDITOR
        var entity = GetEntity(TransformUsageFlags.None);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(authoring.gameObject, out string guid, out long localId);
        // default to the objects' guid, if a prefab is at the root of a bunch of variants, we want to use this guid for comparisons
        var prefabRootGuid = new Hash128(guid);
        if (PrefabUtility.IsPartOfVariantPrefab(authoring.gameObject)) {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                PrefabUtility.GetCorrespondingObjectFromSource(authoring.gameObject), out string rootGuid,
                out long ignore);
            prefabRootGuid = new Hash128(rootGuid);
        }
        AddComponent(entity, new GhostGameObjectPrefabReference {
            PrefabGuid = new Hash128(guid),
            PrefabName = authoring.gameObject.name,
            PrefabRootGuid = prefabRootGuid
        });
        AddComponent<GhostGameObjectGuid>(entity);
        var gameObjectPrefab = authoring.gameObject;
        if (gameObjectPrefab != null) {
            // 扫描预制件上的 GhostMonoBehaviour 及其基类，找到应当放进实体的数据类型。RPC 会作为临时消息创建。
            var ghostComponents = gameObjectPrefab.GetComponentsInChildren<GhostMonoBehaviour>();
            foreach (var component in ghostComponents) {
                // add any IComponentData
                var componentType = component.GetType();
                while (componentType != null && componentType.IsSubclassOf(typeof(GhostMonoBehaviour))) {
                    foreach (var type in componentType.GetNestedTypes()) {
                        if (type.GetInterface(nameof(IComponentData)) != null &&
                            type.GetInterface(nameof(IRpcCommand)) == null)
                            AddComponent(entity, type);
                        if (type.GetInterface(nameof(IBufferElementData)) != null)
                            // this is actually adding a dynamic buffer
                            // as the type it adds is a buffer element
                            // we have to do it this way as there is no equivalent of AddBuffer that takes
                            // a ComponentType
                            AddComponent(entity, type);
                    }
                    componentType = componentType.BaseType;
                }
            }
        }
#endif //UNITY_EDITOR
    }
}