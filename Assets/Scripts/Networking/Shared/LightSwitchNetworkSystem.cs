// 局内灯开关的网络入口。服务端按真实角色和开关位置处理按键，再同步灯状态和按键结果。没有战局 World 的单独场景可直接操作按钮。
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS.Moon;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS {
    public enum LightSwitchFeedback : byte {
        None,
        Pending,
        Accepted,
        Unavailable,
        OutOfRange,
        Obstructed,
        Expired
    }

    // 客户端发送按钮和会话内编号，不发目标灯的开关值，服务器从实际按钮定义决定操作内容。
    public struct LightSwitchRequestRpc : IRpcCommand {
        public FixedString128Bytes ButtonId;
        public ulong Session;
        public uint RequestId;
    }

    public struct LightSwitchStateRpc : IRpcCommand {
        public FixedString128Bytes ButtonId, LightId;
        public ulong Session;
        public uint Revision;
        public bool Powered;
    }

    public struct LightSwitchAckRpc : IRpcCommand {
        public ulong Session;
        public uint RequestId;
        public LightSwitchFeedback Feedback;
    }

    public sealed class LightSwitchClientState : IComponentData {
        public ulong Session;
        public uint NextRequest, PendingRequest;
        public string LastButton;
        public float FeedbackUntil;
        public LightSwitchFeedback Feedback;
        public readonly Dictionary<(string, string), LightSwitchStateRpc> Snapshots = new();
    }

    public static class LightSwitchNetworkContext {
        public static bool ValidId(string id) {
            return !string.IsNullOrWhiteSpace(id) && Encoding.UTF8.GetByteCount(id) <= 120;
        }

        public static LightSwitchClientState State(World world) {
            if (world == null || !world.IsCreated || !world.IsClient()) return null;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<LightSwitchClientState>());
            return query.HasSingleton<LightSwitchClientState>()
                ? world.EntityManager.GetComponentObject<LightSwitchClientState>(query.GetSingletonEntity())
                : null;
        }

        // 没有战局 World 时可操作本地场景。正式联网时必须等已知服务端会话，发 RPC 后等待反馈。
        public static bool Press(World world, LightSwitchButton button) {
            if (!button || !button.isActiveAndEnabled) return false;
            if (world == null && !AirlockNetworkContext.HasNetworkWorlds) return button.Press();
            var state = State(world);
            if (state == null || state.Session == 0 || !ValidId(button.SwitchId) ||
                LightSwitchButton.Find(button.SwitchId) != button)
                return false;
            if (state.PendingRequest != 0 && Time.unscaledTime < state.FeedbackUntil) return false;
            var em = world.EntityManager;
            using var connections = em.CreateEntityQuery(typeof(NetworkId), typeof(NetworkStreamInGame));
            if (connections.CalculateEntityCount() != 1) return false;
            uint request = ++state.NextRequest;
            if (request == 0) request = ++state.NextRequest;
            var entity = em.CreateEntity();
            em.AddComponentData(entity,
                new LightSwitchRequestRpc {
                    ButtonId = new FixedString128Bytes(button.SwitchId), Session = state.Session, RequestId = request
                });
            em.AddComponentData(entity,
                new SendRpcCommandRequest { TargetConnection = connections.GetSingletonEntity() });
            state.PendingRequest = request;
            state.LastButton = button.SwitchId;
            state.Feedback = LightSwitchFeedback.Pending;
            state.FeedbackUntil = Time.unscaledTime + 2.5f;
            return true;
        }

        public static LightSwitchFeedback Feedback(World world, LightSwitchButton button) {
            var state = State(world);
            if (state == null)
                return AirlockNetworkContext.HasNetworkWorlds
                    ? LightSwitchFeedback.Unavailable
                    : LightSwitchFeedback.None;
            if (state.Session == 0) return LightSwitchFeedback.Unavailable;
            if (state.LastButton != button.SwitchId) return LightSwitchFeedback.None;
            if (state.PendingRequest != 0 && Time.unscaledTime >= state.FeedbackUntil)
                return LightSwitchFeedback.Expired;
            return Time.unscaledTime < state.FeedbackUntil ? state.Feedback : LightSwitchFeedback.None;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ServerLightSwitchSystem : SystemBase {
        private readonly Dictionary<Entity, uint> m_Requests = new();
        private readonly Dictionary<Entity, double> m_LastPress = new();
        private readonly HashSet<Entity> m_Synchronized = new();
        private readonly List<Entity> m_Dead = new();
        private readonly Dictionary<(string, string), LightSwitchStateRpc> m_Snapshots = new();
        private readonly HashSet<(LightCircuitControls, string)> m_Visited = new();
        private ulong m_Session;
        private uint m_Revision;
        private double m_NextBroadcast;

        protected override void OnCreate() {
            m_Session = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
            if (m_Session == 0) m_Session = 1;
            RequireForUpdate<NetworkTime>();
        }

        public static LightSwitchFeedback ValidateAndPress(
            EntityManager em,
            Entity connection,
            LightSwitchButton button) {
            if (!button || !em.HasComponent<JoinedClient>(connection) || !em.HasComponent<RaidSession>(connection) ||
                em.GetComponentData<RaidSession>(connection).Phase != RaidPhase.Active)
                return LightSwitchFeedback.Unavailable;
            var player = em.GetComponentData<JoinedClient>(connection).PlayerEntity;
            if (!em.Exists(player) || !em.HasComponent<LocalTransform>(player) ||
                !em.HasComponent<PredictedPlayerGhost>(player) ||
                em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth <= 0)
                return LightSwitchFeedback.Unavailable;
            var position = (Vector3)em.GetComponentData<LocalTransform>(player).Position;
            if (!button.InRange(position)) return LightSwitchFeedback.OutOfRange;
            // 距离够仍要能从人物眼前看到开关。服务器用同一场景做遮挡检查，再真正触发按钮。
            if (!new RaidInteractionTarget(button).VisibleFrom(position + Vector3.up * 1.5f))
                return LightSwitchFeedback.Obstructed;
            return button.Press() ? LightSwitchFeedback.Accepted : LightSwitchFeedback.Unavailable;
        }

        protected override void OnUpdate() {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<LightSwitchRequestRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var r = request.ValueRO;
                var connection = received.ValueRO.SourceConnection;
                if (EntityManager.Exists(connection) && EntityManager.HasComponent<NetworkId>(connection) &&
                    EntityManager.HasComponent<NetworkStreamInGame>(connection) &&
                    !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    m_Requests.TryGetValue(connection, out uint prior);
                    var feedback = LightSwitchFeedback.Expired;
                    // 匹配当前服务端会话与更大的请求号才考虑执行。一个连接还有最短按键间隔。
                    if (r.Session == m_Session && r.RequestId > prior) {
                        m_Requests[connection] = r.RequestId;
                        double now = SystemAPI.Time.ElapsedTime;
                        if (!m_LastPress.TryGetValue(connection, out double previous) || now - previous >= .15) {
                            m_LastPress[connection] = now;
                            feedback = ValidateAndPress(EntityManager, connection,
                                LightSwitchButton.Find(r.ButtonId.ToString()));
                        }
                    }
                    var ack = ecb.CreateEntity();
                    ecb.AddComponent(ack,
                        new LightSwitchAckRpc { Session = m_Session, RequestId = r.RequestId, Feedback = feedback });
                    ecb.AddComponent(ack, new SendRpcCommandRequest { TargetConnection = connection });
                }
                ecb.DestroyEntity(entity);
            }
            // 定时扫描启用按钮对应的灯路。旧连接只收变化，新连接需拿全当前状态。
            if (SystemAPI.Time.ElapsedTime >= m_NextBroadcast) {
                m_NextBroadcast = SystemAPI.Time.ElapsedTime + .2;
                m_Visited.Clear();
                foreach (var button in LightSwitchButton.Active) {
                    if (!button || !button.Controls || !LightSwitchNetworkContext.ValidId(button.SwitchId) ||
                        LightSwitchButton.Find(button.SwitchId) != button)
                        continue;
                    foreach (string id in button.TargetIds) {
                        if (!LightSwitchNetworkContext.ValidId(id) || !m_Visited.Add((button.Controls, id)) ||
                            !button.Controls.TryGetPowered(id, out bool on))
                            continue;
                        var key = (button.SwitchId, id);
                        // 灯的开关值变化时才生成新修订，发送当前状态无需按历史点击次序重放。
                        bool changed = !m_Snapshots.TryGetValue(key, out var snapshot) || snapshot.Powered != on;
                        if (changed) {
                            snapshot = new LightSwitchStateRpc {
                                Session = m_Session, Revision = ++m_Revision,
                                ButtonId = new FixedString128Bytes(button.SwitchId),
                                LightId = new FixedString128Bytes(id), Powered = on
                            };
                            m_Snapshots[key] = snapshot;
                        }
                        foreach (var (networkId, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                                     .WithAll<NetworkStreamInGame>().WithEntityAccess()) {
                            if (!changed && m_Synchronized.Contains(connection)) continue;
                            var rpc = ecb.CreateEntity();
                            ecb.AddComponent(rpc, snapshot);
                            ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                        }
                    }
                }
                foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>().WithAll<NetworkStreamInGame>()
                             .WithEntityAccess())
                    m_Synchronized.Add(connection);
                m_Synchronized.RemoveWhere(e => !EntityManager.Exists(e));
                m_Dead.Clear();
                foreach (var connection in m_Requests.Keys)
                    if (!EntityManager.Exists(connection))
                        m_Dead.Add(connection);
                foreach (var connection in m_Dead) {
                    m_Requests.Remove(connection);
                    m_LastPress.Remove(connection);
                }
            }
            ecb.Playback(EntityManager);
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class ClientLightSwitchSystem : SystemBase {
        private sealed class PowerBatch {
            public readonly List<((string, string) Key, LightSwitchButton Button)> On = new();
            public readonly List<((string, string) Key, LightSwitchButton Button)> Off = new();
        }

        private Entity m_State, m_Connection;
        private readonly Dictionary<(string, string), LightSwitchButton> m_Applied = new();
        private readonly Dictionary<LightCircuitControls, PowerBatch> m_Batches = new();

        protected override void OnCreate() {
            m_State = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(m_State, new LightSwitchClientState());
        }

        protected override void OnUpdate() {
            var state = EntityManager.GetComponentObject<LightSwitchClientState>(m_State);
            SystemAPI.TryGetSingletonEntity<NetworkId>(out var connection);
            // 换连接或收到新服务端会话时清上次的序号、目标和灯路缓存，新的快照自行重建显示。
            if (connection != m_Connection) {
                state.Session = 0;
                state.NextRequest = state.PendingRequest = 0;
                state.Snapshots.Clear();
                m_Applied.Clear();
                m_Batches.Clear();
                m_Connection = connection;
            }
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<LightSwitchStateRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var r = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection && r.Session != 0) {
                    if (state.Session != r.Session) {
                        state.Session = r.Session;
                        state.NextRequest = state.PendingRequest = 0;
                        state.Snapshots.Clear();
                        m_Applied.Clear();
                    }
                    var key = (r.ButtonId.ToString(), r.LightId.ToString());
                    // 每一对按钮和灯都按最新修订接受，存入待应用。旧快照晚到不能把关掉的灯再点亮。
                    if (!state.Snapshots.TryGetValue(key, out var old) || r.Revision > old.Revision) {
                        state.Snapshots[key] = r;
                        m_Applied.Remove(key);
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (ack, received, entity) in SystemAPI
                         .Query<RefRO<LightSwitchAckRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var r = ack.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection &&
                    r.Session == state.Session && state.PendingRequest != 0 && r.RequestId == state.PendingRequest) {
                    if (!World.IsThinClient()) {
                        if (r.Feedback == LightSwitchFeedback.Accepted)
                            MoonkovAudio.PlayWeaponLayer(MoonkovAudio.Library?.RoomSwitch, Vector3.zero, 1, 0);
                        else
                            MoonkovAudio.Error();
                    }
                    state.PendingRequest = 0;
                    state.Feedback = r.Feedback;
                    state.FeedbackUntil = UnityEngine.Time.unscaledTime + 1.5f;
                }
                ecb.DestroyEntity(entity);
            }
            // Host shares the scene with its server World: never replay a toggle there.
            // 房主场景已经由服务器直接改过。来宾才需要把汇总后的灯状态应用到自己的显示对象。
            if (!AirlockNetworkContext.HasServer) {
                foreach (var batch in m_Batches.Values) {
                    batch.On.Clear();
                    batch.Off.Clear();
                }
                foreach (var pair in state.Snapshots) {
                    if (m_Applied.TryGetValue(pair.Key, out var applied) && applied && applied.isActiveAndEnabled &&
                        applied.Controls && applied.Controls.isActiveAndEnabled)
                        continue;
                    var button = LightSwitchButton.Find(pair.Key.Item1);
                    if (!button || !button.Controls || !button.Controls.isActiveAndEnabled) continue;
                    bool owns = false;
                    foreach (string id in button.TargetIds)
                        if (id == pair.Key.Item2) {
                            owns = true;
                            break;
                        }
                    if (!owns) continue;
                    if (!m_Batches.TryGetValue(button.Controls, out var batch))
                        m_Batches.Add(button.Controls, batch = new PowerBatch());
                    (pair.Value.Powered ? batch.On : batch.Off).Add((pair.Key, button));
                }
                // The baked owner composes room lighting: apply one on/off batch per
                // owner, rather than rebuilding lightmaps once per received fixture.
                foreach (var pair in m_Batches) {
                    ApplyBatch(pair.Key, pair.Value.On, true);
                    ApplyBatch(pair.Key, pair.Value.Off, false);
                }
            }
            ecb.Playback(EntityManager);
        }

        private void ApplyBatch(
            LightCircuitControls controls,
            List<((string, string) Key, LightSwitchButton Button)> entries,
            bool on) {
            if (!controls || entries.Count == 0) return;
            string[] ids = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++) ids[i] = entries[i].Key.Item2;
            // 按同一灯光控制器把所有开启或关闭一起提交，减少相同场景反复刷新。成功后才标记对应已应用。
            if (!controls.TryApply(ids, on ? LightCommand.SetOn : LightCommand.SetOff)) return;
            foreach (var entry in entries) m_Applied[entry.Key] = entry.Button;
        }
    }
}