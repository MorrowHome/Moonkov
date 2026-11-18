// 服务器把复活倒计时挂在连接上。只用于没有 RaidSession 的死亡重生路径，远征角色死亡会改走战局结算。
using Unity.Entities;

namespace Unity.MP_FPS {
    public struct PendingRespawn : IComponentData {
        public float RespawnTimer;
    }
}