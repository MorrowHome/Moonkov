#nullable disable
namespace Unity.MP_FPS.Inventory
{
    // Shared by the game and persistence service. Values belong to the catalog,
    // never to the use RPC; old item kinds and inventory saves keep their IDs.
    public sealed class ConsumableDefinition
    {
        public readonly float Energy, Hydration, Satiety;
        public bool Food => Energy > 0;
        public ConsumableDefinition(float energy, float hydration, float satiety)
        { Energy = energy; Hydration = hydration; Satiety = satiety; }
    }
    public static class ConsumableCatalog
    {
        private static readonly ConsumableDefinition Ration = new ConsumableDefinition(45, -5, 35);
        private static readonly ConsumableDefinition Water = new ConsumableDefinition(0, 50, 10);
        public static ConsumableDefinition Get(string code) => code == "ration" ? Ration : code == "water" ? Water : null;
    }
}
