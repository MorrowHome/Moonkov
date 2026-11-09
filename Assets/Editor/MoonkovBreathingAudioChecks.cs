using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovBreathingAudioChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Stamina Breathing")]
    public static string Run()
    {
        s_Checks = 0;
        var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state);
        state.BreathableAir = true;
        Check(!MoonkovOxygenFeedback.Breath(state).Audible, "Rested player in air is quiet");
        state.ControllerState.Sprinting = true; state.ControllerState.MovementSpeed = 5;
        var fresh = MoonkovOxygenFeedback.Breath(state);
        state.TacticalSprintStamina = 40; var tired = MoonkovOxygenFeedback.Breath(state);
        state.TacticalSprintStamina = 0; var empty = MoonkovOxygenFeedback.Breath(state);
        Check(fresh.Audible && tired.Volume > fresh.Volume && empty.Volume > tired.Volume,
            "Lower stamina increases breathing volume");
        Check(fresh.Interval > tired.Interval && tired.Interval > empty.Interval,
            "Lower stamina increases breathing cadence");
        Check(empty.Exerted && empty.Interval >= 2.2f && empty.Interval <= 2.5f && empty.Pitch == 0 && empty.Volume <= 1,
            "Exhaustion uses controlled heavy breathing with space between cycles");
        state.TacticalSprintStamina = 50; state.TacticalSprintExhausted = true;
        Check(MoonkovOxygenFeedback.Breath(state).Interval == empty.Interval, "Exhausted latch retains heavy breathing during initial recovery");
        state.TacticalSprintExhausted = false; state.TacticalSprintStamina = 0;
        state.ControllerState.Sprinting = false; state.ControllerState.MovementSpeed = 0;
        Check(MoonkovOxygenFeedback.Breath(state).Audible, "Stopping sprint cannot instantly remove exhaustion");
        state.TacticalSprintStamina = 60;
        var recovering = MoonkovOxygenFeedback.Breath(state);
        Check(recovering.Audible && recovering.Volume < empty.Volume && recovering.Interval > empty.Interval,
            "Resting stamina recovery eases breathing");
        state.TacticalSprintStamina = 100;
        Check(!MoonkovOxygenFeedback.Breath(state).Audible, "Full recovery in air becomes quiet");
        state.MeleeEpoch = 1; state.MeleeStamina = 10;
        Check(MoonkovOxygenFeedback.Breath(state).Exerted, "Melee fatigue also causes panting");
        state.MeleeEpoch = 0; state.TacticalSprintStamina = float.NaN;
        Check(!MoonkovOxygenFeedback.Breath(state).Audible, "Invalid stamina cannot create invalid playback");
        state.TacticalSprintStamina = 100; state.BreathableAir = false;
        var calm = MoonkovOxygenFeedback.Breath(state);
        Check(calm.Audible && !calm.Exerted, "Suit breathing remains audible at rest");
        state.CurrentHealth = 0;
        Check(!MoonkovOxygenFeedback.Breath(state).Audible, "Death is silent");

        var cadence = new MoonkovBreathingAudio(); int count = 0;
        for (int frame = 0; frame <= 1800; frame++)
        {
            float t = frame / 60f;
            var changing = new OxygenBreath(true, Mathf.Lerp(4.1f, 2.4f, t / 30), 1, 0);
            if (cadence.Observe(changing, t, false)) count++;
        }
        Check(count >= 8 && count <= 12, "Continuously changing stamina must not starve or spam breaths");
        cadence.Reset();
        Check(!cadence.Observe(empty, 0, false) && cadence.Observe(empty, .5f, false), "Short onset then first breath");
        for (int frame = 1; frame <= 600; frame++)
            Check(!cadence.Observe(empty, .5f + frame / 60f, true), "A sustained voice must suppress breathing throughout");
        Check(cadence.Observe(empty, 10.8f, false), "Breathing resumes after the voice finishes");
        Check(!cadence.Observe(default, 11, false) && !cadence.Observe(empty, 11.1f, false), "Recovery resets cadence");

        cadence.Reset();
        cadence.Observe(calm, 0, false); Check(cadence.Observe(calm, .5f, false), "Calm cycle starts");
        Check(!cadence.Observe(empty, .6f, false) && cadence.Observe(empty, .7f, false),
            "Exhaustion must not wait for the old calm timer");

        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        Check(lib.SuitBreath && lib.SuitBreathEffort, "Both breathing banks are bound");
        for (int stamina = 0; stamina <= 100; stamina++)
        {
            state.CurrentHealth = state.MaxHealth; state.TacticalSprintStamina = stamina;
            var breath = MoonkovOxygenFeedback.Breath(state);
            var bank = breath.Exerted ? lib.SuitBreathEffort : lib.SuitBreath;
            Check(bank.Clips.TrueForAll(c => c.length / Mathf.Pow(2, breath.Pitch / 1200) < breath.Interval),
                "The whole inhale/exhale must fit before the next breath");
        }
        foreach (var bank in new[] { lib.SuitBreath, lib.SuitBreathEffort })
        {
            Check(bank.DistanceInfo.SpatialBlend == 0 && bank.MixerGroup == SoundMixer.SoundMixerGroup.SFX &&
                bank.MaxVoices == 1 && bank.PitchAndVolumeInfo.VolumeMax >= -10, "Close body sound and bounded SFX routing");
            var clip = bank.Clips[0];
            Check(clip.channels == 1 && clip.frequency == 48000 && clip.loadType == AudioClipLoadType.DecompressOnLoad,
                "Breathing uses preloaded mono PCM");
        }
        Check(lib.SuitBreathEffort.Clips.Count == 3 && lib.SuitBreathEffort.PlaybackType == SoundDef.PlaybackTypes.RandomNotLast &&
            lib.SuitBreathEffort.PitchAndVolumeInfo.VolumeMax <= -6 &&
            lib.SuitBreathEffort.PitchAndVolumeInfo.VolumeMin - lib.SuitBreath.PitchAndVolumeInfo.VolumeMax >= 2,
            "Heavy breaths should have body above calm breathing without the old near-full-volume gasp");
        return $"PASS: {s_Checks} stamina breathing, continuous cadence, voice priority and asset checks.";
    }
}
