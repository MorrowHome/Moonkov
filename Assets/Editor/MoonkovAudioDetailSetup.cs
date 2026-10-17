using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovAudioDetailSetup
{
    private const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install contact and idle details")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "Clips/Details" }))
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var sample = importer.defaultSampleSettings;
            sample.loadType = AudioClipLoadType.DecompressOnLoad; sample.compressionFormat = AudioCompressionFormat.PCM;
            sample.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; sample.sampleRateOverride = 48000;
            sample.preloadAudioData = true; importer.defaultSampleSettings = sample;
            importer.forceToMono = true; importer.loadInBackground = false; importer.SaveAndReimport();
        }
        var lib = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
        if (!lib) throw new InvalidOperationException("Missing audio library.");
        lib.SuitWallBrush = Bank("SuitWallBrush", -21, 9, 175, SoundDef.MixRole.Foley, 4, .08f);
        lib.SuitWallMetal = Bank("SuitWallMetal", -22, 11, 175, SoundDef.MixRole.Foley, 4, .08f);
        lib.Crawl = Bank("Crawl", -20, 10, 160, SoundDef.MixRole.Foley, 5, .08f);
        lib.StanceChange = Bank("StanceChange", -23, 8, 175, SoundDef.MixRole.Foley, 4, .12f);
        lib.GearMotion = Bank("GearMotion", -25, 7, 185, SoundDef.MixRole.Foley, 3, .15f);
        lib.AlienWallScrape = Bank("AlienWallScrape", -18, 22, 165, SoundDef.MixRole.Foley, 4, .1f);
        lib.AlienWallMetal = Bank("AlienWallMetal", -19, 25, 165, SoundDef.MixRole.Foley, 4, .1f);
        lib.AlienIdleMotion = Bank("AlienIdleMotion", -25, 16, 185, SoundDef.MixRole.Ambience, 3, .3f);
        if (lib.AlienIdle)
        {
            var idle = lib.AlienIdle;
            idle.Role = SoundDef.MixRole.Ambience; idle.Priority = 160; idle.MaxVoices = 3; idle.MinimumInterval = .35f;
            idle.PitchAndVolumeInfo.VolumeMin = -19; idle.PitchAndVolumeInfo.VolumeMax = -18;
            idle.DistanceInfo.VolumeDistMax = 28; idle.DistanceInfo.LPF_MaxDistance = 28;
            EditorUtility.SetDirty(idle); AssetDatabase.SaveAssetIfDirty(idle);
        }
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
        Debug.Log("Contact/idle audio installed: 24 mono PCM clips, 8 banks; creature-local idle budget.");
    }
    private static SoundDef Bank(string name, float db, float range, int priority, SoundDef.MixRole role,
        int voices, float interval)
    {
        string path = Root + "SoundDefs/" + name + ".asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        bool create = !sound;
        if (create) sound = ScriptableObject.CreateInstance<SoundDef>();
        sound.name = name; sound.Role = role; sound.Priority = priority;
        sound.MaxVoices = voices; sound.MinimumInterval = interval; sound.Occlusion = true;
        sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
        sound.MixerGroup = role == SoundDef.MixRole.Foley ? SoundMixer.SoundMixerGroup.Footsteps : SoundMixer.SoundMixerGroup.SFX;
        sound.BasePitchInCents = 0; sound.BaseLowPassCutoff = 0; sound.VolumeScale = 1; sound.PlayCount = 1;
        sound.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast; sound.Clips = new List<AudioClip>();
        for (int i = 1; i <= 3; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Clips/Details/" + name + i + ".wav");
            if (!clip) throw new InvalidOperationException("Missing contact clip " + name + i);
            sound.Clips.Add(clip);
        }
        sound.RepeatInfo = new SoundDef.Repeat(); sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db - 2, VolumeMax = db, PitchMin = -60, PitchMax = 60 };
        sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = 1, VolumeDistMin = 1.2f, VolumeDistMax = range,
            VolumeRolloffMode = AudioRolloffMode.Linear, DopplerScale = 0, LPFRollOffCurveType = Interpolator.CurveType.Linear,
            LPF_MaxDistance = range, LPF_MinCutoff = 1300 };
        sound.LowPassFilter = new SoundDef.Filter { EnableComponent = true, CutoffMin = 11000, CutoffMax = 11000 };
        sound.HighPassFilter = new SoundDef.Filter(); sound.DistortionFilter = new SoundDef.Distortion();
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound); return sound;
    }
}
