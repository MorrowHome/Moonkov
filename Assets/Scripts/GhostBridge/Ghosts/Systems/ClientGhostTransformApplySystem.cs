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
public partial class ClientGhostTransformApplySystem : SingletonSystem<ClientGhostTransformApplySystem>
{
    private const int k_BatchTransformSize = 200;

    private JobHandle m_ApplyTransformsJobHandle;
    public JobHandle ApplyTransformsJobHandle => m_ApplyTransformsJobHandle;
    
    protected override void OnUpdate()
    {
        if (GhostGameObjectLifetimeSystem.TryGetClientInstance(out var lifetimeSystem)
            && lifetimeSystem.GhostGameObjectTransformAccessArray.isCreated)
        {
            m_ApplyTransformsJobHandle.Complete();

            m_ApplyTransformsJobHandle = new ClientApplyTransformsJob
            {
                LocalTransformLookup = GetComponentLookup<LocalTransform>(isReadOnly: true),
                GhostTransformSyncLookup = GetComponentLookup<GhostGameObjectTransformSync>(isReadOnly: true),
                GhostEntityArray = lifetimeSystem.GhostEntityList,
                CurrentTime = UnityEngine.Time.time
            }
            .ScheduleReadOnly(lifetimeSystem.GhostGameObjectTransformAccessArray, k_BatchTransformSize);

            Dependency = m_ApplyTransformsJobHandle;
        }
    }

    [BurstCompile]
    struct ClientApplyTransformsJob : IJobParallelForTransform
    {
        [ReadOnly] public ComponentLookup<LocalTransform> LocalTransformLookup;
        [ReadOnly] public ComponentLookup<GhostGameObjectTransformSync> GhostTransformSyncLookup;
        [ReadOnly] public NativeList<Entity> GhostEntityArray;

        public float CurrentTime;
        public void Execute(int index, TransformAccess transform)
        {
            var ghostEntity = GhostEntityArray[index];

            if (transform.isValid &&
                ghostEntity != Entity.Null &&
                GhostTransformSyncLookup.HasComponent(ghostEntity))
            {
                var localTransform = LocalTransformLookup[ghostEntity];
                var transformSync = GhostTransformSyncLookup[ghostEntity];

                if (!transformSync.DisableTransformSync)
                {
                    // 修正刚开始时保留原先的画面位置，随着时间逐步靠近同步位置。过了这段时间直接采用新坐标。
                    var timeSinceErrorTriggered = CurrentTime - transformSync.ErrorTriggeredTime;
                    if (timeSinceErrorTriggered <= transformSync.ErrorBlendTime)
                    {
                        var blendTime = 1f - (timeSinceErrorTriggered / transformSync.ErrorBlendTime);
                        var offset = math.lerp(float3.zero, transformSync.ErrorOffset, blendTime);

                        transform.SetPositionAndRotation(localTransform.Position - offset, localTransform.Rotation);
                    }
                    else
                    {
                        transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
                    }
                }
            }
        }
    }
}
