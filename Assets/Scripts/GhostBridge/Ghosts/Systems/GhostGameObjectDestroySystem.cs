// GhostInstance 被移除后，清理还留在 World 里的桥接对象和关联。服务端在关键更新前后回收，客户端回收前等待坐标更新作业完成。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

// 开始更新前先结束上一次留下的部分删除，
// 这样 GhostGameObjectUpdateSystem 只会碰到仍然完整的 Ghost，
// 避免读取刚移除的数据。
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(GhostGameObjectUpdateServerSystem))]
public partial class ServerPreUpdateGhostGameObjectDestroySystem : BaseGhostGameObjectDestroySystem {
}

// 服务端还会在主要更新之后清理一次，
// 确保 GhostSendSystem 发送快照前，对象的删除已处理完整。
[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateAfter(typeof(GhostGameObjectUpdateServerSystem))]
[UpdateBefore(typeof(PredictedSimulationSystemGroup))]
public partial class ServerPostUpdateGameObjectDestroySystem : BaseGhostGameObjectDestroySystem {
}

// 客户端在开始更新前清理，
// 避免 GhostGameObjectUpdateSystem 更新数据已经移除的对象。
// 避免读取刚移除的数据。
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(GhostGameObjectUpdateClientSystem))]
public partial class ClientGhostGameObjectDestroySystem : BaseGhostGameObjectDestroySystem {
    protected override void OnUpdate() {
        // 销毁时位置更新作业可能仍在用 Transform，
        // 所以先等它完成。
        // 客户端只有在位置作业停下后才能销毁场景物体。作业可能还拿着这个 Transform 的访问句柄。
        ClientGhostTransformApplySystem.Instance.ApplyTransformsJobHandle.Complete();
        base.OnUpdate();
    }
}

public abstract partial class BaseGhostGameObjectDestroySystem : SystemBase {
    protected override void OnUpdate() {
        var ecb = new EntityCommandBuffer(Allocator.Temp);

        // 将本轮的实体命令缓冲提供给 Ghost 脚本。
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