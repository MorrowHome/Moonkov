using System;
using BodyPart = Unity.MP_FPS.BodyPart;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovDamageAudioChecks
{
    static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Damage Audio Events")]
    public static void Run()
    {
        var state = new PredictedPlayerGhost();
        RaidHealth.Initialize(ref state);
        state.Oxygen = 0;
        float before = state.CurrentHealth;
        for (uint tick = 1; tick <= 120; tick++)
            RaidHealth.TickOxygen(ref state, false, 1f / 60, 180, 0, 0, 1, tick);
        Require(state.CurrentHealth < before && state.LastHitTick != 0, "Hypoxia must still damage and interrupt actions");
        Require(state.LastImpactTick == 0, "Hypoxia emitted impact audio");
        RaidHealth.CombatDamage(ref state, BodyPart.LeftArm, 1, 121, RaidDamageKind.Bullet);
        Require(state.LastImpactTick == 121, "Bullet impact missing");
        foreach (var kind in new[] { RaidDamageKind.Fire, RaidDamageKind.Poison, RaidDamageKind.Bleed })
            RaidHealth.CombatDamage(ref state, BodyPart.LeftArm, .1f, 122, kind);
        RaidHealth.Damage(ref state, BodyPart.Chest, .1f, 123);
        Require(state.LastImpactTick == 121, "Continuous damage overwrote pending combat impact");
        state.ShieldEquipped = true; state.ShieldCharge = 100;
        RaidHealth.CombatDamage(ref state, BodyPart.LeftArm, 1, 124, RaidDamageKind.Melee);
        Require(state.LastImpactTick == 121 && state.ShieldHitSequence == 1, "Fully shielded hit emitted tissue impact");
        state.ShieldCharge = .5f;
        RaidHealth.CombatDamage(ref state, BodyPart.LeftArm, 1, 125, RaidDamageKind.Explosion);
        Require(state.LastImpactTick == 125, "Shield overflow impact missing");
        uint observed = 121;
        Require(MoonkovPlayerAudio.NewTick(125, ref observed) && !MoonkovPlayerAudio.NewTick(125, ref observed)
            && !MoonkovPlayerAudio.NewTick(124, ref observed), "Repeated/out-of-order impact replay");
        Debug.Log("Damage audio: 120 hypoxia ticks, status damage, simultaneous combat, shield absorption/overflow and deduplication passed.");
    }
}
