using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovMeleeVoiceSetup
{
    [MenuItem("Moonkov/Audio/Install boxing effort voices")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!lib.PlayerPain) throw new InvalidOperationException("Install player pain first");
        const string path = "Assets/Audio/Moonkov/SoundDefs/MeleeEffort.asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path); bool create = !sound;
        if (create) sound = UnityEngine.Object.Instantiate(lib.PlayerPain);
        else EditorUtility.CopySerialized(lib.PlayerPain, sound);
        sound.name = "MeleeEffort"; sound.Clips.Clear();
        for (int i = 1; i <= 3; i++)
        {
            string clipPath = "Assets/Audio/Moonkov/Clips/MeleeVoice/MeleeEffort" + i + ".wav";
            var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Missing " + clipPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000; importer.defaultSampleSettings = settings;
            importer.forceToMono = true; importer.loadInBackground = false;
            var serialized = new SerializedObject(importer);
            serialized.FindProperty("m_Normalize").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
            sound.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath));
        }
        sound.PitchAndVolumeInfo.VolumeMin = -10; sound.PitchAndVolumeInfo.VolumeMax = -9;
        sound.PitchAndVolumeInfo.PitchMin = sound.PitchAndVolumeInfo.PitchMax = 0;
        sound.Priority = 70; sound.MaxVoices = 6; sound.MinimumInterval = 0;
        sound.DistanceInfo.VolumeDistMax = sound.DistanceInfo.LPF_MaxDistance = 18;
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound);
        lib.MeleeEffort = sound; EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
    }
}
