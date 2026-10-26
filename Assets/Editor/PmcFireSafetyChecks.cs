using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class PmcFireSafetyChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/AI/Check Fire Evasion Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        bool Active(bool clash = false, double burn = 5, TacticalWorldPhase phase = TacticalWorldPhase.Fire,
            double until = 20, double now = 2, bool inside = true) => PmcFireRules.Active(clash, burn, phase, until, now, inside);
        Check(Active(), "a burning raid actor in active fire should escape");
        Check(!Active(clash: true), "bounty encounters keep their existing behavior");
        Check(!Active(inside: false), "a fire behind cover or outside the area is not an immediate hazard");
        Check(!Active(burn: 2) && !Active(burn: 0), "an actor not burning does not infer a hazard");
        Check(!Active(until: 2) && !Active(phase: TacticalWorldPhase.Flying) && !Active(phase: TacticalWorldPhase.Removed),
            "expired or undetonated objects do not create active fire");
        Check(!Active(now: double.NaN) && !Active(burn: double.PositiveInfinity) && !Active(until: double.NaN),
            "invalid timing cannot invent a hazard");
        Check(PmcSprintRules.Allows(false, PmcAction.EvadeFire, true) &&
            !PmcSprintRules.Allows(false, PmcAction.Retreat, true), "exhausted actors may still attempt to leave fire");
        Check(PmcGrenadeRules.SegmentSafe(new Vector3(1, 0, 0), new Vector3(6, 0, 0), Vector3.zero, .65f) &&
            !PmcGrenadeRules.SegmentSafe(new Vector3(1, 0, 0), new Vector3(-6, 0, 0), Vector3.zero, .65f),
            "the shared escape route cannot first move deeper through the hazard");
        return "PASS " + s_Checks + " PMC fire safety rules";
    }
}
