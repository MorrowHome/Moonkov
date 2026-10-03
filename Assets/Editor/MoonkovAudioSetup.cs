using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovAudioSetup
{
    private const string Clips = "Assets/Audio/Moonkov/Clips/";
    private const string Definitions = "Assets/Audio/Moonkov/SoundDefs/";

    [MenuItem("Tools/Moonkov/Audio/Rebuild Sound Definitions")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build audio assets in Edit mode.");
        System.IO.Directory.CreateDirectory(Definitions);
        AssetDatabase.Refresh();
        var library = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<MoonkovAudioLibrary>();
            AssetDatabase.CreateAsset(library, "Assets/Resources/Moonkov/AudioLibrary.asset");
        }
        library.Click = Define("Click", 3, 0, -17, 0, "UI/ClickA.ogg", "UI/ClickB.ogg", "UI/ClickC.ogg");
        library.Hover = Define("Hover", 3, 0, -25, 0, "UI/HoverA.ogg", "UI/HoverB.ogg");
        library.Confirm = Define("Confirm", 3, 0, -15, 0, "UI/ConfirmShort.wav");
        library.Error = Define("Error", 3, 0, -18, 0, "UI/ErrorShort.wav");
        library.Notification = Define("Notification", 3, 0, -21, 0, "UI/Notification.wav");
        library.Equipment = Define("Equipment", 1, 0, -20, 0, "Interaction/Equipment.wav");
        library.Container = Define("Container", 3, 0, -22, 0, "Interaction/Container.wav");
        library.Jump = Define("Jump", 2, 1, -23, 9, "Character/Cloth.wav");
        library.Land = Define("Land", 2, 1, -15, 14, "Character/RegolithStepA.wav", "Character/RegolithStepB.wav");
        library.Hit = Define("Hit", 1, 1, -16, 18, "Character/BodyImpact.wav");
        library.Death = Define("Death", 1, 1, -13, 20, "Character/BodyImpact.wav");
        library.RegolithFootsteps = Define("RegolithFootsteps", 2, 1, -17, 14, "Character/RegolithStepA.wav", "Character/RegolithStepB.wav");
        library.MetalFootsteps = Define("MetalFootsteps", 2, 1, -16, 18,
            "Character/MetalStep1.wav", "Character/MetalStep2.wav", "Character/MetalStep3.wav", "Character/MetalStep4.wav");
        library.Cloth = Define("Cloth", 2, 1, -26, 6, "Character/Cloth.wav");
        library.ShipInterior = Define("ShipInterior", 1, 0, -31, 0, "Ambience/ShipInterior.wav");
        library.ShipInterior.RepeatInfo.LoopCount = 0;
        library.ShipInterior.PitchAndVolumeInfo.PitchMin = library.ShipInterior.PitchAndVolumeInfo.PitchMax = 0;
        EditorUtility.SetDirty(library.ShipInterior);

        var halo = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/HaloWeapon.asset");
        var revolver = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/RevolverWeapon.asset");
        if (halo == null || revolver == null) throw new InvalidOperationException("DollSinger weapon assets missing.");
        halo.WeaponFireSfx = Define("HaloFire", 1, 1, -14, 70, "Weapons/EnergyShot.wav");
        halo.WeaponFireLayerSfx = Define("HaloSnap", 1, 1, -24, 40, "Weapons/ElectricSnap.wav");
        halo.WeaponReloadSfx = Define("HaloRecharge", 1, 1, -19, 16, "Weapons/HaloRecharge.wav");
        halo.WeaponImpactSfx = Define("HaloImpact", 1, 1, -23, 28, "World/MetalImpact.wav");
        revolver.WeaponFireSfx = Define("RevolverFire", 1, 1, -12, 90, "Weapons/RevolverShot.wav");
        revolver.WeaponFireLayerSfx = Define("RevolverEnergy", 1, 1, -20, 60, "Weapons/EnergyShot.wav");
        revolver.WeaponReloadSfx = Define("RevolverRecharge", 1, 1, -19, 16, "Weapons/RevolverRecharge.wav");
        revolver.WeaponImpactSfx = halo.WeaponImpactSfx;
        EditorUtility.SetDirty(halo); EditorUtility.SetDirty(revolver); EditorUtility.SetDirty(library);

        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio/Moonkov" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            bool ambience = path.Contains("/Ambience/");
            settings.loadType = ambience ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .85f;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 48000;
            settings.preloadAudioData = !ambience;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = !ambience && !path.Contains("/UI/");
            importer.loadInBackground = ambience;
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Moonkov audio wired: 22 SoundDefs, pooled playback, streaming ship ambience.");
    }

    private static SoundDef Define(string name, int mixer, float spatial, float decibels,
        float range, params string[] clips)
    {
        var path = Definitions + name + ".asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        if (sound == null)
        {
            // Clone a valid definition: SoundDef.OnValidate expects its nested settings to exist.
            var template = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Sounddefs/Player/Footsteps/SoundDef_Footstep.asset");
            sound = UnityEngine.Object.Instantiate(template);
            sound.name = name;
            AssetDatabase.CreateAsset(sound, path);
        }
        sound.Clips = new List<AudioClip>();
        foreach (var file in clips)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Clips + file);
            if (clip == null) throw new InvalidOperationException("Audio clip missing: " + Clips + file);
            sound.Clips.Add(clip);
        }
        sound.MixerGroup = (SoundMixer.SoundMixerGroup)mixer;
        sound.PlaybackType = clips.Length > 1 ? SoundDef.PlaybackTypes.RandomNotLast : SoundDef.PlaybackTypes.Random;
        sound.PlayCount = 1; sound.VolumeScale = 1; sound.BasePitchInCents = 0; sound.BaseLowPassCutoff = 0;
        sound.RepeatInfo = new SoundDef.Repeat();
        sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume
            { VolumeMin = decibels - 1, VolumeMax = decibels, PitchMin = -45, PitchMax = 45 };
        sound.DistanceInfo = new SoundDef.Distance
        {
            SpatialBlend = spatial, VolumeDistMin = spatial > 0 ? 1.5f : 1f,
            VolumeDistMax = Mathf.Max(1.5f, range), VolumeRolloffMode = AudioRolloffMode.Linear,
            DopplerScale = 0
        };
        sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter();
        sound.DistortionFilter = new SoundDef.Distortion();
        EditorUtility.SetDirty(sound);
        return sound;
    }
}
