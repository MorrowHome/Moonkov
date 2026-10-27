using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class RaidInteractionInputChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }

    [MenuItem("Moonkov/Checks/Raid Interaction Input")]
    public static void RunFromEditor() => Debug.Log(Run());

    public static string Run()
    {
        s_Checks = 0;
        bool Tactical(bool keyboard = false, bool pad = false, bool mission = false, bool control = false, bool target = false) =>
            RaidInteractionInput.TacticalPressed(keyboard, pad, mission, control, target);
        Check(!Tactical(pad: true, mission: true), "one controller press cannot start both a mission and self-treatment");
        Check(!Tactical(pad: true, mission: true, target: true), "the focused mission owns a controller press even near a tactical item");
        Check(!Tactical(pad: true, control: true), "a focused airlock owns the controller press while the player is bleeding");
        Check(Tactical(pad: true, control: true, target: true), "a directly targeted trap or supply keeps its existing priority");
        Check(Tactical(pad: true), "self-treatment remains available when no other control is focused");
        Check(!Tactical(), "no input sends no tactical interaction");
        Check(Tactical(keyboard: true, mission: true) && Tactical(keyboard: true, control: true),
            "keyboard E remains independent of mission and airlock F");
        Check(!Tactical(keyboard: true, pad: true, mission: true), "a shared mission press cannot also send tactical input");
        return "PASS " + s_Checks + " raid interaction input checks";
    }
}
