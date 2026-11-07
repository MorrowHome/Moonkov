using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovDistressAudioSetup
{
    [MenuItem("Moonkov/Audio/Install low health voice")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!lib.PlayerPain) throw new InvalidOperationException("Install player pain voices first");
        const string path = "Assets/Audio/Moonkov/SoundDefs/PlayerGroan.asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path); bool create = !sound;
        if (create) sound = UnityEngine.Object.Instantiate(lib.PlayerPain);
        else EditorUtility.CopySerialized(lib.PlayerPain, sound);
        sound.name = "PlayerGroan";
        sound.Clips.Clear();
        for (int i = 1; i <= 3; i++)
        {
            string clipPath = "Assets/Audio/Moonkov/Clips/PlayerGroan/PlayerGroan" + i + ".wav";
            var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Missing " + clipPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM; settings.preloadAudioData = true;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride = 48000;
            importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false;
            var serialized = new SerializedObject(importer);
            serialized.FindProperty("m_Normalize").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
            sound.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath));
        }
        sound.PitchAndVolumeInfo.VolumeMin = -12; sound.PitchAndVolumeInfo.VolumeMax = -11;
        sound.Priority = 75; sound.MaxVoices = 4;
        sound.DistanceInfo.VolumeDistMax = sound.DistanceInfo.LPF_MaxDistance = 12;
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound);
        lib.PlayerGroan = sound; EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
    }
}
