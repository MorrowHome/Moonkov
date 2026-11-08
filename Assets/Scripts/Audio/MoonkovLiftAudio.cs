using UnityEngine;

namespace Unity.MP_FPS
{
    // Presentation follows actual cabin/gate motion, including interpolated remote poses.
    public sealed class MoonkovLiftAudio
    {
        private bool m_Initialized, m_Travelling;
        private float m_Travel, m_Gate, m_TravelUntil, m_GateUntil, m_RetryAt;
        private SoundSystem.SoundInfo m_Motor, m_Door;

        public void Update(Transform cabin, float travel, float gate, float now)
        {
            var library = MoonkovAudio.Library;
            if (!library || !cabin) return;
            if (!m_Initialized)
            {
                m_Initialized = true; m_Travel = travel; m_Gate = gate;
                m_TravelUntil = m_GateUntil = now;
                return;
            }
            if (Mathf.Abs(travel - m_Travel) > .00001f) m_TravelUntil = now + .15f;
            if (Mathf.Abs(gate - m_Gate) > .00001f) m_GateUntil = now + .15f;
            m_Travel = travel; m_Gate = gate;
            bool moving = now < m_TravelUntil, opening = now < m_GateUntil;
            var position = cabin.position + Vector3.up;
            if (moving != m_Travelling)
                MoonkovAudio.PlayWeaponLayer(moving ? library.LiftStart : library.LiftStop, position, .8f, 1, follow: cabin);
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
        private static void Stop(ref SoundSystem.SoundInfo sound)
        {
            GameManager.Instance?.SoundSystem?.Stop(sound, .12f); sound = null;
        }
        public void Stop()
        {
            Stop(ref m_Motor); Stop(ref m_Door); m_Initialized = m_Travelling = false; m_RetryAt = 0;
        }
    }
}
