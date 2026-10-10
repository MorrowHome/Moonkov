#nullable disable
using Unity.MP_FPS.Survival;

namespace Unity.MP_FPS.Inventory
{
    // Pure preparation: only a future server adapter may commit both returned states together.
    // Existing scalar InventoryGraph.UseMedical and all of its callers remain unchanged.
    public static class TargetedMedicalRules
    {
        public const double InjectorHealing = 40;

        public static InventoryError Apply(InventoryGraph inventory, string itemId, int expectedVersion,
            RegionalHealthState health, BodyRegion region, RegionalHealthConfig config,
            out InventoryGraph updatedInventory, out RegionalHealthState healed)
        {
            updatedInventory = null;
            healed = health;
            if (inventory == null || config == null || (int)region < 0 || (int)region > 6 ||
                inventory.Validate() != InventoryError.None) return InventoryError.Invalid;
            if (expectedVersion != inventory.Version) return InventoryError.Stale;
            if (inventory.Version == int.MaxValue) return InventoryError.Invalid;
            var item = inventory.Find(itemId);
            if (item == null) return InventoryError.Missing;
            if (!inventory.MedicalAccessible(item)) return InventoryError.Inaccessible;
            if (health.IsDead) return InventoryError.Invalid;
            // A state/config mismatch must not turn a heal into damage or create extra capacity.
            for (int i = 0; i < 7; i++)
            {
                double value = health.Current[(BodyRegion)i];
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > config.Maximum[(BodyRegion)i])
                    return InventoryError.Invalid;
            }
            // Unlike scalar UseMedical, zero in a nonvital target is treatable while the actor lives.
            var candidate = RegionalHealthRules.Heal(health, region, InjectorHealing, config);
            if (candidate.Current[region] <= health.Current[region]) return InventoryError.Invalid;

            var copy = inventory.Clone();
            var consumed = copy.Find(itemId);
            if (--consumed.Quantity == 0) copy.Items.Remove(consumed);
            copy.Version++;
            // Publish only a complete valid pair; inputs are untouched even on success.
            if (copy.Validate() != InventoryError.None) return InventoryError.Invalid;
            updatedInventory = copy;
            healed = candidate;
            return InventoryError.None;
        }
    }
}
