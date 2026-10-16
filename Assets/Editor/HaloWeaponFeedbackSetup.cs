using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

/// <summary>Reuses licensed shipped clips. Creates missing assets without overwriting mix/Inspector edits.</summary>
public static class HaloWeaponFeedbackSetup
{
    private const string Definitions = "Assets/Audio/Moonkov/SoundDefs/";
    private const string LibraryPath = "Assets/Resources/Moonkov/RifleFeedback.asset";

    [MenuItem("Tools/Moonkov/Weapons/Create Missing Weapon Feedback")]
    public static void CreateMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        RifleFeedbackSetup.CreateMissing();
        var library = AssetDatabase.LoadAssetAtPath<RifleFeedbackLibrary>(LibraryPath);
        library.Revolver = Install(library.Revolver, Create("Revolver", "RevolverFire", -9, 0, 90,
            "RevolverEnergy", -21, 100, null, 0, 0, .14f, .26f, 1.1f, 1.2f, 1, new Color(1, .94f, .8f)));
        library.Shotgun = Install(library.Shotgun, Create("Shotgun", "Assets/Audio/Sounddefs/Weapons/Shotgun/Shoot/SoundDef_Shoot_Shotgun.asset", -8, -180, 80,
            "HaloSnap", -24, -350, "RevolverEnergy", -24, -250, .16f, .28f, .75f, 1.1f, .45f, new Color(.88f, 1, .87f)));
        library.Sniper = Install(library.Sniper, Create("Sniper", "RevolverFire", -5.5f, -320, 120,
            "HaloSnap", -19, 120, "HaloFire", -23, -720, .20f, .32f, 1.6f, 2.1f, 1.3f, new Color(.72f, .94f, 1)));
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Moonkov/Weapons/Install Shooting Audiovisual Revision")]
    public static void InstallShootingRevision()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        const string clips = "Assets/Audio/Moonkov/Clips/Weapons/";
        foreach (string name in new[] { "RiflePunch1", "RiflePunch2", "RiflePunch3", "RevolverPunch1", "RevolverPunch2",
            "RevolverPunch3", "SniperPunch1", "SniperPunch2", "SniperPunch3", "DischargeCrack", "SniperAftershock" })
        {
            string path = clips + name + ".wav";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = false;
            var settings = importer.defaultSampleSettings;
            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings; importer.SaveAndReimport();
        }
        var library = AssetDatabase.LoadAssetAtPath<RifleFeedbackLibrary>(LibraryPath);
        var rifleBody = ShotDefinition("RifleShotBody", -6, 90, 18, "RiflePunch1", "RiflePunch2", "RiflePunch3");
        var rifleSnap = ShotDefinition("RifleShotSnap", -27, 90, 12, "DischargeCrack");
        var revolverBody = ShotDefinition("RevolverShotBody", -5, 90, 12, "RevolverPunch1", "RevolverPunch2", "RevolverPunch3");
        var revolverSnap = ShotDefinition("RevolverShotSnap", -28, 90, 10, "DischargeCrack");
        var sniperBody = ShotDefinition("SniperShotBody", -4, 120, 10, "SniperPunch1", "SniperPunch2", "SniperPunch3");
        var sniperSnap = ShotDefinition("SniperShotSnap", -25, 120, 8, "DischargeCrack");
        var sniperTail = ShotDefinition("SniperShotTail", -22, 120, 5, "SniperAftershock");
        sniperTail.StartStopInfo.DelayMin = sniperTail.StartStopInfo.DelayMax = .018f;
        EditorUtility.SetDirty(sniperTail); AssetDatabase.SaveAssetIfDirty(sniperTail);
        library.Rifle = new HaloWeaponFeedbackProfile { FireBody = rifleBody, FireSnap = rifleSnap,
            RockImpact = library.RockImpact, MetalImpact = library.MetalImpact, BodyImpact = library.BodyImpact,
            HitConfirm = library.HitConfirm, KillConfirm = library.KillConfirm, BodyCrossfade = .045f,
            SustainedBodyGain = .94f, BurstPitchRise = 20 };
        library.Revolver.FireBody = revolverBody; library.Revolver.FireSnap = revolverSnap;
        library.Revolver.BodyCrossfade = .06f; library.Revolver.SustainedBodyGain = 1; library.Revolver.BurstPitchRise = 0;
        library.Sniper.FireBody = sniperBody; library.Sniper.FireSnap = sniperSnap; library.Sniper.FireTail = sniperTail;
        library.Sniper.BodyCrossfade = .09f; library.Sniper.TailCrossfade = .12f;
        library.Sniper.SustainedBodyGain = 1; library.Sniper.BurstPitchRise = 0;
        // The shotgun's clips, levels and playback path are deliberately preserved.
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
        foreach (var pair in new[] { ("HaloWeapon", rifleBody, rifleSnap), ("RevolverWeapon", revolverBody, revolverSnap),
            ("SniperWeapon", sniperBody, sniperSnap) })
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/" + pair.Item1 + ".asset");
            weapon.WeaponFireSfx = pair.Item2; weapon.WeaponFireLayerSfx = pair.Item3;
            EditorUtility.SetDirty(weapon); AssetDatabase.SaveAssetIfDirty(weapon);
        }
    }

    private static SoundDef ShotDefinition(string name, float level, float range, float pitchSpread, params string[] clips)
    {
        var sound = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<SoundDef>(Definitions + "HaloFire.asset"));
        sound.name = name; sound.Clips = new System.Collections.Generic.List<AudioClip>();
        foreach (var clip in clips) sound.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Moonkov/Clips/Weapons/" + clip + ".wav"));
        sound.VolumeScale = 1; sound.BasePitchInCents = 0;
        sound.PlaybackType = SoundDef.PlaybackTypes.Random;
        sound.PitchAndVolumeInfo.VolumeMin = level - .5f; sound.PitchAndVolumeInfo.VolumeMax = level;
        sound.PitchAndVolumeInfo.PitchMin = -pitchSpread; sound.PitchAndVolumeInfo.PitchMax = pitchSpread;
        sound.StartStopInfo.DelayMin = sound.StartStopInfo.DelayMax = sound.StartStopInfo.StopDelay = 0;
        sound.StartStopInfo.StartOffsetPercentMin = sound.StartStopInfo.StartOffsetPercentMax = 0;
        sound.DistanceInfo.SpatialBlend = 1; sound.DistanceInfo.VolumeDistMax = range;
        sound.DistanceInfo.LPFRollOffCurveType = Interpolator.CurveType.Linear;
        sound.DistanceInfo.LPF_MinCutoff = 2200; sound.DistanceInfo.LPF_MaxDistance = range;
        sound.LowPassFilter.EnableComponent = true; sound.LowPassFilter.CutoffMin = sound.LowPassFilter.CutoffMax = 19000;
        sound.HighPassFilter.EnableComponent = false; sound.DistortionFilter.EnableComponent = false;
        var existing = AssetDatabase.LoadAssetAtPath<SoundDef>(Definitions + name + ".asset");
        if (!existing) { AssetDatabase.CreateAsset(sound, Definitions + name + ".asset"); return sound; }
        EditorUtility.CopySerialized(sound, existing); UnityEngine.Object.DestroyImmediate(sound);
        EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing); return existing;
    }

    private static HaloWeaponFeedbackProfile Install(HaloWeaponFeedbackProfile current, HaloWeaponFeedbackProfile defaults)
    {
        // Unity may deserialize a newly added inline class as an empty instance instead of null.
        if (current == null || !(current.FireBody || current.FireSnap || current.FireTail || current.RockImpact ||
            current.MetalImpact || current.BodyImpact || current.HitConfirm || current.KillConfirm)) return defaults;
        current.FireBody = current.FireBody ? current.FireBody : defaults.FireBody;
        current.FireSnap = current.FireSnap ? current.FireSnap : defaults.FireSnap;
        current.FireTail = current.FireTail ? current.FireTail : defaults.FireTail;
        current.RockImpact = current.RockImpact ? current.RockImpact : defaults.RockImpact;
        current.MetalImpact = current.MetalImpact ? current.MetalImpact : defaults.MetalImpact;
        current.BodyImpact = current.BodyImpact ? current.BodyImpact : defaults.BodyImpact;
        current.HitConfirm = current.HitConfirm ? current.HitConfirm : defaults.HitConfirm;
        current.KillConfirm = current.KillConfirm ? current.KillConfirm : defaults.KillConfirm;
        return current;
    }

    private static HaloWeaponFeedbackProfile Create(string prefix, string body, float bodyDb, float bodyPitch, float range,
        string snap, float snapDb, float snapPitch, string tail, float tailDb, float tailPitch,
        float hitDuration, float killDuration, float scale, float speed, float count, Color color)
    {
        bool sniper = prefix == "Sniper", shotgun = prefix == "Shotgun";
        float impactPitch = sniper ? -420 : shotgun ? -220 : 140;
        return new HaloWeaponFeedbackProfile
        {
            FireBody = Define(prefix + "ShotBody", body, bodyDb, bodyPitch, range),
            FireSnap = Define(prefix + "ShotSnap", snap, snapDb, snapPitch, range, .12f),
            FireTail = tail == null ? null : Define(prefix + "ShotTail", tail, tailDb, tailPitch, range,
                sniper ? .65f : .22f, sniper ? .04f : .055f),
            RockImpact = Define(prefix + "RockImpact", "HaloImpact", sniper ? -14 : -21, impactPitch, 35, .32f),
            MetalImpact = Define(prefix + "MetalImpact", "HaloSnap", sniper ? -17 : -23, impactPitch, 35, .26f),
            BodyImpact = Define(prefix + "BodyImpact", "HaloImpact", sniper ? -19 : -25, impactPitch - 200, 25, .22f),
            HitConfirm = Define(prefix + "HitConfirm", "HaloSnap", sniper ? -24 : -28,
                sniper ? -350 : shotgun ? -200 : 700, 0, sniper ? .18f : .10f),
            KillConfirm = Define(prefix + "KillConfirm", sniper ? "Confirm" : "HaloSnap", -23,
                sniper ? -250 : shotgun ? -550 : -150, 0, sniper ? .32f : .18f),
            HitDuration = hitDuration, KillDuration = killDuration, HitColor = color,
            HitLineWidth = sniper ? 2.2f : shotgun ? 1.8f : 1.6f, HitOuterRadius = sniper ? 12 : shotgun ? 11 : 10,
            FragmentScale = scale, FragmentSpeed = speed, FragmentCount = count,
            SurfaceSoundInterval = shotgun ? .075f : 0, ImpactFlash = sniper
        };
    }

    private static SoundDef Define(string name, string source, float peakDb, float pitch, float range,
        float duration = 0, float delay = 0)
    {
        string path = Definitions + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        if (existing) return existing;
        var template = AssetDatabase.LoadAssetAtPath<SoundDef>(source.StartsWith("Assets/") ? source : Definitions + source + ".asset");
        if (!template) throw new InvalidOperationException("Missing audio source: " + source);
        var sound = UnityEngine.Object.Instantiate(template); sound.name = name;
        // Use actual imported PCM peak rather than inheriting wildly different source levels.
        // Three sniper layers sum to < .72 of full scale before the mix bus, preserving headroom.
        float peak = 0;
        foreach (var clip in sound.Clips)
        {
            // Legacy FPSSample streaming clips cannot expose GetData. Use unity gain as
            // a conservative peak bound; do not reimport shared audio just to calibrate it.
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad) { peak = Mathf.Max(peak, 1); continue; }
            clip.LoadAudioData();
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0)) throw new InvalidOperationException("Cannot measure audio: " + clip.name);
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
        }
        if (peak < .0001f) throw new InvalidOperationException("Silent source: " + source);
        sound.VolumeScale = 1 / peak;
        sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX;
        sound.BasePitchInCents = pitch;
        sound.PitchAndVolumeInfo.VolumeMin = peakDb - .5f;
        sound.PitchAndVolumeInfo.VolumeMax = peakDb;
        sound.PitchAndVolumeInfo.PitchMin = -20; sound.PitchAndVolumeInfo.PitchMax = 20;
        sound.DistanceInfo.SpatialBlend = range == 0 ? 0 : 1;
        sound.DistanceInfo.VolumeDistMin = 1.5f; sound.DistanceInfo.VolumeDistMax = Mathf.Max(1.5f, range);
        sound.DistanceInfo.DopplerScale = 0;
        sound.StartStopInfo.StartOffsetPercentMin = sound.StartStopInfo.StartOffsetPercentMax = 0;
        sound.StartStopInfo.DelayMin = sound.StartStopInfo.DelayMax = delay;
        sound.StartStopInfo.StopDelay = duration;
        sound.RepeatInfo.LoopCount = sound.RepeatInfo.RepeatMin = sound.RepeatInfo.RepeatMax = 1;
        AssetDatabase.CreateAsset(sound, path);
        return sound;
    }
}
