using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed class LanRoomPump : MonoBehaviour
    {
        private float m_NextPoll;
        private void Update()
        { if (Time.unscaledTime >= m_NextPoll) { m_NextPoll = Time.unscaledTime + 1; LanRoomClient.Poll(); } }
        private void OnDestroy() => LanRoomClient.StopAfterPumpDestroyed();
    }
}
