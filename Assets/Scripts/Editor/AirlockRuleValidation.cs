using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;

public static class AirlockRuleValidation
{
    private static void Check(bool result, string name, List<string> passed)
    { if (!result) throw new InvalidOperationException("Airlock check failed: " + name); passed.Add(name); }
    [MenuItem("Tools/Moonkov/Airlock/Check pressure, interlock and audio rules")]
    public static string Run()
    {
        var passed = new List<string>(); var settings = new AirlockSettings(); var rules = new AirlockRules(settings);
        Check(!rules.Breathable, "vacuum chamber is not breathable", passed);
        Check(rules.Request(AirlockCommand.OpenInner) == AirlockFeedback.PressureMismatch, "inner pressure guard", passed);
        Check(rules.Request(AirlockCommand.OpenOuter) == AirlockFeedback.Accepted, "outside door opens at vacuum", passed);
        rules.Tick(settings.DoorTravelSeconds);
        Check(rules.State.OuterFraction == 1, "outer reaches actual open endpoint", passed);
        Check(rules.Request(AirlockCommand.OpenInner) == AirlockFeedback.OtherDoorNotSealed, "mutual exclusion", passed);
        Check(rules.Request(AirlockCommand.EqualizeInside) == AirlockFeedback.DoorsNotSealed, "no equalization with open door", passed);
        Check(rules.Request(AirlockCommand.CloseOuter, true) == AirlockFeedback.Obstructed, "occupied aperture blocks close", passed);
        rules.Request(AirlockCommand.CloseOuter); rules.Tick(settings.DoorTravelSeconds * .5f);
        Check(rules.Request(AirlockCommand.EqualizeInside) == AirlockFeedback.DoorsNotSealed, "partial close is not a seal", passed);
        rules.Tick(.01f, true); Check(rules.State.OuterOpening, "intrusion reverses a closing door", passed);
        rules.Tick(settings.DoorTravelSeconds); rules.Request(AirlockCommand.CloseOuter); rules.Tick(settings.DoorTravelSeconds);
        Check(rules.OuterSealed, "close completes", passed);
        Check(rules.Request(AirlockCommand.EqualizeInside) == AirlockFeedback.Accepted, "pressurization begins after both seals", passed);
        rules.Tick(settings.PressurizeSeconds * .5f);
        Check(rules.Request(AirlockCommand.OpenInner) == AirlockFeedback.Equalizing, "no early inner open during pressure ramp", passed);
        rules.Tick(settings.PressurizeSeconds);
        Check(rules.Breathable && !rules.State.Equalizing && rules.State.PressureKPa == settings.InsidePressureKPa, "pressure completes and atmosphere qualifies", passed);
        Check(rules.Request(AirlockCommand.OpenInner) == AirlockFeedback.Accepted, "inner opens after threshold", passed);
        rules.Tick(settings.DoorTravelSeconds);
        Check(rules.Request(AirlockCommand.OpenOuter) == AirlockFeedback.OtherDoorNotSealed, "reverse mutual exclusion", passed);
        rules.Request(AirlockCommand.CloseInner); rules.Tick(settings.DoorTravelSeconds);
        Check(rules.Request(AirlockCommand.OpenOuter) == AirlockFeedback.PressureMismatch, "outbound pressure guard", passed);
        rules.Request(AirlockCommand.EqualizeOutside); rules.Tick(settings.DepressurizeSeconds);
        Check(!rules.Breathable && rules.State.PressureKPa == settings.OutsidePressureKPa, "outbound depressurization", passed);
        Check(rules.Request(AirlockCommand.OpenOuter) == AirlockFeedback.Accepted, "outbound outer open", passed);
        var snapshot = rules.State; snapshot.OuterFraction = 1; snapshot.InnerFraction = 1;
        Check(!rules.ApplySnapshot(snapshot,true), "invalid double-open snapshot rejected", passed);
        snapshot = rules.State; snapshot.PressureKPa = float.NaN;
        Check(!rules.ApplySnapshot(snapshot,true), "non-finite snapshot rejected", passed);
        var vacuum = default(AtmosphereSample); var air = new AtmosphereSample("room",101.325f,.21f,true);
        Check(AirlockAcoustics.Gain(AirlockSoundPath.Airborne,air,vacuum,false) == 0, "no external air sound in vacuum", passed);
        Check(AirlockAcoustics.Gain(AirlockSoundPath.HelmetInternal,vacuum,vacuum,false) == 1, "helmet channel survives vacuum", passed);
        Check(AirlockAcoustics.Gain(AirlockSoundPath.Radio,vacuum,vacuum,false) == 1, "radio channel survives vacuum", passed);
        Check(AirlockAcoustics.Gain(AirlockSoundPath.StructureContact,vacuum,vacuum,true) > 0, "contact conduction survives vacuum", passed);
        Check(AirlockAcoustics.Gain(AirlockSoundPath.StructureContact,vacuum,vacuum,false) == 0, "contact path requires contact", passed);
        Check(AirlockAcoustics.Gain(AirlockSoundPath.Airborne,air,air,false) == 1, "same pressurized airspace transmits air sound", passed);
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health); health.Oxygen = 50;
        RaidHealth.TickOxygen(ref health,false,6,600,5,8,3,0);
        Check(Mathf.Abs(health.Oxygen-49) < .001f, "existing suit rule consumes in vacuum", passed);
        RaidHealth.TickOxygen(ref health,true,1,600,5,8,3,0);
        Check(Mathf.Abs(health.Oxygen-54) < .001f, "existing suit rule stops drain and recovers in air", passed);
        Check(!new AirlockSettings { PressurizeSeconds = float.NaN }.IsValid, "invalid configuration rejected", passed);
        string json = "{\"passed\":" + passed.Count + ",\"checks\":[\"" + string.Join("\",\"",passed) + "\"]}";
        System.IO.Directory.CreateDirectory("Temp/MoonAirlockIntegration"); System.IO.File.WriteAllText("Temp/MoonAirlockIntegration/rule_validation.json",json);
        return json;
    }
}
