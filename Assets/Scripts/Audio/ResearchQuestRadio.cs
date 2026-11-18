// 研究任务界面用这段代码按当前语言选出电台语音，再把故事片段排队播放。任务切换或退出界面时可以停下整队语音。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS {
    internal sealed class ResearchQuestRadio : IDisposable {
        private SoundDef m_Definition;
        private ISoundSystem m_System;
        private SoundSystem.SoundInfo m_Playback;
        private double m_End;
        private readonly Queue<string> m_Queue = new();
        internal bool Playing => m_Queue.Count > 0 || (m_Playback != null && Time.unscaledTimeAsDouble < m_End);

        internal static string ClipPath(ResearchQuest quest, bool closing, bool chinese) {
            return StoryPath($"{ResearchQuestRules.Number(quest):00}_{(closing ? "close" : "brief")}", chinese);
        }

        internal static string StoryPath(string cue, bool chinese) {
            return $"Moonkov/QuestRadio/{cue}_{(chinese ? "zh" : "en")}";
        }

        internal void Play(ResearchQuest quest, bool closing, bool chinese) {
            PlayQueue(!closing && quest == ResearchQuest.EndOfTransmission
                ? new[] {
                    StoryPath("kade", chinese), StoryPath("kadeRefusal", chinese), StoryPath("warning", chinese),
                    ClipPath(quest, false, chinese)
                }
                : new[] { ClipPath(quest, closing, chinese) });
        }

        internal void PlayStory(string cue, bool chinese) { PlayQueue(new[] { StoryPath(cue, chinese) }); }

        internal void PlayStory(string[] cues, bool chinese) {
            Stop();
            foreach (string cue in cues) m_Queue.Enqueue(StoryPath(cue, chinese));
            Update();
        }

        internal void EnqueueStory(string cue, bool chinese) {
            m_Queue.Enqueue(StoryPath(cue, chinese));
            Update();
        }

        private void PlayQueue(string[] paths) {
            Stop();
            foreach (string path in paths) m_Queue.Enqueue(path);
            Update();
        }

        internal void Update() {
            // 上一句还没有到预计片尾，就继续等它播完。播完再取队列中的下一个资源，缺失片段会跳过。
            if (m_Playback != null && Time.unscaledTimeAsDouble < m_End) return;
            m_Playback = null;
            if (m_Queue.Count == 0) return;
            var game = GameManager.Instance;
            if (!Application.isPlaying || !game || game.IsHeadless || game.SoundSystem == null) {
                m_Queue.Clear();
                return;
            }
            AudioClip clip = null;
            while (m_Queue.Count > 0 && !clip) clip = Resources.Load<AudioClip>(m_Queue.Dequeue());
            if (!clip) return;
            // 第一次播放时建临时 SoundDef，后面只换里面的单个音频片段。对白沿用菜单音量通道。
            if (!m_Definition) {
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
            // 记录此次片段的预计片尾，下一帧据此判断该不该读下一个。界面退出能把队列和正在播放的片段一起停掉。
            m_End = Time.unscaledTimeAsDouble + clip.length;
        }

        internal void Stop() {
            m_Queue.Clear();
            if (m_Playback != null) m_System?.Kill(m_Playback);
            m_Playback = null;
        }

        public void Dispose() {
            Stop();
            if (m_Definition) UnityEngine.Object.Destroy(m_Definition);
            m_Definition = null;
        }
    }
}