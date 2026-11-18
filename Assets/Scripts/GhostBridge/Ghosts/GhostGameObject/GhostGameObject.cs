// 把 NetCode 的 Ghost 实体与场景里的 GameObject 连起来。实体存网络数据，角色、地图设备等脚本通过这个对象取数据、挂更新和收发消息。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public enum MultiplayerRole {
    Server,
    ClientProxy,
    ClientOwned,

    // 这是用于查询的角色键，
    // 调用者可以用这个键查找
    // 全部客户端对象，包括本机对象和远端代理。
    ClientAll
};

// 这里的 GUID 让两个 World 里的对应对象能互相认出。LocalGhostIndex 只索引本端表，不用跨网络保持同一个下标。
public struct GhostGameObjectGuid : IComponentData {
    [GhostField] public Hash128 Guid;
    [GhostField] public bool ServerLinked;
    [GhostField] public Hash128 ParentGuid;
    [GhostField] public bool ParentToMovingBase;
    public int LocalGhostIndex;
}

// 实体和场景对象之间的位置同步用这份临时数据。切换回来时可先保存一个位置偏差，再慢慢把它消掉。
public struct GhostGameObjectTransformSync : IComponentData {
    public bool DisableTransformSync;

    // 平滑消除的位置偏差。
    public float ErrorTriggeredTime;
    public float3 ErrorOffset;
    public float ErrorBlendTime;
}

public struct GhostGameObjectParentSetup : IComponentData {
}

public struct RequireMovementContextCalculation : IComponentData {
}

public interface IGhostGameObjectDirectedRPC {
}

public class GhostGameObject : MonoBehaviour {
    public struct MovementContext {
        public float MinDistSqrdFromAPlayer;
        public float3 Velocity;
        public float VelocitySqrd;
    }

    // 这个字段只在本地使用。
    public Hash128 Guid { get; private set; }
    public Hash128 PrefabAssetGuid { get; private set; }
    public Hash128 RootPrefabAssetGuid { get; private set; }
    public bool ActivateOnLinked { get; set; } = true;
    public bool Dormant { get; set; } = false;
    public bool RequireMovementContextCalculation { get; set; } = false;
    private MovementContext m_MovementContext;
    public float3 Velocity => m_MovementContext.Velocity;
    public float VelocitySqrd => m_MovementContext.VelocitySqrd;
    public float MinDistSqrdFromAPlayer => m_MovementContext.MinDistSqrdFromAPlayer;
    public bool IsPendingDestroy => m_PendingDestroy;

    // 父与子 Ghost。
    public GhostGameObject Parent { get; private set; }

    public List<GhostGameObject>
        Children { get; private set; } // 初值为空，有子对象时才分配列表。

    [field: SerializeField] public bool RequireTransformSync { get; set; } = false;

    [field: SerializeField]
    public GhostGameObjectUpdateGroup UpdateGroup { get; set; } = GhostGameObjectUpdateGroup.Default;

    [SerializeField] private bool logDestroyBeforeLinked = false;
#pragma warning disable UDR0001
#pragma warning disable UDR0002
    // 静态变量的生命周期由 GhostGameObjectUpdateSystem
    // 和 GhostGameObjectDestroySystem 管理。
    public static EntityCommandBuffer UpdateEntityCommandBuffer;
    public static IGhostGameObjectUpdateSystem UpdateInterface;
    public static Type UpdateInterfaceType;
#pragma warning restore UDR0001
#pragma warning restore UDR0002
    private MultiplayerRole m_Role = MultiplayerRole.ClientOwned;

    public MultiplayerRole Role {
        get {
            if (m_Role == MultiplayerRole.Server || m_World == null || !m_World.IsCreated ||
                !m_World.EntityManager.Exists(m_LinkedEntity))
                return m_Role;
            if (m_World.EntityManager.HasComponent<GhostOwnerIsLocal>(m_LinkedEntity))
                return m_World.EntityManager.IsComponentEnabled<GhostOwnerIsLocal>(m_LinkedEntity)
                    ? MultiplayerRole.ClientOwned
                    : MultiplayerRole.ClientProxy;
            return m_Role;
        }
    }

    private World m_World;
    public World World => m_World;
    public EntityManager EntityManager => m_World.EntityManager;
    private Entity m_LinkedEntity;
    public Entity LinkedEntity => m_LinkedEntity;
    private bool m_PendingDestroy;
    private int m_OwningNetworkId;
    private List<GhostMonoBehaviour> m_GhostMonoBehaviours = new();

    public int Owner {
        get {
            if (m_World != null && m_World.IsCreated && m_World.EntityManager.Exists(m_LinkedEntity) &&
                m_World.EntityManager.HasComponent<GhostOwner>(m_LinkedEntity))
                m_OwningNetworkId = m_World.EntityManager.GetComponentData<GhostOwner>(m_LinkedEntity).NetworkId;
            return m_OwningNetworkId;
        }
    }

    private Dictionary<Type, IEnumerable> m_UpdateGhostMonoBehavioursByType = new();
    private int m_UpdateBehaviourIndex;
    private bool m_GhostTransformUpdatesEnabled = true;
    public bool GhostTransformUpdatesEnabled => m_GhostTransformUpdatesEnabled;
    private bool m_GhostPreDestroyCalled;
    public bool GhostPreDestroyCalled { get => m_GhostPreDestroyCalled; set => m_GhostPreDestroyCalled = value; }
    private List<IUpdateServer> m_CachedUpdateServerBehaviours = null;
    private List<IEarlyUpdateServer> m_CachedEarlyUpdateServerBehaviours = null;
    private List<IUpdateClient> m_CachedEarlyUpdateClientBehaviours = null;
    private List<IUpdateClient> m_CachedUpdateClientBehaviours = null;
    private List<IPhysicsUpdateServer> m_CachedPhysicsUpdateServerBehaviours = null;
    private List<ILateUpdateClient> m_CachedLateUpdateClientBehaviours = null;
    public void SetGuid(Hash128 guid) { Guid = guid; }

    public void SetRequireMovementContextCalculation() {
        if (!RequireMovementContextCalculation) {
            EntityManager.AddComponent<RequireMovementContextCalculation>(LinkedEntity);
            RequireMovementContextCalculation = true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetMovementContext(MovementContext context) { m_MovementContext = context; }

    public void SetPrefabAssetGuid(Hash128 guid, Hash128 rootGuid = default) {
        PrefabAssetGuid = guid;
        RootPrefabAssetGuid = rootGuid;
    }

    public static Hash128 GenerateRandomHash() {
        var hash128 = new UnityEngine.Hash128();
        hash128.Append(System.Guid.NewGuid().ToString());
        return (Hash128)hash128;
    }

    public static Hash128 GenerateNewHash(GameObject go, int uniqueCode = 0) {
        string hash = go.scene.name;
        if (go.transform.parent != null) hash += go.transform.parent.name;
        hash += go.name;
        hash += uniqueCode;
        var hash128 = new UnityEngine.Hash128();
        hash128.Append(hash);
        return hash128;
    }

    public static Hash128 GenerateNewHashOnNameDepthAndSiblingIndex(GameObject go) {
        int depth = 0;
        var parent = go.transform;
        while (parent.transform.parent != null) {
            parent = parent.transform.parent;
            depth++;
        }
        string hash = $"{go.name}_{depth}_{go.transform.GetSiblingIndex()}";
        var hash128 = new UnityEngine.Hash128();
        hash128.Append(hash);
        return hash128;
    }

    public static bool BroadClientServerRolesMatch(MultiplayerRole roleA, MultiplayerRole roleB) {
        if (roleA == roleB)
            return true;
        else if ((roleA == MultiplayerRole.ClientOwned && roleB == MultiplayerRole.ClientProxy) ||
                 (roleA == MultiplayerRole.ClientProxy && roleB == MultiplayerRole.ClientOwned))
            return true;
        else
            return false;
    }

    public void MarkAsPendingDestroy() { m_PendingDestroy = true; }
#if WWISE_AUDIO_SUPPORTED
    private static List<AkGameObj> s_CollectedAkGameObjs = new();
#endif
    // 绑定时记住 World、实体和当前端的角色，再让子物体上的 GhostMonoBehaviour 拿到这个入口。
    public void LinkGhost(World world, Entity linkedGhostEntity, MultiplayerRole role) {
        m_World = world;
        m_LinkedEntity = linkedGhostEntity;
        m_Role = role;
        // 有所有者的 Ghost 记下连接编号。角色的本机输入、相机和 RPC 路由可以据此区别哪个玩家在控制它。
        if (EntityManager.HasComponent<GhostOwner>(linkedGhostEntity)) {
            // 记下拥有者的连接号。
            var owner = EntityManager.GetComponentData<GhostOwner>(linkedGhostEntity);
            m_OwningNetworkId = owner.NetworkId;
        }
        // 收集对象上的 GhostMonoBehaviour 脚本。
        GetComponentsInChildren(m_GhostMonoBehaviours);
        foreach (var ghostBehaviour in m_GhostMonoBehaviours) ghostBehaviour.SetGhostGameObject(this);
        // 需要位置同步的场景物体才挂桥接组件。服务端从这些组件挑需要回写坐标的实体，客户端据同一个标记更新表现体。
        if (RequireTransformSync) EntityManager.AddComponent<GhostGameObjectTransformSync>(linkedGhostEntity);
        if (role == MultiplayerRole.Server) {
            var ghostGuid = ReadGhostComponentData<GhostGameObjectGuid>();
            // 服务端先完成自己的绑定，再通过 Ghost 字段告诉客户端。客户端生命周期系统会等该标记到达后才发布这个物体。
            ghostGuid.ServerLinked = true;

            // 检查是否指定了父 Ghost。
            // if (GhostHasComponent<ParentGhostSetup>())
            // {
            //     var parentSetup = ReadGhostComponentData<ParentGhostSetup>();
            //     ghostGuid.ParentGuid = parentSetup.ParentGuid;
            //     ghostGuid.ParentToMovingBase = parentSetup.ParentToMovingBase;
            //     if (ghostGuid.ParentGuid.IsValid)
            //     {
            //         EntityManager.AddComponent<GhostGameObjectParentSetup>(linkedGhostEntity);
            //     }
            //     else
            //     {
            //         Debug.LogError($"{name} is spawned with a parent setup but the parent guid is invalid");
            //     }
            // }
            WriteGhostComponentData(ghostGuid);
        } else {
            var ghostGuid = ReadGhostComponentData<GhostGameObjectGuid>();
            if (ghostGuid.ParentGuid.IsValid) EntityManager.AddComponent<GhostGameObjectParentSetup>(linkedGhostEntity);
#if WWISE_AUDIO_SUPPORTED
            if (!RequireTransformSync)
            {
                // 没有位置同步的客户端对象可以按静态物体处理，
                // 位置不变时可以停用其音源位置更新。
                GetComponentsInChildren(s_CollectedAkGameObjs);
                foreach (var obj in s_CollectedAkGameObjs)
                {
                    obj.SetDoesNotNeedAutomaticUpdates();
                }

                s_CollectedAkGameObjs.Clear();
            }
#endif
        }
        OnGhostLinked();
    }

    // 根据客户端或服务器身份，登记实现了对应接口的脚本。之后 ECS 到指定阶段会回调它们。
    public void OnGhostLinked() {
        foreach (var ghostBehaviour in m_GhostMonoBehaviours) {
            if (ghostBehaviour.AllowAutoRegisterForUpdates()) {
                if (Role == MultiplayerRole.Server) {
                    AddBehaviourToUpdates<IEarlyUpdateServer>(ghostBehaviour);
                    AddBehaviourToUpdates<IUpdateServer>(ghostBehaviour);
                    AddBehaviourToUpdates<IPhysicsUpdateServer>(ghostBehaviour);
                } else {
                    AddBehaviourToUpdates<IEarlyUpdateClient>(ghostBehaviour);
                    AddBehaviourToUpdates<IUpdateClient>(ghostBehaviour);
                    AddBehaviourToUpdates<ILateUpdateClient>(ghostBehaviour);
                }
            }
            ghostBehaviour.OnGhostLinked();
        }
    }

    public void UpdateParentReference() {
        var ghostGuid = ReadGhostComponentData<GhostGameObjectGuid>();
        Debug.Assert(ghostGuid.ParentGuid.IsValid);
        Debug.Assert(Parent == null);

        // 找到正确的父 Ghost 后再接上场景层级。
        // 父物体可能比子物体晚到。找到同一 World 中已绑定的父 Ghost 后，再接层级并清掉等待挂接标记。
        if (TryFindWorldGhost(ghostGuid.ParentGuid, out var instance) && instance.IsGhostLinked()) {
            Parent = instance;
            Parent.AddChildGhost(this);
            if (ghostGuid.ParentToMovingBase) {
                // 在父物体的层级中寻找移动底座。
                // var movingBase = Parent.GetComponentInChildren<MovementBase>();
                // if (movingBase)
                // {
                //     transform.parent = movingBase.transform;
                // }
                // else
                // {
                //     Debug.LogError($"{name} wants to be parented to a moving base on {Parent} but it doesn't contain one");
                // }
            } else {
                // 挂到父 Ghost 的根节点上。
                transform.parent = Parent.transform;
            }
            EntityManager.RemoveComponent<GhostGameObjectParentSetup>(m_LinkedEntity);
        }
    }

    public void AddChildGhost(GhostGameObject child) {
        if (Children == null) Children = new List<GhostGameObject>();
        Children.Add(child);
    }

    public bool IsGhostLinked() { return World != null; }

    private List<T> GetUpdateGhostMonoBehavioursByType<T>() where T : IGhostMonoBehaviourUpdate {
        return (List<T>)m_UpdateGhostMonoBehavioursByType[typeof(T)];
    }

    public bool IsUpdateRegisteredForBehaviour<T>(GhostMonoBehaviour ghostBehaviour)
        where T : class, IGhostMonoBehaviourUpdate {
        var type = typeof(T);
        var update = ghostBehaviour as T;
        if (update != null)
            if (m_UpdateGhostMonoBehavioursByType.TryGetValue(type, out var updates))
                return ((List<T>)updates).Contains(update);
        return false;
    }

    public bool IsUpdateRegisteredForBehaviour(Type updateType, out IEnumerable updates) {
        if (m_UpdateGhostMonoBehavioursByType.TryGetValue(updateType, out updates)) {
            int count = 0;
            foreach (object update in updates) count++;
            return count > 0;
        }
        return false;
    }

    public void AddBehaviourToUpdates<T>(GhostMonoBehaviour ghostBehaviour) where T : class, IGhostMonoBehaviourUpdate {
        var type = typeof(T);
        var update = ghostBehaviour as T;
        // 有时只是查询该类更新，返回空值很常见。
        if (update != null) {
            if (!m_UpdateGhostMonoBehavioursByType.ContainsKey(type)) {
                m_UpdateGhostMonoBehavioursByType.Add(type, new List<T>());

                // 首次添加时设置一次缓存列表。
                SetCachedLists<T>();
            }
            var updates = GetUpdateGhostMonoBehavioursByType<T>();
            Debug.Assert(!updates.Contains(update),
                $"Behaviour {ghostBehaviour.GetType().Name} on {ghostBehaviour.name} already registered for updates with {type.Name}");
            updates.Add(update);
            if (updates.Count == 1) {
                var system = GhostGameObjectUpdateSystem.GetGhostUpdateSystemByType<T>();
                system.RegisterForUpdate(this);
            }
        }
    }

    private void SetCachedLists<T>() where T : class, IGhostMonoBehaviourUpdate {
        if (typeof(T) == typeof(IEarlyUpdateServer))
            m_CachedEarlyUpdateServerBehaviours = GetUpdateGhostMonoBehavioursByType<IEarlyUpdateServer>();
        else if (typeof(T) == typeof(IUpdateServer))
            m_CachedUpdateServerBehaviours = GetUpdateGhostMonoBehavioursByType<IUpdateServer>();
        else if (typeof(T) == typeof(IUpdateClient))
            m_CachedUpdateClientBehaviours = GetUpdateGhostMonoBehavioursByType<IUpdateClient>();
        else if (typeof(T) == typeof(IPhysicsUpdateServer))
            m_CachedPhysicsUpdateServerBehaviours = GetUpdateGhostMonoBehavioursByType<IPhysicsUpdateServer>();
        else if (typeof(T) == typeof(ILateUpdateClient))
            m_CachedLateUpdateClientBehaviours = GetUpdateGhostMonoBehavioursByType<ILateUpdateClient>();
    }

    public void RemoveBehaviourFromUpdates<T>(GhostMonoBehaviour ghostBehaviour)
        where T : class, IGhostMonoBehaviourUpdate {
        var type = typeof(T);
        var update = ghostBehaviour as T;
        // 有时只是查询该类更新，返回空值很常见。
        if (update != null) {
            var updates = GetUpdateGhostMonoBehavioursByType<T>();
            int index = updates.IndexOf(update);
            if (index >= 0) {
                updates.RemoveAtSwapBack(index);
                if (UpdateInterfaceType == type && index == m_UpdateBehaviourIndex)
                    // 当前正在更新的对象被移除了。
                    // 把索引减一，让下一个循环处理
                    // 交换到空位上的另一个对象。
                    m_UpdateBehaviourIndex--;
                if (updates.Count == 0) {
                    var system = GhostGameObjectUpdateSystem.GetGhostUpdateSystemByType<T>();
                    system.Unregister(this);
                }
            } else {
                Debug.LogWarning(
                    $"Trying to unregister {ghostBehaviour.name} {gameObject.name} from updates for type {type.Name} but it was never registered");
            }
        }
    }

    private void UnregisterAllUpdates<T>() where T : class, IGhostMonoBehaviourUpdate {
        var type = typeof(T);
        if (m_UpdateGhostMonoBehavioursByType.ContainsKey(type)) {
            var updates = GetUpdateGhostMonoBehavioursByType<T>();
            if (updates.Count > 0) {
                var system = GhostGameObjectUpdateSystem.GetGhostUpdateSystemByType<T>();
                system.Unregister(this);
            }
            updates.Clear();
        }
    }

    public void OnDestroy() {
        if (!m_GhostPreDestroyCalled) OnGhostPreDestroy();
    }

    public void OnGhostPreDestroy() {
        // 销毁以前先解开子 Ghost 的 Transform。子物体由它自己的实体决定何时结束，这一步避免 Unity 随父物体一起删除它。
        m_GhostPreDestroyCalled = true;

        // 解除所有子 Ghost 的父节点关系。
        if (Children != null)
            foreach (var child in Children)
                if (child != null)
                    // 将子物体从当前节点取出，
                    // 这样父 Ghost 被销毁时，
                    // 子 Ghost 还能活到自己的退场时机。
                    child.transform.parent = null;
        if (m_GhostMonoBehaviours != null) {
            // 把销毁通知分发给每个脚本，让它们释放资源和订阅。之后注销周期回调，下一帧更新就不会再碰到已销毁的对象。
            foreach (var ghostBehaviour in m_GhostMonoBehaviours) ghostBehaviour.OnGhostPreDestroy();
            if (Role == MultiplayerRole.Server) {
                UnregisterAllUpdates<IEarlyUpdateServer>();
                UnregisterAllUpdates<IUpdateServer>();
                UnregisterAllUpdates<IPhysicsUpdateServer>();
            } else {
                UnregisterAllUpdates<IUpdateClient>();
                UnregisterAllUpdates<ILateUpdateClient>();
            }
        } else {
            if (!logDestroyBeforeLinked) return;
            Debug.Log($"{name} being destroyed before it has been linked. This is handled");
        }
    }

    public void EarlyUpdateServer(float deltaTime) {
        Debug.Assert(m_CachedEarlyUpdateServerBehaviours != null && m_CachedEarlyUpdateServerBehaviours.Count > 0,
            "A ghost game object shouldn't be updated if there are no behaviours registered to update");
        var updates = m_CachedEarlyUpdateServerBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++) {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.EarlyUpdateServer(deltaTime);
        }
        m_UpdateBehaviourIndex = -1;
    }

    public void UpdateServer(float deltaTime) {
        Debug.Assert(m_CachedUpdateServerBehaviours != null && m_CachedUpdateServerBehaviours.Count > 0,
            "A ghost game object shouldn't be updated if there are no behaviours registered to update");
        var updates = m_CachedUpdateServerBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++) {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.UpdateServer(deltaTime);
        }
        m_UpdateBehaviourIndex = -1;
    }

    public void PhysicsUpdateServer(float deltaTime) {
        Debug.Assert(m_CachedPhysicsUpdateServerBehaviours != null && m_CachedPhysicsUpdateServerBehaviours.Count > 0,
            "A ghost game object shouldn't be updated if there are no behaviours registered to update");
        var updates = m_CachedPhysicsUpdateServerBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++) {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.PhysicsUpdateServer(deltaTime);
        }
        m_UpdateBehaviourIndex = -1;
    }

    public void EarlyUpdateClient(float deltaTime) {
        Debug.Assert(m_CachedUpdateClientBehaviours != null && m_CachedUpdateClientBehaviours.Count > 0,
            "A ghost game object shouldn't be updated if there are no behaviours registered to update");
        var updates = m_CachedEarlyUpdateClientBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++) {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.UpdateClient(deltaTime);
        }
        m_UpdateBehaviourIndex = -1;
    }

    public void UpdateClient(float deltaTime) {
        Debug.Assert(m_CachedUpdateClientBehaviours != null && m_CachedUpdateClientBehaviours.Count > 0,
            "A ghost game object shouldn't be updated if there are no behaviours registered to update");
        var updates = m_CachedUpdateClientBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++) {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.UpdateClient(deltaTime);
        }
        m_UpdateBehaviourIndex = -1;
    }

    public void LateUpdateClient(float deltaTime) {
        Debug.Assert(m_CachedLateUpdateClientBehaviours != null && m_CachedLateUpdateClientBehaviours.Count > 0,
            "A ghost game object shouldn't be updated if there are no behaviours registered to update");
        var updates = m_CachedLateUpdateClientBehaviours;
        for (m_UpdateBehaviourIndex = 0; m_UpdateBehaviourIndex < updates.Count; m_UpdateBehaviourIndex++) {
            var ghostBehaviour = updates[m_UpdateBehaviourIndex];
            ghostBehaviour.LateUpdateClient(deltaTime);
        }
        m_UpdateBehaviourIndex = -1;
    }

    public void BroadcastRPC<T>(T rpcCommand) where T : unmanaged, IRpcCommand {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "BroadcastRPC is only valid during an update");
        if (Role == MultiplayerRole.Server) {
            // RPC 通过临时实体交给 NetCode。服务器广播时给它放入消息数据和广播请求，真正发包在后面的网络系统中完成。
            var rpcEntity = UpdateEntityCommandBuffer.CreateEntity();
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, (T)rpcCommand);
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, new SendRpcCommandRequest());
        } else { Debug.LogError($"Broadcasting RPCs is only valid on the server"); }
    }

    // 取到一条同类型消息后，先把它从本轮的共享收件列表拿走，再排队销毁 RPC 实体，防止后续脚本重复消费。
    public bool ConsumeRPC<T>(out T rpc) where T : unmanaged, IRpcCommand {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "ConsumeRPC is only valid during an update");
        if (GhostGameObjectRPCReceiveSystem.TryGetInstance(World, out var rpcSystem)) {
            var rpcEntities = rpcSystem.ReceivedRPCEntities;
            for (int i = 0; i < rpcEntities.Length; i++) {
                var rpcEntity = rpcEntities[i];
                if (EntityManager.HasComponent<T>(rpcEntity)) {
                    var typeIndex = TypeManager.GetTypeIndex<T>();
                    if (!TypeManager.IsZeroSized(typeIndex))
                        rpc = EntityManager.GetComponentData<T>(rpcEntity);
                    else
                        rpc = default;
                    UpdateEntityCommandBuffer.DestroyEntity(rpcEntity);
                    rpcEntities.RemoveAtSwapBack(i);
                    return true;
                }
            }
        }
        rpc = default;
        return false;
    }

    // 由反射把当前 Ghost 的 Guid 填进约定字段。消息接收后用这个 Guid 分发到正确的物体，发送请求本身仍走 NetCode。
    public void SendDirectedRPC<T>(T rpcCommand) where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "SendRPC is only valid during an update");

        // 把拥有者的 GUID 写入消息。
        var targetProperty = typeof(T).GetField("TargetGuid");
        if (targetProperty != null) {
            object rpcBoxed = (object)rpcCommand;
            targetProperty.SetValue(rpcBoxed, Guid);
            var rpcEntity = UpdateEntityCommandBuffer.CreateEntity();
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, (T)rpcBoxed);
            UpdateEntityCommandBuffer.AddComponent(rpcEntity, new SendRpcCommandRequest());
        } else {
            Debug.LogError(
                $"Type '{typeof(T).Name} implements IGhostGameObjectDirectedRPC but does not have a TargetGuid field.");
        }
    }

    public bool ConsumeDirectedRPC<T>(out T rpc) where T : unmanaged, IRpcCommand, IGhostGameObjectDirectedRPC {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "ConsumeDirectedRPC is only valid during an update");
        if (GhostGameObjectRPCReceiveSystem.TryGetInstance(World, out var rpcSystem)) {
            var rpcEntities = rpcSystem.ReceivedRPCEntities;
            for (int i = 0; i < rpcEntities.Length; i++) {
                var rpcEntity = rpcEntities[i];
                if (EntityManager.HasComponent<T>(rpcEntity)) {
                    var receivedRPC = EntityManager.GetComponentData<T>(rpcEntity);
                    var targetProperty = typeof(T).GetField("TargetGuid");
                    if (targetProperty != null) {
                        var targetGUID = (Hash128)targetProperty.GetValue(receivedRPC);

                        // 检查这条 RPC 是否发给当前 Ghost。
                        // 收到定向消息要按实例 GUID 取。同一台机器可以有许多相同类型的设备，只有被点到的那个会消费这条消息。
                        if (targetGUID == Guid) {
                            rpc = receivedRPC;
                            UpdateEntityCommandBuffer.DestroyEntity(rpcEntity);
                            rpcEntities.RemoveAtSwapBack(i);
                            return true;
                        }
                    } else {
                        Debug.LogError(
                            $"Type '{typeof(T).Name} implements IGhostGameObjectDirectedRPC but does not have a TargetGuid field.");
                    }
                }
            }
        }
        rpc = default;
        return false;
    }

    // 有些逻辑只想遍历已收到的同类消息。这个方法把数据交给回调，不改收件列表，实际取走消息走 ConsumeRPC。
    public void IterateRPC<T>(Action<T> callback) where T : unmanaged, IRpcCommand {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "IterateRPC is only valid during an update");
        if (GhostGameObjectRPCReceiveSystem.TryGetInstance(World, out var rpcSystem)) {
            var rpcEntities = rpcSystem.ReceivedRPCEntities;
            for (int i = 0; i < rpcEntities.Length; i++) {
                var rpcEntity = rpcEntities[i];
                if (EntityManager.HasComponent<T>(rpcEntity)) {
                    var typeIndex = TypeManager.GetTypeIndex<T>();
                    if (!TypeManager.IsZeroSized(typeIndex))
                        callback.Invoke(EntityManager.GetComponentData<T>(rpcEntity));
                }
            }
        }
    }

    public void EnableGhostTransformUpdates(bool enable, bool interpolateError = false, float interpolationRate = 1f) {
        Debug.Assert(Role != MultiplayerRole.Server, $"EnableGhostTransformUpdates is only valid on client objects");
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated,
            "EnableGhostTransformUpdates is only valid during UpdateClient");
        m_GhostTransformUpdatesEnabled = enable;
        var transformSync = ReadGhostComponentData<GhostGameObjectTransformSync>();
        transformSync.DisableTransformSync = !enable;
        // 本机控制或预测会让画面位置与收到的实体位置短暂不同。重新启用同步时存下当前偏差，表现系统接下来分几帧抹平。
        if (enable && interpolateError) {
            Debug.Assert(EntityManager.HasComponent<LocalTransform>(m_LinkedEntity),
                "[GHOSTGAMEOBJECT] EnableGhostTransformUpdates - ghost doesn't have a LocalTransform component");
            var localTransform = ReadGhostComponentData<LocalTransform>();
            transformSync.ErrorTriggeredTime = Time.time;
            transformSync.ErrorOffset = localTransform.Position - (float3)transform.position;
            transformSync.ErrorBlendTime = 1f / interpolationRate;
        }
        UpdateEntityCommandBuffer.SetComponent(m_LinkedEntity, transformSync);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_RendererComponents = new List<Renderer>(); }

    private static List<Renderer> s_RendererComponents = new();

    public void DisableRenderers() {
        GetComponentsInChildren(s_RendererComponents);
        foreach (var renderer in s_RendererComponents) renderer.enabled = false;
    }

    // 普通实体被清理时，可能还剩专供清理流程用的组件。检查 GUID 组件仍在，才能把它当成一个可访问的完整 Ghost。
    public bool GhostEntityExists() {
        if (m_World == null || !m_World.IsCreated) return false;

        // 先确认实体仍存在，
        // 再确认它未处于部分清理阶段。
        // 还有 GhostGameObjectGuid 时，实体通常仍处于正常状态。
        // 实体也可能暂时只剩 ICleanupComponentData 清理组件。
        return EntityManager.Exists(m_LinkedEntity) && EntityManager.HasComponent<GhostGameObjectGuid>(m_LinkedEntity);
    }

    public T ReadGhostComponentData<T>() where T : unmanaged, IComponentData {
        return EntityManager.GetComponentData<T>(LinkedEntity);
    }

    public void ReadGhostComponentData<T>(out T data) where T : unmanaged, IComponentData {
        data = EntityManager.GetComponentData<T>(LinkedEntity);
    }

    public void WriteGhostComponentData<T>(in T data) where T : unmanaged, IComponentData {
        EntityManager.SetComponentData(LinkedEntity, data);
    }

    // 需要配合 ECS 当前更新的写入先记录到命令缓冲。系统遍历结束再执行这些修改，避免遍历时突然改动查询结果。
    public void WriteGhostComponentDataECB<T>(in T data) where T : unmanaged, IComponentData {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "WriteGhostComponentDataECB is only valid during an Update");
        UpdateEntityCommandBuffer.SetComponent(LinkedEntity, data);
    }

    public bool GhostHasComponent<T>() where T : unmanaged, IComponentData {
        return World.IsCreated ? EntityManager.HasComponent<T>(LinkedEntity) : false;
    }

    public bool GhostHasDynamicBuffer<T>() where T : unmanaged, IBufferElementData {
        return World.IsCreated ? EntityManager.HasComponent<T>(LinkedEntity) : false;
    }

    public DynamicBuffer<T> GetGhostDynamicBuffer<T>() where T : unmanaged, IBufferElementData {
        return EntityManager.GetBuffer<T>(LinkedEntity);
    }

    public void DestroyEntity() {
        Debug.Assert(UpdateEntityCommandBuffer.IsCreated, "DestroyEntity is only valid during an Update");
        Debug.Assert(Role == MultiplayerRole.Server, "DestroyEntity is only valid on the server");
        Debug.Assert(UpdateInterfaceType == typeof(IUpdateServer),
            "DestroyEntity is only valid from an UpdateServer call");
        OnGhostPreDestroy();
        // 服务器负责删除实体并同步它的消失。场景物体先解绑、从本端索引移除，再立刻关掉自己的 GameObject。
        UpdateEntityCommandBuffer.DestroyEntity(m_LinkedEntity);
        m_LinkedEntity = Entity.Null;
        GhostGameObjectLifetimeSystem.ServerInstance.OnGhostGameObjectDestroyed(Guid);
        DestroyImmediate(gameObject);
    }

    public T ReadSingleton<T>() where T : unmanaged, IComponentData {
#pragma warning disable 0618 // 忽略过时 API 的警告。
        return GhostGameObjectLifetimeSystem.Instance(World).GetSingleton<T>();
#pragma warning restore 0618
    }

    public bool TryReadSingleton<T>(out T singleton) where T : unmanaged, IComponentData {
#pragma warning disable 0618 // 忽略过时 API 的警告。
        if (GhostGameObjectLifetimeSystem.Instance(World).HasSingleton<T>()) {
            singleton = GhostGameObjectLifetimeSystem.Instance(World).GetSingleton<T>();
            return true;
        }
#pragma warning restore 0618
        singleton = default;
        return false;
    }

    public uint GetCurrentTick() {
        if (UpdateEntityCommandBuffer.IsCreated) {
            // 服务端读取其当前模拟 tick。
            // 客户端读取插值 tick。
            return Role == MultiplayerRole.Server
                ? UpdateInterface.GetCurrentServerTick()
                : UpdateInterface.GetCurrentInterpolationTick();
        } else {
            // 回调之外从当前 World 的系统读取网络时间。
            // 客户端里的远端表现按插值时刻看世界，服务端按自己的模拟 tick。回调之外没有缓存的更新时钟时，回到所在 World 查。
            if (TryReadSingleton<NetworkTime>(out var networkTime))
                return Role == MultiplayerRole.Server
                    ? networkTime.ServerTick.TickIndexForValidTick
                    : networkTime.InterpolationTick.TickIndexForValidTick;
            else
                return 0;
        }
    }

    public static bool TryFindGhostGameObject(GameObject currentObject, out GhostGameObject ghost) {
        ghost = null;

        // 沿物体层级往父级寻找 GhostGameObject。
        var trans = currentObject.transform;
        while (trans != null && !trans.gameObject.TryGetComponent(out ghost)) trans = trans.parent;
        return ghost != null;
    }

    public bool TryFindWorldGhost(Hash128 guid, out GhostGameObject worldGhost, bool includeUnlinkedGhosts = false) {
        if (World.IsCreated && GhostGameObjectLifetimeSystem.TryGetInstance(World, out var ghostGameObjectSystem))
            if (ghostGameObjectSystem.TryGetGhostGameObjectByGuid(guid, out worldGhost) &&
                (includeUnlinkedGhosts || worldGhost.IsGhostLinked()))
                return true;
        worldGhost = null;
        return false;
    }

    public uint SecondsToTicks(float seconds) {
        if (TryReadSingleton<ClientServerTickRate>(out var tickRate))
            return (uint)(seconds * tickRate.SimulationTickRate);
        Debug.LogWarning($"Could not read the ClientServerTickRate to convert seconds to ticks");
        return 0;
    }

#if UNITY_EDITOR
    public void OnDrawGizmos() {
        if (Dormant) {
            Handles.color = Color.red;
            var style = EditorStyles.largeLabel;
            style.normal.textColor = Color.red;
            style.fontStyle = FontStyle.Bold;
            Handles.Label(transform.position, "DORMANT", style);
        }
    }
#endif
}