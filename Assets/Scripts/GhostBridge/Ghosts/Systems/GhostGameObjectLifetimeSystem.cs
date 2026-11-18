// 从 Ghost 实体生成并绑定场景物体，维护两者对应表。它负责区分服务端、自己控制的角色和远端角色，并按依赖顺序激活。
#if !ENABLE_PROFILING && !NGS_SUBMISSION_BUILD
#define TRY_CATCH_GHOSTOBJECT_EXCEPTIONS
#endif

//#define VERBOSE_GHOST_LIFETIME_EVENTS
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Jobs;
using Hash128 = Unity.Entities.Hash128;
using Object = UnityEngine.Object;

// 正常的数据先随 Ghost 一起移除，这个关联还能留给清理系统。它记着那只场景物体在哪儿，可以在实体退场后销毁。
public class GhostGameObjectLink : ICleanupComponentData {
    public GhostGameObject LinkedInstance;
}

// 给还在等待激活条件的 Ghost 加上这个标记。
public class GhostGameObjectDeferredActivation : ICleanupComponentData {
}

// 每个已激活的 Ghost 都有这个标记，标记销毁后会自动移除。
// 这一步有时早于场景物体实际销毁。
public class GhostGameObjectActive : IComponentData {
}

public enum ComponentStripBehaviour {
    DisableComponent,
    DestroyComponent,
    DestroyGameObject
};

public interface IStripComponent {
    // MonoBehaviour 的常规生命周期回调。
    GameObject gameObject { get; }
    bool enabled { get; set; }
    ComponentStripBehaviour StripBehaviour { get; }
}

public interface IClientOnlyComponent : IStripComponent {
}

public interface IServerOnlyComponent : IStripComponent {
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class GhostGameObjectLifetimeSystem : ClientServerSingletonSystem<GhostGameObjectLifetimeSystem> {
    private const int k_InitialGhostListCapacity = 100;
    private Dictionary<Hash128, GhostGameObject> m_GhostGameObjects = new();
    private List<GhostGameObject> m_GhostGameObjectList = new(k_InitialGhostListCapacity);
    public List<GhostGameObject> GhostGameObjectList => m_GhostGameObjectList;
    private TransformAccessArray m_TransformAccessArray;

    public TransformAccessArray GhostGameObjectTransformAccessArray {
        get {
            EnsureTransformAccessArrayUpToDate();
            return m_TransformAccessArray;
        }
    }

    private NativeList<Entity> m_GhostEntityList;
    public NativeList<Entity> GhostEntityList => m_GhostEntityList;

    public delegate void OnGhostPrefabSpawnedCallback(
        Hash128 guid,
        GhostGameObject obj,
        GhostGameObjectLifetimeSystem lifetimeSystem);

    public event OnGhostPrefabSpawnedCallback OnGhostPrefabSpawned;
    private int m_NumManagersCreated;
    private int m_NumLocalPlayersCreated;
    private int m_NumDeferredGhosts;
    private bool m_RebuildGhostGameObjectTransformAccessArray;
    private ClientGhostTransformApplySystem m_ClientTransformSystem;
    private ServerGhostTransformRetrieveSystem m_ServerTransformSystem;
    private List<Animator> m_RetrievedAnimators = new();
    private List<Renderer> m_RetrievedRenderers = new();
#if WWISE_AUDIO_SUPPORTED
    private List<AkGameObj> m_RetrievedAkGameObjs = new();
#endif
    private List<IClientOnlyComponent> m_RetrievedClientOnlyComponents = new();
    private List<IServerOnlyComponent> m_RetrievedServerOnlyComponents = new();
    private List<(GhostGameObject ghost, Entity entity, MultiplayerRole role)> _ghostsToLink = new();

    protected override void OnCreate() {
        base.OnCreate();
        m_GhostEntityList = new NativeList<Entity>(k_InitialGhostListCapacity, Allocator.Persistent);
        RequireForUpdate<NetworkId>();
    }

    protected override void OnDestroy() {
        CompleteKnownTransformJobs();
        var query = GetEntityQuery(typeof(GhostGameObjectLink));
        foreach (var entity in query.ToEntityArray(Allocator.Temp)) {
            var ghostLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
            ghostLink.LinkedInstance = null;
            EntityManager.SetComponentData(entity, ghostLink);
        }
        m_GhostEntityList.Dispose();
        if (m_TransformAccessArray.isCreated) m_TransformAccessArray.Dispose();
        base.OnDestroy();
    }

    protected override void OnUpdate() {
        CompleteTransformJobs();
        bool isClient = World.IsClient();
        bool isServer = World.IsServer();
        int networkId = isClient ? SystemAPI.GetSingleton<NetworkId>().Value : 0;
        var prefabSystem = GhostEntityPrefabSystem.Instance(World);
        // 进图期间先等预制件登记和管理器生成器。有了这两样，才开始给新到的 Ghost 建场景对应物件。
        if (!prefabSystem.PrefabsLoaded || ManagerGhostsSpawner.Instance == null) return; // 其他依赖系统尚未就绪。
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach (var (ghostGuid, ghostPrefabReference, ghost, localTransform, entity) in SystemAPI
                     .Query<RefRW<GhostGameObjectGuid>, RefRO<GhostGameObjectPrefabReference>, RefRO<GhostInstance>,
                         RefRO<LocalTransform>>().WithNone<GhostGameObjectLink>().WithEntityAccess()) {
            // 客户端先收到 Ghost 实体时，可能还没有同步到它的身份。
            // 等 GUID 有效后再创建和登记，避免空键冲突导致
            // 命令缓冲执行中断、已登记的对象缺少清理链接。
            // 客户端可能先看到实体，稍后才收到它真正的 GUID。这里等标识有效再登记，避免多个未就绪对象用同一个空键。
            if (!ghostGuid.ValueRO.Guid.IsValid) continue;
            var ghostPrefabGuid = ghostPrefabReference.ValueRO.PrefabGuid;
            var ghostRootPrefabGuid = ghostPrefabReference.ValueRO.PrefabRootGuid;
            if (ghostPrefabGuid.IsValid) {
                var ghostGameObjectPrefab = prefabSystem.GetGameObjectPrefab(ghostPrefabGuid);
                if (ghostGameObjectPrefab != null) {
                    var ghostPrefab = ghostGameObjectPrefab.Asset as GameObject;
                    var instance = Object.Instantiate(ghostPrefab, localTransform.ValueRO.Position,
                        localTransform.ValueRO.Rotation);
#if UNITY_EDITOR || DEBUG
                    instance.name = $"{(isClient ? "[Client]" : "[Server]")} {ghostPrefab.name}";
#endif
                    // 同一个预制件能在客户端和服务端各生成一份。父节点挂到对应的场景树，退局和查看层级时好区分。
                    instance.transform.parent = isClient
                        ? GhostBridgeBootstrap.Instance.ClientGameObjectHierarchy.transform
                        : GhostBridgeBootstrap.Instance.ServerGameObjectHierarchy.transform;
                    if (localTransform.ValueRO.Scale != 1.0f)
                        instance.transform.localScale = new Vector3(localTransform.ValueRO.Scale,
                            localTransform.ValueRO.Scale, localTransform.ValueRO.Scale);
                    var ghostGameObject = instance.GetComponent<GhostGameObject>();
                    ghostGameObject.SetPrefabAssetGuid(ghostPrefabGuid, ghostRootPrefabGuid);
                    ghostGameObject.SetGuid(ghostGuid.ValueRO.Guid);
                    // 列表下标是 ECS 实体与场景 Transform 之间的快速索引。登记时连两份列表一起加，之后批量读坐标能一一对应。
                    ghostGuid.ValueRW.LocalGhostIndex = m_GhostGameObjectList.Count;
#if UNITY_EDITOR || DEBUG
                    instance.name += $" [{ghostGuid.ValueRO.Guid}]";
                    if (m_GhostGameObjects.TryGetValue(ghostGuid.ValueRO.Guid, out var matchingGhost))
                        Debug.LogError(
                            $"Trying to add {ghostGameObject.gameObject.name} but it has the same GUID as {matchingGhost.gameObject.name}");
#endif
#if VERBOSE_GHOST_LIFETIME_EVENTS
                        Debug.Log($"Spawning {instance.name} with local index {ghostGuid.ValueRO.LocalGhostIndex}");
#endif
                    m_GhostGameObjects.Add(ghostGuid.ValueRO.Guid, ghostGameObject);
                    m_GhostGameObjectList.Add(ghostGameObject);
                    m_GhostEntityList.Add(entity);
                    m_RebuildGhostGameObjectTransformAccessArray = true;
                    if (isServer) {
                        // 服务端仍需要角色的碰撞和玩法脚本。视觉渲染和动画在这里关掉，再按脚本声明的端去掉其余客户端组件。
                        instance.GetComponentsInChildren(true, m_RetrievedRenderers);
                        foreach (var renderer in m_RetrievedRenderers) renderer.enabled = false;
                        instance.GetComponentsInChildren(m_RetrievedAnimators);
                        foreach (var animator in m_RetrievedAnimators) animator.enabled = false;
#if WWISE_AUDIO_SUPPORTED
                            instance.GetComponentsInChildren(m_RetrievedAkGameObjs);
                            foreach (var akGameObj in m_RetrievedAkGameObjs)
                            {
                                GameObject.Destroy(akGameObj);
                            }
#endif
                        instance.GetComponentsInChildren(m_RetrievedClientOnlyComponents);
                        foreach (var clientOnlyComponent in m_RetrievedClientOnlyComponents)
                            if (clientOnlyComponent != null)
                                StripComponent(clientOnlyComponent, (MonoBehaviour)clientOnlyComponent);
                    } else {
                        instance.GetComponentsInChildren(m_RetrievedServerOnlyComponents);
                        foreach (var serverOnlyComponent in m_RetrievedServerOnlyComponents)
                            if (serverOnlyComponent != null)
                                StripComponent(serverOnlyComponent, (MonoBehaviour)serverOnlyComponent);
                    }
                    ecb.AddComponent(entity, new GhostGameObjectLink { LinkedInstance = ghostGameObject });
                    ecb.AddComponent(entity, new GhostGameObjectDeferredActivation());
                    m_NumDeferredGhosts++;
                    // 先保留实例和链接，等依赖满足再激活。这样对象的玩法脚本开始运行时，能查到它需要的管理器和网络数据。
                    instance.SetActive(false);
                } else if (ghostGameObjectPrefab == null) {
                    Debug.LogError(
                        $"[GHOSTGAMEOBJECTLIFETIMESYSTEM]: Prefab {ghostPrefabGuid.ToString()} is not registered with GhostEntityPrefabSystem");
                } else {
                    Debug.Log($"[GHOSTGAMEOBJECTLIFETIMESYSTEM]: Prefab {ghostPrefabGuid.ToString()} is still loading");
                }
            } else {
                Debug.LogError(
                    $"[GHOSTGAMEOBJECTLIFETIMESYSTEM]: Wanted to spawn a ghost but it doesn't have a valid prefab reference");
            }
        }

        // 按下面的依赖次序处理待激活 Ghost。
        // 服务端的次序如下。
        // 第一批是管理器。
        // 第二批是其他对象。
        // 客户端的次序如下。
        // 第一批是管理器。
        // 第二批是本机玩家的 Ghost。
        // 第三批是剩余对象。
        if (m_NumDeferredGhosts > 0) {
            int numExpectedManagers = ManagerGhostsSpawner.TryGetInstance(out var managerSpawner)
                ? managerSpawner.ManagersToSpawn.Count
                : 0;
            int numExpectedLocalPlayers = isClient ? 1 : 0;
            var deferredGhostsQuery = SystemAPI.QueryBuilder()
                .WithAll<GhostGameObjectGuid, GhostGameObjectLink, GhostGameObjectDeferredActivation>().Build();

            // 每次处理这一帧已经出现的待激活对象，
            // 先逐帧接好管理器和玩家，依赖满足后再放行其余对象。
            foreach (var entity in deferredGhostsQuery.ToEntityArray(Allocator.Temp)) {
                var ghostGuid = SystemAPI.GetComponent<GhostGameObjectGuid>(entity);
                var link = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                var ghostGameObject = link.LinkedInstance;

                // 若另一系统已在这一帧销毁场景物体，就跳过它。
                if (ghostGameObject == null) {
                    m_NumDeferredGhosts--;
                    ecb.RemoveComponent<GhostGameObjectDeferredActivation>(entity); // 移除已经无对象可激活的等待标记。
                    continue;
                }
                // 阶段由实例用途和当前 World 决定。每个端先建立管理器，客户端再识别由本机网络编号控制的角色。
                bool isManager = ghostGameObject.TryGetComponent<IGhostManager>(out _);
                var role = isServer ? MultiplayerRole.Server : MultiplayerRole.ClientProxy;
                if (isClient && SystemAPI.HasComponent<GhostOwner>(entity))
                    if (SystemAPI.GetComponent<GhostOwner>(entity).NetworkId == networkId)
                        role = MultiplayerRole.ClientOwned;
                // 管理器先绑定，客户端再等服务端已绑定的标记。本地角色准备好之后，剩余物体才开始使用这个 World 的桥接对象。
                bool allowedToActivate = false;
                if (isServer) { allowedToActivate = isManager || m_NumManagersCreated >= numExpectedManagers; } else {
                    // isClient
                    // 实体被同步到客户端还可能只是服务器刚建出来的壳。等服务端标记完成，再按依赖顺序让客户端表现开始工作。
                    if (ghostGuid.ServerLinked) {
                        bool isLocalPlayer = role == MultiplayerRole.ClientOwned &&
                                             SystemAPI.HasComponent<PredictedPlayerGhost>(entity);
                        allowedToActivate = isManager ||
                                            (m_NumManagersCreated >= numExpectedManagers && isLocalPlayer) ||
                                            (m_NumManagersCreated >= numExpectedManagers &&
                                             m_NumLocalPlayersCreated >= numExpectedLocalPlayers);
                    }
                }
                if (allowedToActivate)
                    try {
                        if (isManager)
                            m_NumManagersCreated++;
                        else if (role == MultiplayerRole.ClientOwned &&
                                 SystemAPI.HasComponent<PredictedPlayerGhost>(entity))
                            m_NumLocalPlayersCreated++;
                        ghostGameObject.LinkGhost(World, entity, role);
                        m_NumDeferredGhosts--;
                        OnGhostPrefabSpawned?.Invoke(ghostGameObject.Guid, ghostGameObject, this);
                        // 绑定成功后移除等待标签，换成活动标记。同一对象下一帧就不会再进一次绑定循环。
                        ecb.RemoveComponent<GhostGameObjectDeferredActivation>(entity);
                        ecb.AddComponent<GhostGameObjectActive>(entity);
                        if (ghostGameObject.ActivateOnLinked) ghostGameObject.gameObject.SetActive(true);
                    } catch (Exception e) {
                        Debug.LogError(
                            $"[GHOSTGAMEOBJECTLIFETIMESYSTEM] Link And Activate Ghost failed on '{ghostGameObject.name}'. Destroying entity. Exception: {e.Message}\\r\\n{e.StackTrace}");
                        ecb.DestroyEntity(entity); // 删除绑定出错的实体，避免后面的帧不断重试报错。
                        m_NumDeferredGhosts--;
                    }
            }
        }
        var parentSetupQuery = SystemAPI.QueryBuilder()
            .WithAll<GhostGameObjectParentSetup, GhostGameObjectLink, GhostGameObjectActive>().Build();
        foreach (var entity in parentSetupQuery.ToEntityArray(Allocator.Temp)) {
            var link = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);

            // 先检查对象是否仍可用，避免出现 MissingReferenceException。
            // C# 包装引用和 Unity 底层物体都要确认仍有效。
            if (link.LinkedInstance != null && link.LinkedInstance.gameObject != null)
                link.LinkedInstance.UpdateParentReference();
            else
                // 还没接好父物体就已销毁时，直接清理等待组件。
                ecb.RemoveComponent<GhostGameObjectParentSetup>(entity);
        }
        ecb.Playback(EntityManager);
        ecb.Dispose();
        if (m_RebuildGhostGameObjectTransformAccessArray) EnsureTransformAccessArrayUpToDate();
    }

    private void CompleteTransformJobs() {
        // 更新索引、采纳预测对象或回收场景物体前，先取得本 World 内读写 Transform 的系统并等作业结束。
        m_ClientTransformSystem ??= World.GetExistingSystemManaged<ClientGhostTransformApplySystem>();
        m_ServerTransformSystem ??= World.GetExistingSystemManaged<ServerGhostTransformRetrieveSystem>();
        CompleteKnownTransformJobs();
    }

    private void CompleteKnownTransformJobs() {
        m_ClientTransformSystem?.ApplyTransformsJobHandle.Complete();
        m_ServerTransformSystem?.CompleteTransformRead();
    }

    public void EnsureTransformAccessArrayUpToDate() {
        // 对象增删才需要重建批量访问数组。平时复用上次的数组，省下每帧重新收集一遍 Transform 的开销。
        if (!m_RebuildGhostGameObjectTransformAccessArray) return;
        PostUpdateRemoveStaleGhostGameObjectsFromList();
        RebuildGhostGameObjectTransformAccessArray();
    }

    private void RebuildGhostGameObjectTransformAccessArray() {
        var transforms = new Transform[m_GhostGameObjectList.Count];
        for (int i = 0; i < m_GhostGameObjectList.Count; i++) transforms[i] = m_GhostGameObjectList[i].transform;
        if (m_TransformAccessArray.isCreated)
            m_TransformAccessArray.SetTransforms(transforms);
        else
            m_TransformAccessArray = new TransformAccessArray(transforms);
        m_RebuildGhostGameObjectTransformAccessArray = false;
    }

    // 把本机预测生成的表现对象登记到正式 Ghost 的生命周期表。
    // 本机可能在服务端确认之前就摆出了投射物外观。认领时把原外观登记进正式 Ghost 表，保持同一只物体继续运动。
    public void AdoptPredictedGhost(Entity entity, GhostGameObject instance) {
        CompleteTransformJobs();
        var guid = EntityManager.GetComponentData<GhostGameObjectGuid>(entity);
        var prefab = EntityManager.GetComponentData<GhostGameObjectPrefabReference>(entity);
        instance.SetGuid(guid.Guid);
        instance.SetPrefabAssetGuid(prefab.PrefabGuid, prefab.PrefabRootGuid);
        guid.LocalGhostIndex = m_GhostGameObjectList.Count;
        EntityManager.SetComponentData(entity, guid);
        m_GhostGameObjects.Add(guid.Guid, instance);
        m_GhostGameObjectList.Add(instance);
        m_GhostEntityList.Add(entity);
        m_RebuildGhostGameObjectTransformAccessArray = true;
    }

    private void StripComponent<T>(T componentInterface, MonoBehaviour component) where T : IStripComponent {
        switch (componentInterface.StripBehaviour) {
            // 脚本可以要求只停用、移除该脚本或移除整个子物体。服务端和客户端用同一套规则裁剪各自不需要的分支。
            case ComponentStripBehaviour.DisableComponent: component.enabled = false; break;
            case ComponentStripBehaviour.DestroyComponent: GameObject.DestroyImmediate(component); break;
            case ComponentStripBehaviour.DestroyGameObject: GameObject.DestroyImmediate(component.gameObject); break;
        }
    }

    public bool IsSpawningManagers() {
        return ManagerGhostsSpawner.TryGetInstance(out var managersSpawner) &&
               managersSpawner.ManagersToSpawn.Count > m_NumManagersCreated;
    }

    public GhostGameObject GetGhostGameObjectByGuid(Hash128 guid) {
        if (m_GhostGameObjects.TryGetValue(guid, out var go)) return go;
        return null;
    }

    public bool DoesGhostExistOfType(GhostSpawner.GhostReference prefabType) {
        foreach (var ghost in m_GhostGameObjects.Values)
            if (ghost != null && ghost.PrefabAssetGuid == prefabType.GhostGuid)
                return true;
        return false;
    }

    public bool TryFindGhostByPartialName(string partialName, out GhostGameObject ghostGameObject) {
        ghostGameObject = null;
        foreach (var ghost in m_GhostGameObjects.Values)
            if (ghost != null && ghost.name.Contains(partialName)) {
                if (ghostGameObject == null) { ghostGameObject = ghost; } else {
                    // 这个名字已经找到第一个匹配对象，
                    // 有多个匹配时输出提示，让调用者改用更精确的名字。
                    Debug.LogWarning(
                        $"Detected multiple ghosts when looking for partial name '{partialName}'. Not returning a match");
                    ghostGameObject = null;
                    return false;
                }
            }
        return ghostGameObject != null;
    }

    public bool TryGetGhostGameObjectByGuid(Hash128 guid, out GhostGameObject ghostGameObject) {
        if (m_GhostGameObjects.TryGetValue(guid, out ghostGameObject)) return ghostGameObject != null;
        return false;
    }

    public void OnGhostGameObjectDestroyed(Hash128 guid) {
        CompleteTransformJobs();
#if VERBOSE_GHOST_LIFETIME_EVENTS
        Debug.Log($"{World.Name} OnGhostGameObjectDestroyed {guid}");
#endif
        // 销毁时先让 GUID 查找失效，并给顺序列表留空位。真正压缩和重算下标等到安全阶段集中做。
        m_GhostGameObjects.Remove(guid);
        for (int i = 0; i < m_GhostGameObjectList.Count; i++)
            if (m_GhostGameObjectList[i] != null && m_GhostGameObjectList[i].Guid == guid) {
                m_GhostGameObjectList[i] = null;
                m_GhostEntityList[i] = Entity.Null;
                break;
            }
        m_RebuildGhostGameObjectTransformAccessArray = true;
    }

    public void PostUpdateRemoveStaleGhostGameObjectsFromList() {
        if (!m_RebuildGhostGameObjectTransformAccessArray && m_GhostGameObjects.Count == m_GhostGameObjectList.Count)
            // 索引列表还完整，
            // 这轮可以直接跳过压缩。
            return;
        CompleteTransformJobs();
        // 场景物体虽然还在等 LinkGhost，表里存的实体仍有效。
        // 下一次 Transform 作业前一起压缩三个按下标对应的集合。
        m_GhostGameObjects.Clear();
        int count = 0;
        for (int i = 0; i < m_GhostGameObjectList.Count; i++) {
            var ghost = m_GhostGameObjectList[i];
            var entity = m_GhostEntityList[i];
            if (ghost == null || !EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<GhostGameObjectGuid>(entity))
                continue;
            // 把有效实体和场景物体一起往前压，重写它们的下标。否则列表中间出现空洞后，读坐标作业可能取到另一个对象。
            var guid = EntityManager.GetComponentData<GhostGameObjectGuid>(entity);
            guid.LocalGhostIndex = count;
            EntityManager.SetComponentData(entity, guid);
            m_GhostGameObjectList[count] = ghost;
            m_GhostEntityList[count] = entity;
            m_GhostGameObjects.Add(guid.Guid, ghost);
            count++;
        }
        m_GhostGameObjectList.RemoveRange(count, m_GhostGameObjectList.Count - count);
        m_GhostEntityList.ResizeUninitialized(count);
        m_RebuildGhostGameObjectTransformAccessArray = true;
    }

#if !NGS_SUBMISSION_BUILD
    private static void LogToConsoleAndDebug(string msg) {
        Debug.Log(msg);
        Console.Write(msg);
    }

    private static void LogDormacy(List<GhostGameObject> ghostList, World world, bool logAwakeGhosts = false) {
        Type[] types;
        if (world == ClientServerBootstrap.ServerWorld)
            types = new Type[] {
                typeof(IEarlyUpdateServer),
                typeof(IUpdateServer)
            };
        else
            types = new Type[] {
                typeof(IUpdateClient),
                typeof(ILateUpdateClient)
            };
        int numDormant = 0;
        foreach (var ghost in ghostList)
            if (ghost == null || ghost.Dormant) {
                // 下面的休眠统计会
                // 把待销毁的 Ghost
                // 也计入休眠对象。
                numDormant++;
            } else {
                string detailsLog = "";
                // 对象还醒着，检查它有没有注册需要执行的更新。
                bool willUpdate = false;
                foreach (var type in types)
                    if (ghost.IsUpdateRegisteredForBehaviour(type, out var updates)) {
                        if (logAwakeGhosts) {
                            if (!willUpdate)
                                // 遇到首个已注册的更新时收集详细信息。
                                detailsLog += $"<b>{ghost.name}</b>\n";
                            // if (DormancyManager.TryGetInstanceFromWorld(world, out var dormanyManager))
                            // {
                            //     if (dormanyManager.IsGhostRegisteredForDormancy(ghost, out var registeredType))
                            //     {
                            //         detailsLog += $" -- Dormancy check: <color=\"#8EBDFF\">{registeredType}</color> but currently AWAKE\n";
                            //     }
                            //     else
                            //     {
                            //         detailsLog += $" -- <color=\"red\">NOT registered for dormancy</color>\n";
                            //     }
                            // }
                            foreach (object update in updates)
                                detailsLog += $"   -- {update.GetType().Name}.{type.Name}\n";
                        }
                        willUpdate = true;
                    }
                if (!willUpdate)
                    numDormant++;
                else if (logAwakeGhosts) Debug.Log(detailsLog);
            }
        LogToConsoleAndDebug($" - Dormant {numDormant}");
        LogToConsoleAndDebug($" - Awake {ghostList.Count - numDormant}");
    }

    [UnityEngine.Scripting.Preserve]
    public static void LogGhostStats(string[] args) {
        if (args.Length == 0 || (args.Length == 1 && args[0] == "details")) {
            LogGhostStats(new string[] {
                "server",
                args.Length == 1 ? args[0] : ""
            });
            LogGhostStats(new string[] {
                "client",
                args.Length == 1 ? args[0] : ""
            });
        } else {
            var world = ClientServerBootstrap.ServerWorld;
            if (args[0] == "client") world = ClientServerBootstrap.ClientWorld;
            if (world != null) {
                var lifetimeSystem = Instance(world);
                var ghostList = lifetimeSystem.GhostGameObjectList;
                LogToConsoleAndDebug($"{world.Name} has {ghostList.Count} ghosts");
                LogDormacy(ghostList, world, args.Length == 2 && args[1] == "details");
            }
        }
    }

    [UnityEngine.Scripting.Preserve]
    public static void SpawnGhost(string[] args) {
        if (args.Length == 0) return;
        if (ClientServerBootstrap.ServerWorld == null) {
            Debug.LogWarning("The command 'spawn' is only valid on the host");
            return;
        }
        if (args.Length <= 2) // spawn <object> [optional <count>]
        {
            // if (PlayerGhostManager.TryGetServerInstance(out var playerGhostManager))
            // {
            //     const float forwardOffset = 0.5f;
            //
            //     var players = playerGhostManager.GetPlayersByRole(MultiplayerRole.Server);
            //     if (players.Count > 0)
            //     {
            //         var spawnPos = players[0].transform.position;
            //
            //         // 生成位置略微上抬，让物件能自然落下。
            //         float heightOffset = 1f;
            //         spawnPos += heightOffset * Vector3.up;
            //
            //         // 再向玩家前方偏一点。
            //         spawnPos += forwardOffset * players[0].transform.forward;
            //
            //         int count = 1;
            //         if (args.Length == 2 && Int32.TryParse(args[1], out var parsedCount))
            //         {
            //             count = parsedCount;
            //         }
            //         SpawnGhostAtPosition(args[0], spawnPos, count);
            //     }
            // }
        }

        // 可以按指定位置生成，例如
        // 三个参数的写法为 spawn wood 10 23,45,65,90。
        else if (args.Length >= 3) {
            string spawnObject = args[0];
            int count = 1;
            if (int.TryParse(args[1], out int parsedCount)) count = parsedCount;

            // 把剩余参数拼成一段，
            // 这样就能去掉坐标之间的空白。
            string wholeString = "";
            for (int i = 0; i < args.Length - 2; i++) wholeString += args[2 + i];
            string[] stringParams = wholeString.Split(',');
            float[] spawnParams = new float[4];
            var spawnPos = new Vector3(spawnParams[0], spawnParams[1], spawnParams[2]);
            float yaw = 0f;
            if (spawnParams.Length == 4) yaw = spawnParams[3];
            SpawnGhostAtPosition(spawnObject, spawnPos, count, yaw);
        }
    }

    private static void SpawnGhostAtPosition(string spawnObjectName, Vector3 spawnPos, int count, float yaw = 0f) {
        if (GhostEntityPrefabSystem.ServerInstance.TryFindPrefabByPartialName(spawnObjectName, out var prefab)) {
            var spawnRot = Quaternion.Euler(0f, yaw, 0f);
            const float stackOffset = 0.2f;
            for (int i = 0; i < count; i++)
                GhostSpawner.SpawnGhostPrefab(prefab, spawnPos + Vector3.up * i * stackOffset, spawnRot,
                    GhostGameObject.GenerateRandomHash());
        } else { Debug.LogWarning($"Could not find ghost prefab from the string {spawnObjectName}"); }
    }
#endif
}