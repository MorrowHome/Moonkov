using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class RaidCoughSoundChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/AI/Check Cough Hearing Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        double next = 0;
        Check(!RaidCoughSound.TryEmit(ref next, 0, 0, false), "no exposure produces no sound");
        Check(RaidCoughSound.TryEmit(ref next, 0, 1, false) && next == RaidCoughSound.Interval,
            "exposure emits a single immediate sound");
        Check(!RaidCoughSound.TryEmit(ref next, 3.19, 1, false) &&
            RaidCoughSound.TryEmit(ref next, 3.2, 1, false), "continuing coughs follow the bounded audio cadence");
        Check(!RaidCoughSound.TryEmit(ref next, 3.5, 0, false) &&
            !RaidCoughSound.TryEmit(ref next, 4, 1, false), "briefly leaving and re-entering cannot spam coughs");
        Check(RaidCoughSound.TryEmit(ref next, 6.4, 1, false), "a new cough can occur after the interval");
        Check(!RaidCoughSound.TryEmit(ref next, 10, 1, true), "bounty mode retains its existing hearing");
        Check(!RaidCoughSound.TryEmit(ref next, double.NaN, 1, false) &&
            !RaidCoughSound.TryEmit(ref next, 10, float.NaN, false) &&
            !RaidCoughSound.TryEmit(ref next, 10, float.PositiveInfinity, false), "invalid status and clocks cannot create sounds");
        Check(RaidFootsteps.Audible(25, RaidCoughSound.Range, false) &&
            !RaidFootsteps.Audible(25.01f, RaidCoughSound.Range, false) &&
            RaidFootsteps.Audible(13, RaidCoughSound.Range, true) &&
            !RaidFootsteps.Audible(14, RaidCoughSound.Range, true), "range and wall attenuation match other local sounds");
        return "PASS " + s_Checks + " cough hearing checks";
    }
}
