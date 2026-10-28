using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovWeaponCharacterSetup
{
    private const string Clips = "Assets/Audio/Moonkov/Clips/WeaponCharacter/";
    private const string Banks = "Assets/Audio/Moonkov/SoundDefs/";

    [MenuItem("Tools/Moonkov/Weapons/Install revolver shotgun and precision feedback")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        var library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
        string rifle = JsonUtility.ToJson(library.ForWeapon(DollSingerWeapons.Halo));
        Configure(library, DollSingerWeapons.Revolver, "Revolver", -5.5f, -18, 85, 1.8f, .18f, .85f, .055f, 11, .48f);
        Configure(library, DollSingerWeapons.Shotgun, "Shotgun", -5, -18, 85, 2.8f, .28f, .75f, .085f, 9, .52f);
        Configure(library, DollSingerWeapons.Sniper, "Sniper", -4.5f, -16, 120, 4.2f, .35f, .65f, .13f, 7, .98f);
        if (JsonUtility.ToJson(library.ForWeapon(DollSingerWeapons.Halo)) != rifle)
            throw new InvalidOperationException("The accepted red rifle profile changed.");
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        Debug.Log("Installed three distinct reports and shot responses. Red rifle and all weapon mechanics retained.");
    }

    private static void Configure(RifleFeedbackLibrary library, uint id, string name, float bodyDb, float tailDb,
        float range, float pitch, float yaw, float recovery, float hold, float settle, float gesture)
    {
        var profile = library.ForWeapon(id);
        profile.FireBody = Bank(name + "CharacterBody", profile.FireBody, name + "Body", 3, bodyDb, range);
        profile.FireTail = Bank(name + "CharacterTail", profile.FireTail, name + "Tail", 1, tailDb, range * 1.5f);
        profile.FireSnap = null;
        profile.RecoilPitch = pitch; profile.RecoilYaw = yaw;
        profile.RecoilReturnFraction = recovery; profile.RecoilHoldSeconds = hold;
        profile.RecoilSettleSpeed = settle; profile.GestureReleaseSeconds = gesture;
        profile.BodyCrossfade = .045f; profile.TailCrossfade = .08f;
        profile.SustainedBodyGain = 1; profile.BurstPitchRise = 0;
    }

    private static SoundDef Bank(string name, SoundDef template, string stem, int count, float db, float range)
    {
        string path = Banks + name + ".asset";
        var bank = AssetDatabase.LoadAssetAtPath<SoundDef>(path); bool create = !bank;
        if (create) bank = UnityEngine.Object.Instantiate(template);
        bank.name = name; bank.Clips.Clear();
        for (int i = 1; i <= count; i++)
        {
            string clipPath = Clips + stem + (count == 1 ? "" : i.ToString()) + ".wav";
            if (!(AssetImporter.GetAtPath(clipPath) is AudioImporter)) AssetDatabase.ImportAsset(clipPath);
            var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (!importer) throw new InvalidOperationException("Missing recording: " + clipPath);
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.PCM; settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true; settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000; importer.defaultSampleSettings = settings;
            importer.forceToMono = true; importer.loadInBackground = false;
            // Normalize is only exposed through serialization in this Editor.
            using (var serialized = new SerializedObject(importer))
            {
                var normalize = serialized.FindProperty("m_Normalize");
                if (normalize == null) throw new InvalidOperationException("Audio importer normalization is unavailable.");
                normalize.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            importer.SaveAndReimport();
            bank.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath));
        }
        bank.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast; bank.PlayCount = 1;
        bank.PitchAndVolumeInfo.VolumeMin = bank.PitchAndVolumeInfo.VolumeMax = db;
        bank.PitchAndVolumeInfo.PitchMin = -6; bank.PitchAndVolumeInfo.PitchMax = 6;
        bank.DistanceInfo.VolumeDistMax = bank.DistanceInfo.LPF_MaxDistance = range;
        bank.MixerGroup = SoundMixer.SoundMixerGroup.SFX; bank.Role = SoundDef.MixRole.Weapon;
        bank.Priority = count > 1 ? 40 : 75; bank.MaxVoices = 8; bank.MinimumInterval = 0;
        bank.StartStopInfo = new SoundDef.StartStop(); bank.RepeatInfo = new SoundDef.Repeat();
        if (create) AssetDatabase.CreateAsset(bank, path); else EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssetIfDirty(bank); return bank;
    }
}
