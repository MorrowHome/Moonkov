using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovCombatAudioSetup
{
    const string Root = "Assets/Audio/Moonkov/SoundDefs/";
    [MenuItem("Moonkov/Audio/Install combat feedback")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        lib.MeleeSwing = Bank("MeleeSwing", lib.AlienSwing, -15, 18, 100, 6);
        lib.MeleeEnergySwing = Bank("MeleeEnergySwing", Load("RevolverEnergy"), -17, 24, 95, 6);
        lib.MeleeImpact = Bank("MeleeImpact", Load("RifleBodyImpact"), -12, 22, 85, 6);
        lib.MeleeBlocked = Bank("MeleeBlocked", Load("RifleMetalImpact"), -15, 26, 85, 6);
        lib.ShieldImpact = Bank("ShieldImpact", Load("HaloSnap"), -15, 24, 65, 6);
        lib.ShieldBreak = Bank("ShieldBreak", Load("RevolverEnergy"), -10, 30, 55, 4);
        lib.ShieldRestore = Bank("ShieldRestore", lib.ReloadReady, -16, 16, 90, 4);
        lib.TacticalDeploy = Bank("TacticalDeploy", lib.Equipment, -15, 16, 110, 6);
        lib.TacticalAlarm = Bank("TacticalAlarm", lib.SuitWarning, -10, 55, 60, 4);
        lib.TacticalExplosion = Bank("TacticalExplosion", AssetDatabase.LoadAssetAtPath<SoundDef>(
            "Assets/Audio/Sounddefs/Weapons/Grenade Launcher/Explode/SoundDef_Explode_Grenade.asset"), -7, 80, 45, 6);
        lib.TacticalGas = Bank("TacticalGas", lib.OxygenRefill, -16, 22, 120, 4);
        lib.TacticalCough = Bank("TacticalCough", lib.SuitBreath, -17, 18, 120, 4);
        EditorUtility.SetDirty(lib); AssetDatabase.SaveAssetIfDirty(lib);
    }
    static SoundDef Load(string name) => AssetDatabase.LoadAssetAtPath<SoundDef>(Root + name + ".asset");
    static SoundDef Bank(string name, SoundDef source, float db, float range, int priority, int voices)
    {
        if (!source || source.Clips.Count == 0) throw new InvalidOperationException("Missing source for " + name);
        string path = Root + name + ".asset";
        var sound = Load(name); bool create = !sound;
        if (create) sound = UnityEngine.Object.Instantiate(source);
        else EditorUtility.CopySerialized(source, sound);
        sound.name = name;
        sound.Role = SoundDef.MixRole.Interaction; sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX;
        sound.Priority = priority; sound.MaxVoices = voices; sound.MinimumInterval = .035f;
        sound.Occlusion = true; sound.DistanceInfo.SpatialBlend = 1;
        sound.DistanceInfo.VolumeDistMin = 1.5f; sound.DistanceInfo.VolumeDistMax = range;
        sound.DistanceInfo.LPF_MaxDistance = range; sound.VolumeScale = 1;
        sound.PitchAndVolumeInfo.VolumeMin = db - 2; sound.PitchAndVolumeInfo.VolumeMax = db;
        sound.StartStopInfo = new SoundDef.StartStop(); sound.RepeatInfo = new SoundDef.Repeat();
        sound.PlayCount = 1; sound.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast;
        if (create) AssetDatabase.CreateAsset(sound, path); else EditorUtility.SetDirty(sound);
        AssetDatabase.SaveAssetIfDirty(sound); return sound;
    }
}
