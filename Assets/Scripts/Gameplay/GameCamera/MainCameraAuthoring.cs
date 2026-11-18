// 把场景里的摄像机视野配置烘焙成 ECS 数据。MainCameraSystem 通过 MainCamera 组件找到要跟随的实体，并读取它的视野参数。
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    [DisallowMultipleComponent]
    public class MainCameraAuthoring : MonoBehaviour {
        public float Fov = 75f;

        public class Baker : Baker<MainCameraAuthoring> {
            // Baker 把场景配置对接到动态实体。表现系统可以通过组件查询取姿势和视野。
            public override void Bake(MainCameraAuthoring authoring) {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new MainCamera(authoring.Fov));
            }
        }
    }

    /// <summary>
    /// MainCameraAuthoring 的 Baker 把相机参数写入实体。<see cref="ClientGameSystem"/> 负责发出玩家入场请求。
    /// 客户端相机系统从自己的 World 读取这份组件。
    ///
    /// <see cref="MainCameraSystem"/> 用它和实体的位姿更新场景摄像机的位置、方向和视野。
    /// </summary>
    public struct MainCamera : IComponentData {
        public MainCamera(float fov) { CurrentFov = BaseFov = fov; }
        // 基础视野是配置值。另有 CurrentFov 留给当前画面使用。
        public float BaseFov;
        public float CurrentFov;
    }
}