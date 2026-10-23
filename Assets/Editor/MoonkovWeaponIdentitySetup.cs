using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovWeaponIdentitySetup
{
    const string Clips = "Assets/Audio/Moonkov/Clips/WeaponIdentity/";
    const string Banks = "Assets/Audio/Moonkov/SoundDefs/";
    public static readonly string[] Names = { "Halo", "Revolver", "Shotgun", "Sniper", "Cicada", "Trine", "Aegis", "Vesper", "Thorn", "Sundown", "Rondo", "Tempest", "Nocturne" };
    [MenuItem("Moonkov/Audio/Install weapon identities")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode");
        var library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
        library.WeaponProfiles = new WeaponFeedbackEntry[Names.Length];
        for (int i = 0; i < Names.Length; i++)
        {
            uint id = (uint)i + 2;
            var profile = JsonUtility.FromJson<HaloWeaponFeedbackProfile>(JsonUtility.ToJson(library.BaseForWeapon(id)));
            string name = Names[i];
            float range = id == 6 ? 55 : id == 5 || id == 11 ? 120 : 85;
            profile.FireBody = Bank(name + "IdentityBody", library.Rifle.FireBody, name + "Body", 3, -6, range, 40);
            // The transient is baked into each body; sharing a generic crack would obscure identity.
            profile.FireSnap = null;
            profile.FireTail = Bank(name + "IdentityTail", library.Sniper.FireTail, name + "Tail", 1, -23, range, 75);
            profile.BodyCrossfade = id == 6 || id == 7 || id == 12 ? .018f : id == 8 ? .035f : .055f;
            profile.TailCrossfade = id == 6 || id == 7 ? .04f : .075f;
            profile.SustainedBodyGain = id == 6 ? .90f : id == 8 ? .96f : .94f;
            profile.BurstPitchRise = 0; // Preserve each weapon's recorded pitch under sustained fire.
            library.WeaponProfiles[i] = new WeaponFeedbackEntry { WeaponId = id, Profile = profile };
        }
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        Debug.Log("Installed thirteen individual weapon reports (three body variants and one tail each).");
    }
    static SoundDef Bank(string name, SoundDef template, string stem, int count, float db, float range, int priority)
    {
        string path = Banks + name + ".asset";
        var bank = AssetDatabase.LoadAssetAtPath<SoundDef>(path); bool create = !bank;
        if (create) bank = UnityEngine.Object.Instantiate(template);
        else EditorUtility.CopySerialized(template, bank);
        bank.name = name; bank.Clips.Clear();
        for (int v = 1; v <= count; v++)
        {
            string clipPath = Clips + stem + (count == 1 ? "" : v.ToString()) + ".wav";
            var importer = (AudioImporter)AssetImporter.GetAtPath(clipPath);
            if (!importer) throw new InvalidOperationException("Missing " + clipPath);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; settings.sampleRateOverride = 48000;
            importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false; importer.SaveAndReimport();
            bank.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath));
        }
        bank.MixerGroup = SoundMixer.SoundMixerGroup.SFX; bank.Role = SoundDef.MixRole.Weapon;
        bank.Priority = priority; bank.MaxVoices = 8; bank.MinimumInterval = 0;
        bank.VolumeScale = 1; bank.BasePitchInCents = 0; bank.Occlusion = true;
        bank.PitchAndVolumeInfo.VolumeMin = db; bank.PitchAndVolumeInfo.VolumeMax = db;
        bank.PitchAndVolumeInfo.PitchMin = -12; bank.PitchAndVolumeInfo.PitchMax = 12;
        bank.DistanceInfo.SpatialBlend = 1; bank.DistanceInfo.VolumeDistMin = 2;
        bank.DistanceInfo.VolumeDistMax = range; bank.DistanceInfo.LPF_MaxDistance = range;
        bank.StartStopInfo = new SoundDef.StartStop(); bank.RepeatInfo = new SoundDef.Repeat();
        bank.PlayCount = 1; bank.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast;
        if (create) AssetDatabase.CreateAsset(bank, path); else EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssetIfDirty(bank); return bank;
    }
}
