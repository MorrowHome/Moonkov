using System;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;

public static class MoonkovMeleeAudioChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Moonkov/Check Melee Audio")]
    public static void Run()
    {
        var audio = new MoonkovMeleeAudio();
        var state = new PredictedPlayerGhost { CurrentHealth = 100, MeleeEpoch = 1,
            MeleeWeapon = (byte)MeleeWeaponKind.Fists, MeleeAttackWeapon = (byte)MeleeWeaponKind.Fists,
            MeleeAttack = (byte)MeleeAttackKind.Light, MeleeAttackSequence = 10, MeleeHitSequence = 10,
            MeleePhase = (byte)MeleePhase.Active, MeleeElapsed = .15f };
        Require(audio.Observe(state) == 0, "Late join replayed old attack");
        state.MeleeAttackSequence++; state.MeleeElapsed = 0; state.MeleePhase = (byte)MeleePhase.Windup;
        Require(audio.Observe(state) == 0, "Swing during windup");
        state.MeleeElapsed = .15f; state.MeleePhase = (byte)MeleePhase.Active;
        Require(audio.Observe(state) == (MoonkovMeleeAudio.Cues.Swing | MoonkovMeleeAudio.Cues.Effort), "Active fist swing/voice missing");
        Require(audio.Observe(state) == 0, "Repeated swing");
        state.MeleeHitSequence++;
        Require(audio.Observe(state) == MoonkovMeleeAudio.Cues.Hit, "Confirmed impact missing");
        Require(audio.Observe(state) == 0, "Repeated impact");
        state.MeleeAttackSequence++; state.MeleeElapsed = 0; audio.Observe(state);
        state.MeleeCancelSequence++; state.MeleeElapsed = .15f;
        Require(audio.Observe(state) == 0, "Cancelled attack played");
        state.MeleeAttackSequence++; state.MeleeElapsed = 1;
        Require(audio.Observe(state) == 0, "Old finished attack replayed");
        state.MeleeEpoch++; state.MeleeElapsed = .15f;
        Require(audio.Observe(state) == 0, "Epoch change replayed history");
        state.MeleeWeapon = (byte)MeleeWeaponKind.EnergySword;
        Require(audio.Observe(state) == MoonkovMeleeAudio.Cues.Equip, "Equip missing");
        foreach (var kind in new[] { MeleeWeaponKind.Fists, MeleeWeaponKind.Dagger, MeleeWeaponKind.EnergySword })
            foreach (var attack in new[] { MeleeAttackKind.Light, MeleeAttackKind.Heavy })
            {
                var observer = new MoonkovMeleeAudio();
                state.MeleeWeapon = state.MeleeAttackWeapon = (byte)kind;
                state.MeleeAttack = (byte)attack; state.MeleePhase = (byte)MeleePhase.Windup; state.MeleeElapsed = 0;
                observer.Observe(state); state.MeleeAttackSequence++;
                Require(observer.Observe(state) == 0, "Windup must not grunt");
                MeleeRules.TryDefinition(kind, out var definition);
                state.MeleeElapsed = definition.Timing(attack, 0).Windup + .01f; state.MeleePhase = (byte)MeleePhase.Active;
                var cues = observer.Observe(state);
                Require((cues & MoonkovMeleeAudio.Cues.Swing) != 0 &&
                    ((cues & MoonkovMeleeAudio.Cues.Effort) != 0) == (kind == MeleeWeaponKind.Fists), "Only fists get a boxing voice");
                Require(observer.Observe(state) == 0, "Snapshot replay repeated an effort");
                state.MeleeAttackSequence++; state.CurrentHealth = 0;
                Require(observer.Observe(state) == 0, "Dead character grunted");
                state.CurrentHealth = 100; state.ClashDarkSight = true; state.MeleeAttackSequence++;
                Require(observer.Observe(state) == 0, "Inactive dark sight grunted");
                state.ClashDarkSight = false;
            }
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        foreach (var sound in new[] { lib.MeleeSwing, lib.MeleeEnergySwing, lib.MeleeImpact, lib.MeleeBlocked, lib.MeleeEffort })
            Require(sound && sound.Clips.Count > 0 && sound.Clips.TrueForAll(c => c && c.loadState == AudioDataLoadState.Loaded)
                && sound.MixerGroup == SoundMixer.SoundMixerGroup.SFX && sound.MaxVoices > 0, "Missing/unloaded combat cue");
        Require(lib.MeleeEffort.Clips.Count == 3 && lib.MeleeEffort.PlaybackType == SoundDef.PlaybackTypes.RandomNotLast &&
            lib.MeleeEffort.MinimumInterval == 0 && lib.MeleeEffort.PitchAndVolumeInfo.PitchMin == 0 &&
            lib.MeleeEffort.PitchAndVolumeInfo.PitchMax == 0, "Natural voice variants and per-character cue timing required");
        foreach (var clip in lib.MeleeEffort.Clips)
        {
            Require(!lib.PlayerPain.Clips.Contains(clip) && clip.length > .2f && clip.length < .5f,
                "Effort must use a short attack performance");
            Require(clip.channels == 1 && clip.frequency == 48000 && clip.loadType == AudioClipLoadType.DecompressOnLoad,
                "Effort must be preloaded mono PCM");
        }
        Debug.Log("Melee audio: timing, cancellation, late join, epoch, equip, impact and assets passed.");
    }
}
