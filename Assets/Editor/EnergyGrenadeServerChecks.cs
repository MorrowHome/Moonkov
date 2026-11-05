using UnityEditor;
using UnityEngine;

// Preserve the existing validation menu while exercising the complete held-grenade protocol.
public static class EnergyGrenadeServerChecks
{
    [MenuItem("Moonkov/Energy Grenade/Run Authority Checks")]
    public static void Run() => Debug.Log(GrenadeHandlingChecks.Run());
}
