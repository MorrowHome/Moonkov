using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private Label m_Telemetry;
        private World m_TelemetryWorld;
        private EntityQuery m_TelemetryConnection;
        private float m_TelemetrySeconds;
        private int m_TelemetryFrames;

        private void InitializeTelemetry()
        {
            m_Telemetry = m_Root.Q<Label>("raidTelemetry");
            m_Telemetry.text = "— FPS  — ms";
            m_TelemetrySeconds = 0; m_TelemetryFrames = 0;
        }

        private void UpdateTelemetry(bool visible)
        {
            m_Telemetry.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                m_TelemetrySeconds = 0; m_TelemetryFrames = 0;
                m_Telemetry.text = "— FPS  — ms";
                return;
            }
            // Count rendered frames, independent of prediction ticks and timeScale.
            m_TelemetrySeconds += Time.unscaledDeltaTime;
            m_TelemetryFrames++;
            if (m_TelemetrySeconds < .5f) return;
            int fps = Mathf.RoundToInt(m_TelemetryFrames / m_TelemetrySeconds);
            m_TelemetrySeconds = 0; m_TelemetryFrames = 0;

            if (m_TelemetryWorld != m_World)
            {
                DisposeTelemetry();
                if (m_World != null && m_World.IsCreated)
                {
                    m_TelemetryWorld = m_World;
                    m_TelemetryConnection = m_World.EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<NetworkStreamConnection>(), ComponentType.ReadOnly<NetworkSnapshotAck>());
                }
            }
            string ping = "—";
            if (m_TelemetryWorld != null && m_TelemetryWorld.IsCreated &&
                m_TelemetryConnection.HasSingleton<NetworkStreamConnection>() &&
                m_TelemetryConnection.GetSingleton<NetworkStreamConnection>().CurrentState == ConnectionState.State.Connected)
            {
                float rtt = m_TelemetryConnection.GetSingleton<NetworkSnapshotAck>().EstimatedRTT;
                if (rtt > 0 && !float.IsInfinity(rtt) && !float.IsNaN(rtt)) ping = Mathf.RoundToInt(rtt).ToString();
            }
            m_Telemetry.text = $"{fps} FPS  {ping} ms";
            // Keep the small readout legible when the shared 1920-wide panel scales down.
            float scale = Mathf.Max(1, m_Root.resolvedStyle.width / Mathf.Max(1, Screen.width));
            m_Telemetry.style.fontSize = 12 * scale;
            m_Telemetry.style.left = m_Telemetry.style.top = 16 * scale;
        }

        private void DisposeTelemetry()
        {
            if (m_TelemetryWorld != null && m_TelemetryWorld.IsCreated) m_TelemetryConnection.Dispose();
            m_TelemetryWorld = null;
        }
    }
}
