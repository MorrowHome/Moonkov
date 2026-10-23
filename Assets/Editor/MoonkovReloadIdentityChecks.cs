using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovReloadIdentityChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Moonkov/Check Reload Audio Identities")]
    public static void Run()
    {
        var clips = new HashSet<AudioClip>(); var banks = new HashSet<SoundDef>();
        foreach (var name in MoonkovWeaponIdentitySetup.Names)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/" + name + "Weapon.asset");
            var sound = weapon.WeaponReloadSfx;
            Require(sound && banks.Add(sound) && sound.Clips.Count == 3, "Shared or missing reload bank: " + name);
            Require(sound.PlaybackType == SoundDef.PlaybackTypes.RandomNotLast && sound.PlayCount == 1 && sound.RepeatInfo.LoopCount == 1, "Repeat policy: " + name);
            Require(sound.MixerGroup == SoundMixer.SoundMixerGroup.SFX && sound.MaxVoices == 6 && sound.DistanceInfo.SpatialBlend == 1, "Budget/spatial/mixer: " + name);
            foreach (var clip in sound.Clips)
            {
                Require(clip && clips.Add(clip), "Shared recording: " + name);
                Require(Mathf.Abs(clip.length - weapon.ReloadTime) < .001f, "Reload duration mismatch: " + name);
                Require(clip.frequency == 48000 && clip.channels == 1 && clip.loadType == AudioClipLoadType.DecompressOnLoad, "Import: " + name);
                clip.LoadAudioData(); var samples = new float[clip.samples]; Require(clip.GetData(samples, 0), "Unreadable PCM");
                float peak = 0;
                foreach (float value in samples) { Require(float.IsFinite(value), "Invalid PCM"); peak = Mathf.Max(peak, Mathf.Abs(value)); }
                Require(peak > .4f && peak < .8f, "Silent/clipped reload: " + name);
            }
            foreach (float modifier in new[] { 1f, .6f, .45f, 1.35f, 1.8f })
            {
                float remaining = weapon.ReloadTime * modifier;
                float rate = Mathf.Pow(2, MoonkovReloadAudio.PitchForDuration(sound, remaining) / 1200);
                Require(Mathf.Abs(sound.Clips[0].length / rate - remaining) < .002f, "Modified reload timing: " + name);
            }
            Require(MoonkovReloadAudio.PitchForDuration(sound, float.NaN) == 0 && MoonkovReloadAudio.PitchForDuration(sound, 0) == 0, "Invalid duration");
        }
        Require(clips.Count == 39 && banks.Count == 13, "Incomplete reload identities");
        Debug.Log("13 distinct reload banks / 39 PCM clips; no-repeat policy, duration, module speed, mix budget, peak and references passed.");
    }
    public static string CheckPlayback()
    {
        Require(Application.isPlaying, "Use existing Editor Play mode");
        var anchor = new GameObject("Reload identity check");
        var pool = new SoundGameObjectPool("Reload identity check pool", 4); var system = new SoundSystem();
        int played = 0;
        try
        {
            system.Init(anchor.transform, 4, pool, AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            foreach (var name in MoonkovWeaponIdentitySetup.Names)
            {
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/" + name + "Weapon.asset");
                AudioClip previous = null;
                for (int i = 0; i < 6; i++)
                {
                    float rate = i % 2 == 0 ? 1 : 1 / .6f;
                    var info = new SoundSystem.SoundInfo { SoundDefinition = weapon.WeaponReloadSfx, Position = Vector3.zero,
                        SoundDefOverrideInfo = new SoundEmitter.SoundDefOverrideData(weapon.WeaponReloadSfx)
                        { SpatialBlend = .08f, BasePitchInCents = MoonkovReloadAudio.PitchForDuration(weapon.WeaponReloadSfx, weapon.ReloadTime / rate) } };
                    Require(system.PlayEmitter(info, 1), "Cannot play " + name);
                    var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                    Require(source.isPlaying && source.clip != previous, "Repeated adjacent reload: " + name);
                    Require(source.outputAudioMixerGroup.name == "SFX" && Mathf.Abs(source.pitch - rate) < .001f, "Wrong routing/speed: " + name);
                    previous = source.clip; played++; system.Kill(info);
                }
            }
            Require(pool.AvailableSoundObjectCount == 4, "Reload voice leak");
            return played + " reload plays; no adjacent repeated clips; normal/fast playback rates, SFX and pool reclamation passed";
        }
        finally { system.KillAll(); pool.Dispose(); UnityEngine.Object.Destroy(anchor); }
    }
}
