using System;
using System.Runtime.Serialization;
using Unity.Entities;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class TacticalAlarmHearingChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/Tactical Items/Check Alarm Hearing Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        Check(TacticalAlarmHearing.WithinRange(35 * 35, false) &&
            !TacticalAlarmHearing.WithinRange(35.01f * 35.01f, false), "unobstructed hearing has a finite range");
        Check(TacticalAlarmHearing.WithinRange(19 * 19, true) &&
            !TacticalAlarmHearing.WithinRange(20 * 20, true), "a wall reduces hearing range");
        Check(!TacticalAlarmHearing.WithinRange(float.NaN, false) &&
            !TacticalAlarmHearing.WithinRange(float.PositiveInfinity, false) &&
            !TacticalAlarmHearing.WithinRange(-1, false), "invalid distances are rejected");
        Check(TacticalAlarmHearing.WithinRange(50 * 50, false, RaidEnvironmentalNoise.ExplosionRange) &&
            !TacticalAlarmHearing.WithinRange(50.01f * 50.01f, false, RaidEnvironmentalNoise.ExplosionRange) &&
            TacticalAlarmHearing.WithinRange(27 * 27, true, RaidEnvironmentalNoise.ExplosionRange) &&
            !TacticalAlarmHearing.WithinRange(28 * 28, true, RaidEnvironmentalNoise.ExplosionRange),
            "explosions have their own finite and wall-attenuated range");
        string id = TacticalAlarmHearing.EventId(2, 7);
        Check(id != null && id != TacticalAlarmHearing.EventId(3, 7) &&
            id != TacticalAlarmHearing.EventId(2, 8) && TacticalAlarmHearing.EventId(2, 0) == null,
            "different traps and world epochs cannot reuse a clue");
        Check(RaidEnvironmentalNoise.ExplosionId(false, 2, 7) != RaidEnvironmentalNoise.ExplosionId(false, 3, 7) &&
            RaidEnvironmentalNoise.ExplosionId(false, 2, 7) != RaidEnvironmentalNoise.ExplosionId(true, 2, 7) &&
            RaidEnvironmentalNoise.ExplosionId(false, 2, 7) != id && RaidEnvironmentalNoise.ExplosionId(true, 0, 0) == null,
            "blast types, trap IDs and tactical world epochs cannot collide");

        var brain = (DollSingerEnemyBrain)FormatterServices.GetUninitializedObject(typeof(DollSingerEnemyBrain));
        brain.Action = PmcAction.Scavenge; brain.CommitUntil = 90; brain.NextDecision = 90;
        Vector3 source = new Vector3(3, 0, 4);
        Check(PmcSoundClue.Hear(brain, id, source, 10) && brain.MissionAlarmId == id &&
            brain.MissionAlarmPosition.Equals(source) && brain.MissionAlarmUntil == 40 &&
            brain.Target == Entity.Null && !brain.Visible && brain.NextDecision == 0 && brain.CommitUntil == 0,
            "hearing starts an anonymous location investigation and interrupts scavenging");
        Check(!PmcSoundClue.Hear(brain, id, source, 12) && brain.MissionAlarmUntil == 40,
            "the same alarm cannot extend its own investigation");
        brain.Target = new Entity { Index = 1, Version = 1 };
        Check(!PmcSoundClue.Hear(brain, "trap/2/8", source, 13) && brain.MissionAlarmId == id,
            "a new sound cannot replace an active opponent");
        brain.Target = Entity.Null; brain.Leaving = true;
        Check(!PmcSoundClue.Hear(brain, "trap/2/8", source, 13), "departing enemies keep leaving");
        brain.Leaving = false; brain.ClashParticipantId = 1;
        Check(!PmcSoundClue.Hear(brain, "trap/2/8", source, 13), "bounty mode is unaffected");
        brain.ClashParticipantId = 0;
        Check(!PmcSoundClue.Hear(brain, null, source, 13) &&
            PmcSoundClue.Hear(brain, "trap/2/8", source, 13) && brain.MissionAlarmUntil == 43,
            "a distinct audible alarm can become a new clue");
        return "PASS " + s_Checks + " alarm and blast hearing rules checks";
    }
}
