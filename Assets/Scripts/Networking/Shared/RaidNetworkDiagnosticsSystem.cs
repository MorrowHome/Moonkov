// ICE 战局定时写本地网络诊断。这里把 NetCode 的往返时间、输入滞后和 RPC 排队，与进程帧间隔和 ICE 收发量放在同一行，便于判断卡顿来自哪一段。
using System;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS.EmbeddedNetworking;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.MP_FPS {
    // 每十秒取一次本机样本，不额外发探测或 RPC，也不记录地址、账号身份
    // 或 SDP。原生计数指当前进程总体，不能算某个玩家独有。
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(NetworkReceiveSystemGroup))]
    public partial class RaidNetworkDiagnosticsSystem : SystemBase {
        private EntityQuery m_Connections;
        private EmbeddedIceTransport m_Transport;
        private EmbeddedIceStatistics m_Previous;
        private ulong m_PreviousAdapterDrops;
        private double m_NextSample, m_Start, m_LastUpdate, m_MaxGap;
        private int m_Frames;
        private int m_LastFrame = -1;

        protected override void OnCreate() {
            m_Connections = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(),
                ComponentType.ReadOnly<NetworkSnapshotAck>(), ComponentType.ReadOnly<NetworkId>());
            RequireForUpdate(m_Connections);
        }

        protected override void OnStopRunning() { ResetWindow(); }

        private void ResetWindow() {
            m_Transport = null;
            m_Previous = default;
            m_PreviousAdapterDrops = 0;
            m_NextSample = m_Start = m_LastUpdate = m_MaxGap = 0;
            m_Frames = 0;
            m_LastFrame = -1;
        }

        protected override void OnUpdate() {
            // 服务器可能在一次 Unity 帧内连续补几次模拟 tick。
            // Unity 一帧可能模拟多个 NetCode tick。记录真实渲染循环停顿时只按首个 tick 计一次，否则会抬高帧率。
            if (m_LastFrame == UnityEngine.Time.frameCount) return;
            m_LastFrame = UnityEngine.Time.frameCount;
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (m_LastUpdate > 0) m_MaxGap = Math.Max(m_MaxGap, now - m_LastUpdate);
            m_LastUpdate = now;
            m_Frames++;
            if (now < m_NextSample) return;
            m_NextSample = now + 10;
            bool server = World.IsServer();
            var game = GameManager.GameConnection;
            // 找本局实际使用的 ICE 传输，好友房与官方客户端或独立服来源不同。没有活通道时不输出旧统计。
            var transport = game?.PeerRoom?.RaidTransport ??
                            (server ? OnlineIceServer.DiagnosticTransport : game?.IceSession?.Transport);
            if (transport == null || transport.IsDisposed) {
                m_Transport = null;
                m_Start = 0;
                m_Frames = 0;
                m_MaxGap = 0;
                return;
            }
            var sample = transport.GetStatistics();
            ulong adapterDrops = transport.AdapterDrops;
            // 换传输首个样本只作今后一段的起点，不能拿新连接的累计计数减之前连接的计数。
            bool baseline = !ReferenceEquals(transport, m_Transport);
            double seconds = baseline ? 0 : Math.Max(.001, now - m_Start);
            double fps = seconds > 0 ? m_Frames / seconds : 0;
            double receiveRate = seconds > 0 ? Delta(sample.ReceivedBytes, m_Previous.ReceivedBytes) / seconds : 0;
            double sendRate = seconds > 0 ? Delta(sample.SentBytes, m_Previous.SentBytes) / seconds : 0;
            ulong drops = baseline ? 0 : Delta(sample.DroppedPackets, m_Previous.DroppedPackets);
            ulong errors = baseline ? 0 : Delta(sample.SendErrors, m_Previous.SendErrors);
            ulong adapter = baseline ? 0 : Delta(adapterDrops, m_PreviousAdapterDrops);
            string utc = DateTime.UtcNow.ToString("O");
            using var connections = m_Connections.ToEntityArray(Allocator.Temp);
            foreach (var entity in connections) {
                if (EntityManager.GetComponentData<NetworkStreamConnection>(entity).CurrentState !=
                    ConnectionState.State.Connected)
                    continue;
                // 每条连接单独读取往返时间、抖动、输入滞后和排队。原生传输总字节与丢包是一条进程级数据，不能当成单连接。
                var ack = EntityManager.GetComponentData<NetworkSnapshotAck>(entity);
                int queued = EntityManager.HasBuffer<OutgoingRpcDataStreamBuffer>(entity)
                    ? EntityManager.GetBuffer<OutgoingRpcDataStreamBuffer>(entity, true).Length
                    : 0;
                int connection = EntityManager.GetComponentData<NetworkId>(entity).Value;
                string message = string.Format(CultureInfo.InvariantCulture,
                    "[NetDiag] utc={0} role={1} connection={2} baseline={3} " +
                    "rtt_ms={4:F0} jitter_ms={5:F0} loop_fps={6:F1} max_loop_gap_ms={7:F0} " +
                    "command_age_ticks={8:F2} rpc_queue_bytes={9} snapshot_received={10} snapshot_missing={11} " +
                    "ice_process_rx_Bps={12:F0} ice_process_tx_Bps={13:F0} ice_queue={14} " +
                    "ice_drops={15} ice_send_errors={16} adapter_drops={17}", utc, server ? "server" : "client",
                    connection, baseline, ack.EstimatedRTT, ack.DeviationRTT, fps, m_MaxGap * 1000,
                    ack.ServerCommandAge / 256f, queued,
                    server ? "n/a" : ack.SnapshotPacketLoss.NumPacketsReceived.ToString(CultureInfo.InvariantCulture),
                    server
                        ? "n/a"
                        : ack.SnapshotPacketLoss.NumPacketsDroppedNeverArrived.ToString(CultureInfo.InvariantCulture),
                    receiveRate, sendRate, sample.QueuedPackets, drops, errors, adapter);
                // 统计按时间窗输出，没有为诊断发送额外网络包。下个窗累积重新开始。
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
            }
            m_Transport = transport;
            m_Previous = sample;
            m_PreviousAdapterDrops = adapterDrops;
            m_Start = now;
            m_Frames = 0;
            m_MaxGap = 0;
        }

        private static ulong Delta(ulong current, ulong previous) {
            return current >= previous ? current - previous : current;
        }
    }
}