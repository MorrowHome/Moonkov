using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    /// <summary>Atomic server-side inventory and suit refill transaction.</summary>
    public static class RaidOxygenConsumables
    {
        public const string CanisterCode = "oxygen_canister";
        public const float UseSeconds = 2;

        public static bool CanConsume(in PredictedPlayerGhost state, ConsumableDefinition definition) =>
            definition != null && float.IsFinite(definition.Oxygen) && definition.Oxygen > 0 && RaidOxygen.CanRefill(state);

        public static InventoryError Consume(ref PredictedPlayerGhost state, InventoryGraph graph, string id, int version)
        {
            if (graph == null) return InventoryError.Invalid;
            if (version != graph.Version) return InventoryError.Stale;
            var item = graph.Find(id);
            if (item == null) return InventoryError.Missing;
            if (!graph.ConsumableAccessible(item)) return InventoryError.Inaccessible;
            var definition = ConsumableCatalog.Get(item.Code);
            if (!CanConsume(state, definition)) return InventoryError.Invalid;

            // Prepare the state change before consuming. A full/dead/invalid suit,
            // inaccessible stack or stale request leaves both owners untouched.
            var replenished = state;
            if (RaidOxygen.Refill(ref replenished, definition.Oxygen) <= 0) return InventoryError.Invalid;
            var error = graph.ConsumeOne(id, version);
            if (error == InventoryError.None) state = replenished;
            return error;
        }
    }
}
