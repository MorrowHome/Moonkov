// 补给和医疗物品共用的数值表。食饮、补氧、治疗与止血读这里的适用条件、效果和耗时，让界面展示与写入角色的数值一致。
#nullable disable
namespace Unity.MP_FPS.Inventory {
    // Shared by the game and persistence service. Values belong to the catalog,
    // never to the use RPC; old item kinds and inventory saves keep their IDs.
    public sealed class ConsumableDefinition {
        public readonly float Energy, Hydration, Satiety, Oxygen, Health, SurgeSeconds;
        public readonly AlienFoodEffect Effect;
        public readonly float EffectSeconds;
        public bool Food => Energy > 0;

        public ConsumableDefinition(
            float energy,
            float hydration,
            float satiety,
            float oxygen = 0,
            float health = 0,
            float surgeSeconds = 0,
            AlienFoodEffect effect = AlienFoodEffect.None,
            float effectSeconds = 0) {
            Energy = energy;
            Hydration = hydration;
            Satiety = satiety;
            Oxygen = oxygen;
            Health = health;
            SurgeSeconds = surgeSeconds;
            Effect = effect;
            EffectSeconds = effectSeconds;
        }
    }

    public static class ConsumableCatalog {
        private static readonly ConsumableDefinition Ration = new(45, -5, 35);
        private static readonly ConsumableDefinition Water = new(0, 50, 10);
        private static readonly ConsumableDefinition OxygenCanister = new(0, 0, 0, 40);
        private static readonly ConsumableDefinition EnergyGel = new(25, -2, 10);
        private static readonly ConsumableDefinition ProteinBar = new(35, -8, 20);
        private static readonly ConsumableDefinition Electrolyte = new(0, 35, 5);
        private static readonly ConsumableDefinition OxygenReserve = new(0, 0, 0, 80);
        private static readonly ConsumableDefinition HydrationGel = new(10, 25, 10);
        private static readonly ConsumableDefinition CompressedRation = new(75, -15, 50);
        private static readonly ConsumableDefinition EmergencyOxygen = new(0, 0, 0, 20);
        private static readonly ConsumableDefinition AlienMeat = new(35, -20, 30, health: 20);
        private static readonly ConsumableDefinition NeuralOrgan = new(10, -25, 15, surgeSeconds: 60);

        public static ConsumableDefinition Get(string code) {
            switch (code) {
                case "ration": return Ration;
                case "water": return Water;
                case "oxygen_canister": return OxygenCanister;
                case "energy_gel": return EnergyGel;
                case "protein_bar": return ProteinBar;
                case "electrolyte": return Electrolyte;
                case "oxygen_reserve": return OxygenReserve;
                case "hydration_gel": return HydrationGel;
                case "compressed_ration": return CompressedRation;
                case "emergency_oxygen": return EmergencyOxygen;
                case "alien_raw_meat": return AlienMeat;
                case "alien_neural_core": return NeuralOrgan;
                default: return AlienBiology.Organ(code)?.Food;
            }
        }

        public static float MedicalRestore(string code) {
            return code == "medkit" ? 40 :
                code == "med_gel" ? 20 :
                code == "trauma_kit" ? 70 :
                code == "hemostatic_bandage" ? 15 :
                code == "rescue_kit" ? 100 : 0;
        }

        public static bool StopsBleeding(string code) { return code == "hemostatic_bandage"; }

        public static float MedicalSeconds(string code, float standard = 3) {
            return code == "hemostatic_bandage" ? 1.5f : code == "rescue_kit" ? 5 : standard;
        }

        public static float UseSeconds(string code) {
            if (code == "emergency_oxygen") return 1;
            if (code == "hydration_gel") return 1.5f;
            if (code == "compressed_ration") return 5;
            var supply = Get(code);
            return supply?.Oxygen > 0 ? 2 : supply?.Food == true ? 3 : 2;
        }
    }
}