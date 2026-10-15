using System;

namespace Unity.MP_FPS
{
    // Engine-independent identifiers live in the visual assembly so that both
    // DollSinger.Runtime and the default gameplay assembly can consume them.
    // The backend compiles this same source without a Unity dependency.
    [Flags] public enum HaloModule : byte
    {
        None = 0, Capacitor = 1, ChargeBridge = 2, FocusLens = 4,
        FanArray = 8, PulseController = 16, HitMarker = 32
    }
}
