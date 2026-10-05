using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using Mixer = SoundMixer.SoundMixerGroup;

/// <summary>
/// Authors the 22 Moonkov SoundDefs from one readable table.
///
/// Source of truth: this table plus Assets/Audio/Moonkov/Clips. Running the menu item
/// rebuilds the .asset files, so Inspector edits to these definitions are overwritten.
///
/// The peak levels below are calibrated, not guessed: LocalData/AudioRebuild/
/// calibrate_levels.py measures every clip's peak/RMS and resolves a level per role from
/// a target mix. The shipped values were measurably broken (the Halo rifle played about
/// 23 dB below the revolver, and landing was quieter than a footstep).
///
/// Audio: Sonniss GDC 2024 Game Audio Bundle. See Sources.json / Edits.json.
/// </summary>
public static class MoonkovAudioSetup
{
    private const string Clips = "Assets/Audio/Moonkov/Clips/";
    private const string Definitions = "Assets/Audio/Moonkov/SoundDefs/";
    private const string LibraryPath = "Assets/Resources/Moonkov/AudioLibrary.asset";
    private const string TemplatePath = "Assets/Audio/Sounddefs/Player/Footsteps/SoundDef_Footstep.asset";

    private sealed class Row
    {
        public string Name;
        public Mixer Mixer;
        public float Spatial;       // 0 = 2D, 1 = fully positional
        public float Db;            // peak level of the clip, in dB relative to full scale
        public float SpreadDb = 1f; // random per-play level variation
        public float Range;         // audible distance for positional sounds
        public float PitchCents = 45f;
        // 0 = loop forever, 1 = play once, 2 = twice ...  Only the ambience bed loops.
        public int Loop = 1;
        public string[] Clips;
    }

    /// <summary>
    /// Peak levels were calibrated against the shipped mix, then corrected where that mix
    /// was inconsistent. Sequence matters: rows are rebuilt in order.
    /// </summary>
    private static readonly Row[] Table =
    {
        // ---- interface: 2D, Menu bus. Hover is deliberately far below Click. ----
        new Row { Name = "Click", Mixer = Mixer.Menu, Spatial = 0f, Db = -18f, PitchCents = 20f,
                  Clips = new[] { "UI/Click.wav" } },
        new Row { Name = "Hover", Mixer = Mixer.Menu, Spatial = 0f, Db = -31f, SpreadDb = .5f, PitchCents = 20f,
                  Clips = new[] { "UI/Hover.wav" } },
        // Raid / extraction success: resonant metallic gong strike.
        new Row { Name = "Confirm", Mixer = Mixer.Menu, Spatial = 0f, Db = -14f, PitchCents = 10f,
                  Clips = new[] { "UI/Confirm.wav" } },
        new Row { Name = "Error", Mixer = Mixer.Menu, Spatial = 0f, Db = -15f, PitchCents = 15f,
                  Clips = new[] { "UI/Error.wav" } },
        new Row { Name = "Notification", Mixer = Mixer.Menu, Spatial = 0f, Db = -20f, PitchCents = 15f,
                  Clips = new[] { "UI/Notification.wav" } },

        // ---- interaction ----
        new Row { Name = "Equipment", Mixer = Mixer.SFX, Spatial = 0f, Db = -19f, PitchCents = 40f,
                  Clips = new[] { "Interaction/Equipment.wav" } },
        new Row { Name = "Container", Mixer = Mixer.Menu, Spatial = 0f, Db = -21f, PitchCents = 30f,
                  Clips = new[] { "Interaction/Container.wav" } },
        // Deployment confirmation, layered: success gong body + android order acknowledgement.
        new Row { Name = "DeployConfirm", Mixer = Mixer.SFX, Spatial = 1f, Db = -13f, Range = 22f,
                  PitchCents = 0f, SpreadDb = 0f, Clips = new[] { "Interaction/DeployConfirm.wav" } },

        // ---- character ----
        new Row { Name = "Jump", Mixer = Mixer.Footsteps, Spatial = 1f, Db = -24f, Range = 12f,
                  PitchCents = 55f, Clips = new[] { "Character/Jump.wav" } },
        new Row { Name = "Land", Mixer = Mixer.Footsteps, Spatial = 1f, Db = -17f, Range = 20f,
                  SpreadDb = 2f, PitchCents = 50f,
                  Clips = new[] { "Character/LandA.wav", "Character/LandB.wav" } },
        new Row { Name = "Hit", Mixer = Mixer.SFX, Spatial = 1f, Db = -13f, Range = 20f,
                  SpreadDb = 2f, PitchCents = 50f, Clips = new[] { "Character/Hit.wav" } },
        new Row { Name = "Death", Mixer = Mixer.SFX, Spatial = 1f, Db = -12f, Range = 26f,
                  SpreadDb = 2f, PitchCents = 50f, Clips = new[] { "Character/Death.wav" } },
        new Row { Name = "RegolithFootsteps", Mixer = Mixer.Footsteps, Spatial = 1f, Db = -19f, Range = 10f,
                  SpreadDb = 2.5f, PitchCents = 70f,
                  Clips = new[] { "Character/RegolithStep1.wav", "Character/RegolithStep2.wav",
                                  "Character/RegolithStep3.wav", "Character/RegolithStep4.wav" } },
        new Row { Name = "MetalFootsteps", Mixer = Mixer.Footsteps, Spatial = 1f, Db = -19f, Range = 12f,
                  SpreadDb = 2.5f, PitchCents = 70f,
                  Clips = new[] { "Character/MetalStep1.wav", "Character/MetalStep2.wav",
                                  "Character/MetalStep3.wav", "Character/MetalStep4.wav" } },
        new Row { Name = "Cloth", Mixer = Mixer.Footsteps, Spatial = 1f, Db = -26f, Range = 8f,
                  SpreadDb = 2f, PitchCents = 80f,
                  Clips = new[] { "Character/Cloth1.wav", "Character/Cloth2.wav" } },

        // ---- ambience: ship music bed, loops forever. Hand-picked by ear and wired in the
        // Inspector; this row mirrors that choice so a forced rebuild does not undo it.
        new Row { Name = "ShipInterior", Mixer = Mixer.Music, Spatial = 0f, Db = -13.6f, SpreadDb = 0f,
                  PitchCents = 0f, Loop = 0, Clips = new[] { "Ambience/ShipInterior.flac" } },

        // ---- Halo rifle: metallic "ding" per request, four ringing strikes + bright tink layer ----
        new Row { Name = "HaloFire", Mixer = Mixer.SFX, Spatial = 1f, Db = -10f, Range = 70f,
                  SpreadDb = 1.5f, PitchCents = 60f,
                  Clips = new[] { "Weapons/HaloFire1.wav", "Weapons/HaloFire2.wav",
                                  "Weapons/HaloFire3.wav", "Weapons/HaloFire4.wav" } },
        new Row { Name = "HaloSnap", Mixer = Mixer.SFX, Spatial = 1f, Db = -16f, Range = 40f,
                  PitchCents = 80f, Clips = new[] { "Weapons/HaloSnap.wav" } },
        new Row { Name = "HaloRecharge", Mixer = Mixer.SFX, Spatial = 1f, Db = -20f, Range = 16f,
                  PitchCents = 35f, Clips = new[] { "Weapons/HaloRecharge.wav" } },
        new Row { Name = "HaloImpact", Mixer = Mixer.SFX, Spatial = 1f, Db = -18f, Range = 28f,
                  SpreadDb = 2f, PitchCents = 70f, Clips = new[] { "Weapons/HaloImpact.wav" } },

        // ---- revolver ----
        new Row { Name = "RevolverFire", Mixer = Mixer.SFX, Spatial = 1f, Db = -11f, Range = 90f,
                  SpreadDb = 1.5f, PitchCents = 55f,
                  Clips = new[] { "Weapons/RevolverFire1.wav", "Weapons/RevolverFire2.wav" } },
        new Row { Name = "RevolverEnergy", Mixer = Mixer.SFX, Spatial = 1f, Db = -26f, Range = 60f,
                  PitchCents = 60f, Clips = new[] { "Weapons/RevolverEnergy.wav" } },
        new Row { Name = "RevolverRecharge", Mixer = Mixer.SFX, Spatial = 1f, Db = -19f, Range = 16f,
                  PitchCents = 35f, Clips = new[] { "Weapons/RevolverRecharge.wav" } },
    };

    // Two entry points on purpose. Hand-wiring clips and tuning in the Inspector is a normal
    // way to work on these, and a single "rebuild" that silently overwrote that work was a
    // trap: the safe command only creates what is missing, the explicit one is authoritative.
    [MenuItem("Tools/Moonkov/Audio/Rebuild Sound Definitions (overwrites hand edits)", false, 10)]
    public static void Rebuild() => Build(overwriteExisting: true);

    [MenuItem("Tools/Moonkov/Audio/Create Missing Sound Definitions", false, 11)]
    public static void CreateMissing() => Build(overwriteExisting: false);

    /// <summary>Authoritative rebuild; also the -executeMethod entry point.</summary>
    public static void Build() => Rebuild();

    private static void Build(bool overwriteExisting)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build audio assets in Edit mode.");

        System.IO.Directory.CreateDirectory(Definitions);
        AssetDatabase.Refresh();

        var byName = new Dictionary<string, SoundDef>();
        int written = 0, kept = 0;
        foreach (var row in Table)
        {
            var path = Definitions + row.Name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
            if (!overwriteExisting && existing != null)
            {
                byName[row.Name] = existing;
                kept++;
                continue;
            }
            byName[row.Name] = Define(row);
            written++;
        }

        var library = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<MoonkovAudioLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.Click = byName["Click"];
        library.Hover = byName["Hover"];
        library.Confirm = byName["Confirm"];
        library.Error = byName["Error"];
        library.Notification = byName["Notification"];
        library.Equipment = byName["Equipment"];
        library.Container = byName["Container"];
        library.Jump = byName["Jump"];
        library.Land = byName["Land"];
        library.Hit = byName["Hit"];
        library.Death = byName["Death"];
        library.RegolithFootsteps = byName["RegolithFootsteps"];
        library.MetalFootsteps = byName["MetalFootsteps"];
        library.Cloth = byName["Cloth"];
        library.ShipInterior = byName["ShipInterior"];
        library.DeployConfirm = byName["DeployConfirm"];
        EditorUtility.SetDirty(library);

        var halo = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/HaloWeapon.asset");
        var revolver = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/RevolverWeapon.asset");
        if (halo == null || revolver == null) throw new InvalidOperationException("DollSinger weapon assets missing.");
        halo.WeaponFireSfx = byName["HaloFire"];
        halo.WeaponFireLayerSfx = byName["HaloSnap"];
        halo.WeaponReloadSfx = byName["HaloRecharge"];
        halo.WeaponImpactSfx = byName["HaloImpact"];
        revolver.WeaponFireSfx = byName["RevolverFire"];
        revolver.WeaponFireLayerSfx = byName["RevolverEnergy"];
        revolver.WeaponReloadSfx = byName["RevolverRecharge"];
        revolver.WeaponImpactSfx = byName["HaloImpact"];
        EditorUtility.SetDirty(halo);
        EditorUtility.SetDirty(revolver);

        if (overwriteExisting) RetuneLegacySpawnSound();
        ApplyImportSettings();
        AssetDatabase.SaveAssets();
        Debug.Log($"Moonkov audio: {written} SoundDefs written, {kept} left as they were, " +
                  $"{CountClips()} clips in the table.");
    }

    /// <summary>
    /// The player ghost prefabs still point m_SpawnSFX at the original FPS Sample
    /// SoundDef_PlayerSpawn, which played PlayerRespawn.wav. Only its clip is replaced
    /// here, so the three prefabs pick up the new arrival cue without being edited
    /// (rewriting a prefab as large as DollSingerNetworkPlayer.prefab is not worth the risk).
    /// Its serialized fields are also migrated: the asset predates the current Distance
    /// layout and its VolumeDistMin/Max were reading back as zero.
    /// </summary>
    private static void RetuneLegacySpawnSound()
    {
        const string path = "Assets/Audio/Sounddefs/UI and HUD/SoundDef_PlayerSpawn.asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        if (sound == null)
        {
            Debug.LogWarning("Spawn SoundDef not found, teammates keep the legacy spawn cue: " + path);
            return;
        }

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Clips + "Interaction/Spawn.wav");
        if (clip == null) throw new InvalidOperationException("Audio clip missing: " + Clips + "Interaction/Spawn.wav");

        sound.Clips = new List<AudioClip> { clip };
        sound.MixerGroup = Mixer.Footsteps;
        sound.PlaybackType = SoundDef.PlaybackTypes.Random;
        sound.PlayCount = 1;
        sound.VolumeScale = 1;
        sound.BasePitchInCents = 0;
        sound.BaseLowPassCutoff = 0;
        sound.RepeatInfo = new SoundDef.Repeat { LoopCount = 1, RepeatMin = 1, RepeatMax = 1 };
        sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume
        {
            VolumeMin = -21f, VolumeMax = -20f, PitchMin = -40f, PitchMax = 40f,
        };
        sound.DistanceInfo = new SoundDef.Distance
        {
            SpatialBlend = 1f, VolumeDistMin = 1.5f, VolumeDistMax = 25f,
            VolumeRolloffMode = AudioRolloffMode.Linear, DopplerScale = 0f,
        };
        sound.LowPassFilter = new SoundDef.Filter();
        sound.HighPassFilter = new SoundDef.Filter();
        sound.DistortionFilter = new SoundDef.Distortion();
        EditorUtility.SetDirty(sound);
    }

    private static int CountClips()
    {
        int n = 0;
        foreach (var row in Table) n += row.Clips.Length;
        return n;
    }

    /// <summary>
    /// Builds or updates one SoundDef. Existing assets keep their GUID so every reference
    /// (library, weapons, scenes) survives a rebuild.
    /// </summary>
    private static SoundDef Define(Row row)
    {
        var path = Definitions + row.Name + ".asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        if (sound == null)
        {
            // Clone a valid definition: SoundDef.OnValidate expects its nested settings to exist.
            var template = AssetDatabase.LoadAssetAtPath<SoundDef>(TemplatePath);
            if (template == null) throw new InvalidOperationException("SoundDef template missing: " + TemplatePath);
            sound = UnityEngine.Object.Instantiate(template);
            sound.name = row.Name;
            AssetDatabase.CreateAsset(sound, path);
        }

        sound.Clips = new List<AudioClip>();
        foreach (var file in row.Clips)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Clips + file);
            if (clip == null) throw new InvalidOperationException("Audio clip missing: " + Clips + file);
            sound.Clips.Add(clip);
        }

        sound.MixerGroup = row.Mixer;
        sound.PlaybackType = row.Clips.Length > 2
            ? SoundDef.PlaybackTypes.RandomNotLast   // >2 clips: avoid repeating the previous one
            : SoundDef.PlaybackTypes.Random;
        sound.PlayCount = 1;
        sound.VolumeScale = 1;
        sound.BasePitchInCents = 0;
        sound.BaseLowPassCutoff = 0;

        sound.RepeatInfo = new SoundDef.Repeat { LoopCount = row.Loop, RepeatMin = 1, RepeatMax = 1 };
        sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume
        {
            VolumeMin = row.Db - row.SpreadDb,
            VolumeMax = row.Db,
            PitchMin = -row.PitchCents,
            PitchMax = row.PitchCents,
        };

        bool spatial = row.Spatial > 0f;
        sound.DistanceInfo = new SoundDef.Distance
        {
            SpatialBlend = row.Spatial,
            VolumeDistMin = spatial ? 1.5f : 1f,
            VolumeDistMax = Mathf.Max(1.5f, row.Range),
            VolumeRolloffMode = AudioRolloffMode.Linear,
            DopplerScale = 0f,
        };

        sound.LowPassFilter = new SoundDef.Filter();
        sound.HighPassFilter = new SoundDef.Filter();
        sound.DistortionFilter = new SoundDef.Distortion();
        EditorUtility.SetDirty(sound);
        return sound;
    }

    /// <summary>
    /// 48 kHz Vorbis, mono for positional sounds, streaming for the ambience bed.
    /// Derived from the measured source format (the bundle is mostly 96 kHz / 24-bit).
    /// </summary>
    private static void ApplyImportSettings()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Moonkov" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            bool ambience = path.Contains("/Ambience/");
            bool ui = path.Contains("/UI/");

            var settings = importer.defaultSampleSettings;
            settings.loadType = ambience ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .85f;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000;
            settings.preloadAudioData = !ambience;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = !ambience && !ui;
            importer.loadInBackground = ambience;
            importer.SaveAndReimport();
        }
    }
}
