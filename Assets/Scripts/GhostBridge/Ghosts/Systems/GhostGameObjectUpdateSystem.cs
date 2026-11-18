// 把 ECS 的更新阶段转发到 Ghost 场景脚本。服务端有早期、普通和物理三个阶段，客户端分早期、模拟和画面表现三个阶段。
#if UNITY_EDITOR || ENABLE_PROFILING
#define USE_GHOST_GROUP_PROFILE_MARKERS
#endif
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Profiling;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

public enum GhostGameObjectUpdateGroup {
    Default,
    Managers,
    Players,

    // 新增更新组请放在这里，避免改变旧 Ghost 已保存的组编号。

    // 这是保留值，供边界检查用。
    Invalid
};

public class GhostGameObjectUpdateGroupHelpers {
// 这些静态字段会保持固定，关闭对应警告。
#if USE_GHOST_GROUP_PROFILE_MARKERS
    public static readonly ProfilerMarker[] s_UpdateGroupMarkers = {
        new("Default"),
        new("Managers"),
        new("Players")
    };
#endif

    // 定义各更新组的执行顺序。
    // 数值越小，在这一帧越早更新。
    public static readonly int[] s_UpdateGroupOrder = {
        3, //默认组。
        1, //管理器。
        2 //玩家。
    };
}

[ResetOnPlayMode("Reset")]
public static class GhostGameObjectUpdateSystem {
#pragma warning disable UDR0001
    private static Dictionary<Type, SystemBase> s_GhostUpdateSystemByType = new(); // Reset 会清空这张表。
#pragma warning restore UDR0001
    // 开始新的一局先清对旧 World 的系统引用，再调用 GatherWorldSystems 登记各端的新实例。
    public static void Reset() { s_GhostUpdateSystemByType.Clear(); }

    // World 启动时先记录每种更新接口该交给哪个 ECS 系统。之后新绑定的 Ghost 可以按接口注册。
    public static void GatherWorldSystems(World world) {
        if (world.IsServer()) {
            s_GhostUpdateSystemByType[typeof(IEarlyUpdateServer)] =
                world.GetExistingSystemManaged<GhostGameObjectEarlyUpdateServerSystem>();
            s_GhostUpdateSystemByType[typeof(IUpdateServer)] =
                world.GetExistingSystemManaged<GhostGameObjectUpdateServerSystem>();
            s_GhostUpdateSystemByType[typeof(IPhysicsUpdateServer)] =
                world.GetExistingSystemManaged<GhostGameObjectPhysicsUpdateServerSystem>();
        } else if (world.IsClient()) {
            s_GhostUpdateSystemByType[typeof(IEarlyUpdateClient)] =
                world.GetExistingSystemManaged<GhostGameObjectEarlyUpdateClientSystem>();
            s_GhostUpdateSystemByType[typeof(IUpdateClient)] =
                world.GetExistingSystemManaged<GhostGameObjectUpdateClientSystem>();
            s_GhostUpdateSystemByType[typeof(ILateUpdateClient)] =
                world.GetExistingSystemManaged<GhostGameObjectLateUpdateClientSystem>();
        }
    }

    public static GhostGameObjectUpdateSystemBase<T> GetGhostUpdateSystemByType<T>()
        where T : class, IGhostMonoBehaviourUpdate {
        var typeofT = typeof(T);
        if (s_GhostUpdateSystemByType.ContainsKey(typeofT))
            return (GhostGameObjectUpdateSystemBase<T>)s_GhostUpdateSystemByType[typeof(T)];
        return null;
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(GhostGameObjectLifetimeSystem))]
public partial class GhostGameObjectEarlyUpdateClientSystem : GhostGameObjectUpdateSystemBase<IEarlyUpdateClient> {
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) {
        updateObject.EarlyUpdateClient(deltaTime);
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class GhostGameObjectUpdateClientSystem : GhostGameObjectUpdateSystemBase<IUpdateClient> {
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) {
        updateObject.UpdateClient(deltaTime);
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GhostGameObjectLateUpdateClientSystem : GhostGameObjectUpdateSystemBase<ILateUpdateClient> {
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) {
        updateObject.LateUpdateClient(deltaTime);
    }

    protected override void OnAfterGhostUpdates() {
        GhostGameObjectLifetimeSystem.ClientInstance.PostUpdateRemoveStaleGhostGameObjectsFromList();
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(GhostGameObjectLifetimeSystem))]
public partial class GhostGameObjectEarlyUpdateServerSystem : GhostGameObjectUpdateSystemBase<IEarlyUpdateServer> {
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) {
        updateObject.EarlyUpdateServer(deltaTime);
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(PredictedSimulationSystemGroup))]
public partial class GhostGameObjectUpdateServerSystem : GhostGameObjectUpdateSystemBase<IUpdateServer> {
    private List<GhostGameObject> m_ObjectsPendingDestroy = new();

    protected override void OnCreate() {
        base.OnCreate();
#if USE_GHOST_GROUP_PROFILE_MARKERS
        Debug.Assert(
            GhostGameObjectUpdateGroupHelpers.s_UpdateGroupMarkers.Length == (int)GhostGameObjectUpdateGroup.Invalid,
            "There are mismatching entries in s_UpdateGroupMarkers and the UpdateGroup enum");
        Debug.Assert(
            GhostGameObjectUpdateGroupHelpers.s_UpdateGroupOrder.Length == (int)GhostGameObjectUpdateGroup.Invalid,
            "There are mismatching entries in s_UpdateGroupOrder and the UpdateGroup enum");
#endif
    }

    // 如果销毁请求发生在当前回调以外，先排进服务端队列。轮到服务端更新时再调用需要命令缓冲的正式删除入口。
    public static void AddGhostForPendingDestroy(GhostGameObject ghost) {
        if (Instance != null) {
            var instance = Instance as GhostGameObjectUpdateServerSystem;
            instance.m_ObjectsPendingDestroy.Add(ghost);
        }
    }

    protected override void OnBeforeGhostUpdates() {
        // 检查有没有等待销毁的对象。
        if (m_ObjectsPendingDestroy != null && m_ObjectsPendingDestroy.Count > 0) {
            // 开始更新前先处理待删对象。这样本轮其余 Ghost 脚本就不会继续操作这些已经准备退场的实体。
            int count = m_ObjectsPendingDestroy.Count;
            for (int i = 0; i < count; i++) {
                var ghost = m_ObjectsPendingDestroy[i];
                ghost.DestroyEntity();
            }
            m_ObjectsPendingDestroy.Clear();
        }
    }

    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) {
        updateObject.UpdateServer(deltaTime);
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PredictedSimulationSystemGroup))]
public partial class GhostGameObjectPhysicsUpdateServerSystem : GhostGameObjectUpdateSystemBase<IPhysicsUpdateServer> {
    protected override void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) {
        updateObject.PhysicsUpdateServer(deltaTime);
    }

    protected override void OnAfterGhostUpdates() {
        // 服务端物理更新结束才压缩对象列表。在这之前逻辑可能一直读同一批下标，统一时点更新便于保持对齐。
        GhostGameObjectLifetimeSystem.ServerInstance.PostUpdateRemoveStaleGhostGameObjectsFromList();
    }
}

public interface IGhostGameObjectUpdateSystem {
    delegate void OnDelayedCall(float deltaTime);

    event OnDelayedCall PreEvent;
    bool Paused { get; }
    void SetPaused(bool paused);
    uint GetCurrentServerTick();
    uint GetCurrentInterpolationTick();
}

public abstract partial class GhostGameObjectUpdateSystemBase<T> : SingletonSystem<GhostGameObjectUpdateSystemBase<T>>,
    IGhostGameObjectUpdateSystem where T : class, IGhostMonoBehaviourUpdate {
    private List<GhostGameObject> m_UpdateObjects = new();
    private bool m_UpdateObjectsDirty;
    private int m_UpdateObjectIndex;
    public bool Paused { get; private set; }
    private NetworkTick m_ServerTick;
    private NetworkTick m_InterpolationTick;
#if USE_GHOST_GROUP_PROFILE_MARKERS
    private static readonly ProfilerMarker s_OnUpdatePrepareMarker = new("GhostGameObjectUpdateSystemBase.Prepare");
    private static readonly Dictionary<Hash128, ProfilerMarker> s_GhostUpdateMarker = new();
#endif
    public event IGhostGameObjectUpdateSystem.OnDelayedCall PreEvent;

    protected override void OnDestroy() {
        m_UpdateObjects.Clear();
        base.OnDestroy();
    }

    public void RegisterForUpdate(GhostGameObject ghostGameObject) {
        m_UpdateObjects.Add(ghostGameObject);
        m_UpdateObjectsDirty = true;
    }

    public void Unregister(GhostGameObject ghostGameObject) {
        int index = m_UpdateObjects.IndexOf(ghostGameObject);
        if (index >= 0) {
            m_UpdateObjects.RemoveAtSwapBack(index);
            if (index == m_UpdateObjectIndex)
                // 当前正在更新的对象被移除了。
                // 把索引减一，让下一个循环处理
                // 交换到空位上的另一个对象。
                m_UpdateObjectIndex--;
            m_UpdateObjectsDirty = true;
        }
    }

    public uint GetCurrentServerTick() { return m_ServerTick.IsValid ? m_ServerTick.TickIndexForValidTick : 0; }

    public uint GetCurrentInterpolationTick() {
        return m_InterpolationTick.IsValid ? m_InterpolationTick.TickIndexForValidTick : 0;
    }

    protected virtual void OnBeforeGhostUpdates() { }
    protected virtual void OnAfterGhostUpdates() { }

    protected override void OnUpdate() {
        if (m_UpdateObjects.Count == 0 || Paused) return;
#if USE_GHOST_GROUP_PROFILE_MARKERS
        s_OnUpdatePrepareMarker.Begin();
#endif
        if (m_UpdateObjectsDirty) {
            // 按更新组的顺序重排列表。
            SortUpdateObjectsList();
            m_UpdateObjectsDirty = false;
        }
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        float dt = World.Time.DeltaTime;
        var networkTime = SystemAPI.GetSingleton<NetworkTime>();
        m_ServerTick = networkTime.ServerTick;
        m_InterpolationTick = networkTime.InterpolationTick;
        GhostGameObject.UpdateInterface = this;
        GhostGameObject.UpdateInterfaceType = typeof(T);
        GhostGameObject.UpdateEntityCommandBuffer = ecb;
        OnBeforeGhostUpdates();

        //先取出回调并清空原槽位，回调执行时才能再登记新请求。
        var delayedCalls = PreEvent;
        PreEvent = null;
        delayedCalls?.Invoke(dt);
#if USE_GHOST_GROUP_PROFILE_MARKERS
        s_OnUpdatePrepareMarker.End();
#endif
        var previousGroup = GhostGameObjectUpdateGroup.Invalid;
#if USE_GHOST_GROUP_PROFILE_MARKERS
        ProfilerMarker profilerMarker = default;
#endif
        for (m_UpdateObjectIndex = 0; m_UpdateObjectIndex < m_UpdateObjects.Count; m_UpdateObjectIndex++) {
            var updateObject = m_UpdateObjects[m_UpdateObjectIndex];
#if USE_GHOST_GROUP_PROFILE_MARKERS
            if (updateObject.UpdateGroup != previousGroup) {
                if (previousGroup != GhostGameObjectUpdateGroup.Invalid) profilerMarker.End();
                profilerMarker = GhostGameObjectUpdateGroupHelpers.s_UpdateGroupMarkers[(int)updateObject.UpdateGroup];
                profilerMarker.Begin();
            }
#endif
            if (!updateObject.Dormant) {
#if USE_GHOST_GROUP_PROFILE_MARKERS
                if (!s_GhostUpdateMarker.TryGetValue(updateObject.Guid, out var marker))
                    marker = s_GhostUpdateMarker[updateObject.Guid] = new ProfilerMarker(updateObject.name);
                using (marker.Auto())
#endif
                {
                    UpdateGhostGameObject(updateObject, dt);
                }
            }
            previousGroup = updateObject.UpdateGroup;
        }
#if USE_GHOST_GROUP_PROFILE_MARKERS
        profilerMarker.End();
#endif
        m_UpdateObjectIndex = -1;
        OnAfterGhostUpdates();
        ecb.Playback(EntityManager);
        GhostGameObject.UpdateEntityCommandBuffer = default;
        GhostGameObject.UpdateInterface = null;
        GhostGameObject.UpdateInterfaceType = null;
    }

    private void SortUpdateObjectsList() {
        for (int i = 0; i < m_UpdateObjects.Count; i++) {
            int smallestIndex = i;
            for (int j = i + 1; j < m_UpdateObjects.Count; j++) {
                var a = m_UpdateObjects[smallestIndex];
                var b = m_UpdateObjects[j];
                if (GhostGameObjectUpdateGroupHelpers.s_UpdateGroupOrder[(int)a.UpdateGroup]
                        .CompareTo(GhostGameObjectUpdateGroupHelpers.s_UpdateGroupOrder[(int)b.UpdateGroup]) > 0)
                    smallestIndex = j;
            }
            var swap = m_UpdateObjects[i];
            m_UpdateObjects[i] = m_UpdateObjects[smallestIndex];
            m_UpdateObjects[smallestIndex] = swap;
        }
    }

    public void SetPaused(bool paused) { Paused = paused; }
    protected virtual void UpdateGhostGameObject(GhostGameObject updateObject, float deltaTime) { }
}