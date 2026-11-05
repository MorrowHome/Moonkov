using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum GrenadePhase : byte { None, Drawing, Safe, Priming, Live, Throwing, Recovering, Stowing }
    public enum GrenadeCommand : byte { Equip, Prime, Release, Stow, Drop }

    // One timeline for authoritative release and the baked hand animation.
    public static class GrenadeHandling
    {
        public const float DrawSeconds = .35f, PrimeSeconds = .55f, ReleaseSeconds = .32f;
        public const float RecoverySeconds = .42f, StowSeconds = .3f, LowSpeed = 6f;
        public static bool Occupied(in PredictedPlayerGhost state) => state.GrenadePhase != 0;
        public static bool Live(GrenadePhase phase) => phase == GrenadePhase.Priming || phase == GrenadePhase.Live || phase == GrenadePhase.Throwing;
        public static bool Throwable(string code) => code == EnergyGrenadeRules.Code || TacticalItemRules.Find(code)?.Throwable == true;
        public static bool Accessible(InventoryGraph graph, InventoryItem item) => item != null && Throwable(item.Code) &&
            (item.Code == EnergyGrenadeRules.Code ? EnergyGrenadeRules.Accessible(graph, item) : TacticalItemRules.Accessible(graph, item));
        public static float Fuse(string code) => code == EnergyGrenadeRules.Code ? EnergyGrenadeRules.Fuse : TacticalItemRules.Find(code)?.Fuse ?? 0;
        public static float Duration(GrenadePhase phase) => phase switch {
            GrenadePhase.Drawing => DrawSeconds, GrenadePhase.Priming => PrimeSeconds,
            GrenadePhase.Throwing => ReleaseSeconds, GrenadePhase.Recovering => RecoverySeconds,
            GrenadePhase.Stowing => StowSeconds, _ => 0
        };
        public static InventoryError Take(InventoryGraph graph, string itemId, int version) => graph?.Find(itemId)?.Code == EnergyGrenadeRules.Code
            ? EnergyGrenadeRules.Take(graph, itemId, version) : TacticalItemRules.Take(graph, itemId, version);
    }
}
