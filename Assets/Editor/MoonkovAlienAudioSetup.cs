using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovAlienAudioSetup
{
    private const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install species combat audio")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        if (!lib) throw new InvalidOperationException("Missing library.");
        lib.AlienSwing = Bank("AlienSwing", -15, 32, 85, 5, .045f);
        lib.AlienSpit = Bank("AlienSpit", -13, 36, 75, 4, .06f);
        lib.AlienImpact = Bank("AlienImpact", -11, 65, 65, 5, .08f);
        lib.AlienWetMove = Bank("AlienWetMove", -19, 24, 130, 6, .08f);
        lib.AlienHeavyStep = Bank("AlienHeavyStep", -14, 65, 115, 5, .1f);
        lib.AlienGrip = Bank("AlienGrip", -17, 16, 100, 4, .1f);
        lib.AlienBossVoice = Bank("AlienBossVoice", -12, 80, 55, 3, .2f);
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
        // Carapace contacts are audible at ordinary pursuit distances, but do not displace telegraphs.
        lib.AlienContact.Priority = 120; lib.AlienContact.MaxVoices = 8;
        lib.AlienContact.DistanceInfo.VolumeDistMax = 26; lib.AlienContact.DistanceInfo.LPF_MaxDistance = 26;
        EditorUtility.SetDirty(lib.AlienContact); AssetDatabase.SaveAssetIfDirty(lib.AlienContact);
        Debug.Log("Species combat audio installed: seven recorded banks and client snapshot timing.");
    }
    private static SoundDef Bank(string name, float db, float range, int priority, int voices, float interval)
    {
        string path = Root + "SoundDefs/" + name + ".asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        bool create = !sound;
        if (create) sound = UnityEngine.Object.Instantiate(Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary").AlienStrike);
        sound.name = name; sound.Clips.Clear();
        for (int i = 1; i <= 3; i++)
        {
            string clipPath = Root + "Clips/AlienCombat/" + name + i + ".wav";
            var importer = (AudioImporter)AssetImporter.GetAtPath(clipPath);
            if (!importer) throw new InvalidOperationException("Missing clip " + clipPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000; importer.defaultSampleSettings = settings;
            importer.forceToMono = true; importer.loadInBackground = false; importer.SaveAndReimport();
            sound.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath));
        }
        sound.Role = name == "AlienWetMove" || name == "AlienHeavyStep" ? SoundDef.MixRole.Foley : SoundDef.MixRole.Creature;
        sound.MixerGroup = sound.Role == SoundDef.MixRole.Foley ? SoundMixer.SoundMixerGroup.Footsteps : SoundMixer.SoundMixerGroup.SFX;
        sound.Priority = priority; sound.MaxVoices = voices; sound.MinimumInterval = interval; sound.Occlusion = true;
        sound.PitchAndVolumeInfo.VolumeMin = db - 2; sound.PitchAndVolumeInfo.VolumeMax = db;
        sound.DistanceInfo.VolumeDistMin = name == "AlienBossVoice" ? 5 : 1.8f;
        sound.DistanceInfo.VolumeDistMax = range; sound.DistanceInfo.LPF_MaxDistance = range;
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound); return sound;
    }
}
