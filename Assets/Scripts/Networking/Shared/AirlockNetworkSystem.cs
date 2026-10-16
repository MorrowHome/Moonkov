using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public struct AirlockActionRequestRpc : IRpcCommand
    {
        public FixedString64Bytes AirlockId, ButtonId;
        public uint RequestId;
        public ulong ServerSession;
    }
    public struct AirlockStateRpc : IRpcCommand
    {
        public FixedString64Bytes AirlockId;
        public ulong ServerSession;
        public uint Revision;
        public float PressureKPa, OuterFraction, InnerFraction;
        public bool OuterOpening, InnerOpening, Equalizing, TargetInside;
        public AirlockFeedback Feedback;
        public AirlockState State => new AirlockState { Revision = Revision, PressureKPa = PressureKPa,
            OuterFraction = OuterFraction, InnerFraction = InnerFraction, OuterOpening = OuterOpening,
            InnerOpening = InnerOpening, Equalizing = Equalizing, TargetInside = TargetInside, Feedback = Feedback };
    }
    public struct AirlockActionAckRpc : IRpcCommand
    {
        public FixedString64Bytes AirlockId, ButtonId;
        public uint RequestId;
        public ulong ServerSession;
        public AirlockFeedback Feedback;
    }
    public sealed class AirlockClientState : IComponentData
    {
        public readonly Dictionary<string, AirlockStateRpc> Snapshots = new Dictionary<string, AirlockStateRpc>();
        public uint NextRequest, LastAck;
        public AirlockFeedback Feedback;
        public string LastButton;
        public ulong Session;
    }
    public static class AirlockNetworkContext
    {
        public static bool HasNetworkWorlds
        {
            get { foreach (var w in World.All) if (w.IsCreated && (w.IsClient() || w.IsServer())) return true; return false; }
        }
        public static bool HasServer
        {
            get { foreach (var w in World.All) if (w.IsCreated && w.IsServer()) return true; return false; }
        }
        public static bool Send(World world, AirlockButton button)
        {
            if (world == null || !world.IsCreated || !world.IsClient() || !button || !button.Owner) return false;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<AirlockClientState>());
            if (q.IsEmptyIgnoreFilter) return false;
            var state = em.GetComponentObject<AirlockClientState>(q.GetSingletonEntity());
            if (state.Session == 0) return false;
            using var connections = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            if (connections.CalculateEntityCount() != 1) return false;
            var rpc = em.CreateEntity();
            em.AddComponentData(rpc, new AirlockActionRequestRpc { AirlockId = new FixedString64Bytes(button.Owner.AirlockId),
                ButtonId = new FixedString64Bytes(button.ButtonId), RequestId = ++state.NextRequest, ServerSession = state.Session });
            em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections.GetSingletonEntity() });
            return true;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateBefore(typeof(ServerGameSystem))]
    public partial class ServerAirlockSystem : SystemBase
    {
        private readonly Dictionary<Entity, uint> m_Requests = new Dictionary<Entity, uint>();
        private readonly Dictionary<string, uint> m_Revision = new Dictionary<string, uint>();
        private readonly HashSet<Entity> m_Synchronized = new HashSet<Entity>();
        private readonly List<Entity> m_DeadRequests = new List<Entity>();
        private ulong m_Session;
        private double m_NextBroadcast;
        public ulong Session => m_Session;
        protected override void OnCreate()
        {
            m_Session = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            RequireForUpdate<NetworkTime>();
        }
        protected override void OnUpdate()
        {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<AirlockActionRequestRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var r = request.ValueRO; var connection = received.ValueRO.SourceConnection;
                var result = AirlockFeedback.Unavailable;
                bool validConnection = EntityManager.Exists(connection) && EntityManager.HasComponent<NetworkId>(connection) &&
                    EntityManager.HasComponent<NetworkStreamInGame>(connection) && !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection);
                if (validConnection)
                {
                    m_Requests.TryGetValue(connection, out uint prior);
                    if (r.ServerSession != m_Session || r.RequestId == 0 || r.RequestId <= prior) result = AirlockFeedback.StaleRequest;
                    else
                    {
                        m_Requests[connection] = r.RequestId;
                        var controller = AirlockController.Find(r.AirlockId.ToString());
                        var button = AirlockButton.Find(r.ButtonId.ToString());
                        if (controller && button && button.Owner == controller &&
                            EntityManager.HasComponent<JoinedClient>(connection) && EntityManager.HasComponent<RaidSession>(connection) &&
                            EntityManager.GetComponentData<RaidSession>(connection).Phase == RaidPhase.Active)
                        {
                            var player = EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity;
                            if (EntityManager.Exists(player) && EntityManager.HasComponent<LocalTransform>(player) &&
                                EntityManager.HasComponent<PredictedPlayerGhost>(player) && EntityManager.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth > 0)
                            {
                                var position = (Vector3)EntityManager.GetComponentData<LocalTransform>(player).Position;
                                result = button.InRange(position) ? controller.RequestAuthoritative(button.Command) : AirlockFeedback.OutOfRange;
                            }
                        }
                    }
                    var ack = ecb.CreateEntity();
                    ecb.AddComponent(ack, new AirlockActionAckRpc { AirlockId = r.AirlockId, ButtonId = r.ButtonId,
                        RequestId = r.RequestId, ServerSession = m_Session, Feedback = result });
                    ecb.AddComponent(ack, new SendRpcCommandRequest { TargetConnection = connection });
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var controller in AirlockController.Active) if (controller && controller.Ready) controller.TickAuthoritative(SystemAPI.Time.DeltaTime);

            double now = SystemAPI.Time.ElapsedTime;
            bool periodic = now >= m_NextBroadcast;
            if (periodic) m_NextBroadcast = now + .1;
            foreach (var controller in AirlockController.Active)
            {
                if (!controller || !controller.Ready) continue;
                var s = controller.State;
                bool changed = !m_Revision.TryGetValue(controller.AirlockId, out uint revision) || revision != s.Revision;
                foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithEntityAccess())
                {
                    if (!periodic && !changed && m_Synchronized.Contains(connection)) continue;
                    // At most 10 Hz during motion; new connections receive the complete current state.
                    if (!periodic && m_Synchronized.Contains(connection)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new AirlockStateRpc { AirlockId = new FixedString64Bytes(controller.AirlockId),
                        ServerSession = m_Session, Revision = s.Revision, PressureKPa = s.PressureKPa, OuterFraction = s.OuterFraction,
                        InnerFraction = s.InnerFraction, OuterOpening = s.OuterOpening, InnerOpening = s.InnerOpening,
                        Equalizing = s.Equalizing, TargetInside = s.TargetInside, Feedback = s.Feedback });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
                m_Revision[controller.AirlockId] = s.Revision;
            }
            foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithEntityAccess()) m_Synchronized.Add(connection);
            m_Synchronized.RemoveWhere(e => !EntityManager.Exists(e));
            // Disconnects must not accumulate request state over a long server session.
            m_DeadRequests.Clear();
            foreach (var e in m_Requests.Keys) if (!EntityManager.Exists(e)) m_DeadRequests.Add(e);
            foreach (var e in m_DeadRequests) m_Requests.Remove(e);
            ecb.Playback(EntityManager);
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class ClientAirlockSystem : SystemBase
    {
        private Entity m_State, m_Connection;
        protected override void OnCreate()
        {
            m_State = EntityManager.CreateEntity(); EntityManager.AddComponentObject(m_State, new AirlockClientState());
        }
        protected override void OnUpdate()
        {
            var state = EntityManager.GetComponentObject<AirlockClientState>(m_State);
            SystemAPI.TryGetSingletonEntity<NetworkId>(out var connection);
            if (connection != m_Connection)
            { state.Snapshots.Clear(); state.Session = 0; state.LastAck = state.NextRequest = 0; m_Connection = connection; }
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<AirlockStateRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume(); var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection && r.ServerSession != 0)
                {
                    bool fresh = state.Session != r.ServerSession;
                    if (fresh) { state.Snapshots.Clear(); state.Session = r.ServerSession; state.NextRequest = state.LastAck = 0; }
                    string id = r.AirlockId.ToString();
                    bool newer = !state.Snapshots.TryGetValue(id, out var prior) || r.Revision > prior.Revision;
                    var controller = AirlockController.Find(id);
                    if (newer && controller && (AirlockNetworkContext.HasServer || controller.ApplyNetworkState(r.State, fresh || !state.Snapshots.ContainsKey(id)))) state.Snapshots[id] = r;
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<AirlockActionAckRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume(); var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection && r.ServerSession == state.Session &&
                    r.RequestId > state.LastAck && r.RequestId <= state.NextRequest)
                { state.LastAck = r.RequestId; state.Feedback = r.Feedback; state.LastButton = r.ButtonId.ToString(); AirlockButton.Find(state.LastButton)?.ReceiveFeedback(r.Feedback); }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
    }
}
