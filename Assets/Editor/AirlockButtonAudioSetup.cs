using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class AirlockButtonAudioSetup
{
    const string ClipPath = "Assets/Audio/Moonkov/Clips/GameFoley/HL2AirlockButton.wav";
    const string BankPath = "Assets/Audio/Moonkov/SoundDefs/AirlockButtonPress.asset";

    [MenuItem("Moonkov/Audio/Install independent airlock button")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if (!clip) throw new InvalidOperationException("Missing " + ClipPath);
        var importer = (AudioImporter)AssetImporter.GetAtPath(ClipPath);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
        settings.sampleRateOverride = 48000; settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false;
        var serialized = new SerializedObject(importer);
        serialized.FindProperty("m_Normalize").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();

        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!lib) throw new InvalidOperationException("Missing audio library");
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(BankPath);
        bool create = !sound;
        if (create) sound = ScriptableObject.CreateInstance<SoundDef>();
        sound.name = "AirlockButtonPress";
        sound.Clips = new List<AudioClip> { clip };
        sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
        sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX; sound.Role = SoundDef.MixRole.Interaction;
        sound.Priority = 100; sound.MaxVoices = 2; sound.MinimumInterval = .08f; sound.Occlusion = false;
        sound.VolumeScale = 1; sound.BasePitchInCents = 0; sound.PlayCount = 1;
        sound.PlaybackType = SoundDef.PlaybackTypes.Sequential;
        sound.RepeatInfo = new SoundDef.Repeat { LoopCount = 1 };
        sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = -10, VolumeMax = -10 };
        sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = 0, DopplerScale = 0 };
        sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter();
        sound.DistortionFilter = new SoundDef.Distortion();
        if (create) AssetDatabase.CreateAsset(sound, BankPath);
        else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound);
        lib.AirlockButtonPress = sound;
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
    }
}
