// 服务器把复活倒计时挂在连接上。只用于没有 RaidSession 的死亡重生路径，远征角色死亡会改走战局结算。
using Unity.Entities;

namespace Unity.MP_FPS {
    public struct PendingRespawn : IComponentData {
        // 保存距离再次出生还剩的秒数，服务端逐帧减。到零后用同一连接的 JoinedClient 设定出生，再移掉计时组件。
        public float RespawnTimer;
    }
}