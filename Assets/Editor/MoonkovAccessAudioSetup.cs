using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovAccessAudioSetup
{
    private const string Root = "Assets/Audio/Moonkov/";

    [MenuItem("Moonkov/Audio/Install opening and login audio")]
    public static void Install()
    {
        if (EditorApplication.isPlaying || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Wait for an idle Editor.");
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "Clips/Access" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            bool loop = path.EndsWith("AccessCarrier.wav") || path.EndsWith("AccessRoom.wav");
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var sample = importer.defaultSampleSettings;
            sample.loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            sample.compressionFormat = loop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            sample.quality = .8f;
            sample.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; sample.sampleRateOverride = 48000;
            sample.preloadAudioData = true; importer.defaultSampleSettings = sample;
            importer.forceToMono = false; importer.loadInBackground = false; importer.SaveAndReimport();
        }
        var lib = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
        if (!lib) throw new InvalidOperationException("Missing audio library.");
        lib.AccessCarrier = Bank("AccessCarrier", -18, true, 1, 1);
        lib.AccessTerminal = Bank("AccessTerminal", -8, false, 1, 8, "AccessTerminal1");
        lib.AccessCut = Bank("AccessCut", -12, false, 1, 1);
        lib.AccessReveal = Bank("AccessReveal", -9, false, 1, 2);
        lib.AccessClose = Bank("AccessClose", -8, false, 1, 1);
        lib.AccessRoom = Bank("AccessRoom", -15, true, 1, 1);
        lib.AccessGlitch = Bank("AccessGlitch", -24, false, 3, 1);
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
        Debug.Log("Opening/login audio installed: 9 stereo clips, 7 Menu banks; fixed-pitch punchy pop and full impact tails.");
    }

    private static SoundDef Bank(string name, float db, bool loop, int variants, int voices, string singleClip = null)
    {
        string path = Root + "SoundDefs/" + name + ".asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        bool create = !sound;
        if (create) sound = ScriptableObject.CreateInstance<SoundDef>();
        sound.name = name; sound.Role = SoundDef.MixRole.Default;
        sound.Priority = loop ? 180 : 100; sound.MaxVoices = voices;
        sound.MinimumInterval = name == "AccessGlitch" ? 3 : 0; sound.Occlusion = false;
        sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
        sound.MixerGroup = SoundMixer.SoundMixerGroup.Menu;
        sound.BasePitchInCents = 0; sound.BaseLowPassCutoff = 0; sound.VolumeScale = 1; sound.PlayCount = 1;
        sound.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast; sound.Clips = new List<AudioClip>();
        for (int i = 1; i <= variants; i++)
        {
            string clipName = variants > 1 ? name + i : singleClip ?? name;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Clips/Access/" + clipName + ".wav");
            if (!clip) throw new InvalidOperationException("Missing access clip " + clipName);
            sound.Clips.Add(clip);
        }
        sound.RepeatInfo = new SoundDef.Repeat { LoopCount = loop ? 0 : 1 };
        sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db, VolumeMax = db };
        sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = 0, DopplerScale = 0 };
        sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter();
        sound.DistortionFilter = new SoundDef.Distortion();
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound); return sound;
    }
}
