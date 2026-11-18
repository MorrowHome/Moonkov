// 挂在常驻对象上。每帧驱动跨网收发，每秒让 LanRoomClient 查询一次房间状态。对象销毁时通知客户端结束房间流程。
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed class LanRoomPump : MonoBehaviour {
        private float m_NextPoll;

        private void Update() {
            // P2P 通道的收发和握手每帧推进。低频的房间快照查询单独按真实时间每秒触发一次。
            LanRoomClient.PumpPeerRoom();
            if (Time.unscaledTime >= m_NextPoll) {
                m_NextPoll = Time.unscaledTime + 1;
                LanRoomClient.Poll();
            }
        }

        // 引擎退出也可能经过销毁。把停止网络、等待房主待写结算的工作交回 LanRoomClient。
        private void OnDestroy() { LanRoomClient.StopAfterPumpDestroyed(); }
    }
}