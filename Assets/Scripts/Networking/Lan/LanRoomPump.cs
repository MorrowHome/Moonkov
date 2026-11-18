// 挂在常驻对象上。每帧驱动跨网收发，每秒让 LanRoomClient 查询一次房间状态。对象销毁时通知客户端结束房间流程。
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed class LanRoomPump : MonoBehaviour {
        private float m_NextPoll;

        private void Update() {
            LanRoomClient.PumpPeerRoom();
            if (Time.unscaledTime >= m_NextPoll) {
                m_NextPoll = Time.unscaledTime + 1;
                LanRoomClient.Poll();
            }
        }

        private void OnDestroy() { LanRoomClient.StopAfterPumpDestroyed(); }
    }
}