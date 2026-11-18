// Halos 登录界面把自己当前的画面阶段传给这里。这里用同一个声音池播放终端弹出、切黑、开合和登录舱室底噪。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    // 由登录演出控制，沿用音源池和 Menu 混音总线。
    // 每个音效都由当前可见的背景通知，声音与画面共用同一条时间线。
    public sealed class HalosAccessAudio : IDisposable {
        public enum Stage {
            Build,
            Cut,
            Blackout,
            Shutter,
            Login
        }

        private ISoundSystem m_System;
        private MoonkovAudioLibrary m_Library;
        private SoundSystem.SoundInfo m_Bed, m_Transition, m_Glitch;
        private readonly SoundSystem.SoundInfo[] m_Pops = new SoundSystem.SoundInfo[8];
        private int m_PopSlot;
        private Stage? m_Stage;
        private int m_LastTerminal = -1, m_GlitchIndex;
        private float m_RoomGain, m_LoginAge, m_NextGlitch = 3.5f;
        private bool m_Disposed;
        public HalosAccessAudio() { }

        // 也支持编辑器隔离试听。试听使用自己的一套音源池。
        public HalosAccessAudio(ISoundSystem system, MoonkovAudioLibrary library) {
            m_System = system;
            m_Library = library;
        }

        private bool Ready() {
            // 界面结束后即使回调晚到也跳过。试听可以注入另一套声音系统，正式界面则等 GameManager 和声音库就绪。
            if (m_Disposed) return false;
            if (m_System != null) return m_Library != null;
            var game = GameManager.Instance;
            if (!Application.isPlaying || game == null || game.IsHeadless || game.SoundSystem == null) return false;
            m_System = game.SoundSystem;
            m_Library = MoonkovAudio.Library;
            return m_Library != null;
        }

        public void Present(Stage stage, float intensity, float deltaTime) {
            if (!Ready()) return;
            // 只有阶段真的切换，才停掉旧底噪并起过渡声。同一阶段继续进来时，只调整已经在播的环境层。
            if (m_Stage != stage) {
                Stop(ref m_Bed, .025f);
                m_RoomGain = 0;
                if (stage == Stage.Cut) {
                    StopPops();
                    Replace(ref m_Transition, m_Library.AccessCut);
                } else if (stage == Stage.Blackout) {
                    StopPops();
                    Stop(ref m_Transition, .015f);
                } else if (stage == Stage.Shutter) { Shutter(false); }
                m_Stage = stage;
            }
            // 终端生成时按画面强度调整电流底噪，进到登录阶段则把舱室声慢慢推高。都与画面阶段共用时间。
            if (stage == Stage.Build) { Bed(m_Library.AccessCarrier, .25f + .75f * intensity * intensity); } else if
                (stage == Stage.Login) {
                m_RoomGain = Mathf.MoveTowards(m_RoomGain, 1, Mathf.Max(0, deltaTime) / .6f);
                m_LoginAge += Mathf.Max(0, deltaTime);
                Bed(m_Library.AccessRoom, m_RoomGain);
            }
        }

        // 用窗口序号补齐一帧内同时出现的终端响声。环形句柄数组限制叠加数量，也让短尾音自然重叠。
        public void TerminalOpened(int newest, float intensity) {
            if (newest <= m_LastTerminal) return;
            int first = m_LastTerminal + 1;
            m_LastTerminal = newest;
            if (!Ready() || m_Stage != Stage.Build) return;
            var definition = m_Library.AccessTerminal;
            if (definition == null) return;
            // 每个刚出现的窗口都播放一次弹出声。即使低帧率下一帧出现多个窗口，也分别播。
            // 在有限数量的播放句柄中保留重叠的短尾音，避免一个新音效立刻切断另一个。
            for (int i = first; i <= newest; i++) {
                Stop(ref m_Pops[m_PopSlot], 0);
                m_Pops[m_PopSlot] = m_System.CreateEmitter(definition, Vector3.zero, .65f + .35f * intensity);
                m_PopSlot = (m_PopSlot + 1) % m_Pops.Length;
            }
        }

        public void Shutter(bool closing) {
            if (Ready()) Replace(ref m_Transition, closing ? m_Library.AccessClose : m_Library.AccessReveal);
        }

        public void StopShutter() { Stop(ref m_Transition, .015f); }

        private void StopPops() {
            for (int i = 0; i < m_Pops.Length; i++) Stop(ref m_Pops[i], 0);
        }

        public void LoginFlicker() {
            // 只有亮度真的变化才播放声音，两次至少间隔 3.5 秒。
            // 登录故障声依附实际亮度闪动，并限制两次之间的时间。这样定时到了也要等画面有动作时才响。
            if (!Ready() || m_Stage != Stage.Login || m_LoginAge < m_NextGlitch) return;
            Replace(ref m_Glitch, m_Library.AccessGlitch);
            m_NextGlitch = m_LoginAge + 3.5f + ++m_GlitchIndex % 4 * .8f;
        }

        private void Bed(SoundDef definition, float gain) {
            if (m_Bed?.Handle == null || !m_Bed.Handle.IsValid())
                m_Bed = definition == null ? null : m_System.CreateEmitter(definition, Vector3.zero, gain);
            else
                m_Bed.Handle.Emitter.SetVolume(gain);
        }

        private void Replace(ref SoundSystem.SoundInfo sound, SoundDef definition) {
            Stop(ref sound, .01f);
            sound = definition == null ? null : m_System.CreateEmitter(definition, Vector3.zero);
        }

        private void Stop(ref SoundSystem.SoundInfo sound, float fade) {
            if (sound != null) m_System?.Stop(sound, fade);
            sound = null;
        }

        // 自动登录等流程可直接跳过开场。先收掉之前响的弹窗与切黑声，再切到登录阶段。
        public void SkipIntro() {
            StopPops();
            Stop(ref m_Transition, .025f);
            Stop(ref m_Glitch, .015f);
            if (m_Stage == Stage.Login) return;
            Stop(ref m_Bed, .03f);
            m_Stage = Stage.Login;
        }

        // 暂时离开画面只停止声音。回到界面时仍记得弹过哪些窗口，可以从原阶段接回。
        public void Suspend() {
            Stop(ref m_Bed, 0);
            StopPops();
            Stop(ref m_Transition, 0);
            Stop(ref m_Glitch, 0);
            m_RoomGain = 0;
            // 记住当前阶段和终端编号。控件重新挂上面板时无需补播旧音效。
        }

        public void Dispose() {
            if (m_Disposed) return;
            Suspend();
            m_Disposed = true;
        }
    }
}