using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class PmcSprintChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static bool Near(float actual, float expected) => Math.Abs(actual - expected) < .001f;

    [MenuItem("Moonkov/AI/Check Sprint Rules")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        var stamina = new TacticalSprintState(); float3 previous = default; bool known = false;
        void Tick(float3 position, float dt = 1, bool alive = true, bool sprinting = true,
            float speed = 5, float weight = 0, bool stimulated = false) =>
            PmcSprintRules.Tick(ref stamina, ref previous, ref known, position, dt, alive, sprinting, speed, weight, stimulated);

        Tick(float3.zero);
        Check(known && Near(stamina.Value, 100), "the first position initializes a full reserve");
        Tick(float3.zero);
        Check(Near(stamina.Value, 100), "sprinting against an obstacle does not drain reserve");
        Tick(new float3(0, 3, 0));
        Check(Near(stamina.Value, 100), "vertical movement alone does not count as running");
        Tick(new float3(1, 3, 0));
        Check(Near(stamina.Value, 98), "actual sprinting uses the player's light-load drain");
        Tick(new float3(2, 3, 0), weight: 45);
        Check(Near(stamina.Value, 94.5f), "carried weight increases the real sprint drain");
        Tick(new float3(100, 3, 0), dt: .1f);
        Check(Near(stamina.Value, 94.5f), "a teleport is not treated as a sprint");
        Tick(new float3(float.NaN, 0, 0), dt: .1f, sprinting: true);
        Check(!known && Near(stamina.Value, 94.5f), "an invalid position clears the movement baseline");
        Tick(new float3(3, 3, 0), dt: .1f);
        Check(known && Near(stamina.Value, 94.5f), "the first position after invalid data does not drain reserve");
        Tick(new float3(4, 3, 0), dt: 0);
        Check(!known && Near(stamina.Value, 94.5f), "an invalid timestep resets movement observation");

        TacticalSprintRules.Reset(ref stamina); known = false; previous = default;
        Tick(float3.zero, weight: 45);
        for (int i = 1; i <= 30; i++) Tick(new float3(i, 0, 0), weight: 45);
        Check(stamina.Exhausted && Near(stamina.Value, 0), "continuous heavy sprint reaches exhaustion");
        Check(!PmcSprintRules.Allows(false, PmcAction.Extract, true) &&
            !PmcSprintRules.Allows(false, PmcAction.Retreat, true), "an exhausted raid actor walks to recover");
        Check(PmcSprintRules.Allows(false, PmcAction.EvadeGrenade, true), "an exhausted actor may still flee an explosive");
        Check(PmcSprintRules.Allows(true, PmcAction.Extract, true), "bounty movement remains unchanged");
        Tick(new float3(30, 0, 0), sprinting: false, weight: 45);
        Check(Near(stamina.Value, 0) && stamina.Exhausted, "recovery respects the rest delay");
        Tick(new float3(30, 0, 0), sprinting: false, weight: 45);
        Check(Near(stamina.Value, 6.5f) && stamina.Exhausted, "heavy load slows partial recovery");
        Tick(new float3(30, 0, 0), sprinting: false, weight: 45);
        Tick(new float3(30, 0, 0), sprinting: false, weight: 45);
        Check(!stamina.Exhausted && Near(stamina.Value, 32.5f), "the actor may sprint again after recovery reaches the threshold");
        Tick(new float3(31, 0, 0), weight: 45, stimulated: true);
        Check(Near(stamina.Value, 100) && !stamina.Exhausted, "the existing stimulant rule is preserved");
        Tick(new float3(31, 0, 0), alive: false);
        Check(!known && Near(stamina.Value, 100), "a dead actor does not retain a moving baseline");
        return "PASS " + s_Checks + " PMC sprint checks";
    }
}
