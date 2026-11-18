// GhostInstance 被移除后，清理还留在 World 里的桥接对象和关联。服务端在关键更新前后回收，客户端回收前等待坐标更新作业完成。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

// This has to happen before any updates to ensure that partially deleted ghosts
// are fully removed before they are to be updated by the GhostGameObjectUpdateSystem
// otherwise they'll try to access ghost data that has already been removed
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(GhostGameObjectUpdateServerSystem))]
public partial class ServerPreUpdateGhostGameObjectDestroySystem : BaseGhostGameObjectDestroySystem {
}

// on the server it's important this happens AFTER any update to ensure that
// partially deleted ghosts are fully removed before the GhostSendSystem runs
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateAfter(typeof(GhostGameObjectUpdateServerSystem))]
[UpdateBefore(typeof(PredictedSimulationSystemGroup))]
public partial class ServerPostUpdateGameObjectDestroySystem : BaseGhostGameObjectDestroySystem {
}

// on the client it's important this happens BEFORE any update to ensure that
// partially deleted ghosts are fully removed before they are to be updated by the GhostGameObjectUpdateSystem
// otherwise they'll try to access ghost data that has already been removed
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(GhostGameObjectUpdateClientSystem))]
public partial class ClientGhostGameObjectDestroySystem : BaseGhostGameObjectDestroySystem {
    protected override void OnUpdate() {
        // it's possible we are trying to destroy items whilst transforms are still being updated
        // so we should force that job to complete first
        // 客户端只有在位置作业停下后才能销毁场景物体。作业可能还拿着这个 Transform 的访问句柄。
        ClientGhostTransformApplySystem.Instance.ApplyTransformsJobHandle.Complete();
        base.OnUpdate();
    }
}

public abstract partial class BaseGhostGameObjectDestroySystem : SystemBase {
    protected override void OnUpdate() {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // set the ecb for the ghostgameobject code to use
        GhostGameObject.UpdateEntityCommandBuffer = ecb;
        var lifeTimeSystem = GhostGameObjectLifetimeSystem.Instance(World);
        // 这批实体在 Ghost 已消失时还只有待激活的链接。它们尚未完成 OnGhostLinked，可以直接销毁留下的表现体和清理组件。
        var ghostGameObjectDeferredActivationQuery = SystemAPI.QueryBuilder()
            .WithAll<GhostGameObjectDeferredActivation, GhostGameObjectLink>().WithNone<GhostInstance>().Build();
        foreach (var entity in ghostGameObjectDeferredActivationQuery.ToEntityArray(Allocator.Temp)) {
            var gameObjectLink = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
            var obj = gameObjectLink.LinkedInstance;
            if (obj != null) {
                if (obj.TryGetComponent<GhostGameObject>(out var ghostGameObject))
                    lifeTimeSystem.OnGhostGameObjectDestroyed(ghostGameObject.Guid);
                Object.DestroyImmediate(obj.gameObject);
            }
            ecb.RemoveComponent<GhostGameObjectLink>(entity);
            ecb.DestroyEntity(entity);
        }
        // 已激活过的对象有脚本和更新登记。删除前先调用 OnGhostPreDestroy 让它们释放，再真正移走关联。
        var ghostGameObjectLinkQuery = SystemAPI.QueryBuilder().WithAll<GhostGameObjectLink>()
            .WithNone<GhostInstance, GhostGameObjectDeferredActivation>().Build();
        foreach (var entity in ghostGameObjectLinkQuery.ToEntityArray(Allocator.Temp)) {
            var gameObjectLink = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
            var obj = gameObjectLink.LinkedInstance;
            if (obj != null) {
                if (obj.TryGetComponent<GhostGameObject>(out var ghostGameObject)) {
                    lifeTimeSystem.OnGhostGameObjectDestroyed(ghostGameObject.Guid);
                    ghostGameObject.OnGhostPreDestroy();
                }
                Object.DestroyImmediate(obj.gameObject);
            }
            ecb.RemoveComponent<GhostGameObjectLink>(entity);
            ecb.DestroyEntity(entity);
        }
        // 遍历要删的对象时先记 ECS 操作，扫描结束后统一执行。清理组件移光后，实体才算完全删除。
        ecb.Playback(EntityManager);
        GhostGameObject.UpdateEntityCommandBuffer = default;
    }
}