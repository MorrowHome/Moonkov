// 局内角落的 FPS、延迟、丢包与连接方式。FPS 用真实渲染帧数，网络读数来自正在使用的客户端连接。
using System.Globalization;
using Unity.Entities;
using Unity.NetCode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private Label m_Telemetry;
        private World m_TelemetryWorld;
        private EntityQuery m_TelemetryConnection;
        private Entity m_TelemetryConnectionEntity;
        private bool m_TelemetryOffline, m_TelemetryLan, m_TelemetryVisible;
        private ulong m_TelemetryReceived;
        private double m_TelemetryLastPacket = double.NegativeInfinity;
        private float m_TelemetrySeconds;
        private int m_TelemetryFrames;
        private const string TelemetryFormat = "{0} FPS  |  Ping {1}\nLoss {2}  |  Connection {3}";

        private void InitializeTelemetry() {
            m_Telemetry = m_Root.Q<Label>("raidTelemetry");
            m_Telemetry.text = "";
            m_TelemetryVisible = false;
            m_TelemetrySeconds = 0;
            m_TelemetryFrames = 0;
        }

        private void UpdateTelemetry(bool visible) {
            m_Telemetry.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) {
                m_TelemetrySeconds = 0;
                m_TelemetryFrames = 0;
                if (m_TelemetryVisible) DisposeTelemetry();
                m_TelemetryVisible = false;
                return;
            }
            // Count rendered frames, independent of prediction ticks and timeScale.
            // UI 运行一次算一帧。把这一小段真实经过的时间累计起来，再除帧数换成 FPS。
            m_TelemetrySeconds += Time.unscaledDeltaTime;
            m_TelemetryFrames++;
            if (m_TelemetryVisible && m_TelemetrySeconds < .5f) return;
            m_TelemetryVisible = true;
            int fps = m_TelemetrySeconds > 0 ? Mathf.RoundToInt(m_TelemetryFrames / m_TelemetrySeconds) : 0;
            m_TelemetrySeconds = 0;
            m_TelemetryFrames = 0;
            // 从现在的客户端 World 找连接，并先区分单人和局域网。上一局留下的句柄和包统计一起释放。
            if (m_TelemetryWorld != m_World) {
                DisposeTelemetry();
                if (m_World != null && m_World.IsCreated) {
                    m_TelemetryWorld = m_World;
                    m_TelemetryConnection = m_World.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<NetworkStreamConnection>());
                    using var offline =
                        m_World.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<OfflineRaidMode>());
                    using var lan = m_World.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<LanRaidMode>());
                    m_TelemetryOffline = !offline.IsEmptyIgnoreFilter;
                    m_TelemetryLan = !lan.IsEmptyIgnoreFilter;
                }
            }
            ReadTelemetryNetwork(out string ping, out string loss, out string method);
            MoonkovLocalization.Set(m_Telemetry, TelemetryFormat,
                fps > 0 ? fps.ToString(CultureInfo.InvariantCulture) : "Measuring", ping, loss, method);
            // Keep the readout legible when the shared 1920-wide panel scales down.
            float scale = Mathf.Max(1, m_Root.resolvedStyle.width / Mathf.Max(1, Screen.width));
            m_Telemetry.style.fontSize = 12 * scale;
            m_Telemetry.style.left = m_Telemetry.style.top = 16 * scale;
            m_Telemetry.style.paddingLeft = m_Telemetry.style.paddingRight = 8 * scale;
            m_Telemetry.style.paddingTop = m_Telemetry.style.paddingBottom = 4 * scale;
        }

        private void ReadTelemetryNetwork(out string ping, out string loss, out string method) {
            ping = loss = "Measuring";
            method = TelemetryConnectionMethod();
            // 单人有内部 World，但没有需要展示的公网往返延迟和丢包。显示本地状态更清楚。
            if (m_TelemetryOffline) {
                ping = "Local";
                loss = "Not applicable";
                return;
            }
            if (m_TelemetryWorld == null || !m_TelemetryWorld.IsCreated ||
                !m_TelemetryConnection.TryGetSingletonEntity<NetworkStreamConnection>(out var entity)) {
                ping = loss = "No connection";
                method = "No connection";
                ResetTelemetryPackets();
                return;
            }
            var connection = m_TelemetryConnection.GetSingleton<NetworkStreamConnection>();
            if (connection.CurrentState != ConnectionState.State.Connected) {
                ping = loss = connection.CurrentState == ConnectionState.State.Disconnected
                    ? "No connection"
                    : "Connecting...";
                if (connection.CurrentState == ConnectionState.State.Disconnected) method = "No connection";
                ResetTelemetryPackets();
                return;
            }
            // 重连会换连接实体。清掉上次的包计数和时间，再开始统计这条连接。
            if (entity != m_TelemetryConnectionEntity) {
                ResetTelemetryPackets();
                m_TelemetryConnectionEntity = entity;
            }
            var em = m_TelemetryWorld.EntityManager;
            if (!em.HasComponent<NetworkSnapshotAck>(entity)) return;
            var ack = em.GetComponentData<NetworkSnapshotAck>(entity);
            var packets = ack.SnapshotPacketLoss;
            double now = Time.realtimeSinceStartupAsDouble;
            if (packets.NumPacketsReceived != m_TelemetryReceived) {
                m_TelemetryReceived = packets.NumPacketsReceived;
                m_TelemetryLastPacket = packets.NumPacketsReceived > 0 ? now : double.NegativeInfinity;
            }
            // 还没收到过一份快照时保留采集中的文案。显示零丢包会让人以为已经测完。
            if (packets.NumPacketsReceived == 0) return;
            // A connected transport can stop delivering snapshots before its timeout expires.
            // 连接可能还没超时，但已经收不到新快照。持续无数据就暂停展示旧延迟和丢包值。
            if (now - m_TelemetryLastPacket > 2) {
                ping = loss = "Waiting for data";
                return;
            }
            if (float.IsFinite(ack.EstimatedRTT) && ack.EstimatedRTT >= 0)
                ping = Mathf.RoundToInt(ack.EstimatedRTT).ToString(CultureInfo.InvariantCulture) + " ms";
            // Session-wide downstream snapshot loss. Do not count low-FPS clobbering or late packets as network loss.
            // 这是会话期间的下行快照丢包比例。用 Netcode 已有统计，不主动发额外测量包。
            loss = (packets.NetworkPacketLossPercent * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        // 显示实际连接使用的路径。例如何时直连看当前 IceSession，是否中转看已进入的线上实例。
        private string TelemetryConnectionMethod() {
            if (m_TelemetryOffline) return "Single player";
            var game = GameManager.GameConnection;
            if (m_TelemetryLan) {
                if (game?.PeerRoom == null) return "LAN";
                return LanRoomClient.IsHost ? "Local" : "P2P direct";
            }
            if (game == null) return "Unknown connection";
            if (game.IceSession != null) return "P2P direct";
            if (game.SessionConnectionType == NetworkType.Relay) return "Relay";
            var lobby = OnlineLobbyClient.Snapshot;
            if (OnlineLobbyClient.InInstance && lobby?.InstanceId == OnlineLobbyClient.InstanceId &&
                lobby.ConnectionPath == Online.OnlineNetworkProtocol.Vps)
                return "VPS relay";
            return "Direct";
        }

        private void ResetTelemetryPackets() {
            m_TelemetryConnectionEntity = Entity.Null;
            m_TelemetryReceived = 0;
            m_TelemetryLastPacket = double.NegativeInfinity;
        }

        private void DisposeTelemetry() {
            if (m_TelemetryWorld != null && m_TelemetryWorld.IsCreated) m_TelemetryConnection.Dispose();
            m_TelemetryWorld = null;
            m_TelemetryOffline = m_TelemetryLan = false;
            ResetTelemetryPackets();
        }
    }
}