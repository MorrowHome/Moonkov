using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Owned by the login presentation. Uses the existing pool and Menu mixer bus.
    // There is no independent timeline: the visible backdrop supplies every cue.
    public sealed class HalosAccessAudio : IDisposable
    {
        public enum Stage { Build, Cut, Blackout, Shutter, Login }
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

        // Also supports an isolated Editor audition with its own pooled sound system.
        public HalosAccessAudio(ISoundSystem system, MoonkovAudioLibrary library)
        { m_System = system; m_Library = library; }

        private bool Ready()
        {
            if (m_Disposed) return false;
            if (m_System != null) return m_Library != null;
            var game = GameManager.Instance;
            if (!Application.isPlaying || game == null || game.IsHeadless || game.SoundSystem == null) return false;
            m_System = game.SoundSystem; m_Library = MoonkovAudio.Library;
            return m_Library != null;
        }

        public void Present(Stage stage, float intensity, float deltaTime)
        {
            if (!Ready()) return;
            if (m_Stage != stage)
            {
                Stop(ref m_Bed, .025f);
                m_RoomGain = 0;
                if (stage == Stage.Cut)
                {
                    StopPops();
                    Replace(ref m_Transition, m_Library.AccessCut);
                }
                else if (stage == Stage.Blackout)
                {
                    StopPops(); Stop(ref m_Transition, .015f);
                }
                else if (stage == Stage.Shutter) Shutter(false);
                m_Stage = stage;
            }
            if (stage == Stage.Build)
                Bed(m_Library.AccessCarrier, .25f + .75f * intensity * intensity);
            else if (stage == Stage.Login)
            {
                m_RoomGain = Mathf.MoveTowards(m_RoomGain, 1, Mathf.Max(0, deltaTime) / .6f);
                m_LoginAge += Mathf.Max(0, deltaTime);
                Bed(m_Library.AccessRoom, m_RoomGain);
            }
        }

        public void TerminalOpened(int newest, float intensity)
        {
            if (newest <= m_LastTerminal) return;
            int first = m_LastTerminal + 1;
            m_LastTerminal = newest;
            if (!Ready() || m_Stage != Stage.Build) return;
            var definition = m_Library.AccessTerminal;
            if (definition == null) return;
            // Every newly visible window gets its own pop, including shared low-FPS ticks.
            // Short tails overlap in a bounded set of handles instead of cutting one another off.
            for (int i = first; i <= newest; i++)
            {
                Stop(ref m_Pops[m_PopSlot], 0);
                m_Pops[m_PopSlot] = m_System.CreateEmitter(definition, Vector3.zero, .65f + .35f * intensity);
                m_PopSlot = (m_PopSlot + 1) % m_Pops.Length;
            }
        }

        public void Shutter(bool closing)
        {
            if (Ready()) Replace(ref m_Transition, closing ? m_Library.AccessClose : m_Library.AccessReveal);
        }

        public void StopShutter() => Stop(ref m_Transition, .015f);

        private void StopPops()
        {
            for (int i = 0; i < m_Pops.Length; i++) Stop(ref m_Pops[i], 0);
        }

        public void LoginFlicker()
        {
            // Only actual brightness changes can trigger a sound, at least 3.5 s apart.
            if (!Ready() || m_Stage != Stage.Login || m_LoginAge < m_NextGlitch) return;
            Replace(ref m_Glitch, m_Library.AccessGlitch);
            m_NextGlitch = m_LoginAge + 3.5f + (++m_GlitchIndex % 4) * .8f;
        }

        private void Bed(SoundDef definition, float gain)
        {
            if (m_Bed?.Handle == null || !m_Bed.Handle.IsValid())
                m_Bed = definition == null ? null : m_System.CreateEmitter(definition, Vector3.zero, gain);
            else m_Bed.Handle.Emitter.SetVolume(gain);
        }

        private void Replace(ref SoundSystem.SoundInfo sound, SoundDef definition)
        {
            Stop(ref sound, .01f);
            sound = definition == null ? null : m_System.CreateEmitter(definition, Vector3.zero);
        }

        private void Stop(ref SoundSystem.SoundInfo sound, float fade)
        {
            if (sound != null) m_System?.Stop(sound, fade);
            sound = null;
        }

        public void SkipIntro()
        {
            StopPops(); Stop(ref m_Transition, .025f); Stop(ref m_Glitch, .015f);
            if (m_Stage == Stage.Login) return;
            Stop(ref m_Bed, .03f);
            m_Stage = Stage.Login;
        }

        public void Suspend()
        {
            Stop(ref m_Bed, 0); StopPops();
            Stop(ref m_Transition, 0); Stop(ref m_Glitch, 0);
            m_RoomGain = 0;
            // Retain stage and terminal index: reattaching must not replay old impact cues.
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            Suspend(); m_Disposed = true;
        }
    }
}
