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
        Require(audio.Observe(state) == MoonkovMeleeAudio.Cues.Swing, "Active swing missing");
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
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        foreach (var sound in new[] { lib.MeleeSwing, lib.MeleeEnergySwing, lib.MeleeImpact, lib.MeleeBlocked })
            Require(sound && sound.Clips.Count > 0 && sound.Clips.TrueForAll(c => c && c.loadState == AudioDataLoadState.Loaded)
                && sound.MixerGroup == SoundMixer.SoundMixerGroup.SFX && sound.MaxVoices > 0, "Missing/unloaded combat cue");
        Debug.Log("Melee audio: timing, cancellation, late join, epoch, equip, impact and assets passed.");
    }
}
