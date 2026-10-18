using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // Shared inventory rules have no Unity dependency and also run in persistence checks.
    public static class EnergyGrenadeRules
    {
        public const string Code = "energy_grenade";
        public const float Fuse = 3f, Radius = 6f, InnerRadius = 1.5f, Damage = 480f;
        public const float Speed = 14f, Gravity = 7.5f, BodyRadius = .13f, Cooldown = .8f;

        public static bool Accessible(InventoryGraph graph, InventoryItem item) =>
            graph != null && item != null && item.Code == Code && item.Quantity > 0 &&
            (item.Parent == InventoryCatalog.Pockets || item.Parent == graph.Equipped("ChestRig")?.Id);

        public static InventoryError Take(InventoryGraph graph, string id, int version)
        {
            if (graph == null) return InventoryError.Invalid;
            if (graph.Version != version) return InventoryError.Stale;
            var item = graph.Find(id);
            if (item == null) return InventoryError.Missing;
            if (!Accessible(graph, item)) return InventoryError.Inaccessible;
            if (--item.Quantity == 0) graph.Items.Remove(item);
            graph.Version++;
            return InventoryError.None;
        }

        public static float DamageAt(float distance)
        {
            if (!float.IsFinite(distance) || distance < 0 || distance >= Radius) return 0;
            return Damage * Math.Min(1f, (Radius - distance) / (Radius - InnerRadius));
        }
    }
}
