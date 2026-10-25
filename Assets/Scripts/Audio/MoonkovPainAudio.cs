namespace Unity.MP_FPS
{
    // Per character: never queue cries suppressed during automatic fire.
    public sealed class MoonkovPainAudio
    {
        public const float MinimumInterval = 1.1f;
        private bool m_Initialized;
        private uint m_Tick;
        private float m_NextTime;
        public bool Observe(uint impactTick, bool alive, float now)
        {
            if (!m_Initialized) { m_Initialized = true; m_Tick = impactTick; return false; }
            bool impact = MoonkovPlayerAudio.NewTick(impactTick, ref m_Tick);
            if (!impact || !alive || now < m_NextTime) return false;
            m_NextTime = now + MinimumInterval;
            return true;
        }
    }
}
