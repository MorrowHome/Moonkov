using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

/// <summary>Production recognition and route rules without a scene or navigation mesh.</summary>
public static class PmcGrenadeAwarenessChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/AI/Check Grenade Awareness Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        bool Candidate(TacticalItemKind kind, TacticalWorldPhase phase = TacticalWorldPhase.Flying,
            float distance = 5, float radius = 8, float angle = 30, float cone = 70, bool clash = false) =>
            PmcGrenadeRules.Candidate(clash, kind, phase, distance, radius, angle, cone);
        Check(Candidate(TacticalItemKind.Frag) && Candidate(TacticalItemKind.Dynamite), "both live explosives can be noticed");
        Check(!Candidate(TacticalItemKind.Frag, clash: true), "other game mode remains unchanged");
        Check(!Candidate(TacticalItemKind.Smoke) && !Candidate(TacticalItemKind.Fire) &&
            !Candidate(TacticalItemKind.AlertTrap), "other items are not treated as blast grenades");
        Check(!Candidate(TacticalItemKind.Frag, TacticalWorldPhase.Explosion) &&
            !Candidate(TacticalItemKind.Frag, TacticalWorldPhase.Removed), "expired objects are not live threats");
        Check(Candidate(TacticalItemKind.Frag, distance: 10, angle: 70) &&
            !Candidate(TacticalItemKind.Frag, distance: 10.01f) &&
            !Candidate(TacticalItemKind.Frag, angle: 70.01f), "distance and field of view have bounded edges");
        Check(!Candidate(TacticalItemKind.Frag, distance: float.NaN) &&
            !Candidate(TacticalItemKind.Frag, distance: -1) &&
            !Candidate(TacticalItemKind.Frag, radius: 0) &&
            !Candidate(TacticalItemKind.Frag, radius: float.PositiveInfinity) &&
            !Candidate(TacticalItemKind.Frag, angle: float.NaN) &&
            !Candidate(TacticalItemKind.Frag, cone: float.NaN), "invalid inputs cannot create a threat");
        Check(PmcGrenadeRules.VisibleDanger(false, 8, EnergyGrenadeRules.Radius, 70, 70) &&
            !PmcGrenadeRules.VisibleDanger(false, 8.01f, EnergyGrenadeRules.Radius, 0, 70) &&
            !PmcGrenadeRules.VisibleDanger(true, 1, EnergyGrenadeRules.Radius, 0, 70),
            "energy grenades use their own range and remain excluded from bounty mode");
        Check(PmcGrenadeRules.EnergyId(0) == 0 && PmcGrenadeRules.EnergyId(1) != 1 &&
            PmcGrenadeRules.EnergyId(uint.MaxValue) != uint.MaxValue,
            "energy and tactical server IDs cannot collide");

        var memory = new PmcGrenadeAwareness();
        var first = new Vector3(1, 0, 3);
        Check(!memory.Observe(1, first, 8, 14, 10) && !memory.Active(10), "first sight is not instant recognition");
        Check(!memory.Observe(1, first, 8, 14, 10.1) && !memory.Observe(1, first, 8, 14, 10.2),
            "recognition needs continuous observation");
        Check(memory.Observe(1, first, 8, 14, 10.3) && memory.Active(10.3), "a sustained sighting becomes a threat");
        Check(!memory.Observe(1, first, 8, 14, 10.4) && memory.Active(10.4), "unchanged sightings do not force a replan");
        memory.Miss();
        Check(memory.Active(13.9) && memory.Position.Equals(first), "turning away remembers the last seen location");
        Check(!memory.Active(14), "memory ends at detonation");
        Check(!memory.Observe(1, new Vector3(9, 0, 9), 8, 20, 14.1) && !memory.Active(14.1) &&
            memory.Position.Equals(first), "a new glimpse cannot move the old memory immediately");
        Check(!memory.Observe(1, first, 8, 20, 14.5) && !memory.Active(14.5), "a long observation gap resets recognition");
        memory.Observe(1, first, 8, 20, 14.6);
        memory.Observe(1, first, 8, 20, 14.7);
        Check(memory.Observe(1, first, 8, 20, 14.8) && memory.Active(14.8), "recognition resumes after fresh observation");
        Check(memory.Observe(1, new Vector3(3, 0, 3), 8, 20, 14.9) &&
            memory.Position.Equals(new Vector3(3, 0, 3)), "a visibly moved grenade requests a new plan");
        Check(!memory.Observe(0, first, 8, 20, 15) && !memory.Observe(2, first, float.NaN, 20, 15) &&
            !memory.Observe(2, first, 8, double.NaN, 15) && !memory.Observe(2, first, 8, 14, 15) &&
            !memory.Observe(2, new Vector3(float.NaN, 0, 0), 8, 20, 15), "invalid observations are rejected");
        Check(!memory.Active(double.NaN) && !memory.Active(14), "invalid and earlier times cannot revive memory");

        var mixed = new PmcGrenadeAwareness();
        mixed.Observe(1, first, 8, 9, 1); mixed.Observe(1, first, 8, 9, 1.1);
        mixed.Observe(1, first, 8, 9, 1.2); mixed.Observe(1, first, 8, 9, 1.3);
        ulong energyId = PmcGrenadeRules.EnergyId(1);
        var energyPosition = new Vector3(2, 0, 2);
        Check(!mixed.Observe(energyId, energyPosition, 6, 4, 1.4) && mixed.Position.Equals(first),
            "a different grenade with the same numeric ID needs its own recognition");
        mixed.Observe(energyId, energyPosition, 6, 4, 1.5); mixed.Observe(energyId, energyPosition, 6, 4, 1.6);
        Check(mixed.Observe(energyId, energyPosition, 6, 4, 1.7) && mixed.Position.Equals(energyPosition) && mixed.Radius == 6,
            "energy grenade replaces the old clue only after it is seen");

        var bounded = new PmcGrenadeAwareness();
        bounded.Observe(7, first, 8, 100, 1);
        bounded.Observe(7, first, 8, 100, 1.1);
        bounded.Observe(7, first, 8, 100, 1.2);
        bounded.Observe(7, first, 8, 100, 1.3);
        bounded.Miss();
        Check(bounded.Active(5.29) && !bounded.Active(5.3), "last seen danger is bounded even for an invalid long fuse");
        var other = new Vector3(4, 0, 1);
        Check(!bounded.Observe(8, other, 8, 10, 5.4), "a different object starts its own recognition");
        bounded.Observe(8, other, 8, 10, 5.5);
        bounded.Observe(8, other, 8, 10, 5.6);
        Check(bounded.Observe(8, other, 8, 10, 5.7) && bounded.Position.Equals(other), "new object replaces the clue after recognition");

        Vector3 grenade = Vector3.zero, start = new Vector3(4, 0, 0);
        Check(PmcGrenadeRules.SegmentSafe(start, new Vector3(8, 0, 0), grenade, 3.65f), "a route directly away is safe");
        Check(PmcGrenadeRules.SegmentSafe(start, new Vector3(4, 0, 8), grenade, 3.65f), "a route past the side is safe");
        Check(!PmcGrenadeRules.SegmentSafe(start, new Vector3(-8, 0, 0), grenade, 3.65f), "a route through the grenade is rejected");
        Check(!PmcGrenadeRules.SegmentSafe(start, new Vector3(-2, 0, 8), grenade, 3.65f), "a nearer intermediate point is rejected");
        Check(!PmcGrenadeRules.SegmentSafe(start, new Vector3(float.NaN, 0, 0), grenade, 3) &&
            !PmcGrenadeRules.SegmentSafe(start, start, grenade, -1), "invalid route data is rejected");
        return "PASS " + s_Checks + " PMC grenade awareness checks";
    }
}
