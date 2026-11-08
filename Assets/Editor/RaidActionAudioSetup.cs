using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class RaidActionAudioSetup
{
    [MenuItem("Tools/Moonkov/Actions/Install recorded action foley")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play.");
        const string clips = "Assets/Audio/Moonkov/Clips/Actions/";
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { clips.TrimEnd('/') }))
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.PCM; settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride = 48000;
            settings.preloadAudioData = true; importer.forceToMono = true;
            importer.defaultSampleSettings = settings;
            var serialized = new SerializedObject(importer); serialized.FindProperty("m_Normalize").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
        }
        var library = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
        library.EatBite = Bank("EatBite", 3, -12);
        library.DrinkSip = Bank("DrinkSip", 3, -12);
        library.BottleOpen = Bank("BottleOpen", 3, -15);
        library.PacketOpen = Bank("PacketOpen", 1, -15);
        library.InjectorCap = Bank("InjectorCap", 1, -15);
        library.InjectorPress = Bank("InjectorPress", 1, -14);
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        ConsumableAudioSetup.Install();
    }
    private static SoundDef Bank(string name, int count, float db)
    {
        string path = "Assets/Audio/Moonkov/SoundDefs/" + name + ".asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        bool create = !sound;
        if (create) sound = ScriptableObject.CreateInstance<SoundDef>();
        sound.name = name; sound.Clips = new List<AudioClip>();
        for (int i = 1; i <= count; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/Moonkov/Clips/Actions/{name}{i}.wav");
            if (!clip) throw new InvalidOperationException("Missing recorded cue " + name + i);
            sound.Clips.Add(clip);
        }
        sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
        sound.Role = SoundDef.MixRole.Interaction; sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX;
        sound.Priority = 100; sound.MaxVoices = 6; sound.MinimumInterval = 0; sound.Occlusion = true;
        sound.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast; sound.PlayCount = 1; sound.VolumeScale = 1;
        sound.RepeatInfo = new SoundDef.Repeat(); sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db - 1, VolumeMax = db };
        sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = 1, VolumeDistMin = .6f, VolumeDistMax = 8,
            VolumeRolloffMode = AudioRolloffMode.Linear, DopplerScale = 0, LPF_MaxDistance = 8, LPF_MinCutoff = 1800 };
        sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter(); sound.DistortionFilter = new SoundDef.Distortion();
        if (create) AssetDatabase.CreateAsset(sound, path);
        EditorUtility.SetDirty(sound); AssetDatabase.SaveAssetIfDirty(sound); return sound;
    }
}
