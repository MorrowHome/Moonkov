// MoonkovPlayerAudio 把当前的呼吸状态交给这里。它安排下一口气的时间，并在有咳嗽或受伤人声时给这些声音留位置。
namespace Unity.MP_FPS {
    // 每次换气时只算一次呼吸间隔。耐力每帧变化时按现有计时把这一口气播完。
    public sealed class MoonkovBreathingAudio {
        private bool m_Audible, m_Exerted;
        private float m_Next;

        public bool Observe(in OxygenBreath breath, float now, bool voiceBusy) {
            if (!breath.Audible) {
                Reset();
                return false;
            }
            // 刚进入可听状态先留一点起始时间。体力骤降时可以把下一声拉近，其他状态变化等到下一口气再选节奏。
            if (!m_Audible) {
                m_Audible = true;
                m_Next = now + .45f;
            } else if (breath.Exerted && !m_Exerted) { m_Next = System.Math.Min(m_Next, now + .08f); }
            m_Exerted = breath.Exerted;
            // 声音已经被受击、咳嗽或上一口气占用时，延后采样时间。人声结束后从新节奏继续。
            if (voiceBusy) {
                m_Next = System.Math.Max(m_Next, now + .2f);
                return false;
            }
            if (now < m_Next) return false;
            m_Next = now + breath.Interval;
            return true;
        }

        public void Reset() {
            m_Audible = false;
            m_Exerted = false;
            m_Next = 0;
        }
    }
}