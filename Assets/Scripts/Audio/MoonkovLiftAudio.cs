// RaidLift 把轿厢和门的实际进度传进来。这里根据进度是否还在变，给门和电机分别起停循环声，因此平滑过的远端动作也能对上声音。
using UnityEngine;

namespace Unity.MP_FPS {
    // Presentation follows actual cabin/gate motion, including interpolated remote poses.
    public sealed class MoonkovLiftAudio {
        private bool m_Initialized, m_Travelling;
        private float m_Travel, m_Gate, m_TravelUntil, m_GateUntil, m_RetryAt;
        private SoundSystem.SoundInfo m_Motor, m_Door;

        public void Update(Transform cabin, float travel, float gate, float now) {
            var library = MoonkovAudio.Library;
            if (!library || !cabin) return;
            // 第一帧只记录舱体和门的位置。途中进入场景时，静止在任意位置的部件不会被误认为刚启动。
            if (!m_Initialized) {
                m_Initialized = true;
                m_Travel = travel;
                m_Gate = gate;
                m_TravelUntil = m_GateUntil = now;
                return;
            }
            // 两次状态之间有一点移动就延长短时窗口。快照暂时没变化时，循环音仍能连续跟着真正的开门或移动。
            if (Mathf.Abs(travel - m_Travel) > .00001f) m_TravelUntil = now + .15f;
            if (Mathf.Abs(gate - m_Gate) > .00001f) m_GateUntil = now + .15f;
            m_Travel = travel;
            m_Gate = gate;
            bool moving = now < m_TravelUntil, opening = now < m_GateUntil;
            var position = cabin.position + Vector3.up;
            // 开始和停止运动拿来播单次提示。电机和门分别维护循环句柄，池子忙时按间隔再次申请。
            if (moving != m_Travelling)
                MoonkovAudio.PlayWeaponLayer(moving ? library.LiftStart : library.LiftStop, position, .8f, 1,
                    follow: cabin);
            m_Travelling = moving;
            if (!moving) Stop(ref m_Motor);
            if (!opening) Stop(ref m_Door);
            if (now < m_RetryAt) return;
            m_RetryAt = now + .35f;
            if (moving && m_Motor?.Handle?.IsValid() != true)
                m_Motor = MoonkovAudio.PlayWeaponLayer(library.LiftMotor, position, 1, 1, follow: cabin);
            if (opening && m_Door?.Handle?.IsValid() != true)
                m_Door = MoonkovAudio.PlayWeaponLayer(library.LiftGate, position, 1, 1, follow: cabin);
        }

        private static void Stop(ref SoundSystem.SoundInfo sound) {
            GameManager.Instance?.SoundSystem?.Stop(sound, .12f);
            sound = null;
        }

        public void Stop() {
            Stop(ref m_Motor);
            Stop(ref m_Door);
            m_Initialized = m_Travelling = false;
            m_RetryAt = 0;
        }
    }
}