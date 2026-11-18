// 声明服务端用来找人的 ECS 组件。JoinedClient 留在连接上，ClientsMap 用 NetworkId 快速找到连接实体、玩家实体和角色控制实体。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    /// <summary>
    /// Joined player's entity
    /// </summary>
    public struct JoinedClient : IComponentData {
        public Entity PlayerEntity;
        // 角色实体可以死后再次生成，名字和选择的角色编号随连接保留，复活用这些值重建。
        public FixedString64Bytes PlayerName;
        public int CharacterIndex;
    }

    /// <summary>
    /// Stores the spawned entity
    /// </summary>
    public struct SpawnCharacter : IComponentData {
        public Entity ClientEntity;
        public float Delay;
    }

    /// <summary>
    /// This buffer is used to map clients using their <see cref="NetworkId"/> index as a key and this struct as a value,
    /// making it easy to find Entities relating to that specific client.
    /// </summary>
    public struct ClientsMap : IBufferElementData {
        /// <summary>The <see cref="NetworkStreamConnection"/> entity for this <see cref="NetworkId"/> index.</summary>
        public Entity ConnectionEntity;

        /// <summary>The <see cref="FirstPersonPlayer"/> entity for this <see cref="NetworkId"/> index.</summary>
        public Entity PlayerEntity;

        /// <summary>The <see cref="FirstPersonCharacterControl"/> entity for this <see cref="NetworkId"/> index.</summary>
        public Entity CharacterControllerEntity;

        // 命令缓冲刚登记的实体要等回放才有最终 ID。这个非零编号告诉更新函数该把回放后的引用补回哪一格。
        /// <summary>If != default, need to remap this to the entity.</summary>
        public NetworkId OwnerNetworkId;
    }
}