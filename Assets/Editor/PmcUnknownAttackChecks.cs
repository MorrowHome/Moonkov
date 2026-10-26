using System;
using Unity.Mathematics;
using Unity.MP_FPS;

public static class PmcUnknownAttackChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    public static string Run()
    {
        s_Checks = 0;
        Check(PmcUnknownAttack.NeedsEvasion(false, false, 10, 10), "a fresh unseen hit starts evasion");
        Check(PmcUnknownAttack.NeedsEvasion(false, false, 10, 12.99), "the short danger interval persists");
        Check(!PmcUnknownAttack.NeedsEvasion(false, false, 10, 13), "unknown danger eventually expires");
        Check(!PmcUnknownAttack.NeedsEvasion(false, true, 10, 11), "a known contact uses existing combat tactics");
        Check(!PmcUnknownAttack.NeedsEvasion(true, false, 10, 11), "bounty mode is unaffected");
        Check(!PmcUnknownAttack.NeedsEvasion(false, false, 10, 9), "future damage is not treated as an observation");
        Check(!PmcUnknownAttack.NeedsEvasion(false, false, double.NaN, 10) &&
            !PmcUnknownAttack.NeedsEvasion(false, false, 10, double.PositiveInfinity), "invalid clocks do not create danger");
        var towardExit = PmcUnknownAttack.Direction(new float3(1, 4, 2), new float3(31, 100, 42), 180);
        Check(math.distance(towardExit, new float3(.6f, 0, .8f)) < .001f,
            "unseen danger chooses the known exit direction without inventing a shooter");
        var backward = PmcUnknownAttack.Direction(float3.zero, float3.zero, 0);
        Check(math.distance(backward, new float3(0, 0, -1)) < .001f, "at the exit, use a bounded backward direction");
        backward = PmcUnknownAttack.Direction(float3.zero, float3.zero, 90);
        Check(math.distance(backward, new float3(-1, 0, 0)) < .001f, "fallback follows the actor's facing");
        var safe = PmcUnknownAttack.Direction(new float3(float.NaN, 0, 0), float3.zero, float.NaN);
        Check(math.all(math.isfinite(safe)) && math.abs(math.length(safe) - 1) < .001f,
            "invalid scene positions still yield a finite fallback direction");
        safe = PmcUnknownAttack.Direction(new float3(float.MaxValue, 0, 0), new float3(-float.MaxValue, 0, 0), 0);
        Check(math.all(math.isfinite(safe)) && math.abs(math.length(safe) - 1) < .001f,
            "overflowing coordinates cannot enter navigation as NaN");
        return "PASS " + s_Checks + " PMC unseen attack checks";
    }
}
