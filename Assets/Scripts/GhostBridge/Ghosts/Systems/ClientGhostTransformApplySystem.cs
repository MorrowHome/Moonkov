// 客户端收到位置与朝向后，批量把实体的位置应用到场景中的 Ghost 表现体。少量偏差可以用一段渐消的位移抵消，减少眼前的跳变。
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine.Jobs;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(GhostSimulationSystemGroup))]
[UpdateAfter(typeof(GhostReceiveSystem))]
[UpdateBefore(typeof(GhostPredictionSwitchingSystem))]
public partial class ClientGhostTransformApplySystem : SingletonSystem<ClientGhostTransformApplySystem> {
    private const int k_BatchTransformSize = 200;
    private JobHandle m_ApplyTransformsJobHandle;
    public JobHandle ApplyTransformsJobHandle => m_ApplyTransformsJobHandle;

    protected override void OnUpdate() {
        if (GhostGameObjectLifetimeSystem.TryGetClientInstance(out var lifetimeSystem) &&
            lifetimeSystem.GhostGameObjectTransformAccessArray.isCreated) {
            // 上一帧还可能在工作线程写 Transform。先等它结束，再给这一批位置安排新作业，生命周期系统清表时也要等同一个句柄。
            m_ApplyTransformsJobHandle.Complete();
            m_ApplyTransformsJobHandle = new ClientApplyTransformsJob {
                LocalTransformLookup = GetComponentLookup<LocalTransform>(true),
                GhostTransformSyncLookup = GetComponentLookup<GhostGameObjectTransformSync>(true),
                GhostEntityArray = lifetimeSystem.GhostEntityList,
                CurrentTime = UnityEngine.Time.time
            }.ScheduleReadOnly(lifetimeSystem.GhostGameObjectTransformAccessArray, k_BatchTransformSize);
            Dependency = m_ApplyTransformsJobHandle;
        }
    }

    [BurstCompile]
    private struct ClientApplyTransformsJob : IJobParallelForTransform {
        [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
        [ReadOnly] public ComponentLookup<GhostGameObjectTransformSync> GhostTransformSyncLookup;
        [ReadOnly] public NativeList<Entity> GhostEntityArray;
        public float CurrentTime;

        public void Execute(int index, TransformAccess transform) {
            // 场景 Transform 与实体列表共用下标。实体还在、同步组件存在时，才把从 ECS 读出的坐标写到对应物体。
            var ghostEntity = GhostEntityArray[index];
            if (transform.isValid && ghostEntity != Entity.Null && GhostTransformSyncLookup.HasComponent(ghostEntity)) {
                var localTransform = LocalTransformLookup[ghostEntity];
                var transformSync = GhostTransformSyncLookup[ghostEntity];
                // 切到本机驱动的对象会暂停这一方向的位置同步。控制器有机会自己更新场景物体，避免一个画面对象被两套逻辑争着写。
                if (!transformSync.DisableTransformSync) {
                    // 修正刚开始时保留原先的画面位置，随着时间逐步靠近同步位置。过了这段时间直接采用新坐标。
                    float timeSinceErrorTriggered = CurrentTime - transformSync.ErrorTriggeredTime;
                    if (timeSinceErrorTriggered <= transformSync.ErrorBlendTime) {
                        float blendTime = 1f - timeSinceErrorTriggered / transformSync.ErrorBlendTime;
                        var offset = math.lerp(float3.zero, transformSync.ErrorOffset, blendTime);
                        transform.SetPositionAndRotation(localTransform.Position - offset, localTransform.Rotation);
                    } else { transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation); }
                }
            }
        }
    }
}