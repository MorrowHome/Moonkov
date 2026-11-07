namespace Unity.MP_FPS
{
    // Cadence is sampled once per breath. Changing stamina must not restart its timer every frame.
    public sealed class MoonkovBreathingAudio
    {
        private bool m_Audible;
        private float m_Next;

        public bool Observe(in OxygenBreath breath, float now, bool voiceBusy)
        {
            if (!breath.Audible) { Reset(); return false; }
            if (!m_Audible) { m_Audible = true; m_Next = now + .45f; }
            if (voiceBusy)
            {
                m_Next = System.Math.Max(m_Next, now + .2f);
                return false;
            }
            if (now < m_Next) return false;
            m_Next = now + breath.Interval;
            return true;
        }

        public void Reset() { m_Audible = false; m_Next = 0; }
    }
}
