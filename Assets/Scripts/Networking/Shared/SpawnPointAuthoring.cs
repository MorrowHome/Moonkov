// 地图里摆这个组件作为出生点。烘焙时把场景物件变成带 SpawnPoint 的 ECS 实体，服务器可按标记查询位置和朝向。
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class SpawnPointAuthoring : MonoBehaviour
    {
        public class Baker : Unity.Entities.Baker<SpawnPointAuthoring>
        {
            public override void Bake(SpawnPointAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new SpawnPoint());
                AddComponent<LocalToWorld>(entity);
            }
        }
    }

    /// <summary>
    /// Placed in the GameScene subscene, the SpawnPoint components are used by the <see cref="ServerGameSystem"/>
    /// to spawn player characters during a game session.
    /// </summary>
    public struct SpawnPoint : IComponentData
    {
    }
}