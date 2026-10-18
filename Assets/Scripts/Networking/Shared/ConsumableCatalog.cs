#nullable disable
namespace Unity.MP_FPS.Inventory
{
    // Shared by the game and persistence service. Values belong to the catalog,
    // never to the use RPC; old item kinds and inventory saves keep their IDs.
    public sealed class ConsumableDefinition
    {
        public readonly float Energy, Hydration, Satiety, Oxygen, Health, SurgeSeconds;
        public bool Food => Energy > 0;
        public ConsumableDefinition(float energy, float hydration, float satiety, float oxygen = 0, float health = 0, float surgeSeconds = 0)
        { Energy = energy; Hydration = hydration; Satiety = satiety; Oxygen = oxygen; Health = health; SurgeSeconds = surgeSeconds; }
    }
    public static class ConsumableCatalog
    {
        private static readonly ConsumableDefinition Ration = new ConsumableDefinition(45, -5, 35);
        private static readonly ConsumableDefinition Water = new ConsumableDefinition(0, 50, 10);
        private static readonly ConsumableDefinition OxygenCanister = new ConsumableDefinition(0, 0, 0, 40);
        private static readonly ConsumableDefinition EnergyGel = new ConsumableDefinition(25, -2, 10);
        private static readonly ConsumableDefinition ProteinBar = new ConsumableDefinition(35, -8, 20);
        private static readonly ConsumableDefinition Electrolyte = new ConsumableDefinition(0, 35, 5);
        private static readonly ConsumableDefinition OxygenReserve = new ConsumableDefinition(0, 0, 0, 80);
        private static readonly ConsumableDefinition AlienMeat = new ConsumableDefinition(35, -20, 30, health: 20);
        private static readonly ConsumableDefinition NeuralOrgan = new ConsumableDefinition(10, -25, 15, surgeSeconds: 60);

        public static ConsumableDefinition Get(string code)
        {
            switch (code)
            {
                case "ration": return Ration;
                case "water": return Water;
                case "oxygen_canister": return OxygenCanister;
                case "energy_gel": return EnergyGel;
                case "protein_bar": return ProteinBar;
                case "electrolyte": return Electrolyte;
                case "oxygen_reserve": return OxygenReserve;
                case "alien_raw_meat": return AlienMeat;
                case "alien_neural_core": return NeuralOrgan;
                default: return null;
            }
        }
        public static float MedicalRestore(string code) => code == "medkit" ? 40 :
            code == "med_gel" ? 20 : code == "trauma_kit" ? 70 : 0;
    }
}
