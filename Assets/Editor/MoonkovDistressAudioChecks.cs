using System;
using Unity.MP_FPS;
using UnityEngine;

public static class MoonkovDistressAudioChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Run()
    {
        var cue = new MoonkovDistressAudio();
        Require(!cue.Observe(.2f, true, false, false, 0), "Immediate low-health spam");
        int count = 0;
        for (int frame = 1; frame <= 1800; frame++)
            if (cue.Observe(.2f, true, false, false, frame / 60f)) count++;
        Require(count == 3, "Expected only 3 groans in 30 seconds, got " + count);
        Require(!cue.Observe(.31f, true, false, false, 30.1f) && cue.Active, "Threshold jitter");
        Require(!cue.Observe(.36f, true, false, false, 30.2f) && !cue.Active, "Recovery did not stop");
        Require(!cue.Observe(1, true, true, false, 31), "Status onset spam");
        Require(cue.Observe(1, true, true, false, 34.5f), "Painful status missing");
        Require(!cue.Observe(1, true, true, true, 43.5f), "Direct cry overlapped groan");
        Require(!cue.Observe(1, true, true, false, 44), "Direct cry did not postpone groan");
        Require(cue.Observe(1, true, true, false, 52.5f), "Sustained distress never resumes");
        Require(!cue.Observe(0, false, true, false, 70) && !cue.Active, "Dead character groaned");
        var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state); RaidNutrition.Initialize(ref state);
        state.BreathableAir = true; state.Oxygen = 100;
        Require(!MoonkovDistressAudio.PainfulStatus(state), "Healthy status hurts");
        state.Energy = state.Hydration = 20;
        Require(!MoonkovDistressAudio.PainfulStatus(state), "Mild hunger/thirst hurts");
        state.TacticalPoisonSeconds = 3;
        Require(MoonkovDistressAudio.PainfulStatus(state), "Poison silent");
        state.TacticalPoisonSeconds = 0; state.Energy = 0;
        Require(MoonkovDistressAudio.PainfulStatus(state), "Starvation silent");
        state.Energy = 100; state.Hydration = 0;
        Require(MoonkovDistressAudio.PainfulStatus(state), "Dehydration silent");
        state.Hydration = 100; state.BreathableAir = false; state.Oxygen = 10;
        Require(!MoonkovDistressAudio.PainfulStatus(state), "Low oxygen warning hurts");
        state.Oxygen = 0;
        Require(MoonkovDistressAudio.PainfulStatus(state), "Hypoxia silent");
        state.BreathableAir = true; state.TacticalWireSeconds = 2;
        Require(MoonkovDistressAudio.PainfulStatus(state), "Wire status silent");
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        Require(lib.PlayerGroan && lib.PlayerGroan != lib.PlayerPain && lib.PlayerGroan.Clips.Count == 3, "Groan bank missing");
        Require(lib.PlayerGroan.PitchAndVolumeInfo.VolumeMax < lib.PlayerPain.PitchAndVolumeInfo.VolumeMax, "Groan too loud");
        Debug.Log("Distress: 30 seconds/1800 updates produce 3 groans; delay, hysteresis, recovery, direct-hit priority, death and status classification passed.");
    }
}
