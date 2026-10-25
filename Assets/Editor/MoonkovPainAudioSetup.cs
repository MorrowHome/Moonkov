using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovPainAudioSetup
{
    [MenuItem("Moonkov/Audio/Install player pain voice")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        const string path = "Assets/Audio/Moonkov/SoundDefs/PlayerPain.asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path); bool create = !sound;
        if (create) sound = UnityEngine.Object.Instantiate(lib.Hit);
        sound.name = "PlayerPain"; sound.Clips.Clear();
        for (int i = 1; i <= 4; i++)
        {
            string clipPath = "Assets/Audio/Moonkov/Clips/PlayerPain/PlayerPain" + i + ".wav";
            var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Missing " + clipPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000; importer.defaultSampleSettings = settings;
            importer.forceToMono = true; importer.loadInBackground = false; importer.SaveAndReimport();
            sound.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath));
        }
        sound.Role = SoundDef.MixRole.Default; sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX;
        sound.Priority = 60; sound.MaxVoices = 6;
        sound.MinimumInterval = 0; // Rate limiting belongs to each character, not the shared bank.
        sound.Occlusion = true; sound.VolumeScale = 1; sound.BasePitchInCents = 0;
        sound.PitchAndVolumeInfo.VolumeMin = -10; sound.PitchAndVolumeInfo.VolumeMax = -9;
        sound.PitchAndVolumeInfo.PitchMin = sound.PitchAndVolumeInfo.PitchMax = 0;
        sound.DistanceInfo.SpatialBlend = 1; sound.DistanceInfo.VolumeDistMin = 2; sound.DistanceInfo.VolumeDistMax = 22;
        sound.DistanceInfo.LPF_MaxDistance = 22; sound.StartStopInfo = new SoundDef.StartStop();
        sound.RepeatInfo = new SoundDef.Repeat { LoopCount = 1, RepeatMin = 1, RepeatMax = 1 };
        sound.PlayCount = 1; sound.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast;
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound);
        lib.PlayerPain = sound; EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
    }
}
