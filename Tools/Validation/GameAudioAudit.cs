using System;
using System.Collections.Generic;
using System.IO;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class GameAudioAudit
{
    [Serializable] public class Bank { public string name, mixer; public int clips, voices; public float range; public bool occlusion; }
    [Serializable] public class Report { public int librarySlots, banks, uniqueClips; public List<Bank> inventory = new List<Bank>(); }
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        var report = new Report(); var clips = new HashSet<AudioClip>();
        var library = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        foreach (var field in typeof(MoonkovAudioLibrary).GetFields())
        {
            if (field.FieldType != typeof(SoundDef)) continue;
            if (!(SoundDef)field.GetValue(library)) throw new InvalidOperationException("Unwired library slot " + field.Name);
            report.librarySlots++;
        }
        foreach (var guid in AssetDatabase.FindAssets("t:SoundDef", new[] { "Assets/Audio/Moonkov/SoundDefs" }))
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundDef>(AssetDatabase.GUIDToAssetPath(guid));
            if (!bank || bank.Clips == null || bank.Clips.Count == 0) throw new InvalidOperationException("Empty bank " + bank);
            foreach (var clip in bank.Clips)
            {
                if (!clip || clip.samples <= 0 || clip.frequency <= 0) throw new InvalidOperationException("Missing clip " + bank.name);
                clips.Add(clip);
            }
            if (!bank.UnityAudioMixer) throw new InvalidOperationException("Missing mixer " + bank.name);
            report.inventory.Add(new Bank { name = bank.name, mixer = bank.MixerGroup.ToString(), clips = bank.Clips.Count,
                voices = bank.MaxVoices, range = bank.DistanceInfo.VolumeDistMax, occlusion = bank.Occlusion });
        }
        report.banks = report.inventory.Count; report.uniqueClips = clips.Count;
        MoonkovRaidAudioChecks.Run(); MoonkovAudioDetailChecks.Run(); MoonkovAlienAudioChecks.Run();
        MoonkovWeaponIdentityChecks.Run(); MoonkovReloadIdentityChecks.Run(); MoonkovShootingAudiovisualChecks.Run();
        MoonkovInteractionAudioChecks.Run(); MoonkovCombatAudioChecks.Run(); MoonkovMeleeAudioChecks.Run();
        MoonkovDamageAudioChecks.Run(); MoonkovPainAudioChecks.Run(); MoonkovDistressAudioChecks.Run();
        string breath = MoonkovBreathingAudioChecks.Run(), cough = RaidCoughSoundChecks.Run();
        foreach (var name in new[] { "Hit", "MeleeImpact", "RifleBodyImpact", "RevolverBodyImpact", "ShotgunBodyImpact", "SniperBodyImpact" })
        {
            var bank = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/SoundDefs/" + name + ".asset");
            if (bank.Clips.Count != 3 || bank.Clips.Exists(c => !c.name.StartsWith("BodyImpact")) ||
                bank.PlaybackType != SoundDef.PlaybackTypes.RandomNotLast || bank.StartStopInfo.StopDelay != 0 || !bank.Occlusion || bank.MaxVoices != 6)
                throw new InvalidOperationException("Incorrect physical impact binding " + name);
        }
        if (library.TacticalCough.Clips[0] == library.SuitBreath.Clips[0] || library.TacticalCough.Clips[0].length > RaidCoughSound.Interval)
            throw new InvalidOperationException("Cough is a breath placeholder or overlaps its own repeat");
        Directory.CreateDirectory("LocalData/PlayerVoiceReview");
        File.WriteAllText("LocalData/PlayerVoiceReview/GameAudioAudit.json", JsonUtility.ToJson(report, true));
        return $"PASS {report.librarySlots} library slots; {report.banks} banks; {report.uniqueClips} unique clips; 14 existing regression groups; physical impacts/cough bindings. {breath}; {cough}";
    }
}
