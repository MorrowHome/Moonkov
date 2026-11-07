using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovCoughAudioSetup
{
    [MenuItem("Moonkov/Audio/Install recorded cough")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        const string path = "Assets/Audio/Moonkov/Clips/Cough/CoughFemale.wav";
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (!importer) throw new InvalidOperationException("Missing " + path);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.preloadAudioData = true; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
        settings.sampleRateOverride = 48000; importer.defaultSampleSettings = settings;
        importer.forceToMono = true; importer.loadInBackground = false;
        var serialized = new SerializedObject(importer); serialized.FindProperty("m_Normalize").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
        var sound = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary").TacticalCough;
        if (!sound) throw new InvalidOperationException("Install combat audio first");
        sound.Clips.Clear(); sound.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
        sound.BasePitchInCents = 0; sound.PitchAndVolumeInfo.PitchMin = sound.PitchAndVolumeInfo.PitchMax = 0;
        sound.PitchAndVolumeInfo.VolumeMin = -12; sound.PitchAndVolumeInfo.VolumeMax = -11;
        sound.Priority = 72; sound.MaxVoices = 4; sound.MinimumInterval = 0;
        sound.DistanceInfo.VolumeDistMax = sound.DistanceInfo.LPF_MaxDistance = RaidCoughSound.Range;
        EditorUtility.SetDirty(sound); AssetDatabase.SaveAssetIfDirty(sound);
    }
}
