using System.Globalization;
using Unity.Entities;
using Unity.NetCode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
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

        private void InitializeTelemetry()
        {
            m_Telemetry = m_Root.Q<Label>("raidTelemetry");
            m_Telemetry.text = "";
            m_TelemetryVisible = false;
            m_TelemetrySeconds = 0; m_TelemetryFrames = 0;
        }

        private void UpdateTelemetry(bool visible)
        {
            m_Telemetry.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                m_TelemetrySeconds = 0; m_TelemetryFrames = 0;
                if (m_TelemetryVisible) DisposeTelemetry();
                m_TelemetryVisible = false;
                return;
            }
            // Count rendered frames, independent of prediction ticks and timeScale.
            m_TelemetrySeconds += Time.unscaledDeltaTime;
            m_TelemetryFrames++;
            if (m_TelemetryVisible && m_TelemetrySeconds < .5f) return;
            m_TelemetryVisible = true;
            int fps = m_TelemetrySeconds > 0 ? Mathf.RoundToInt(m_TelemetryFrames / m_TelemetrySeconds) : 0;
            m_TelemetrySeconds = 0; m_TelemetryFrames = 0;

            if (m_TelemetryWorld != m_World)
            {
                DisposeTelemetry();
                if (m_World != null && m_World.IsCreated)
                {
                    m_TelemetryWorld = m_World;
                    m_TelemetryConnection = m_World.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<NetworkStreamConnection>());
                    using var offline = m_World.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<OfflineRaidMode>());
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

        private void ReadTelemetryNetwork(out string ping, out string loss, out string method)
        {
            ping = loss = "Measuring";
            method = TelemetryConnectionMethod();
            if (m_TelemetryOffline)
            {
                ping = "Local"; loss = "Not applicable";
                return;
            }
            if (m_TelemetryWorld == null || !m_TelemetryWorld.IsCreated ||
                !m_TelemetryConnection.TryGetSingletonEntity<NetworkStreamConnection>(out var entity))
            {
                ping = loss = "No connection"; method = "No connection";
                ResetTelemetryPackets();
                return;
            }
            var connection = m_TelemetryConnection.GetSingleton<NetworkStreamConnection>();
            if (connection.CurrentState != ConnectionState.State.Connected)
            {
                ping = loss = connection.CurrentState == ConnectionState.State.Disconnected ? "No connection" : "Connecting...";
                if (connection.CurrentState == ConnectionState.State.Disconnected) method = "No connection";
                ResetTelemetryPackets();
                return;
            }
            if (entity != m_TelemetryConnectionEntity)
            {
                ResetTelemetryPackets();
                m_TelemetryConnectionEntity = entity;
            }
            var em = m_TelemetryWorld.EntityManager;
            if (!em.HasComponent<NetworkSnapshotAck>(entity)) return;
            var ack = em.GetComponentData<NetworkSnapshotAck>(entity);
            var packets = ack.SnapshotPacketLoss;
            double now = Time.realtimeSinceStartupAsDouble;
            if (packets.NumPacketsReceived != m_TelemetryReceived)
            {
                m_TelemetryReceived = packets.NumPacketsReceived;
                m_TelemetryLastPacket = packets.NumPacketsReceived > 0 ? now : double.NegativeInfinity;
            }
            if (packets.NumPacketsReceived == 0) return;
            // A connected transport can stop delivering snapshots before its timeout expires.
            if (now - m_TelemetryLastPacket > 2)
            {
                ping = loss = "Waiting for data";
                return;
            }
            if (float.IsFinite(ack.EstimatedRTT) && ack.EstimatedRTT >= 0)
                ping = Mathf.RoundToInt(ack.EstimatedRTT).ToString(CultureInfo.InvariantCulture) + " ms";
            // Session-wide downstream snapshot loss. Do not count low-FPS clobbering or late packets as network loss.
            loss = (packets.NetworkPacketLossPercent * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private string TelemetryConnectionMethod()
        {
            if (m_TelemetryOffline) return "Single player";
            var game = GameManager.GameConnection;
            if (m_TelemetryLan)
            {
                if (game?.PeerRoom == null) return "LAN";
                return LanRoomClient.IsHost ? "Local" : "P2P direct";
            }
            if (game == null) return "Unknown connection";
            if (game.IceSession != null) return "P2P direct";
            if (game.SessionConnectionType == NetworkType.Relay) return "Relay";
            var lobby = OnlineLobbyClient.Snapshot;
            if (OnlineLobbyClient.InInstance && lobby?.InstanceId == OnlineLobbyClient.InstanceId &&
                lobby.ConnectionPath == Online.OnlineNetworkProtocol.Vps) return "VPS relay";
            return "Direct";
        }

        private void ResetTelemetryPackets()
        {
            m_TelemetryConnectionEntity = Entity.Null;
            m_TelemetryReceived = 0;
            m_TelemetryLastPacket = double.NegativeInfinity;
        }

        private void DisposeTelemetry()
        {
            if (m_TelemetryWorld != null && m_TelemetryWorld.IsCreated) m_TelemetryConnection.Dispose();
            m_TelemetryWorld = null;
            m_TelemetryOffline = m_TelemetryLan = false;
            ResetTelemetryPackets();
        }
    }
}
