using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class RaidClientSystem : SystemBase
    {
        private Entity m_State;

        protected override void OnCreate()
        {
            m_State = EntityManager.CreateEntity(typeof(RaidClientState));
        }

        protected override void OnUpdate()
        {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, entity) in SystemAPI.Query<RefRO<RaidSnapshotRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                var current = EntityManager.GetComponentData<RaidClientState>(m_State).Snapshot;
                if (rpc.ValueRO.Sequence > current.Sequence)
                {
                    EntityManager.SetComponentData(m_State, new RaidClientState { Snapshot = rpc.ValueRO });
                    if (!World.IsThinClient()) AccountClient.UpdateStash(rpc.ValueRO.StashDust, rpc.ValueRO.StashAlloy, rpc.ValueRO.StashCells);
                }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
    }
}
