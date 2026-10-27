using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class PmcFireSafetyChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/AI/Check Environmental Hazard Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        var status = new TacticalServerWorld.Status { Fire = 5, Poison = 5, Wire = 5 };
        bool Active(bool clash = false, TacticalWorldPhase phase = TacticalWorldPhase.Fire,
            double until = 20, double now = 2, bool inside = true) => PmcHazardRules.Active(clash, status, phase, until, now, inside);
        Check(Active() && Active(phase: TacticalWorldPhase.Poison) && Active(phase: TacticalWorldPhase.Wire),
            "actual fire, poison and wire effects can prompt escape");
        Check(!Active(clash: true), "bounty encounters keep their existing behavior");
        Check(!Active(inside: false), "a hazard behind cover or outside its area is not immediate");
        status.Fire = 2; status.Poison = 2; status.Wire = 2;
        Check(!Active() && !Active(phase: TacticalWorldPhase.Poison) && !Active(phase: TacticalWorldPhase.Wire),
            "expired status effects cannot reveal a hazard");
        status.Fire = status.Poison = status.Wire = 5;
        Check(!Active(until: 2) && !Active(phase: TacticalWorldPhase.Flying) && !Active(phase: TacticalWorldPhase.Armed) &&
            !Active(phase: TacticalWorldPhase.Removed), "expired or untriggered objects do not create an active hazard");
        status.Antidote = 8;
        Check(!Active(phase: TacticalWorldPhase.Poison) && Active() && Active(phase: TacticalWorldPhase.Wire),
            "poison immunity does not hide fire or wire");
        status.Antidote = 0; status.Wire = double.PositiveInfinity;
        Check(!Active(now: double.NaN) && !Active(phase: TacticalWorldPhase.Wire) && !Active(until: double.NaN) &&
            !PmcHazardRules.Active(false, null, TacticalWorldPhase.Fire, 20, 2, true),
            "invalid timing cannot invent a hazard");
        Check(PmcSprintRules.Allows(false, PmcAction.EvadeHazard, true) &&
            !PmcSprintRules.Allows(false, PmcAction.Retreat, true), "exhausted actors may still attempt to leave a hazard");
        Check(PmcGrenadeRules.SegmentSafe(new Vector3(1, 0, 0), new Vector3(6, 0, 0), Vector3.zero, .65f) &&
            !PmcGrenadeRules.SegmentSafe(new Vector3(1, 0, 0), new Vector3(-6, 0, 0), Vector3.zero, .65f),
            "the shared escape route cannot first move deeper through the hazard");
        return "PASS " + s_Checks + " PMC environmental hazard rules";
    }
}
