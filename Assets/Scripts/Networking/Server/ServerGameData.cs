// 声明服务端用来找人的 ECS 组件。JoinedClient 留在连接上，ClientsMap 用 NetworkId 快速找到连接实体、玩家实体和角色控制实体。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    /// <summary>
    /// 已进场玩家的角色实体。
    /// </summary>
    public struct JoinedClient : IComponentData {
        public Entity PlayerEntity;
        // 角色实体可以死后再次生成，名字和选择的角色编号随连接保留，复活用这些值重建。
        public FixedString64Bytes PlayerName;
        public int CharacterIndex;
    }

    /// <summary>
    /// 保存已经生成的实体。
    /// </summary>
    public struct SpawnCharacter : IComponentData {
        public Entity ClientEntity;
        public float Delay;
    }

    /// <summary>
    /// 此缓冲用 <see cref="NetworkId"/> 作下标，以该结构为内容，
    /// 方便快速找到特定客户端的各个实体。
    /// </summary>
    public struct ClientsMap : IBufferElementData {
        /// <summary><see cref="NetworkStreamConnection"/> 实体，用 <see cref="NetworkId"/> 索引。</summary>
        public Entity ConnectionEntity;

        /// <summary><see cref="FirstPersonPlayer"/> 实体，用 <see cref="NetworkId"/> 索引。</summary>
        public Entity PlayerEntity;

        /// <summary><see cref="FirstPersonCharacterControl"/> 实体，用 <see cref="NetworkId"/> 索引。</summary>
        public Entity CharacterControllerEntity;

        // 命令缓冲刚登记的实体要等回放才有最终 ID。这个非零编号告诉更新函数该把回放后的引用补回哪一格。
        /// <summary>编号为非默认值时，需把实体引用补到对应的索引。</summary>
        public NetworkId OwnerNetworkId;
    }
}