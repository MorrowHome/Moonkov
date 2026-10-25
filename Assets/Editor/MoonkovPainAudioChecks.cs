using System;
using BodyPart = Unity.MP_FPS.BodyPart;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class MoonkovPainAudioChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Run()
    {
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        var voice = new MoonkovPainAudio();
        Require(!voice.Observe(health.LastImpactTick, true, 0), "Initial snapshot cried");
        health.Oxygen = 0;
        for (uint tick = 1; tick <= 120; tick++)
        {
            RaidHealth.TickOxygen(ref health, false, 1f / 60, 180, 0, 0, 1, tick);
            Require(!voice.Observe(health.LastImpactTick, true, tick / 60f), "Hypoxia cried");
        }
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 121, RaidDamageKind.Bullet);
        Require(voice.Observe(health.LastImpactTick, true, 2.1f), "Bullet pain missing");
        Require(!voice.Observe(health.LastImpactTick, true, 5), "Repeated snapshot cried");
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 122, RaidDamageKind.Melee);
        Require(!voice.Observe(health.LastImpactTick, true, 2.2f), "Rapid hits stacked voices");
        Require(!voice.Observe(health.LastImpactTick, true, 4), "Suppressed cry was queued");
        health.ShieldEquipped = true; health.ShieldCharge = 100;
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 123, RaidDamageKind.Bullet);
        Require(!voice.Observe(health.LastImpactTick, true, 5), "Fully shielded hit cried");
        health.ShieldCharge = .5f;
        RaidHealth.CombatDamage(ref health, BodyPart.LeftArm, 1, 124, RaidDamageKind.Explosion);
        Require(voice.Observe(health.LastImpactTick, true, 5), "Shield overflow pain missing");
        Require(!voice.Observe(125, false, 7), "Dead player cried");
        Require(!new MoonkovPainAudio().Observe(125, true, 8), "Late join replayed pain");
        var second = new MoonkovPainAudio(); second.Observe(0, true, 0);
        Require(second.Observe(10, true, 5), "Other character was globally suppressed");
        var bank = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary").PlayerPain;
        Require(bank && bank.Clips.Count == 3 && bank.PlaybackType == SoundDef.PlaybackTypes.RandomNotLast, "Missing voice variants");
        Require(bank.MinimumInterval == 0 && bank.MaxVoices > 0 && bank.MixerGroup == SoundMixer.SoundMixerGroup.SFX, "Invalid voice policy");
        foreach (var clip in bank.Clips)
        {
            Require(clip && clip.length > .2f && clip.length < MoonkovPainAudio.MinimumInterval, "Voice longer than cooldown");
            Require(clip.channels == 1 && clip.frequency == 48000 && clip.loadType == AudioClipLoadType.DecompressOnLoad, "Invalid import");
            var samples = new float[clip.samples]; clip.LoadAudioData(); Require(clip.GetData(samples, 0), "Unreadable PCM");
            float peak = 0; foreach (float value in samples) { Require(float.IsFinite(value), "Invalid PCM"); peak = Mathf.Max(peak, Mathf.Abs(value)); }
            Require(peak > .5f && peak < .8f, "Silent/clipped pain clip");
        }
        Debug.Log("Player pain: direct injury/overflow, 120 hypoxia ticks, repeats, cooldown, no delayed cries, late join, death and independent characters passed; three voice assets valid.");
    }
    public static string CheckPlayback()
    {
        Require(Application.isPlaying, "Use Play mode");
        var anchor = new GameObject("Pain voice check"); var pool = new SoundGameObjectPool("Pain voice check pool", 4);
        var system = new SoundSystem(); var bank = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary").PlayerPain;
        try
        {
            system.Init(anchor.transform, 4, pool, AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            AudioClip previous = null;
            for (int i = 0; i < 16; i++)
            {
                var info = system.CreateEmitter(bank, Vector3.forward * 2);
                Require(info != null && info.Handle.IsValid(), "Voice allocation failed");
                var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Require(source.isPlaying && source.volume > 0 && source.clip != previous && source.outputAudioMixerGroup.name == "SFX", "Playback/repeat/mix failure");
                previous = source.clip; system.Kill(info);
            }
            Require(pool.AvailableSoundObjectCount == 4, "Leaked pain voice");
            return "16 pain voice plays; no adjacent repeats; SFX; all sources reclaimed";
        }
        finally { system.KillAll(); pool.Dispose(); UnityEngine.Object.Destroy(anchor); }
    }
}
