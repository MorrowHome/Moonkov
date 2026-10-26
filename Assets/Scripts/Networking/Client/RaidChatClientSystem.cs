using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class RaidChatClientSystem : SystemBase
    {
        private readonly List<RaidChatMessageRpc> m_Incoming = new List<RaidChatMessageRpc>();
        private Entity m_State;
        private EntityQuery m_Connections;
        protected override void OnCreate()
        {
            m_State = EntityManager.CreateEntity(); EntityManager.AddComponentObject(m_State, new RaidChatClientState());
            m_Connections = GetEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
        }
        protected override void OnUpdate()
        {
            var data = EntityManager.GetComponentObject<RaidChatClientState>(m_State);
            Entity connection = m_Connections.CalculateEntityCount() == 1 ? m_Connections.GetSingletonEntity() : Entity.Null;
            if (connection != Entity.Null && (EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                EntityManager.HasComponent<NetworkStreamConnection>(connection) && EntityManager.GetComponentData<NetworkStreamConnection>(connection).CurrentState != ConnectionState.State.Connected)) connection = Entity.Null;
            if (connection == Entity.Null && data.Session != 0) data.Reset();
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (result, received, entity) in SystemAPI.Query<RefRO<RaidChatResultRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection) data.Receive(result.ValueRO);
                ecb.DestroyEntity(entity);
            }
            m_Incoming.Clear();
            foreach (var (message, received, entity) in SystemAPI.Query<RefRO<RaidChatMessageRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection) m_Incoming.Add(message.ValueRO);
                ecb.DestroyEntity(entity);
            }
            m_Incoming.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (var message in m_Incoming) data.Receive(message);
            if (data.PendingRequest != 0 && SystemAPI.Time.ElapsedTime - data.PendingAt > 5)
            { data.PendingRequest = 0; data.Feedback = RaidChatResult.Expired; data.Revision++; }
            ecb.Playback(EntityManager);
        }
    }
}
