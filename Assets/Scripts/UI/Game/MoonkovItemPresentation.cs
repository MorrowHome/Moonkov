using System.Collections.Generic;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    internal static class MoonkovItemPresentation
    {
        public static string Effect(string code)
        {
            float restore = ConsumableCatalog.MedicalRestore(code);
            if (restore > 0) return MoonkovLocalization.Format($"RESTORE UP TO {restore:0} HP");
            var supply = ConsumableCatalog.Get(code);
            if (supply == null) return "";
            var effects = new List<string>(4);
            if (supply.Health > 0) effects.Add(MoonkovLocalization.Format($"RESTORE UP TO {supply.Health:0} HP"));
            if (supply.SurgeSeconds > 0) effects.Add(MoonkovLocalization.Text("DAMAGE +30% / 60s"));
            if(supply.EffectSeconds>0)effects.Add(MoonkovLocalization.Text(AlienBiology.Activity(supply.Effect)));
            if (supply.Energy != 0) effects.Add(MoonkovLocalization.Format($"{supply.Energy:+0;-0;0} ENERGY"));
            if (supply.Hydration != 0) effects.Add(MoonkovLocalization.Format($"{supply.Hydration:+0;-0;0} HYDRATION"));
            if (supply.Satiety != 0) effects.Add(MoonkovLocalization.Format($"{supply.Satiety:+0;-0;0} FULLNESS"));
            if (supply.Oxygen != 0) effects.Add(MoonkovLocalization.Format($"{supply.Oxygen:+0;-0;0} OXYGEN"));
            return string.Join(" / ", effects);
        }

        public static string UseAction(string code)
        {
            float restore = ConsumableCatalog.MedicalRestore(code);
            if (restore > 0) return MoonkovLocalization.Format($"USE / RESTORE UP TO {restore:0} HP");
            var supply = ConsumableCatalog.Get(code);
            if (supply == null) return "";
            string action = supply.Oxygen > 0 ? "REFILL SUIT" : supply.Food ? "EAT" : "DRINK";
            return MoonkovLocalization.Text(action) + " / " + Effect(code);
        }

        public static string Details(string code)
        {
            var definition = InventoryCatalog.Get(code);
            if (definition == null) return "";
            string detail = MoonkovLocalization.Format($"{definition.Width}×{definition.Height} FOOTPRINT / {definition.Weight:0.##} KG EACH / STACK {definition.MaxStack}");
            if (definition.Container)
            {
                int capacity = 0;
                var compartments = new List<string>(definition.Regions.Length);
                foreach (var region in definition.Regions)
                {
                    capacity += region.Width * region.Height;
                    compartments.Add($"{region.Width}×{region.Height}");
                }
                detail += " · " + MoonkovLocalization.Format($"{capacity} STORAGE CELLS / COMPARTMENTS {string.Join(" + ", compartments)}");
            }
            string effect = Effect(code);
            if (!string.IsNullOrEmpty(effect)) detail += " · " + effect;
            if (definition.Kind == ItemKind.Material && ShopCatalog.Find(code)?.SellDust > 0)
                detail += " · " + MoonkovLocalization.Text("RECOVER AND SELL TO THE SUPPLIER");
            string description = Description(code);
            if (!string.IsNullOrEmpty(description)) detail += " · " + description;
            return detail;
        }

        private static string Description(string code)
        {
            var organ=AlienBiology.Organ(code);if(organ!=null)return MoonkovLocalization.Text(organ.Description);
            string description;
            switch (code)
            {
                case "alien_raw_meat": description = "Dissect alien remains to harvest. Restores living tissue and energy, but causes severe thirst. Cannot revive destroyed body parts."; break;
                case "alien_tissue": description = "Alien tissue harvested by dissection. Keep it as a biological specimen."; break;
                case "alien_core": description = "A boss nucleus preserved as a rare biological specimen."; break;
                case "alien_neural_core": description = "Extract and store for permanent halo research, or eat now for +30% halo damage for 60s. Causes severe thirst. Reusing refreshes duration; does not stack."; break;
                case "med_gel": description = "Compact first aid for a selected body part. Keep it in pockets or an equipped rig."; break;
                case "trauma_kit": description = "High-capacity single-use treatment for a selected body part. Use from an equipped rig."; break;
                case "energy_gel": description = "A light snack with low stomach occupancy. Consumes a little hydration."; break;
                case "protein_bar": description = "Dense field food. Plan drinking water alongside its energy recovery."; break;
                case "electrolyte": description = "Compact drinking supply with low stomach occupancy."; break;
                case "oxygen_reserve": description = "A large suit-air refill. Use from an equipped rig; excess oxygen is lost."; break;
                case "scout_rig": description = "Light chest storage with three narrow compartments. Keeps medical supplies and cells accessible."; break;
                case "field_rig": description = "Roomy chest storage with three broad compartments. Carries larger medical and oxygen supplies within reach."; break;
                case "sling_pack": description = "A light backpack for short scavenging trips. Move medicine, oxygen and cells into the rig before use."; break;
                case "cargo_pack": description = "A heavy pack with a large continuous grid for bulky loot. Total carried weight still applies."; break;
                case "circuit_board": description = "Recovered control electronics. Trade goods for the lunar supplier."; break;
                case "copper_coil": description = "A dense industrial induction coil. Its sale value comes with extra carried weight."; break;
                case "ceramic_plate": description = "Industrial ceramic stock. A trade good, not wearable armor."; break;
                case "optical_sensor": description = "An intact precision sensor. Compact trade goods recovered from lunar equipment."; break;
                case "data_drive": description = "An encrypted survey archive. Extract it and sell it for moon dust."; break;
                case "helium3_sample": description = "A sealed helium-3 research sample. Valuable trade goods; does not refill suit oxygen."; break;
                case "quantum_core": description = "A rare quantum processor. High-value cargo to bring back from a raid."; break;
                case "black_box": description = "A recovered survey recorder. Bulky, valuable cargo worth reserving pack space for."; break;
                default: return "";
            }
            return MoonkovLocalization.Text(description);
        }
    }
}
