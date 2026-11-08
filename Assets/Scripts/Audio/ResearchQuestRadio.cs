using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS
{
    internal sealed class ResearchQuestRadio : IDisposable
    {
        private SoundDef m_Definition;
        private ISoundSystem m_System;
        private SoundSystem.SoundInfo m_Playback;
        private double m_End;

        internal bool Playing => m_Playback != null && Time.unscaledTimeAsDouble < m_End;

        internal static string ClipPath(ResearchQuest quest, bool closing, bool chinese) =>
            $"Moonkov/QuestRadio/{(int)quest + 1:00}_{(closing ? "close" : "brief")}_{(chinese ? "zh" : "en")}";

        internal void Play(ResearchQuest quest, bool closing, bool chinese)
        {
            Stop();
            var game = GameManager.Instance;
            if (!Application.isPlaying || !game || game.IsHeadless || game.SoundSystem == null) return;
            var clip = Resources.Load<AudioClip>(ClipPath(quest, closing, chinese));
            if (!clip) return;
            if (!m_Definition)
            {
                m_Definition = ScriptableObject.CreateInstance<SoundDef>();
                m_Definition.name = "Research quest radio";
                m_Definition.hideFlags = HideFlags.HideAndDontSave;
                m_Definition.MixerGroup = SoundMixer.SoundMixerGroup.Menu;
                m_Definition.Priority = 64;
                m_Definition.MaxVoices = 1;
                m_Definition.Clips = new List<AudioClip>(1);
                m_Definition.RepeatInfo = new SoundDef.Repeat();
                m_Definition.StartStopInfo = new SoundDef.StartStop();
                m_Definition.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = 0, VolumeMax = 0 };
                m_Definition.DistanceInfo = new SoundDef.Distance { SpatialBlend = 0 };
                m_Definition.LowPassFilter = new SoundDef.Filter();
                m_Definition.HighPassFilter = new SoundDef.Filter();
                m_Definition.DistortionFilter = new SoundDef.Distortion();
            }
            m_Definition.Clips.Clear();
            m_Definition.Clips.Add(clip);
            m_System = game.SoundSystem;
            m_Playback = m_System.CreateEmitter(m_Definition, Vector3.zero);
            m_End = Time.unscaledTimeAsDouble + clip.length;
        }

        internal void Stop()
        {
            if (m_Playback != null) m_System?.Kill(m_Playback);
            m_Playback = null;
        }

        public void Dispose()
        {
            Stop();
            if (m_Definition) UnityEngine.Object.Destroy(m_Definition);
        }
    }
}
