// 把 PlayerCommandTarget 烘焙到输入实体上。服务器把连接编号写进去，客户端用同步到的编号找到专属于自己的输入缓冲。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public struct PlayerCommandTarget : IComponentData
{
    [GhostField]
    public int NetworkId;
}

public class PlayerCommandTargetAuthoring : MonoBehaviour
{
}

public class PlayerCommandTargetBaker : Baker<PlayerCommandTargetAuthoring>
{
    public override void Bake(PlayerCommandTargetAuthoring authoring)
    {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new PlayerCommandTarget {NetworkId = -1});
    }
}
