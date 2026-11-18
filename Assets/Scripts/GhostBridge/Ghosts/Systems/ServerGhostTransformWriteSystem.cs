// 服务器先在物理之后批量读取 GameObject 的位置和旋转，再写进对应 ECS 实体的 LocalTransform，让 NetCode 有最新坐标可以同步。
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine.Jobs;

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GhostGameObjectPhysicsUpdateServerSystem))]
public partial class ServerGhostTransformRetrieveSystem : SingletonSystem<ServerGhostTransformRetrieveSystem> {
    private const int k_BatchTransformSize = 200;
    private NativeArray<LocalTransform> m_GhostGameObjectTransforms;

    public NativeArray<LocalTransform> GhostTransformsArray {
        get {
            m_GhostTransformWriteHandle.Complete();
            return m_GhostGameObjectTransforms;
        }
    }

    private JobHandle m_GhostTransformWriteHandle;
    public void CompleteTransformRead() { m_GhostTransformWriteHandle.Complete(); }

    protected override void OnDestroy() {
        CompleteTransformRead();
        if (m_GhostGameObjectTransforms.IsCreated) m_GhostGameObjectTransforms.Dispose();
        base.OnDestroy();
    }

    protected override void OnUpdate() {
        m_GhostTransformWriteHandle.Complete();
        var lifeTimeSystem = GhostGameObjectLifetimeSystem.ServerInstance;
        // 先确保实体列表和场景物体相互对应，再按当前数量准备一块输出给工作线程。对象数量改变时多余的旧数组会释放。
        lifeTimeSystem.EnsureTransformAccessArrayUpToDate();
        if (!m_GhostGameObjectTransforms.IsCreated ||
            m_GhostGameObjectTransforms.Length != lifeTimeSystem.GhostGameObjectList.Count) {
            if (m_GhostGameObjectTransforms.IsCreated) m_GhostGameObjectTransforms.Dispose();
            m_GhostGameObjectTransforms =
                new NativeArray<LocalTransform>(lifeTimeSystem.GhostGameObjectList.Count, Allocator.Persistent);
        }
        if (lifeTimeSystem.GhostGameObjectTransformAccessArray.isCreated) {
            m_GhostTransformWriteHandle = new ServerRetrieveTransformsJob {
                GhostTransformsArray = m_GhostGameObjectTransforms
            }.ScheduleReadOnly(lifeTimeSystem.GhostGameObjectTransformAccessArray, k_BatchTransformSize);
            Dependency = m_GhostTransformWriteHandle;
        }
    }

    [BurstCompile]
    private struct ServerRetrieveTransformsJob : IJobParallelForTransform {
        [NativeDisableParallelForRestriction] public NativeArray<LocalTransform> GhostTransformsArray;

        public void Execute(int index, TransformAccess transform) {
            if (transform.isValid)
                GhostTransformsArray[index] = new LocalTransform {
                    // 工作线程只并行采集位置和旋转。实际写到 ECS 组件的操作，在下一段系统遍历中完成。
                    Position = transform.position,
                    Rotation = transform.rotation
                };
        }
    }
}

[WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(ServerGhostTransformRetrieveSystem))]
public partial class ServerGhostTransformWriteSystem : SystemBase {
    protected override void OnUpdate() {
        // update server ghosts with gameobject positions
        // 这个属性会等采集作业结束。随后按各 Ghost 的本地下标把对应结果写到 LocalTransform，其他字段沿用原值。
        var transforms = ServerGhostTransformRetrieveSystem.Instance.GhostTransformsArray;
        if (transforms.Length == 0) return;
        foreach (var (localTransform, ghostGuid) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<GhostGameObjectGuid>>()
                     .WithAll<GhostGameObjectTransformSync, GhostInstance>()) {
            var transform = transforms[ghostGuid.ValueRO.LocalGhostIndex];
            localTransform.ValueRW.Position = transform.Position;
            localTransform.ValueRW.Rotation = transform.Rotation;
        }
    }
}