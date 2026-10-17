using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovRaidAudioSetup
{
    private const string Root = "Assets/Audio/Moonkov/";
    [MenuItem("Moonkov/Audio/Install immersive raid audio")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before installing audio assets.");
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "Clips/Raid" }))
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var sample = importer.defaultSampleSettings;
            sample.loadType = AudioClipLoadType.DecompressOnLoad; sample.compressionFormat = AudioCompressionFormat.PCM;
            sample.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; sample.sampleRateOverride = 48000;
            sample.preloadAudioData = true; importer.defaultSampleSettings = sample;
            importer.forceToMono = true; importer.loadInBackground = false; importer.SaveAndReimport();
        }
        var lib = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
        if (!lib) throw new InvalidOperationException("Missing existing library.");
        lib.RegolithFootsteps = Bank("RegolithFootsteps", "Regolith", 8, -12.5f, SoundDef.MixRole.Foley, 13, 8, .025f);
        lib.MetalFootsteps = Bank("MetalFootsteps", "Metal", 8, -14, SoundDef.MixRole.Foley, 18, 8, .025f);
        lib.HardFootsteps = Bank("HardFootsteps", "Hard", 8, -14, SoundDef.MixRole.Foley, 14, 8, .025f);
        lib.AlienContact = Bank("AlienContact", "AlienContact", 4, -15, SoundDef.MixRole.Creature, 18, 10, .04f);
        lib.AlienWarning = Bank("AlienWarning", "AlienWarning", 3, -8.5f, SoundDef.MixRole.Creature, 32, 5, .045f);
        lib.AlienStrike = Bank("AlienStrike", "AlienStrike", 3, -10, SoundDef.MixRole.Creature, 26, 5, .035f);
        lib.AlienHurt = Bank("AlienHurt", "AlienHurt", 3, -14, SoundDef.MixRole.Creature, 22, 5, .07f);
        lib.AlienDeath = Bank("AlienDeath", "AlienDeath", 3, -11, SoundDef.MixRole.Creature, 28, 5, .05f);
        lib.AlienIdle = Bank("AlienIdle", "AlienIdle", 3, -16, SoundDef.MixRole.Creature, 36, 2, 1.5f);
        foreach (string name in new[] { "Aim", "Light", "DryFire", "ReloadReady", "Medical", "Pickup", "OxygenRefill" })
            typeof(MoonkovAudioLibrary).GetField(name).SetValue(lib,
                Bank(name, name, 1, name == "Aim" ? -23 : -17, SoundDef.MixRole.Interaction, 0, 3, .12f));
        lib.SuitBreath = Bank("SuitBreath", "SuitBreath", 1, -22, SoundDef.MixRole.Suit, 0, 1, .3f);
        lib.Heartbeat = Bank("Heartbeat", "Heartbeat", 1, -24, SoundDef.MixRole.Suit, 0, 1, 1);
        lib.SuitWarning = Bank("SuitWarning", "SuitWarning", 1, -18, SoundDef.MixRole.Suit, 0, 1, 3);
        lib.ExtractStart = Bank("ExtractStart", "ExtractStart", 1, -18, SoundDef.MixRole.Suit, 0, 1, .5f);
        lib.ExtractCancel = Bank("ExtractCancel", "ExtractCancel", 1, -21, SoundDef.MixRole.Suit, 0, 1, .5f);
        lib.ExtractTick = Bank("ExtractTick", "ExtractTick", 1, -24, SoundDef.MixRole.Suit, 0, 1, .8f);
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
        foreach (string name in new[] { "RaidHullDrone", "RaidStructureCreak" })
            Policy(name, SoundDef.MixRole.Ambience, name == "RaidHullDrone" ? 1 : 2, .5f, name != "RaidHullDrone", 210);
        foreach (string name in new[] { "Cloth", "Jump", "Land" }) Policy(name, SoundDef.MixRole.Foley, 8, .025f, true, 155);
        foreach (string weapon in new[] { "Rifle", "Revolver", "Sniper", "Shotgun" })
            foreach (string layer in new[] { "ShotBody", "ShotSnap", "ShotTail" })
                Policy(weapon + layer, SoundDef.MixRole.Weapon, 8, 0, true, layer == "ShotBody" ? 50 : 80);
        // Generic legacy reload/impact assets also benefit from distance obstruction and a voice budget.
        foreach (string name in new[] { "HaloRecharge", "RevolverRecharge", "HaloImpact", "Hit", "Death" })
            Policy(name, SoundDef.MixRole.Default, 8, .02f, true, 100);
        AssetDatabase.Refresh();
        MoonkovAudioDetailSetup.Install();
        Debug.Log("Raid audio installed: recorded contact/vocal banks, suit/detail cues and pooled mix policies.");
    }
    private static SoundDef Bank(string name, string prefix, int count, float db, SoundDef.MixRole role,
        float range, int voices, float interval)
    {
        string path = Root + "SoundDefs/" + name + ".asset";
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        bool create = !sound;
        if (create) sound = ScriptableObject.CreateInstance<SoundDef>();
        sound.name = name; sound.Role = role; sound.Priority = role == SoundDef.MixRole.Creature ? 70 : role == SoundDef.MixRole.Suit ? 65 : 140;
        sound.MaxVoices = voices; sound.MinimumInterval = interval; sound.Occlusion = range > 0;
        sound.UnityAudioMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer");
        sound.MixerGroup = role == SoundDef.MixRole.Foley ? SoundMixer.SoundMixerGroup.Footsteps : SoundMixer.SoundMixerGroup.SFX;
        sound.BasePitchInCents = 0; sound.BaseLowPassCutoff = 0; sound.VolumeScale = 1; sound.PlayCount = 1;
        sound.PlaybackType = count > 1 ? SoundDef.PlaybackTypes.RandomNotLast : SoundDef.PlaybackTypes.Sequential;
        sound.Clips = new List<AudioClip>();
        for (int i = 1; i <= count; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Clips/Raid/" + prefix + (count > 1 ? i.ToString() : "") + ".wav");
            if (!clip) throw new InvalidOperationException("Missing audio: " + prefix + i);
            sound.Clips.Add(clip);
        }
        sound.RepeatInfo = new SoundDef.Repeat(); sound.StartStopInfo = new SoundDef.StartStop();
        sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume { VolumeMin = db - 1, VolumeMax = db,
            PitchMin = count > 1 ? -45 : 0, PitchMax = count > 1 ? 45 : 0 };
        sound.DistanceInfo = new SoundDef.Distance { SpatialBlend = range > 0 ? 1 : 0,
            VolumeDistMin = role == SoundDef.MixRole.Creature ? 2.5f : 1.5f, VolumeDistMax = Mathf.Max(2, range),
            VolumeRolloffMode = AudioRolloffMode.Linear, DopplerScale = 0,
            LPFRollOffCurveType = range > 0 ? Interpolator.CurveType.Linear : Interpolator.CurveType.None,
            LPF_MaxDistance = Mathf.Max(1, range), LPF_MinCutoff = 1500 };
        sound.LowPassFilter = new SoundDef.Filter { EnableComponent = range > 0, CutoffMin = 14000, CutoffMax = 14000 };
        sound.HighPassFilter = new SoundDef.Filter(); sound.DistortionFilter = new SoundDef.Distortion();
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound); return sound;
    }
    private static void Policy(string name, SoundDef.MixRole role, int voices, float interval, bool occlusion, int priority)
    {
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(Root + "SoundDefs/" + name + ".asset");
        if (!sound) return;
        sound.Role = role; sound.MaxVoices = voices; sound.MinimumInterval = interval;
        sound.Occlusion = occlusion; sound.Priority = priority;
        EditorUtility.SetDirty(sound); AssetDatabase.SaveAssetIfDirty(sound);
    }
}
