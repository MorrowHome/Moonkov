// 把收到的局内聊天和发送回执放进 RaidChatClientState，HUD 读这里来显示消息或错误提示。用序号避免旧回执盖住较新的发送结果。
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class RaidChatClientSystem : SystemBase {
        private readonly List<RaidChatMessageRpc> m_Incoming = new();
        private Entity m_State;
        private EntityQuery m_Connections;

        protected override void OnCreate() {
            m_State = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(m_State, new RaidChatClientState());
            m_Connections = GetEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
        }

        protected override void OnUpdate() {
            var data = EntityManager.GetComponentObject<RaidChatClientState>(m_State);
            // 清楚知道哪个实体是本局的已入场连接时才收聊天。切换战局或断开期间，旧连接来的消息会被丢弃。
            var connection = m_Connections.CalculateEntityCount() == 1
                ? m_Connections.GetSingletonEntity()
                : Entity.Null;
            if (connection != Entity.Null && (EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                                              (EntityManager.HasComponent<NetworkStreamConnection>(connection) &&
                                               EntityManager.GetComponentData<NetworkStreamConnection>(connection)
                                                   .CurrentState != ConnectionState.State.Connected)))
                connection = Entity.Null;
            // 退出本局顺便清聊天状态，避免下一局刚加载时还保留上局频道和未发完的请求。
            if (connection == Entity.Null && data.Session != 0) data.Reset();
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (result, received, entity) in SystemAPI
                         .Query<RefRO<RaidChatResultRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection)
                    data.Receive(result.ValueRO);
                ecb.DestroyEntity(entity);
            }
            m_Incoming.Clear();
            foreach (var (message, received, entity) in SystemAPI
                         .Query<RefRO<RaidChatMessageRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection)
                    m_Incoming.Add(message.ValueRO);
                ecb.DestroyEntity(entity);
            }
            // 同一帧到达的聊天先按服务端序号排好，再交给状态对象去重。回执单独处理，不进入聊天历史。
            m_Incoming.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            foreach (var message in m_Incoming) data.Receive(message);
            // 等不到回执就清除待发送状态并让 HUD 更新失败提示，玩家可以重新操作。
            if (data.PendingRequest != 0 && SystemAPI.Time.ElapsedTime - data.PendingAt > 5) {
                data.PendingRequest = 0;
                data.Feedback = RaidChatResult.Expired;
                data.Revision++;
            }
            ecb.Playback(EntityManager);
        }
    }
}