// 客户端的 ECS 表现系统。读取带 MainCamera 组件的实体位姿并写给实际渲染的 GameObject 摄像机。
using Unity.Entities;
using Unity.Transforms;

namespace Unity.MP_FPS {
    /// <summary>
    /// Updates the <see cref="MainGameObjectCamera"/> postion to match the current player <see cref="MainCamera"/> component position if it exists.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MainCameraSystem : SystemBase {
        // 客户端世界要存在相机实体才会运行此表现系统，加载中没有相机时不用空查。
        protected override void OnCreate() { RequireForUpdate<MainCamera>(); }

        protected override void OnUpdate() {
            if (MainCameraSingleton.Instance != null) {
                // 先等实体变换相关任务完成，再把最新坐标和旋转送到真实 Camera。
                EntityManager.CompleteAllTrackedJobs();
                var mainEntityCameraEntity = SystemAPI.GetSingletonEntity<MainCamera>();
                var mainCamera = SystemAPI.GetSingleton<MainCamera>();
                var targetLocalToWorld = SystemAPI.GetComponent<LocalToWorld>(mainEntityCameraEntity);
                MainCameraSingleton.Instance.transform.SetPositionAndRotation(targetLocalToWorld.Position,
                    targetLocalToWorld.Rotation);
                MainCameraSingleton.Instance.fieldOfView = mainCamera.CurrentFov = mainCamera.BaseFov;
            }
        }
    }
}