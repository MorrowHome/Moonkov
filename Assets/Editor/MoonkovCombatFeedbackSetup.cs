using System;
using System.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovCombatFeedbackSetup
{
    private const string Clips = "Assets/Audio/Moonkov/Clips/HuntWeaponReports/";
    private const string Banks = "Assets/Audio/Moonkov/SoundDefs/";

    // Rows follow the weapon IDs in MoonkovWeaponIdentitySetup.Names.
    private static readonly (float Db, float Range, float Fade, float BurstGain,
        float Pitch, float Yaw, float Return, float Hold, float Settle, float Gesture)[] Tuning =
    {
        (-2.7f,  94, .125f, .88f,  .92f, .12f, .88f, .038f, 14, .23f), // Halo
        (-2.3f,  87, .115f, .96f, 1.95f, .21f, .85f, .060f, 11, .37f), // Revolver
        (-1.7f,  90, .145f, .96f, 2.95f, .31f, .75f, .095f,  9, .44f), // Shotgun
        (-1.3f, 128, .160f, 1.0f, 4.30f, .37f, .65f, .135f,  7, .67f), // Sniper
        (-4.0f,  66, .085f, .85f,  .52f, .08f, .91f, .036f, 17, .16f), // Cicada
        (-3.4f,  84, .100f, .87f,  .71f, .11f, .91f, .037f, 15, .18f), // Trine
        (-2.6f,  96, .115f, .89f,  .93f, .14f, .87f, .042f, 14, .23f), // Aegis
        (-2.3f, 104, .135f, .94f, 1.60f, .20f, .82f, .055f, 12, .30f), // Vesper
        (-2.4f,  80, .115f, .90f, 1.95f, .24f, .82f, .060f, 11, .34f), // Thorn
        (-1.6f, 120, .150f, .97f, 3.75f, .34f, .70f, .110f,  8, .56f), // Sundown
        (-3.8f,  72, .090f, .87f,  .62f, .09f, .91f, .036f, 16, .16f), // Rondo
        (-2.6f,  92, .125f, .92f, 1.70f, .25f, .81f, .065f, 10, .32f), // Tempest
        (-2.1f, 116, .145f, .95f, 2.55f, .27f, .78f, .080f, 10, .43f), // Nocturne
    };

    [MenuItem("Tools/Moonkov/Weapons/Install Hunt Reports and Recoil")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        var library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
        var names = MoonkovWeaponIdentitySetup.Names;
        if (!library || names.Length != Tuning.Length) throw new InvalidOperationException("Weapon feedback library is incomplete.");
        var entries = names.Select((name, i) =>
        {
            uint id = (uint)i + 2;
            var entry = library.WeaponProfiles?.SingleOrDefault(x => x != null && x.WeaponId == id);
            if (entry?.Profile == null) throw new InvalidOperationException("Missing feedback profile for " + name);
            var clips = Enumerable.Range(1, 3).Select(take =>
                AssetDatabase.LoadAssetAtPath<AudioClip>(Clips + name + "Report" + take + ".wav")).ToArray();
            if (clips.Any(clip => !clip)) throw new InvalidOperationException("Missing Hunt report for " + name);
            var template = AssetDatabase.LoadAssetAtPath<SoundDef>(Banks + name + "IdentityBody.asset");
            if (!template) throw new InvalidOperationException("Missing SoundDef template for " + name);
            return (name, entry.Profile, clips, template, Tuning[i]);
        }).ToArray();

        foreach (var (name, profile, clips, template, tune) in entries)
        {
            string bankName = name + "HuntReport";
            string path = Banks + bankName + ".asset";
            var bank = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
            bool create = !bank;
            if (create) bank = UnityEngine.Object.Instantiate(template);
            else EditorUtility.CopySerialized(template, bank);
            bank.name = bankName;
            bank.Clips.Clear();
            bank.Clips.AddRange(clips);
            bank.PitchAndVolumeInfo.VolumeMin = bank.PitchAndVolumeInfo.VolumeMax = tune.Db;
            bank.PitchAndVolumeInfo.PitchMin = bank.PitchAndVolumeInfo.PitchMax = 0;
            bank.DistanceInfo.VolumeDistMax = bank.DistanceInfo.LPF_MaxDistance = tune.Range;
            bank.Priority = 36;
            bank.MaxVoices = 24;
            if (create) AssetDatabase.CreateAsset(bank, path);
            else EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssetIfDirty(bank);

            profile.FireBody = bank;
            profile.FireSnap = profile.FireTail = null;
            profile.BodyCrossfade = tune.Fade;
            profile.SustainedBodyGain = tune.BurstGain;
            profile.BurstPitchRise = 0;
            profile.RecoilPitch = tune.Pitch;
            profile.RecoilYaw = tune.Yaw;
            profile.RecoilReturnFraction = tune.Return;
            profile.RecoilHoldSeconds = tune.Hold;
            profile.RecoilSettleSpeed = tune.Settle;
            profile.GestureReleaseSeconds = tune.Gesture;
        }
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }
}
