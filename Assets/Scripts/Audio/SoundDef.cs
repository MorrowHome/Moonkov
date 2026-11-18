// 这是可在 Unity 里配置的音效配方。它把音频片段、播放方式、距离衰减、混音分组和限频放在一起，交给 SoundSystem 使用。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "Sound", menuName = "SoundDef/Create", order = 10000)]
public class SoundDef : ScriptableObject {
    public enum PlaybackTypes {
        Random,
        RandomNotLast,
        Sequential,
        User
    }

    [Serializable]
    public class Filter {
        public bool EnableComponent;
        [Range(0, 22000)] public float CutoffMin = 22000.0f;
        [Range(0, 22000)] public float CutoffMax = 22000.0f;
    }

    [Serializable]
    public class Distortion {
        public bool EnableComponent;
        [Range(0.0f, 1.0f)] public float DistortionMin = 0f;
        [Range(0.0f, 1.0f)] public float DistortionMax = 0f;
    }

    [Serializable]
    // 一个音效可以从片段中段起播，也能先等一会儿再响或限定时长。装填等动作能借用同一个录音的不同片段。
    public class StartStop {
        [Range(0, 10.0f)] public float DelayMin = 0.0f;
        [Range(0, 10.0f)] public float DelayMax = 0.0f;
        [Space(5)] [Range(0, 100)] public int StartOffsetPercentMin = 0;
        [Range(0, 100)] public int StartOffsetPercentMax = 0;
        [Space(5)] [Range(0, 60)] public float StopDelay = 0.0f;
    }

    [Serializable]
    public class PitchAndVolume {
        [Range(-60.0f, 0.0f)] public float VolumeMin = -6.0f;
        [Range(-60.0f, 0.0f)] public float VolumeMax = -6.0f;
        [Space(5)] [Range(-8000, 8000.0f)] public float PitchMin = 0.0f;
        [Range(-8000, 8000.0f)] public float PitchMax = 0.0f;
    }

    [Serializable]
    public class Repeat {
        [Range(0, 10)] public int LoopCount = 1;
        [Space(5)] [Range(1, 20)] public int RepeatMin = 1;
        [Range(1, 20)] public int RepeatMax = 1;
    }

    [Serializable]
    // 离听者远了可以同时变轻、变闷或改变空间感。这些参数由声源按实际位置算，给每类音效单独调。
    public class Distance {
        [Range(0.1f, 100.0f)] public float VolumeDistMin = 1.5f;
        [Range(0.1f, 100.0f)] public float VolumeDistMax = 30.0f;
        [Space(5)] [Range(0.0f, 1.0f)] public float SpatialBlend = 1.0f;

        [Space(5)] [Range(0.0f, 1.0f)]
        public float
            DopplerScale = 0.0f; // 0 = no doppler effect applied to pitch. 1 = full doppler effect applied to pitch

        [Space(5)] public AudioRolloffMode VolumeRolloffMode = AudioRolloffMode.Linear;
        [Space(5)] public Interpolator.CurveType LPFRollOffCurveType;
        [Range(0, 22000)] public float LPF_MinCutoff;
        [Range(0f, 100.0f)] public float LPF_MaxDistance;
        public Interpolator.CurveType HPFRollOffCurveType;
        [Space(5)] [Range(0, 22000)] public float HPF_MinCutoff;
        [Range(0f, 100.0f)] public float HPF_MaxDistance;
        [Space(5)] public Interpolator.CurveType SpatialBlendCurveType;
        [Range(0f, 100.0f)] public float SpatialBlend_MaxDistance;
        [Space(5)] [Range(-1.0f, 1.0f)] public float PanMin = 0.0f;
        [Range(-1.0f, 1.0f)] public float PanMax = 0.0f;
    }

#if UNITY_EDITOR
    [Header("Editor Only")] public float EditorVolume = 1;
    public AudioMixer UnityAudioMixer;
#endif
    // Role 决定开枪时哪些音效临时降音量，Priority 决定声音池满了先让谁腾位置。两者用于不同的调度环节。
    public enum MixRole {
        Default,
        Weapon,
        Foley,
        Creature,
        Ambience,
        Suit,
        Interaction
    }

    [Header("Playback budget / mix")] public MixRole Role;
    [Range(0, 256)] public int Priority = 128;
    [Min(0)] public int MaxVoices;
    [Min(0)] public float MinimumInterval;
    public bool Occlusion;
    [Header("Sound Definition")] public SoundMixer.SoundMixerGroup MixerGroup;
    [Space(5)] public float BasePitchInCents = 0;
    public float VolumeScale = 1;
    public float BaseLowPassCutoff = 0;
    [Space(10)] public PlaybackTypes PlaybackType;
    // 一次发声能并行借用若干声源叠层。片段选择有顺序、随机等方式，具体由 SoundGameObject 跟当前任务一起处理。
    public int PlayCount = 1;
    [Space(10)] public List<AudioClip> Clips;
    [Space(10)] public Repeat RepeatInfo;
    [Space(5)] public StartStop StartStopInfo;
    [Space(5)] public PitchAndVolume PitchAndVolumeInfo;
    [Space(5)] public Distance DistanceInfo;
    [Space(5)] public Filter LowPassFilter;
    public Filter HighPassFilter;
    [Space(5)] public Distortion DistortionFilter;

    // 保存资源时把最小值限制在同组最大值以内。这样运行时抽取随机延时、偏移和滤波参数时都会有有效的范围。
    public void OnValidate() {
        PitchAndVolumeInfo.VolumeMin = PitchAndVolumeInfo.VolumeMin > PitchAndVolumeInfo.VolumeMax
            ? PitchAndVolumeInfo.VolumeMax
            : PitchAndVolumeInfo.VolumeMin;
        DistanceInfo.VolumeDistMin = DistanceInfo.VolumeDistMin > DistanceInfo.VolumeDistMax
            ? DistanceInfo.VolumeDistMax
            : DistanceInfo.VolumeDistMin;
        PitchAndVolumeInfo.PitchMin = PitchAndVolumeInfo.PitchMin > PitchAndVolumeInfo.PitchMax
            ? PitchAndVolumeInfo.PitchMax
            : PitchAndVolumeInfo.PitchMin;
        StartStopInfo.DelayMin = StartStopInfo.DelayMin > StartStopInfo.DelayMax
            ? StartStopInfo.DelayMax
            : StartStopInfo.DelayMin;
        RepeatInfo.RepeatMin =
            RepeatInfo.RepeatMin > RepeatInfo.RepeatMax ? RepeatInfo.RepeatMax : RepeatInfo.RepeatMin;
        DistanceInfo.PanMin = DistanceInfo.PanMin > DistanceInfo.PanMax ? DistanceInfo.PanMax : DistanceInfo.PanMin;
        LowPassFilter.CutoffMin = LowPassFilter.CutoffMin > LowPassFilter.CutoffMax
            ? LowPassFilter.CutoffMax
            : LowPassFilter.CutoffMin;
        HighPassFilter.CutoffMin = HighPassFilter.CutoffMin > HighPassFilter.CutoffMax
            ? HighPassFilter.CutoffMax
            : HighPassFilter.CutoffMin;
        DistortionFilter.DistortionMin = DistortionFilter.DistortionMin > DistortionFilter.DistortionMax
            ? DistortionFilter.DistortionMax
            : DistortionFilter.DistortionMin;
        StartStopInfo.StartOffsetPercentMin = StartStopInfo.StartOffsetPercentMin > StartStopInfo.StartOffsetPercentMax
            ? StartStopInfo.StartOffsetPercentMax
            : StartStopInfo.StartOffsetPercentMin;
    }
}