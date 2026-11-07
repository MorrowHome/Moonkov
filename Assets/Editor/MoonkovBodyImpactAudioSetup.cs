using System;
using UnityEditor;
using UnityEngine;

public static class MoonkovBodyImpactAudioSetup
{
    [MenuItem("Moonkov/Audio/Install physical body impacts")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        var clips = new AudioClip[3];
        for (int i = 0; i < clips.Length; i++)
        {
            string path = "Assets/Audio/Moonkov/Clips/BodyImpact/BodyImpact" + (i + 1) + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Missing " + path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000; importer.defaultSampleSettings = settings;
            importer.forceToMono = true; importer.loadInBackground = false;
            var serialized = new SerializedObject(importer); serialized.FindProperty("m_Normalize").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        foreach (string name in new[] { "Hit", "MeleeImpact", "RifleBodyImpact", "RevolverBodyImpact", "ShotgunBodyImpact", "SniperBodyImpact" })
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/" + name + ".asset");
            if (!bank) continue; // Compatibility installers may not have created the weapon banks yet.
            bank.Clips.Clear(); bank.Clips.AddRange(clips); bank.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast;
            bank.StartStopInfo.StopDelay = 0; // Preserve the recorded tail instead of chopping at 0.22 s.
            bank.BasePitchInCents = 0; bank.PitchAndVolumeInfo.PitchMin = -35; bank.PitchAndVolumeInfo.PitchMax = 35;
            bank.MixerGroup = SoundMixer.SoundMixerGroup.SFX; bank.Role = SoundDef.MixRole.Foley;
            bank.Priority = 85; bank.MaxVoices = 6; bank.MinimumInterval = .035f; bank.Occlusion = true;
            EditorUtility.SetDirty(bank); AssetDatabase.SaveAssetIfDirty(bank);
        }
        var block = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/MeleeBlocked.asset");
        if (block)
        {
            // The old body-hit clip is an actual recorded shield block. Keep it for the appropriate contact.
            block.Clips.Clear(); block.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Moonkov/Clips/Character/Hit.wav"));
            EditorUtility.SetDirty(block); AssetDatabase.SaveAssetIfDirty(block);
        }
    }
}
