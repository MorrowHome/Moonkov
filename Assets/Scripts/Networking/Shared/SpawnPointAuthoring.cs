// 地图里摆这个组件作为出生点。烘焙时把场景物件变成带 SpawnPoint 的 ECS 实体，服务器可按标记查询位置和朝向。
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS {
    public class SpawnPointAuthoring : MonoBehaviour {
        public class Baker : Baker<SpawnPointAuthoring> {
            public override void Bake(SpawnPointAuthoring authoring) {
                // 烘焙保留变换的数据，使服务器拿到场景摆的实际位置和朝向，运行时不需要查同名 GameObject。
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                // 这是查询用空标记。ServerGameSystem 遍历这些实体并挑距离和碰撞允许的一个。
                AddComponent(entity, new SpawnPoint());
                AddComponent<LocalToWorld>(entity);
            }
        }
    }

    /// <summary>
    /// Placed in the GameScene subscene, the SpawnPoint components are used by the <see cref="ServerGameSystem"/>
    /// to spawn player characters during a game session.
    /// </summary>
    public struct SpawnPoint : IComponentData {
    }
}