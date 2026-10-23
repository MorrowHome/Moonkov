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
            m_State = EntityManager.CreateEntity(typeof(RaidClientState), typeof(RaidMissionClientState));
            EntityManager.AddComponentObject(m_State, new RaidInventoryClientState());
            EntityManager.AddComponentObject(m_State,new RaidDeathBagClientState());
        }

        protected override void OnUpdate()
        {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach(var (mission,received,entity) in SystemAPI.Query<RefRO<RaidMissionSnapshotRpc>,RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var current=EntityManager.GetComponentData<RaidMissionClientState>(m_State).Snapshot;
                if(mission.ValueRO.Sequence>current.Sequence || !mission.ValueRO.WorldId.Equals(current.WorldId))
                    EntityManager.SetComponentData(m_State,new RaidMissionClientState {Snapshot=mission.ValueRO});
                ecb.DestroyEntity(entity);
            }
            foreach(var (bag,received,entity) in SystemAPI.Query<RefRO<RaidGroundItemMarkerRpc>,RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();EntityManager.GetComponentObject<RaidDeathBagClientState>(m_State).Receive(bag.ValueRO);ecb.DestroyEntity(entity);
            }
            foreach(var (bag,received,entity) in SystemAPI.Query<RefRO<RaidCorpseRpc>,RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();EntityManager.GetComponentObject<RaidDeathBagClientState>(m_State).Receive(bag.ValueRO);ecb.DestroyEntity(entity);
            }
            foreach (var (chunk, received, entity) in SystemAPI.Query<RefRO<RaidInventoryChunkV2Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                EntityManager.GetComponentObject<RaidInventoryClientState>(m_State).Receive(chunk.ValueRO, RaidInventoryTransport.Decode);
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<RaidSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var current = EntityManager.GetComponentData<RaidClientState>(m_State).Snapshot;
                if (rpc.ValueRO.Sequence > current.Sequence)
                {
                    EntityManager.SetComponentData(m_State, new RaidClientState { Snapshot = rpc.ValueRO });
                    if (!World.IsThinClient()) PlayerProfileClient.UpdateStash(rpc.ValueRO.StashDust, rpc.ValueRO.StashAlloy, rpc.ValueRO.StashCells);
                }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
    }
}
