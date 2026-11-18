// 局内 HUD 发出请求时记下战局和请求编号，收到同一笔请求的回执后才决定播开始、成功或失败。同一阶段只提醒一次。
namespace Unity.MP_FPS
{
    // Only locally submitted requests may produce feedback. Progress snapshots are silent.
    public sealed class MoonkovActionAudio
    {
        private int m_Raid;
        private uint m_Request;
        private SoundDef m_Success, m_Start;
        private bool m_Started;
        public void Track(int raid, uint request, SoundDef success, SoundDef start = null)
        { m_Raid = raid; m_Request = request; m_Success = success; m_Start = start; m_Started = false; }
        public void Reset() { m_Request = 0; m_Success = m_Start = null; }
        public SoundDef Observe(int raid, uint request, bool active, bool failed, SoundDef error)
        {
            if (m_Request == 0 || raid != m_Raid || request != m_Request) return null;
            if (failed) { Reset(); return error; }
            if (active)
            {
                if (m_Started) return null;
                m_Started = true; return m_Start;
            }
            var result = m_Success; Reset(); return result;
        }
    }
}
