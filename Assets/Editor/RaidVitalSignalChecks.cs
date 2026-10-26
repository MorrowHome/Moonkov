using System;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;

public static class RaidVitalSignalChecks
{
    [MenuItem("Tools/Moonkov/Check Vital Monitor Signal")]
    public static string Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks++; }
        var state = default(PredictedPlayerGhost);
        var missing = RaidVitalSignal.Read(state);
        Check(!missing.Ready && !missing.Alive && missing.Frequency == 0 && missing.Warning == "", "Unknown snapshot has no invented life signal");
        RaidHealth.Initialize(ref state);
        var rest = RaidVitalSignal.Read(state);
        Check(rest.Ready && rest.Alive && !rest.Critical && rest.Warning == "", "Healthy baseline stays quiet");
        state.ControllerState.Sprinting = true;
        Check(RaidVitalSignal.Read(state).Frequency > rest.Frequency, "Sprinting increases signal pace");
        state.ControllerState.Sprinting = false;
        state.ChestHealth = 10;
        var injured = RaidVitalSignal.Read(state);
        Check(injured.Critical && injured.Frequency > rest.Frequency && injured.Warning == "VITALS CRITICAL", "Critical chest is visible despite healthy limbs");
        RaidHealth.Initialize(ref state); state.Oxygen = 0;
        var hypoxic = RaidVitalSignal.Read(state);
        Check(hypoxic.Critical && hypoxic.Warning == "HYPOXIA", "Vacuum with empty oxygen shows hypoxia");
        state.BreathableAir = true;
        Check(!RaidVitalSignal.Read(state).Critical && RaidVitalSignal.Read(state).Warning == "", "Empty reserve in breathable air is not hypoxia");
        RaidHealth.Initialize(ref state); state.Hydration = 0;
        Check(RaidVitalSignal.Read(state).Critical && RaidVitalSignal.Read(state).Warning == "DEHYDRATED", "Dehydration remains visible in consolidated monitor");
        RaidHealth.Initialize(ref state); state.Energy = 0;
        Check(RaidVitalSignal.Read(state).Critical && RaidVitalSignal.Read(state).Warning == "STARVING", "Starvation remains visible");
        RaidHealth.Initialize(ref state); state.TacticalPoisonSeconds = 4;
        Check(RaidVitalSignal.Read(state).Warning == "POISONED", "Poison status is not hidden by merging the panels");
        state.CurrentHealth = 0;
        var dead = RaidVitalSignal.Read(state);
        Check(dead.Ready && !dead.Alive && dead.Frequency == 0, "Death flattens the signal");
        state.ClashEnabled = true; state.ClashLife = ClashLife.Downed;
        Check(RaidVitalSignal.Read(state).Alive && RaidVitalSignal.Read(state).Critical, "Revivable downed player must not flatline");
        state.ClashLife = ClashLife.Dead;
        Check(!RaidVitalSignal.Read(state).Alive, "Eliminated player is not reported alive");
        Check(RaidVitalSignal.ReserveCells(0, 12) == 0 && RaidVitalSignal.ReserveCells(.01f, 12) == 1, "A tiny positive reserve stays distinguishable from empty");
        Check(RaidVitalSignal.ReserveCells(99.99f, 12) == 11 && RaidVitalSignal.ReserveCells(100, 12) == 12, "Only full reserves fill every cell");
        Check(RaidVitalSignal.ReserveCells(float.NaN, 12) == 0, "Invalid reserve cannot light cells");
        int previous = 0;
        for (int value = 0; value <= 100; value++)
        {
            int filled = RaidVitalSignal.ReserveCells(value, 12);
            if (filled < previous || filled < 0 || filled > 12) throw new InvalidOperationException("Reserve must rise monotonically");
            previous = filled;
        }
        Check(previous == 12, "The full reserve range remains bounded");
        Check(Mathf.Abs(RaidVitalSignal.Sample(0) - RaidVitalSignal.Sample(1)) < .00001f, "The trace loops without a discontinuity");
        Check(RaidVitalSignal.Sample(.28f) > .7f && RaidVitalSignal.Sample(.315f) < -.1f, "The signal retains its distinct peak and recovery");
        return $"PASS: {checks} vital monitor baseline, exertion, hypoxia, nutrition, death/downed and trace continuity checks.";
    }
}
