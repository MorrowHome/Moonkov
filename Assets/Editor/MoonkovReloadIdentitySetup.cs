using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovReloadIdentitySetup
{
    public const string Clips = "Assets/Audio/Moonkov/Clips/ReloadIdentity/";
    public const string Banks = "Assets/Audio/Moonkov/SoundDefs/";
    [MenuItem("Moonkov/Audio/Install reload identities")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode");
        var template = AssetDatabase.LoadAssetAtPath<SoundDef>(Banks + "HaloRecharge.asset");
        foreach (var name in MoonkovWeaponIdentitySetup.Names)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/" + name + "Weapon.asset");
            string path = Banks + name + "Reload.asset";
            var bank = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
            bool create = !bank;
            if (create) bank = UnityEngine.Object.Instantiate(template);
            bank.name = name + "Reload"; bank.Clips.Clear();
            for (int i = 1; i <= 3; i++)
            {
                string clipPath = Clips + name + "Reload" + i + ".wav";
                var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
                if (!importer) throw new InvalidOperationException("Missing " + clipPath);
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;
                settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                settings.sampleRateOverride = 48000;
                importer.defaultSampleSettings = settings; importer.forceToMono = true;
                importer.loadInBackground = false; importer.SaveAndReimport();
                bank.Clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath));
            }
            bank.PlaybackType = SoundDef.PlaybackTypes.RandomNotLast; bank.PlayCount = 1;
            bank.Role = SoundDef.MixRole.Weapon; bank.Priority = 80; bank.MaxVoices = 6;
            bank.MinimumInterval = 0; bank.Occlusion = true;
            bank.MixerGroup = SoundMixer.SoundMixerGroup.SFX;
            bank.VolumeScale = 1; bank.BasePitchInCents = 0;
            bank.PitchAndVolumeInfo.VolumeMin = -13; bank.PitchAndVolumeInfo.VolumeMax = -12;
            // Runtime duration adjustment follows the actual reload timer, including modules.
            bank.PitchAndVolumeInfo.PitchMin = bank.PitchAndVolumeInfo.PitchMax = 0;
            bank.DistanceInfo.SpatialBlend = 1; bank.DistanceInfo.VolumeDistMin = 2;
            bank.DistanceInfo.VolumeDistMax = 18; bank.DistanceInfo.LPF_MaxDistance = 18;
            bank.StartStopInfo = new SoundDef.StartStop();
            bank.RepeatInfo = new SoundDef.Repeat { LoopCount = 1, RepeatMin = 1, RepeatMax = 1 };
            if (create) AssetDatabase.CreateAsset(bank, path); else EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssetIfDirty(bank);
            weapon.WeaponReloadSfx = bank; EditorUtility.SetDirty(weapon); AssetDatabase.SaveAssetIfDirty(weapon);
        }
        Debug.Log("Installed 13 reload identities, each with three non-repeating recorded variants.");
    }
}
