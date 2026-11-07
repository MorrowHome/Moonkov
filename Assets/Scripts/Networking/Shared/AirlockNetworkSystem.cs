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
    public struct RaidLiftStateRpc : IRpcCommand
    {
        public FixedString64Bytes LiftId;
        public ulong ServerSession;
        public uint Revision;
        public float Travel, Gate;
        public byte Dock, Destination;
        public RaidLiftPhase Phase;
        public RaidLiftState State => new RaidLiftState { Revision = Revision, Travel = Travel, Gate = Gate,
            Dock = Dock, Destination = Destination, Phase = Phase };
    }
    public sealed class AirlockClientState : IComponentData
    {
        public readonly Dictionary<string, AirlockStateRpc> Snapshots = new Dictionary<string, AirlockStateRpc>();
        public readonly Dictionary<string, uint> LiftRevisions = new Dictionary<string, uint>();
        public uint NextRequest, LastAck;
        public AirlockFeedback Feedback;
        public string LastButton;
        public ulong Session;

        public void ResetSession(ulong session)
        {
            Snapshots.Clear();
            LiftRevisions.Clear();
            Session = session;
            LastAck = NextRequest = 0;
        }
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
        public static bool Send(World world, AirlockButton button) =>
            button && button.Owner && Send(world, button.Owner.AirlockId, button.ButtonId);

        public static bool Send(World world, string ownerId, string buttonId)
        {
            if (world == null || !world.IsCreated || !world.IsClient()) return false;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<AirlockClientState>());
            if (q.IsEmptyIgnoreFilter) return false;
            var state = em.GetComponentObject<AirlockClientState>(q.GetSingletonEntity());
            if (state.Session == 0) return false;
            using var connections = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            if (connections.CalculateEntityCount() != 1) return false;
            var rpc = em.CreateEntity();
            em.AddComponentData(rpc, new AirlockActionRequestRpc { AirlockId = new FixedString64Bytes(ownerId),
                ButtonId = new FixedString64Bytes(buttonId), RequestId = ++state.NextRequest, ServerSession = state.Session });
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
                        var liftButton = controller ? null : RaidLiftButton.Find(r.ButtonId.ToString());
                        bool airlockControl = controller && button && button.Owner == controller;
                        bool liftControl = liftButton && liftButton.Owner && liftButton.Owner.LiftId == r.AirlockId.ToString();
                        if ((airlockControl || liftControl) &&
                            EntityManager.HasComponent<JoinedClient>(connection) && EntityManager.HasComponent<RaidSession>(connection) &&
                            EntityManager.GetComponentData<RaidSession>(connection).Phase == RaidPhase.Active)
                        {
                            var player = EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity;
                            if (EntityManager.Exists(player) && EntityManager.HasComponent<LocalTransform>(player) &&
                                EntityManager.HasComponent<PredictedPlayerGhost>(player) && EntityManager.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth > 0)
                            {
                                var position = (Vector3)EntityManager.GetComponentData<LocalTransform>(player).Position;
                                result = airlockControl
                                    ? button.InRange(position) ? controller.RequestAuthoritative(button.Command) : AirlockFeedback.OutOfRange
                                    : liftButton.InRange(position) ? liftButton.Owner.Request(liftButton.Stop) : AirlockFeedback.OutOfRange;
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
            foreach (var lift in RaidLift.Active) if (lift && lift.Ready) lift.TickAuthoritative(SystemAPI.Time.DeltaTime);

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
            foreach (var lift in RaidLift.Active)
            {
                if (!lift || !lift.Ready) continue;
                var state = lift.State;
                foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>().WithEntityAccess())
                {
                    if (!periodic && m_Synchronized.Contains(connection)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new RaidLiftStateRpc { LiftId = new FixedString64Bytes(lift.LiftId), ServerSession = m_Session,
                        Revision = state.Revision, Travel = state.Travel, Gate = state.Gate, Dock = state.Dock,
                        Destination = state.Destination, Phase = state.Phase });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
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
            { state.ResetSession(0); m_Connection = connection; }
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<AirlockStateRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume(); var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection && r.ServerSession != 0)
                {
                    bool fresh = state.Session != r.ServerSession;
                    if (fresh) state.ResetSession(r.ServerSession);
                    string id = r.AirlockId.ToString();
                    bool newer = !state.Snapshots.TryGetValue(id, out var prior) || r.Revision > prior.Revision;
                    var controller = AirlockController.Find(id);
                    if (newer && controller && (AirlockNetworkContext.HasServer || controller.ApplyNetworkState(r.State, fresh || !state.Snapshots.ContainsKey(id)))) state.Snapshots[id] = r;
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<RaidLiftStateRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection && r.ServerSession != 0)
                {
                    if (state.Session != r.ServerSession) state.ResetSession(r.ServerSession);
                    string id = r.LiftId.ToString();
                    bool first = !state.LiftRevisions.TryGetValue(id, out uint revision);
                    var lift = RaidLift.Find(id);
                    if ((first || r.Revision > revision) && lift && (AirlockNetworkContext.HasServer || lift.ApplyNetworkState(r.State, first)))
                        state.LiftRevisions[id] = r.Revision;
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<AirlockActionAckRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume(); var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection && r.ServerSession == state.Session &&
                    r.RequestId > state.LastAck && r.RequestId <= state.NextRequest)
                {
                    state.LastAck = r.RequestId; state.Feedback = r.Feedback; state.LastButton = r.ButtonId.ToString();
                    if (!World.IsThinClient())
                    {
                        var button = AirlockButton.Find(state.LastButton);
                        if (button) button.ReceiveFeedback(r.Feedback);
                        else RaidLiftButton.Find(state.LastButton)?.ReceiveFeedback(r.Feedback);
                    }
                }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
    }
}
