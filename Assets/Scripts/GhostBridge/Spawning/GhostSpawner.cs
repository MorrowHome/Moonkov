// 场景里的出生标记和服务器生成工具。它用登记过的 Ghost 预制件创建实体，写入位置、旋转与 GUID，场景里的表现体随后由生命周期系统生成。
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Hash128 = Unity.Entities.Hash128;
using Unity.GhostBridge;

public interface IGhostSpawnerCustomiser {
    void OnGhostPrefabSpawned(Entity ghostEntity, EntityCommandBuffer ecb);
}

#if UNITY_EDITOR
public interface IGhostSpawnerEditorVisuals {
    void OnEditorVisualsRefreshed();
    GhostSpawner.GhostReference GetAdditionalEditorVisualPrefab(GameObject ghostPrefab);
    Vector3 EditorVisualScale { get; }
}

public interface IGhostSpawnerEditorVisualsStripTag {
    string TagToApply { get; }
}
#endif

[SelectionBase]
public class GhostSpawner : MonoBehaviour {
    [Serializable]
    public class GhostReference : ISerializationCallbackReceiver {
        [field: SerializeField] public AssetReferenceGameObject GhostPrefab { get; set; }
        public Hash128 GhostGuid => m_GhostGuid;
        [SerializeField] [HideInInspector] private Hash128 m_GhostGuid;

        public bool OnValidate() {
            // 场景保存的是 Addressables 资源引用，同时把它的 GUID 缓存给运行时代码。生成前可以很快判断引用是否配置完整。
            bool validReference = GhostPrefab != null && GhostPrefab.RuntimeKeyIsValid();
            m_GhostGuid = validReference ? new Hash128(GhostPrefab.AssetGUID) : new Hash128();
            return validReference;
        }

        public void OnBeforeSerialize() { OnValidate(); }
        public void OnAfterDeserialize() { }

        public void SetAssetReference(AssetReferenceGameObject prefab) {
            GhostPrefab = prefab;
            OnValidate();
        }
    }

    [field: SerializeField] public GhostReference GhostPrefabReference { get; set; } = new();
    [field: SerializeField] public bool RandomiseRotation { get; private set; }
    [field: SerializeField] public bool UniformScaleOverride { get; private set; }
    [field: SerializeField] public bool PersistGameObject { get; private set; } = false;
    [field: SerializeField] public string LockedBehindFeature { get; private set; } = "";
    public Hash128 GhostNetGUID { get; private set; }
    private static readonly List<IGhostSpawnerCustomiser> s_GhostSpawnerCustomisers = new();
    protected bool m_PersistSpawnerAfterSpawn;
    protected bool m_GhostSpawned;
    private string m_cachedName = "";
#if UNITY_EDITOR
    private const HideFlags kDefaultHideValues = HideFlags.NotEditable | HideFlags.DontSave;
    private List<GameObject> m_EditorVisualObjectsToDestroy;
    private GameObject m_EditorVisualObjectForLightbaking;
    private List<Component> m_EditorComponentsToDestroy;
    private AssetReferenceGameObject m_PreviouslyCachedGhostPrefab = new("");

    public virtual void OnValidate() {
        if (!Application.isPlaying) {
            if (gameObject.scene.name != null && EditorVisualsNeedUpdate()) StripEditorVisuals(true);
            //RestoreEditorVisuals();
            if (TryGetComponent<GhostGameObject>(out _))
                Debug.LogError(
                    $"[SCENEGHOSTSPAWNER] OnValidate: Spawner {gameObject.name} has a GhostGameObject component. It shouldn't");
        }
    }

    private void OnDestroy() {
        EditorApplication.delayCall -= EditorVisualDestroy;
        EditorApplication.update -= EditorLightBakeHeartbeat;
    }

    public void StripEditorVisuals(bool defer) {
        MarkEditorOnlyChildrenForDestroy(transform);
        if (defer)
            EditorApplication.delayCall += EditorVisualDestroy;
        else
            EditorVisualDestroy();
    }

    // public void RestoreEditorVisuals()
    // {
    //     EditorApplication.delayCall -= EditorVisualCreate; //CH: 清掉之前的回调，避免重复执行。
    //     EditorApplication.delayCall += EditorVisualCreate;
    //     // 挂接光照烘焙的回调。
    //     Lightmapping.bakeStarted -= EditorLightBakeStart;
    //     Lightmapping.bakeStarted += EditorLightBakeStart;
    // }

    private GameObject InstantiateEditorVisual(GameObject prefab, Transform parent) {
        var instance = Instantiate(prefab, parent);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        var components = instance.GetComponentsInChildren<Component>();
        foreach (var component in components) {
            if (component is Collider componentCollider) { componentCollider.enabled = false; } else if
                (!(component is Renderer || component is Transform || component is MeshFilter || component is Light)) {
                if (m_EditorComponentsToDestroy == null) m_EditorComponentsToDestroy = new List<Component>();
                m_EditorComponentsToDestroy.Add(component);
            }

            // if (component is MovementBase)
            // {
            //     var children = component.GetComponentsInChildren<Transform>(includeInactive: false);
            //     foreach (var child in children)
            //     {
            //         child.gameObject.layer = (int)LayerIndex.ServerMovingBase;
            //     }
            // }
            var stripTag = component as IGhostSpawnerEditorVisualsStripTag;
            if (stripTag != null) component.gameObject.tag = stripTag.TagToApply;
        }
        return instance;
    }

    private static void ModifyHideFlagsRecursive(
        GameObject rootGameObject,
        HideFlags hideFlagsToSet,
        HideFlags hideFlagsToClear) {
        var rootTransform = rootGameObject.transform;
        int numChildren = rootTransform.childCount;
        rootGameObject.hideFlags |= hideFlagsToSet;
        rootGameObject.hideFlags &= ~hideFlagsToClear;
        for (int i = 0; i < numChildren; i++) {
            var childGameObject = rootTransform.GetChild(i).gameObject;
            ModifyHideFlagsRecursive(childGameObject, hideFlagsToSet, hideFlagsToClear);
        }
    }

    protected void EditorVisualCreate_Internal(
        GhostReference visualPrefab,
        Transform parent,
        ref AssetReferenceGameObject cachedReference) {
        cachedReference = new AssetReferenceGameObject(visualPrefab.GhostPrefab.AssetGUID);
        var prefab = visualPrefab.GhostPrefab.editorAsset;
        if (prefab != null && prefab.TryGetComponent<GhostGameObject>(out _)) {
            var editorVisualScale = Vector3.one;
            var customisers = parent.GetComponents<IGhostSpawnerEditorVisuals>();
            foreach (var customiser in customisers)
                if (customiser.EditorVisualScale != Vector3.one) {
                    editorVisualScale = customiser.EditorVisualScale;
                    break;
                }
            var instance = InstantiateEditorVisual(prefab, parent);
            instance.transform.localScale = editorVisualScale;
            foreach (var customiser in customisers) {
                var editorVisual = customiser.GetAdditionalEditorVisualPrefab(instance);
                var customiserPrefab = editorVisual != null ? editorVisual.GhostPrefab.editorAsset : null;
                if (customiserPrefab != null) InstantiateEditorVisual(customiserPrefab, instance.transform);
                customiser.OnEditorVisualsRefreshed();
            }
            m_EditorVisualObjectForLightbaking = instance;
            var playModeSettings = PlayModeSettingsHandler.GetEditorSettings();
            var hideFlagsToSet = playModeSettings.ShowEditorVisuals ? HideFlags.None : HideFlags.HideInInspector;
            instance.tag = "EditorOnly";
            instance.name = $"[EDITORVISUAL] {GhostPrefabReference.GhostPrefab.editorAsset.name}.{prefab.name}";
            ModifyHideFlagsRecursive(instance, kDefaultHideValues | hideFlagsToSet, HideFlags.None);
        } else {
            Debug.LogWarning(
                $"[SCENEGHOSTSPAWNER] EditorVisualCreate_Internal: This ghost prefab in spawner {name} is not a GhostGameObject, aborting");
        }
    }

    protected void MarkEditorOnlyChildrenForDestroy(Transform parent) {
        // 移除只为编辑器预览创建的子物体。
        for (int i = 0; i < parent.childCount; i++) {
            var child = parent.GetChild(i);
            if (child.gameObject.CompareTag("EditorOnly")) {
                if (m_EditorVisualObjectsToDestroy == null) m_EditorVisualObjectsToDestroy = new List<GameObject>();
                m_EditorVisualObjectsToDestroy.Add(child.gameObject);
            } else { MarkEditorOnlyChildrenForDestroy(child); }
        }
    }

    protected virtual bool EditorVisualsNeedUpdate() {
        return GhostPrefabReference.GhostPrefab != null &&
               GhostPrefabReference.GhostPrefab.AssetGUID != m_PreviouslyCachedGhostPrefab.AssetGUID;
    }

    // protected virtual void EditorVisualCreate()
    // {
    //     if (!Application.isPlaying && this != null && gameObject.activeInHierarchy)
    //     {
    //         EditorVisualCreate_Internal(GhostPrefabReference, transform, ref m_PreviouslyCachedGhostPrefab);
    //
    //         EditorVisualDestroy();
    //     }
    // }

    protected void EditorLightBakeStart() {
        if (m_EditorVisualObjectForLightbaking == null) return;
        ModifyHideFlagsRecursive(m_EditorVisualObjectForLightbaking, HideFlags.None, kDefaultHideValues);

        // 光照烘焙只有完成时有回调。持续检查可以兼顾中途取消。
        // 待办。切到 2023.3 或 Unity 6 时可使用完成与取消回调。
        EditorApplication.update += EditorLightBakeHeartbeat;
    }

    protected void EditorLightBakeHeartbeat() {
        if (m_EditorVisualObjectForLightbaking == null || Lightmapping.isRunning) return;
        ModifyHideFlagsRecursive(m_EditorVisualObjectForLightbaking, kDefaultHideValues, HideFlags.None);
        EditorApplication.update -= EditorLightBakeHeartbeat;
    }

    protected void EditorVisualDestroy() {
        if (m_EditorVisualObjectsToDestroy != null) {
            foreach (var go in m_EditorVisualObjectsToDestroy) DestroyImmediate(go);
            m_EditorVisualObjectsToDestroy.Clear();
        }
        if (m_EditorComponentsToDestroy != null) {
            // 先移除 GhostMonoBehaviour 脚本。
            foreach (var comp in m_EditorComponentsToDestroy)
                if (comp is GhostMonoBehaviour ||
#if WWISE_AUDIO_SUPPORTED
                    comp is AkEvent ||
                    comp is AkAmbient ||
#endif
                    //WWISE_AUDIO_SUPPORTED
                    comp is GhostAuthoringComponent || comp is Joint)
                    DestroyImmediate(comp);

            // 接着移除其他预览组件。
            foreach (var comp in m_EditorComponentsToDestroy)
                if (comp != null)
                    DestroyImmediate(comp);
            m_EditorComponentsToDestroy.Clear();
        }
    }
#endif //UNITY_EDITOR

    protected virtual void Awake() {
        if (!ClientServerBootstrap.HasServerWorld)
            // Ghost 的生成由
            // 拥有服务端 World 的进程负责。
            DestroySpawner();
        else
            AllocateGhostGUID();
    }

    // 场景标记可以用配置好的固定 GUID。没有固定值时生成一个新的，客户端通过同步到的标识认识这个物体。
    protected void AllocateGhostGUID() {
        if (TryGetComponent<GhostGuidSpawnerCustomiser>(out var guidCustomiser))
            GhostNetGUID = guidCustomiser.Guid;
        else
            GhostNetGUID = GhostGameObject.GenerateRandomHash();
    }

    private void DestroySpawner() {
        if (PersistGameObject) {
#if UNITY_EDITOR
            // 运行时先撤掉只供摆场景用的外观。
            MarkEditorOnlyChildrenForDestroy(transform);
            EditorVisualDestroy();
#endif

            // 保留这个场景物体，移除其生成器组件。
            GetComponentsInChildren(s_GhostSpawnerCustomisers);
            foreach (var componentToDestroy in s_GhostSpawnerCustomisers) Destroy((Component)componentToDestroy);
            Destroy(this);
        } else { Destroy(gameObject); }
    }

    // 在 LateUpdate 生成，让绑定在下一帧的正确阶段进行。
    // 如果 Update 里创建，随后的 GhostSend 可能马上发出尚未接好的实体。
    // GhostGameObjectLifetimeSystem 会在下一帧开始接手，
    // 在下一次 GhostSend 前完成初始化。
    public virtual void LateUpdate() {
        // 资源登记包含异步加载。出生标记要等到登记结束再试，避免刚进地图还没有模板就开始生成。
        if (GhostEntityPrefabSystem.ServerInstance != null && !GhostEntityPrefabSystem.ServerInstance.PrefabsLoaded)
            return;
        if (GhostBridgeManager.Instance.IsServerListening())
            //if (ServerConnectionSystem.TryGetInstance(out var serverConnectionSystem) && serverConnectionSystem.IsPlaying)
        {
            var spawnRotation = transform.rotation;
            if (RandomiseRotation) spawnRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            m_GhostSpawned = false;
            if (GhostPrefabReference.GhostGuid.IsValid) {
                // 部分地图物体只能在功能开关启用时出现。未开就标记这一处已经处理，普通生成器不用留场继续重试。
                if (FeatureToggle.IsAllowedByFeature(LockedBehindFeature)) {
                    var prefabEntity = FindGhostPrefabEntity(GhostPrefabReference);
                    if (prefabEntity != Entity.Null) {
                        Debug.Assert(GhostNetGUID.IsValid,
                            "[SCENEGHOSTSPAWNER] GhostNetGUID is invalid, should have been pre-allocated on Awake");
                        GetComponents(s_GhostSpawnerCustomisers);
                        m_GhostSpawned = SpawnGhostPrefab(prefabEntity, transform.position, spawnRotation, GhostNetGUID,
                            UniformScaleOverride ? transform.localScale.x : 1.0f, (entity, ecb) => {
                                OnGhostPrefabSpawned(entity, ecb);
                                // 实体刚创建时可以写入这一个出生点的额外配置。这些写入和生成请求使用同一份命令缓冲。
                                foreach (var customiser in s_GhostSpawnerCustomisers)
                                    customiser.OnGhostPrefabSpawned(entity, ecb);
                            });
                    } else {
                        Debug.LogError(
                            $"[SCENEGHOSTSPAWNER] Update: GhostPrefabReference entity not found for {GetCachedName()}",
                            this);
                    }
                } else {
                    // 功能关掉时也将这个生成点标记为已处理，
                    // 走一次成功处理后的清理流程。
                    m_GhostSpawned = true;

                    // 这个出生点停用时也撤掉常驻标记。
                    m_PersistSpawnerAfterSpawn = false;
                    PersistGameObject = false;
                }
            } else {
                Debug.LogError($"[SCENEGHOSTSPAWNER] Update: GhostPrefabReference invalid for {GetCachedName()}", this);
            }
            // 多数出生点生成一次就完成。只在创建成功后拆掉出生点，找不到资源时暂时保留并留待下次重试。
            if (!m_PersistSpawnerAfterSpawn) {
                if (m_GhostSpawned)
                    // 生成完成，出生点可以收回。
                    DestroySpawner();
                else
                    Debug.LogWarning(
                        $"[SCENEGHOSTSPAWNER] Unable to spawn scene ghost {GetCachedName()}. Will keep trying", this);
            }
        }
    }

    private string GetCachedName() {
        if (string.IsNullOrEmpty(m_cachedName)) m_cachedName = gameObject.name;
        return m_cachedName;
    }

    protected virtual void OnGhostPrefabSpawned(Entity ghostEntity, EntityCommandBuffer ecb) { }

    public static Entity FindGhostPrefabEntity(GhostReference ghostReference) {
        return FindGhostPrefabEntity(ghostReference.GhostGuid);
    }

    public static Entity FindGhostPrefabEntity(Hash128 ghostGuid) {
        if (GhostBridgeManager.Instance.IsServerListening())
            return GhostEntityPrefabSystem.ServerInstance.GetEntityPrefab(ghostGuid);
        return Entity.Null;
    }

    public static GameObject FindGhostPrefab(GhostReference ghostReference) {
        GameObject gObj = null;
        if (GhostBridgeManager.Instance.IsServerListening()) {
            var prefab = GhostEntityPrefabSystem.ServerInstance.GetGameObjectPrefab(ghostReference.GhostGuid);
            if (prefab != null && prefab.Asset != null) gObj = prefab.Asset as GameObject;
        }
        if (gObj == null) {
#if UNITY_EDITOR
            Debug.LogError(
                $"Failed to find {ghostReference.GhostPrefab.editorAsset.name} - has it been added to the GlobalGhostPrefabs prefab?");
#else
            Debug.LogError($"Failed to find GhostReference with GUID {ghostReference.GhostGuid.ToString()} - has it been added to the GlobalGhostPrefabs prefab?");
#endif
        }
        return gObj;
    }

    public static bool SpawnGhostPrefab(
        GhostReference ghostPrefab,
        Vector3 spawnPos,
        Quaternion spawnRot,
        Hash128 netGuid,
        float uniformScale = 1.0f,
        Action<Entity, EntityCommandBuffer> postSpawnSpecialisation = null) {
        var prefabEntity = FindGhostPrefabEntity(ghostPrefab);
        if (prefabEntity != Entity.Null)
            return SpawnGhostPrefab(prefabEntity, spawnPos, spawnRot, netGuid, uniformScale, postSpawnSpecialisation);
        else
            Debug.LogError(
                $"[SCENEGHOSTSPAWNER] SpawnGhostPrefab trying to spawn an entity prefab '{ghostPrefab.GhostPrefab.AssetGUID}' that doesn't seem to exist. Was it added to GhostPrefabs?");
        return false;
    }

    public static bool SpawnGhostPrefab(
        Entity prefabEntity,
        Vector3 spawnPos,
        Quaternion spawnRot,
        Hash128 netGuid,
        float uniformScale = 1.0f,
        Action<Entity, EntityCommandBuffer> postSpawnSpecialisation = null) {
        if (prefabEntity == Entity.Null) {
            Debug.LogError(
                $"SpawnGhostPrefab trying to spawn an entity prefab that doesn't exist. Was it added to GhostPrefabs?");
            return false;
        }

        // 如果 Ghost 更新已经准备了命令缓冲，就沿用它。
        // 在某个 Ghost 更新中生成时沿用现成的命令缓冲。回调之外调用则新建一个，记录完后当场交给服务端执行。
        var ecb = GhostGameObject.UpdateEntityCommandBuffer;
        bool ecbNeedsPlayback = false;
        if (!ecb.IsCreated) {
            // 如果当前在更新之外，就创建临时缓冲并当场执行。
            ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            ecbNeedsPlayback = true;
        }
        if (GhostBridgeManager.Instance.IsServerListening())
            //if (ServerConnectionSystem.Instance.IsPlaying)
        {
            // 从服务端的模板复制实体后，先给它初始坐标、缩放和网络标识。生命周期系统读到这些值后才创建对应表现。
            var instance = ecb.Instantiate(prefabEntity);
            ecb.SetComponent(instance, new LocalTransform {
                Position = spawnPos,
                Rotation = spawnRot,
                Scale = uniformScale
            });
            if (netGuid.IsValid) ecb.SetComponent(instance, new GhostGameObjectGuid { Guid = netGuid });
            postSpawnSpecialisation?.Invoke(instance, ecb);
            if (ecbNeedsPlayback)
                //ecb.Playback(ServerConnectionSystem.Instance.EntityManager);
                if (GhostBridgeManager.Instance.TryGetServerEntityManager(out var entityManager))
                    ecb.Playback(entityManager);
            return true;
        } else {
            Debug.LogError(
                $"SpawnGhostPrefab cannot spawn a ghost as the server doesn't appear to be in a playing state yet");
        }
        return false;
    }
}