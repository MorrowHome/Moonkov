// 把 PlayerCommandTarget 烘焙到输入实体上。服务器把连接编号写进去，客户端用同步到的编号找到专属于自己的输入缓冲。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

public struct PlayerCommandTarget : IComponentData {
    // 服务器给输入 Ghost 配所有者后，把连接编号同步给客户端。ClientInputInitSystem 用它找本机的那份输入实体。
    [GhostField] public int NetworkId;
}

public class PlayerCommandTargetAuthoring : MonoBehaviour {
}

public class PlayerCommandTargetBaker : Baker<PlayerCommandTargetAuthoring> {
    public override void Bake(PlayerCommandTargetAuthoring authoring) {
        var entity = GetEntity(TransformUsageFlags.None);
        // 烘焙时先设为 -1，表示这个预制件还没分给任何连接。真正生成时由 ServerGameSystem 填有效编号。
        AddComponent(entity, new PlayerCommandTarget { NetworkId = -1 });
    }
}