using Unity.Entities;

namespace Unity.MP_FPS
{
    public struct PendingRespawn : IComponentData
    {
        public float RespawnTimer;
    }
}