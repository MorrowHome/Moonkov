using System;
using Unity.MP_FPS;
using UnityEngine;

public static class AlienGunshotHearingChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    public static string Run()
    {
        s_Checks = 0;
        Check(RaidFootsteps.Audible(12, 16, false), "open gunfire within range is audible");
        Check(!RaidFootsteps.Audible(12, 16, true), "a wall reduces gunshot hearing range");
        Check(RaidFootsteps.Audible(6, 16, true), "nearby gunfire can still be heard through a wall");
        Check(!RaidFootsteps.Audible(17, 16, false), "gunfire outside the hearing range is ignored");
        Check(!RaidFootsteps.Audible(0, 0, false), "zero hearing range disables sound even at zero distance");
        Check(!RaidFootsteps.Audible(-1, 16, false) && !RaidFootsteps.Audible(float.NaN, 16, false) &&
            !RaidFootsteps.Audible(1, float.PositiveInfinity, false), "invalid hearing inputs do not create clues");
        Vector3 source = new Vector3(3, 2, 8);
        var first = RaidAlienHearing.Estimate(source, 8, 16, false, 41, 12);
        var repeated = RaidAlienHearing.Estimate(source, 8, 16, false, 41, 12);
        Check(first == repeated, "repeated shots from one position retain a stable bearing");
        Check(Vector3.Distance(first, source) > 1 && Mathf.Approximately(first.y, source.y),
            "an unseen sound is uncertain and stays on its observed level");
        var blocked = RaidAlienHearing.Estimate(source, 8, 16, true, 41, 12);
        Check(Vector3.Distance(blocked, source) > Vector3.Distance(first, source) + 1.9f,
            "a blocked sound has greater uncertainty");
        Check(Vector3.Dot((first - source).normalized, (blocked - source).normalized) > .999f,
            "the bearing stays stable as obstruction changes");
        var moved = RaidAlienHearing.Estimate(source + Vector3.right * 3, 8, 16, false, 41, 12);
        Check(Vector3.Distance(moved - first, Vector3.right * 3) < .001f,
            "a later sound follows the new observation rather than the hidden live transform");
        var other = RaidAlienHearing.Estimate(source, 8, 16, false, 41, 13);
        Check(Vector3.Distance(other, first) > .1f, "different sources do not share an identical bearing");
        var nearby = RaidAlienHearing.Estimate(source, 1, 16, false, 41, 12);
        var far = RaidAlienHearing.Estimate(source, 15, 16, false, 41, 12);
        Check(Vector3.Distance(nearby, source) < Vector3.Distance(far, source), "distance increases uncertainty");
        return "PASS " + s_Checks + " alien gunshot hearing checks";
    }
}
