using System;

namespace Unity.MP_FPS
{
    // Engine-independent identifiers live in the visual assembly so that both
    // DollSinger.Runtime and the default gameplay assembly can consume them.
    // The backend compiles this same source without a Unity dependency.
    public enum AlienFoodEffect : byte
    { None, SilkFocus, AdrenalRush, ContractilePower, VenomPower, Regeneration, GelArmor, OxygenReserve, NeuralOverdrive }
    [Flags] public enum HaloModule : ushort
    {
        None = 0, Capacitor = 1, ChargeBridge = 2, FocusLens = 4,
        FanArray = 8, PulseController = 16, HitMarker = 32,
        BiologicalResearch = 64, AlienSurge = 128,
        BiologicalCapacity = 256, BiologicalCharging = 512, BiologicalStability = 1024,
        MooncoreRecycler = 2048, FoodEffectMask = 61440
    }
}
