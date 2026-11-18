// 角色受击音用命中的网络 tick 去重。快照重复或过旧时不再播，连着受击时也限制喊声频率。
namespace Unity.MP_FPS {
    // 每个角色单独处理。连发时被抑制的喊声不留到之后排队补播。
    public sealed class MoonkovPainAudio {
        public const float MinimumInterval = 1.1f;
        private bool m_Initialized;
        private uint m_Tick;
        private float m_NextTime;

        public bool Observe(uint impactTick, bool alive, float now) {
            // 第一份快照只记受击编号。之后收到新命中时先推进编号，即使没到可播放时间也不会在稍后重复补响。
            if (!m_Initialized) {
                m_Initialized = true;
                m_Tick = impactTick;
                return false;
            }
            bool impact = MoonkovPlayerAudio.NewTick(impactTick, ref m_Tick);
            if (!impact || !alive || now < m_NextTime) return false;
            // 伤痛声有独立的最短间隔。近距离自动连发时每一枪仍可更新受伤，但角色人声可以只响适当次数。
            m_NextTime = now + MinimumInterval;
            return true;
        }
    }
}