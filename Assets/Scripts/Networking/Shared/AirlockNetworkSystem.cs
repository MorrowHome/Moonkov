// 气闸和电梯由客户端发操作请求，服务端依据实际角色和设备状态裁决。设备快照和操作回执送回客户端，让门、电梯与提示一起跟上。
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS {
    // 请求仅说明按的是哪台设备的哪个按钮。能否按由服务器从当前连接、人物和距离验证。
    public struct AirlockActionRequestRpc : IRpcCommand {
        public FixedString64Bytes AirlockId, ButtonId;
        public uint RequestId;
        public ulong ServerSession;
    }

    // 门缝、压力、均压和反馈一起同步，客户端画门和仪表要用来自同一次修订的数据。
    public struct AirlockStateRpc : IRpcCommand {
        public FixedString64Bytes AirlockId;
        public ulong ServerSession;
        public uint Revision;
        public float PressureKPa, OuterFraction, InnerFraction;
        public bool OuterOpening, InnerOpening, Equalizing, TargetInside;
        public AirlockFeedback Feedback;

        public AirlockState State =>
            new() {
                Revision = Revision, PressureKPa = PressureKPa,
                OuterFraction = OuterFraction, InnerFraction = InnerFraction, OuterOpening = OuterOpening,
                InnerOpening = InnerOpening, Equalizing = Equalizing, TargetInside = TargetInside, Feedback = Feedback
            };
    }

    public struct AirlockActionAckRpc : IRpcCommand {
        public FixedString64Bytes AirlockId, ButtonId;
        public uint RequestId;
        public ulong ServerSession;
        public AirlockFeedback Feedback;
    }

    // 电梯用同一按钮入口，但位置、闸门和目的楼层走独立快照，避免套用气压字段。
    public struct RaidLiftStateRpc : IRpcCommand {
        public FixedString64Bytes LiftId;
        public ulong ServerSession;
        public uint Revision;
        public float Travel, Gate;
        public byte Dock, Destination;
        public RaidLiftPhase Phase;

        public RaidLiftState State =>
            new() {
                Revision = Revision, Travel = Travel, Gate = Gate,
                Dock = Dock, Destination = Destination, Phase = Phase
            };
    }

    public sealed class AirlockClientState : IComponentData {
        public readonly Dictionary<string, AirlockStateRpc> Snapshots = new();
        public readonly Dictionary<string, uint> LiftRevisions = new();
        public uint NextRequest, LastAck;
        public AirlockFeedback Feedback;
        public string LastButton;
        public ulong Session;

        // 换服务器会话时清请求号与每台设备的最新修订。新 World 可以从较小编号重新同步。
        public void ResetSession(ulong session) {
            Snapshots.Clear();
            LiftRevisions.Clear();
            Session = session;
            LastAck = NextRequest = 0;
        }
    }

    public static class AirlockNetworkContext {
        public static bool HasNetworkWorlds {
            get {
                foreach (var w in World.All)
                    if (w.IsCreated && (w.IsClient() || w.IsServer()))
                        return true;
                return false;
            }
        }

        public static bool HasServer {
            get {
                foreach (var w in World.All)
                    if (w.IsCreated && w.IsServer())
                        return true;
                return false;
            }
        }

        public static bool Send(World world, AirlockButton button) {
            return button && button.Owner && Send(world, button.Owner.AirlockId, button.ButtonId);
        }

        // 网络场景从本地客户端发带会话的操作，不能直接改变共享场景中服务端掌管的设备。
        public static bool Send(World world, string ownerId, string buttonId) {
            if (world == null || !world.IsCreated || !world.IsClient()) return false;
            var em = world.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<AirlockClientState>());
            if (q.IsEmptyIgnoreFilter) return false;
            var state = em.GetComponentObject<AirlockClientState>(q.GetSingletonEntity());
            if (state.Session == 0) return false;
            using var connections = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            if (connections.CalculateEntityCount() != 1) return false;
            var rpc = em.CreateEntity();
            em.AddComponentData(rpc, new AirlockActionRequestRpc {
                AirlockId = new FixedString64Bytes(ownerId),
                ButtonId = new FixedString64Bytes(buttonId), RequestId = ++state.NextRequest,
                ServerSession = state.Session
            });
            em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections.GetSingletonEntity() });
            return true;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateBefore(typeof(ServerGameSystem))]
    public partial class ServerAirlockSystem : SystemBase {
        private readonly Dictionary<Entity, uint> m_Requests = new();
        private readonly Dictionary<string, uint> m_Revision = new();
        private readonly HashSet<Entity> m_Synchronized = new();
        private readonly List<Entity> m_DeadRequests = new();
        private ulong m_Session;
        private double m_NextBroadcast;
        public ulong Session => m_Session;

        protected override void OnCreate() {
            m_Session = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            RequireForUpdate<NetworkTime>();
        }

        protected override void OnUpdate() {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<AirlockActionRequestRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var r = request.ValueRO;
                var connection = received.ValueRO.SourceConnection;
                var result = AirlockFeedback.Unavailable;
                bool validConnection = EntityManager.Exists(connection) &&
                                       EntityManager.HasComponent<NetworkId>(connection) &&
                                       EntityManager.HasComponent<NetworkStreamInGame>(connection) &&
                                       !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection);
                if (validConnection) {
                    m_Requests.TryGetValue(connection, out uint prior);
                    // 先检查这个请求属于当前服务器，并为这条连接去重。有效的序号无论结果成功失败都只消费一次。
                    if (r.ServerSession != m_Session || r.RequestId == 0 || r.RequestId <= prior) {
                        result = AirlockFeedback.StaleRequest;
                    } else {
                        m_Requests[connection] = r.RequestId;
                        var controller = AirlockController.Find(r.AirlockId.ToString());
                        var button = AirlockButton.Find(r.ButtonId.ToString());
                        var liftButton = controller ? null : RaidLiftButton.Find(r.ButtonId.ToString());
                        // 按钮必须真注册在请求的设备下。服务端再查角色位置，玩家不能用别处的按钮 ID 操作这台设备。
                        bool airlockControl = controller && button && button.Owner == controller;
                        bool liftControl = liftButton && liftButton.Owner &&
                                           liftButton.Owner.LiftId == r.AirlockId.ToString();
                        if ((airlockControl || liftControl) && EntityManager.HasComponent<JoinedClient>(connection) &&
                            EntityManager.HasComponent<RaidSession>(connection) &&
                            EntityManager.GetComponentData<RaidSession>(connection).Phase == RaidPhase.Active) {
                            var player = EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity;
                            if (EntityManager.Exists(player) && EntityManager.HasComponent<LocalTransform>(player) &&
                                EntityManager.HasComponent<PredictedPlayerGhost>(player) && EntityManager
                                    .GetComponentData<PredictedPlayerGhost>(player).CurrentHealth > 0) {
                                var position = (Vector3)EntityManager.GetComponentData<LocalTransform>(player).Position;
                                result = airlockControl
                                    ?
                                    button.InRange(position)
                                        ? controller.RequestAuthoritative(button.Command)
                                        : AirlockFeedback.OutOfRange
                                    : liftButton.InRange(position)
                                        ? liftButton.Owner.Request(liftButton.Stop)
                                        : AirlockFeedback.OutOfRange;
                            }
                        }
                    }
                    // 回执只告诉发起人这次操作是否成功。门和电梯如何移动由更后面的公共快照同步所有连接。
                    var ack = ecb.CreateEntity();
                    ecb.AddComponent(ack, new AirlockActionAckRpc {
                        AirlockId = r.AirlockId, ButtonId = r.ButtonId,
                        RequestId = r.RequestId, ServerSession = m_Session, Feedback = result
                    });
                    ecb.AddComponent(ack, new SendRpcCommandRequest { TargetConnection = connection });
                }
                ecb.DestroyEntity(entity);
            }
            // 即使这一帧没人点按钮，门与压力也由服务端按计时推进，客户端不自己改玩法规则。
            foreach (var controller in AirlockController.Active)
                if (controller && controller.Ready)
                    controller.TickAuthoritative(SystemAPI.Time.DeltaTime);
            foreach (var lift in RaidLift.Active)
                if (lift && lift.Ready)
                    lift.TickAuthoritative(SystemAPI.Time.DeltaTime);
            double now = SystemAPI.Time.ElapsedTime;
            // 已同步连接按更新间隔收到新状态。刚连接的人先拿到全部设备状态，无需知道之前按过哪些按钮。
            bool periodic = now >= m_NextBroadcast;
            if (periodic) m_NextBroadcast = now + .1;
            foreach (var controller in AirlockController.Active) {
                if (!controller || !controller.Ready) continue;
                var s = controller.State;
                bool changed = !m_Revision.TryGetValue(controller.AirlockId, out uint revision) ||
                               revision != s.Revision;
                foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>()
                             .WithEntityAccess()) {
                    if (!periodic && !changed && m_Synchronized.Contains(connection)) continue;
                    // At most 10 Hz during motion; new connections receive the complete current state.
                    if (!periodic && m_Synchronized.Contains(connection)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new AirlockStateRpc {
                        AirlockId = new FixedString64Bytes(controller.AirlockId),
                        ServerSession = m_Session, Revision = s.Revision, PressureKPa = s.PressureKPa,
                        OuterFraction = s.OuterFraction,
                        InnerFraction = s.InnerFraction, OuterOpening = s.OuterOpening, InnerOpening = s.InnerOpening,
                        Equalizing = s.Equalizing, TargetInside = s.TargetInside, Feedback = s.Feedback
                    });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
                m_Revision[controller.AirlockId] = s.Revision;
            }
            foreach (var lift in RaidLift.Active) {
                if (!lift || !lift.Ready) continue;
                var state = lift.State;
                foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>()
                             .WithEntityAccess()) {
                    if (!periodic && m_Synchronized.Contains(connection)) continue;
                    var rpc = ecb.CreateEntity();
                    ecb.AddComponent(rpc, new RaidLiftStateRpc {
                        LiftId = new FixedString64Bytes(lift.LiftId), ServerSession = m_Session,
                        Revision = state.Revision, Travel = state.Travel, Gate = state.Gate, Dock = state.Dock,
                        Destination = state.Destination, Phase = state.Phase
                    });
                    ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }
            foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>()
                         .WithEntityAccess())
                m_Synchronized.Add(connection);
            m_Synchronized.RemoveWhere(e => !EntityManager.Exists(e));
            // Disconnects must not accumulate request state over a long server session.
            m_DeadRequests.Clear();
            foreach (var e in m_Requests.Keys)
                if (!EntityManager.Exists(e))
                    m_DeadRequests.Add(e);
            foreach (var e in m_DeadRequests) m_Requests.Remove(e);
            ecb.Playback(EntityManager);
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class ClientAirlockSystem : SystemBase {
        private Entity m_State, m_Connection;

        protected override void OnCreate() {
            m_State = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(m_State, new AirlockClientState());
        }

        protected override void OnUpdate() {
            var state = EntityManager.GetComponentObject<AirlockClientState>(m_State);
            SystemAPI.TryGetSingletonEntity<NetworkId>(out var connection);
            // 换连接先清掉旧设备缓存。随后从当前连接收气闸、电梯和操作结果，各按自己的会话和版本接受。
            if (connection != m_Connection) {
                state.ResetSession(0);
                m_Connection = connection;
            }
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<AirlockStateRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection &&
                    r.ServerSession != 0) {
                    bool fresh = state.Session != r.ServerSession;
                    if (fresh) state.ResetSession(r.ServerSession);
                    string id = r.AirlockId.ToString();
                    // 只有版本真的变新才应用门状态。房主客户端与服务器共用实体场景，只保留快照供 UI 用，不从客户端再改一次。
                    bool newer = !state.Snapshots.TryGetValue(id, out var prior) || r.Revision > prior.Revision;
                    var controller = AirlockController.Find(id);
                    if (newer && controller && (AirlockNetworkContext.HasServer ||
                                                controller.ApplyNetworkState(r.State,
                                                    fresh || !state.Snapshots.ContainsKey(id))))
                        state.Snapshots[id] = r;
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<RaidLiftStateRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection &&
                    r.ServerSession != 0) {
                    if (state.Session != r.ServerSession) state.ResetSession(r.ServerSession);
                    string id = r.LiftId.ToString();
                    bool first = !state.LiftRevisions.TryGetValue(id, out uint revision);
                    var lift = RaidLift.Find(id);
                    if ((first || r.Revision > revision) && lift &&
                        (AirlockNetworkContext.HasServer || lift.ApplyNetworkState(r.State, first)))
                        state.LiftRevisions[id] = r.Revision;
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<AirlockActionAckRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection &&
                    // 回执必须对应当前连接已发送且还没收过的请求。接受后提示到原按钮，不影响其他设备的操作反馈。
                    r.ServerSession == state.Session && r.RequestId > state.LastAck &&
                    r.RequestId <= state.NextRequest) {
                    state.LastAck = r.RequestId;
                    state.Feedback = r.Feedback;
                    state.LastButton = r.ButtonId.ToString();
                    if (!World.IsThinClient()) {
                        var button = AirlockButton.Find(state.LastButton);
                        if (button)
                            button.ReceiveFeedback(r.Feedback);
                        else
                            RaidLiftButton.Find(state.LastButton)?.ReceiveFeedback(r.Feedback);
                    }
                }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
    }
}