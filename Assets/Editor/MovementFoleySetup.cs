using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

// Final movement bindings. Preserve the existing banks and their hearing distances.
public static class MovementFoleySetup
{
    const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install prominent movement sounds")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        Bind(lib.RegolithFootsteps, -5, "CSSFootGravel1", "CSSFootGravel2", "CSSFootGravel3", "CSSFootGravel4");
        Bind(lib.MetalFootsteps, -5, "CSSFootMetal1", "CSSFootMetal2", "CSSFootMetal3", "CSSFootMetal4");
        Bind(lib.HardFootsteps, -5, "CSSFootConcrete1", "CSSFootConcrete2", "CSSFootConcrete3", "CSSFootConcrete4");
        Bind(lib.Cloth, -18, "TarkovPouchHandle1", "TarkovPouchHandle2");
    }
    static void Bind(SoundDef bank, float db, params string[] names)
    {
        if (!bank) throw new InvalidOperationException("Missing movement bank");
        bank.Clips = new List<AudioClip>();
        foreach (var name in names)
        {
            var path = Root + "Clips/GameFoley/" + name + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Missing " + path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride = 48000;
            settings.preloadAudioData = true; importer.defaultSampleSettings = settings;
            importer.forceToMono = true; importer.loadInBackground = false;
            var serialized = new SerializedObject(importer); serialized.FindProperty("m_Normalize").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
            bank.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
        }
        bank.MixerGroup = SoundMixer.SoundMixerGroup.Footsteps; bank.Role = SoundDef.MixRole.Foley;
        bank.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast;
        bank.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db - 1, VolumeMax = db, PitchMin = -25, PitchMax = 25 };
        bank.LowPassFilter = new SoundDef.Filter(); bank.HighPassFilter = new SoundDef.Filter();
        EditorUtility.SetDirty(bank); AssetDatabase.SaveAssetIfDirty(bank);
    }
}
