// 在客户端驱动不做本地预测的角色。把接收到并插值后的实体位姿同步到 FirstPersonController，再更新动画和贴地状态。
// 自己的预测角色走 PlayerPredictionSystem。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class ClientInterpolatedPlayerMovementSystem : SystemBase {
        protected override void OnUpdate() {
            float deltaTime = World.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (predictedGhost, ghost, entity) in SystemAPI
                         .Query<RefRO<PredictedPlayerGhost>, RefRO<GhostInstance>>()
                         .WithNone<PlayerControllerLink, PredictedGhost>().WithAll<GhostGameObjectLink>()
                         .WithEntityAccess()) {
                // 网络实体可能先生成，外观稍后才链接。两者都到齐时缓存 FirstPersonController 供后续帧使用。
                var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (gameObjectLink.LinkedInstance != null)
                    // get controller
                    if (gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller))
                        ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
            }
            // 先安装本帧找到的控制器链接，下面的查询随即能把这些角色也纳入同步。
            ecb.Playback(EntityManager);
            ecb.Dispose();
            foreach (var (predictedGhost, transform, controllerConsts, entity) in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<LocalTransform>,
                             RefRO<PredictedPlayerControllerConsts>>().WithEntityAccess().WithNone<PredictedGhost>()
                         .WithAll<PlayerControllerLink>()) {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller.ApplyInterpolatedClientState(ref predictedGhost.ValueRW.ControllerState,
                    controllerConsts.ValueRO.ControllerConsts, transform.ValueRO, deltaTime, true);
                // 插值处理的是画面位置，动画仍按收到的状态参数走。贴地更新给表面相关表现取数据。
                controllerLink.Controller.ApplyAnimatorState(predictedGhost.ValueRO.ControllerState,
                    controllerConsts.ValueRO.ControllerConsts, deltaTime);
                controllerLink.Controller.UpdateGround(ref predictedGhost.ValueRW.ControllerState,
                    controllerConsts.ValueRO.ControllerConsts);
            }
        }

        protected override void OnDestroy() {
            var query = GetEntityQuery(typeof(PlayerControllerLink));
            foreach (var entity in query.ToEntityArray(Allocator.Temp)) {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller = null;
                EntityManager.SetComponentData(entity, controllerLink);
            }
            base.OnDestroy();
        }
    }
}