using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class RaidExtractionRulesChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Raid Extraction Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        Vector3 primary = new Vector3(10, 2, 20);
        Check(RaidExtractionRules.Count(null) == 1 &&
            RaidExtractionRules.TryPosition(primary, null, 0, out var original) && original == primary,
            "legacy maps keep their authored exit");
        Check(!RaidExtractionRules.TryPosition(primary, null, 1, out _) &&
            !RaidExtractionRules.TryPosition(primary, null, -1, out _), "exit indexes are bounded");
        var random = new System.Random(1234);
        for (int i = 0; i < 1000; i++)
        {
            var p = primary + new Vector3((float)(random.NextDouble() * 14 - 7),
                (float)(random.NextDouble() * 10 - 5), (float)(random.NextDouble() * 14 - 7));
            Vector3 delta = p - primary;
            bool legacy = delta.x * delta.x + delta.z * delta.z <= 25 && Mathf.Abs(delta.y) <= 3;
            Check((RaidExtractionRules.AtPosition(primary, null, p, 5, -1) == 0) == legacy,
                "legacy radius and floor rules changed");
        }
        Check(RaidExtractionRules.AtPosition(primary, null, primary + new Vector3(5, 3, 0), 5, -1) == 0 &&
            RaidExtractionRules.AtPosition(primary, null, primary + new Vector3(5.01f, 0, 0), 5, -1) == -1 &&
            RaidExtractionRules.AtPosition(primary, null, primary + new Vector3(0, 3.01f, 0), 5, -1) == -1,
            "zone edge and vertical separation match the original rule");

        var exits = new[] { new Vector3(30, 2, 20), new Vector3(10, 15, 20) };
        Check(RaidExtractionRules.Count(exits) == 3 && RaidExtractionRules.NearestIndex(primary, exits, new Vector3(29, 2, 20)) == 1,
            "the nearest configured exit is selected");
        Check(RaidExtractionRules.NearestIndex(primary, exits, new Vector3(10, 14, 20)) == 2,
            "distance considers floor height rather than pointing to the wrong level");
        Check(RaidExtractionRules.Nearest(primary, exits, new Vector3(29, 2, 20)) == exits[0], "guidance uses that same exit");
        var invalid = new[] { new Vector3(float.NaN, 0, 0), exits[0], new Vector3(float.PositiveInfinity, 0, 0) };
        Check(!RaidExtractionRules.TryPosition(primary, invalid, 1, out _) &&
            RaidExtractionRules.NearestIndex(primary, invalid, exits[0]) == 2,
            "invalid authored points cannot become destinations");
        Check(RaidExtractionRules.NearestIndex(primary, exits, new Vector3(float.NaN, 0, 0)) == -1 &&
            RaidExtractionRules.AtPosition(primary, exits, primary, float.NaN, -1) == -1,
            "invalid player positions and radii never trigger extraction");
        var tooMany = new Vector3[12];
        Check(RaidExtractionRules.Count(tooMany) == RaidExtractionRules.MaxExits &&
            !RaidExtractionRules.TryPosition(primary, tooMany, RaidExtractionRules.MaxExits, out _),
            "authored exit work is bounded");

        var session = new RaidSession { Phase = RaidPhase.Active };
        RaidExtractionRules.Advance(ref session, primary, exits, primary, 5, 3);
        Check(session.ExtractionIndex == 0 && session.ExtractionProgress == 3, "first exit starts its own timer");
        RaidExtractionRules.Advance(ref session, primary, exits, primary, 5, 1);
        Check(session.ExtractionProgress == 4, "remaining inside continues the same timer");
        RaidExtractionRules.Advance(ref session, primary, exits, exits[0], 5, 1);
        Check(session.ExtractionIndex == 1 && session.ExtractionProgress == 1, "changing exits resets the previous timer");
        RaidExtractionRules.Advance(ref session, primary, exits, new Vector3(100, 2, 100), 5, 1);
        Check(session.ExtractionIndex == -1 && session.ExtractionProgress == 0, "leaving every exit cancels progress");
        RaidExtractionRules.Advance(ref session, primary, exits, exits[0], 5, 2);
        Check(session.ExtractionIndex == 1 && session.ExtractionProgress == 2, "returning starts fresh");
        RaidExtractionRules.Advance(ref session, primary, exits, exits[0], 5, float.NaN);
        Check(session.ExtractionProgress == 2, "invalid delta time does not advance the timer");
        RaidExtractionRules.Advance(ref session, primary, exits, exits[0] + Vector3.up * 4, 5, 1);
        Check(session.ExtractionProgress == 0 && session.ExtractionIndex == -1, "another floor cannot continue extraction");

        var overlap = new[] { new Vector3(7, 0, 0) };
        session = new RaidSession { Phase = RaidPhase.Active };
        RaidExtractionRules.Advance(ref session, Vector3.zero, overlap, new Vector3(3, 0, 0), 5, 2);
        RaidExtractionRules.Advance(ref session, Vector3.zero, overlap, new Vector3(4, 0, 0), 5, 2);
        Check(session.ExtractionIndex == 0 && session.ExtractionProgress == 4,
            "moving within overlapping zones keeps the chosen exit instead of flickering");
        RaidExtractionRules.Advance(ref session, Vector3.zero, overlap, new Vector3(6, 0, 0), 5, 1);
        Check(session.ExtractionIndex == 1 && session.ExtractionProgress == 1,
            "leaving the chosen overlap starts the other exit from zero");
        var other = new RaidSession { Phase = RaidPhase.Active };
        RaidExtractionRules.Advance(ref other, Vector3.zero, overlap, Vector3.zero, 5, 2);
        Check(other.ExtractionProgress == 2 && session.ExtractionIndex == 1 && session.ExtractionProgress == 1,
            "different players have independent extraction progress");
        Check(RaidRules.TrySettle(ref session, RaidPhase.Extracted) && session.ExtractionProgress == 0 && session.ExtractionIndex == -1,
            "settlement clears the exit timer");
        RaidRules.BeginNext(ref session, 600);
        Check(session.ExtractionProgress == 0 && session.ExtractionIndex == -1 && session.TimeLeft == 600,
            "the next raid starts without an old exit selection");
        return "PASS " + s_Checks + " raid extraction rules checks";
    }
}
