using System;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class OxygenSupplyFeedbackChecks
{
    private const string SoundPath = "Assets/Audio/Moonkov/SoundDefs/OxygenSupply.asset";
    private const string SourcePath = "Assets/Audio/Moonkov/Airlock/SoundDefs/ValveAir_Loop.asset";
    private static int s_Checks;
    private static void Check(bool condition, string message)
    { s_Checks++; if (!condition) throw new InvalidOperationException(message); }
    private static PredictedPlayerGhost Player(float oxygen = 20)
    {
        var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state);
        state.ProtectionInitialized = state.HeadDomeEquipped = true;
        state.Oxygen = oxygen; state.OxygenDrainPerSecond = 1;
        return state;
    }

    public static void InstallSound()
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(SoundPath);
        if (asset && !(asset is SoundDef)) throw new InvalidOperationException("Oxygen sound path is occupied by another asset type.");
        var sound = asset as SoundDef;
        if (!sound)
        {
            var source = AssetDatabase.LoadAssetAtPath<SoundDef>(SourcePath);
            if (!source) throw new InvalidOperationException("Missing existing licensed valve recording.");
            sound = UnityEngine.Object.Instantiate(source); sound.name = "OxygenSupply";
            sound.Role = SoundDef.MixRole.Suit; sound.Priority = 80; sound.MaxVoices = 1;
            sound.MinimumInterval = .1f; sound.Occlusion = false;
            sound.DistanceInfo.SpatialBlend = 0;
            sound.PitchAndVolumeInfo.VolumeMin = sound.PitchAndVolumeInfo.VolumeMax = -26;
            sound.LowPassFilter.CutoffMin = sound.LowPassFilter.CutoffMax = 1800;
            AssetDatabase.CreateAsset(sound, SoundPath);
        }
        var library = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
        if (!library) throw new InvalidOperationException("Missing game audio library.");
        if (library.OxygenSupply != sound)
        { library.OxygenSupply = sound; EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library); }
    }

    [MenuItem("Tools/Moonkov/Oxygen/Check External Supply Feedback")]
    public static string Run()
    {
        s_Checks = 0;
        var state = Player();
        Check(!RaidOxygen.ReceivingExternalSupply(state), "Portable use or a reserve change alone cannot start the external flow loop");
        state.OxygenSupplyPerSecond = 11;
        Check(RaidOxygen.ReceivingExternalSupply(state) && Mathf.Abs(RaidOxygen.RefillSeconds(state) - 8) < .001f,
            "Refill estimate subtracts breathing demand");
        state.OxygenSupplyPerSecond = .5f;
        Check(RaidOxygen.ReceivingExternalSupply(state) && float.IsPositiveInfinity(RaidOxygen.RefillSeconds(state)) &&
            Mathf.Abs(RaidOxygen.RemainingSeconds(state) - 40) < .001f, "Weak external flow is audible but still depletes reserve");
        state.OxygenSupplyPerSecond = 1;
        Check(RaidOxygen.ReceivingExternalSupply(state) && float.IsPositiveInfinity(RaidOxygen.RefillSeconds(state)) &&
            float.IsPositiveInfinity(RaidOxygen.RemainingSeconds(state)), "Balanced supply maintains reserve without a fake refill countdown");
        state.OxygenSupplyPerSecond = 0;
        Check(!RaidOxygen.ReceivingExternalSupply(state), "Power loss or leaving supply stops flow");
        state.OxygenSupplyPerSecond = 11; state.Oxygen = 100;
        Check(!RaidOxygen.ReceivingExternalSupply(state) && RaidOxygen.RefillSeconds(state) == 0, "Full suit stops flow feedback");
        state.Oxygen = 20; state.HeadDomeEquipped = false;
        Check(!RaidOxygen.ReceivingExternalSupply(state) && float.IsPositiveInfinity(RaidOxygen.RefillSeconds(state)), "Unsealed equipment cannot claim refill feedback");
        state.HeadDomeEquipped = true; state.CurrentHealth = 0;
        Check(!RaidOxygen.ReceivingExternalSupply(state), "Death stops flow feedback");
        state = Player(); state.BreathableAir = true;
        Check(!RaidOxygen.ReceivingExternalSupply(state), "Breathable air alone does not pretend to fill the suit");
        state.OxygenSupplyPerSecond = 5; state.OxygenDrainPerSecond = 0;
        Check(RaidOxygen.ReceivingExternalSupply(state) && RaidOxygen.RefillSeconds(state) == 16, "Room refill uses its actual rate");
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
        {
            state.OxygenSupplyPerSecond = invalid;
            Check(!RaidOxygen.ReceivingExternalSupply(state) && float.IsPositiveInfinity(RaidOxygen.RefillSeconds(state)), "Invalid supply cannot trigger flow or countdown");
        }
        state = Player(); state.OxygenSupplyPerSecond = 5; state.OxygenDrainPerSecond = float.NaN;
        Check(!RaidOxygen.ReceivingExternalSupply(state) && float.IsPositiveInfinity(RaidOxygen.RemainingSeconds(state)), "Invalid drain does not produce a broken clock");
        Check(RaidOxygen.DisplayPercent(99.99f) == 99 && RaidOxygen.DisplayPercent(100) == 100,
            "Almost full cannot display full while filling");
        Check(RaidOxygen.DisplayPercent(.01f) == 1 && RaidOxygen.DisplayPercent(0) == 0 && RaidOxygen.DisplayPercent(float.NaN) == -1,
            "Tiny reserve and missing readings stay distinct from empty");
        Monitor(); Sound();
        return $"PASS: {s_Checks} external oxygen flow, reserve/refill estimates, monitor and loop-asset checks.";
    }

    private static void Monitor()
    {
        var state = Player(); var view = new RaidVitalsMonitor(); view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "LOW OXYGEN / 00:20", "Low oxygen surfaces actual remaining time");
        state.Oxygen = 19; view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "LOW OXYGEN / 00:19", "Time updates within same warning stage");
        state.Oxygen = 10; state.OxygenDrainPerSecond = 2; view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "CRITICAL OXYGEN / 00:05", "Exertion changes the visible estimate");
        state = Player(40); state.BreathableAir = true; state.OxygenDrainPerSecond = 0; state.OxygenSupplyPerSecond = 5;
        view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "OXYGEN REFILLING / 00:12", "Refill clock counts towards full");
        state.Oxygen = 99.9f; view.Present(state);
        Check(view.Q("vitals-reserve-0").Q<Label>().text == "O₂  99%" && view.Q<Label>("vitals-warning").text.EndsWith("00:01"),
            "Display does not claim full before actual completion");
        state.Oxygen = 100; view.Present(state);
        Check(view.Q<Label>("vitals-warning").style.display == DisplayStyle.None, "Full refill clears status");
        state = Player(20); state.BreathableAir = true; view.Present(state);
        Check(view.Q<Label>("vitals-warning").style.display == DisplayStyle.None, "Safe air without refill has no fake ETA");
        state.OxygenSupplyPerSecond = 5; state.OxygenDrainPerSecond = 0; state.Hydration = 0; view.Present(state);
        Check(view.Q<Label>("vitals-warning").text == "DEHYDRATED", "Refill clock cannot replace a more urgent health warning");
    }

    private static void Sound()
    {
        var sound = AssetDatabase.LoadAssetAtPath<SoundDef>(SoundPath);
        var source = AssetDatabase.LoadAssetAtPath<SoundDef>(SourcePath);
        var library = AssetDatabase.LoadAssetAtPath<MoonkovAudioLibrary>("Assets/Resources/Moonkov/AudioLibrary.asset");
        Check(sound && library.OxygenSupply == sound, "Game audio library contains external supply loop");
        Check(sound != source && sound.Clips[0] == source.Clips[0] && sound.RepeatInfo.LoopCount == 0,
            "Uses existing seamless recording without changing airlock sound");
        Check(sound.MaxVoices == 1 && sound.Role == SoundDef.MixRole.Suit && sound.DistanceInfo.SpatialBlend == 0,
            "Supply sound has one local pooled voice");
        Check(sound.Clips[0].length > 1 && sound.PitchAndVolumeInfo.VolumeMax <= -20,
            "Loop is a quiet sustained cue rather than repeated inventory foley");
    }

    public static void RunBatch()
    {
        try
        {
            InstallSound(); Debug.Log(Run()); Debug.Log(OxygenFeedbackChecks.Run()); MoonkovOxygenChecks.Run();
            System.IO.File.WriteAllText("Library/BuildValidation/OxygenSupply-result.txt", "PASS");
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
