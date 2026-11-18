// 把场景里的摄像机视野配置烘焙成 ECS 数据。MainCameraSystem 通过 MainCamera 组件找到要跟随的实体，并读取它的视野参数。
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS {
    [DisallowMultipleComponent]
    public class MainCameraAuthoring : MonoBehaviour {
        public float Fov = 75f;

        public class Baker : Baker<MainCameraAuthoring> {
            public override void Bake(MainCameraAuthoring authoring) {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new MainCamera(authoring.Fov));
            }
        }
    }

    /// <summary>
    /// This Camera component is added to the player character entity by the <see cref="ClientGameSystem"/>
    /// after the server spawns a new character.
    ///
    /// It is used by the <see cref="MainCameraSystem"/> to position the GameObject of the MainCamera at the player position.
    /// </summary>
    public struct MainCamera : IComponentData {
        public MainCamera(float fov) { CurrentFov = BaseFov = fov; }
        public float BaseFov;
        public float CurrentFov;
    }
}