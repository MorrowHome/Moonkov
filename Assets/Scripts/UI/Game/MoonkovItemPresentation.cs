using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    internal static class MoonkovItemPresentation
    {
        private static string CategoryClass(string code)
        {
            var tactical = TacticalItemRules.Find(code);
            if (tactical?.Injectable == true)
                return "item-category-medical";
            return InventoryCatalog.Get(code)?.Kind switch
            {
                ItemKind.LongGun or ItemKind.Pistol or ItemKind.Melee => "item-category-weapon",
                ItemKind.Helmet or ItemKind.Shield or ItemKind.Rig or ItemKind.Backpack or ItemKind.SecureContainer or ItemKind.SpecialEquipment => "item-category-equipment",
                ItemKind.Medical => "item-category-medical",
                ItemKind.Food => "item-category-food",
                ItemKind.Drink => "item-category-drink",
                ItemKind.Oxygen => "item-category-oxygen",
                ItemKind.Cell => "item-category-energy",
                ItemKind.Throwable => "item-category-tactical",
                ItemKind.StorageModule or ItemKind.ShapingModule or ItemKind.UtilityModule => "item-category-module",
                ItemKind.Material => "item-category-material",
                _ => "item-category-other"
            };
        }

        public static void ApplyCategory(VisualElement element, string code)
        {
            string previous = element.GetClasses().FirstOrDefault(c => c.StartsWith("item-category-", StringComparison.Ordinal));
            if (previous != null) element.RemoveFromClassList(previous);
            element.EnableInClassList("item-category", code != null);
            if (code != null) element.AddToClassList(CategoryClass(code));
        }

        public static string Effect(string code)
        {
            if (code == EnergyGrenadeRules.Code) return MoonkovLocalization.Text("3s FUSE / 6m BLAST / UP TO 480 DAMAGE");
            if (MeleeRules.TryDefinition(MeleeInventory.Kind(code), out var melee))
                return MoonkovLocalization.Format($"REACH {melee.Reach:0.##} m / LIGHT {melee.LightDamage:0} / HEAVY {melee.HeavyDamage:0} / STAMINA {melee.LightCost:0}/{melee.HeavyCost:0}");
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

        public static string ItemState(InventoryItem item)
        {
            if (item == null) return "";
            if (item.Code == "energy_shield_generator") return MoonkovLocalization.Text("RETIRED EQUIPMENT");
            if (item.Code == "energy_head_dome" || item.Code == "helmet")
                return item.SuitOxygen < 0 ? MoonkovLocalization.Text("NEW COLLAR / INITIAL OXYGEN ON FIRST DEPLOYMENT")
                    : MoonkovLocalization.Format($"COLLAR O₂ / {item.SuitOxygen:0}%");
            return "";
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

        public static string ModulePurpose(string code)
        {
            switch (code)
            {
                case "halo_capacitor": return "More energy for attack and defense";
                case "halo_charge_bridge": return "Get back to firing sooner";
                case "halo_mooncore_recycler": return "Spend less energy per shot";
                case "halo_focus_lens": return "Keep shotgun pellets together";
                case "halo_fan_array": return "Cover a wider area with pellets";
                case "halo_pulse_controller": return "Fire controlled three-round bursts";
                default: return "Show your team where a hit landed";
            }
        }
        public static string ModuleBenefit(string code)
        {
            switch (code)
            {
                case "halo_capacitor": return "Capacity +50%";
                case "halo_charge_bridge": return "Recovery speed +67%";
                case "halo_mooncore_recycler": return "Energy per round reduced by one third";
                case "halo_focus_lens": return "Pellet spread -55%";
                case "halo_fan_array": return "Wider spread with 12 pellets; total damage unchanged";
                case "halo_pulse_controller": return "Three rounds per trigger press";
                default: return "Last hit position marked for 3 seconds";
            }
        }
        public static string ModuleTradeoff(string code)
        {
            switch (code)
            {
                case "halo_capacitor": return "Recovery speed -26%";
                case "halo_charge_bridge": return "Energy per round +50%";
                case "halo_mooncore_recycler": return "Recovery speed -17%";
                case "halo_focus_lens": return "Energy per round +33% (rounded up)";
                case "halo_fan_array": return "Less damage per pellet; spread +60%";
                case "halo_pulse_controller": return "Press again for the next burst; at least 0.42s between bursts";
                default: return "Energy per round +25%";
            }
        }

        public static string Purpose(string code)
        {
            string purpose;
            switch (code)
            {
                case "rifle": case "compact": case "cicada": case "aegis":
                    purpose = "Energy weapon for sustained fire. Uses shared halo energy, which recovers after firing stops."; break;
                case "pistol":
                    purpose = "Revolver halo with strong individual shots. Uses the same energy pool as your other halos."; break;
                case "shotgun": case "thorn":
                    purpose = "Fires several pellets per shot for close-range combat. Total damage depends on how many pellets hit."; break;
                case "sniper": case "vesper": case "nocturne":
                    purpose = "Precision halo for aimed fire at distant targets. Uses shared halo energy."; break;
                case "trine": case "rondo":
                    purpose = "Fires a short burst with each trigger press. Each round spends shared halo energy."; break;
                case "sundown": case "tempest":
                    purpose = "Launches explosive projectiles that damage targets around the impact. Uses shared halo energy."; break;
                case "medkit": return Description("med_gel");
                case "ration":
                    purpose = "Field food that restores energy and fullness. Keep drinking water nearby."; break;
                case "water": return Description("electrolyte");
                case "oxygen_canister":
                    purpose = "Refills the life-support collar. Use from pockets or an equipped badge; excess oxygen is lost."; break;
                case "secure_case":
                    purpose = "Protects its contents after death or timeout when equipped. Supplies must be moved to pockets or a badge before use. Weapons, worn gear and mission outputs cannot be stored inside."; break;
                case "backpack": case "small_pack": case "sling_pack": case "cargo_pack":
                    purpose = "Carries extra loot and supplies. Move medical items, food and cells into pockets or an equipped badge before using them."; break;
                case "rig":
                    purpose = "A chest badge with subspace storage. Medical supplies, food, grenades and cells inside remain directly accessible."; break;
                case "dust": purpose = "Used to buy supplies and fund halo research at the supplier."; break;
                case "alloy": purpose = "Material for halo research and fabrication. Can also be sold to the supplier."; break;
                default:
                    purpose = Description(code);
                    if (!string.IsNullOrEmpty(purpose)) return purpose;
                    return InventoryCatalog.Get(code)?.Kind == ItemKind.Material
                        ? MoonkovLocalization.Text("RECOVER AND SELL TO THE SUPPLIER") : "";
            }
            return MoonkovLocalization.Text(purpose) + (DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(code))
                ? "\n" + ResearchQuestCopy.HaloDescription() : "");
        }

        private static string Description(string code)
        {
            var organ=AlienBiology.Organ(code);if(organ!=null)return MoonkovLocalization.Text(organ.Description);
            if (InventoryCatalog.IsResearchSupply(code)) return ResearchQuestCopy.ItemDescription(code);
            string description;
            switch (code)
            {
                case "secure_case": description = "装备在中栏保险箱槽后，箱体与内部 2×2 格物品在死亡或超时结算时保留。仅保护仍在箱内的物品；不计成功撤离或任务首次回收。禁止武器、穿戴装备、嵌套容器和任务产物。箱内补给须先移至衣兜或胸章才能使用。"; break;
                case "survey_compass": description = "装入任意特殊装备槽后，HUD 自动显示当前视线的罗盘方位。0° 为世界北向（+Z）。死亡和超时保留；放在普通背包中不生效且会掉落。"; break;
                case "oxygen_recycler": description = "Equip in a special slot to reduce suit oxygen consumption by 25%. Requires a life-support collar. Multiple recyclers do not stack."; break;
                case "glare_filter": description = "Equip in a special slot to reduce aimed-light glare and flash grenade exposure by 60%. Multiple filters do not stack."; break;
                case "survey_multitool": description = "Combines compass bearings and 500-metre laser ranging in one special slot. Measures the first solid surface; cannot see through walls."; break;
                case "hemostatic_bandage": description = "Apply for 1.5 seconds to stop bleeding and restore up to 15 HP to one body part. Use from pockets or an equipped badge."; break;
                case "rescue_kit": description = "Treat one body part for 5 seconds to restore up to 100 HP. Use from an equipped badge. Does not raise the recoverable health limit."; break;
                case "hydration_gel": description = "Consume in 1.5 seconds for 10 energy and 25 hydration. Occupies 10 fullness."; break;
                case "compressed_ration": description = "Eat for 5 seconds for 75 energy and 50 fullness. Consumes 15 hydration."; break;
                case "emergency_oxygen": description = "Refill 20 oxygen in 1 second. Use from pockets or an equipped badge with a life-support collar. Excess oxygen is lost."; break;
                case "flash_grenade": description = "2-second fuse. Dazzles exposed targets within 12 metres for up to 4 seconds. Looking away, cover and smoke reduce or block exposure. Can dazzle the thrower."; break;
                case "poison_bomb": description = "3-second fuse. Creates a 3-metre poison cloud for 15 seconds. Poison deals damage and prevents healing. Antidote protects; choke clouds disperse it."; break;
                case "team_tracker": description = "Equip in a special slot to see teammates outlined in teal, even through walls. Only the wearer sees the effect. No energy cost. Inactive in a backpack; retained on death or timeout while equipped."; break;
                case "laser_rangefinder": description = "装入任意特殊装备槽后，HUD 自动显示视线首个实体表面的距离，上限 500m；无命中显示超出量程，不透墙。死亡和超时保留；普通背包中不生效且会掉落。"; break;
                case "energy_shield_generator": description = "Legacy generator. Defense now uses the halo and its shared energy. Can be stored or sold."; break;
                case "energy_head_dome": description = "A neck-fitted life-support collar with a lilac bow and a gold cat bell. Follows the neck bones and uses the existing oxygen reserve, canisters and pressurized air. Does not provide ballistic protection or combat shielding."; break;
                case "helmet": description = "A plain neck-fitted life-support collar. Projects a spherical helmet without cat ears and uses the existing oxygen reserve, canisters and pressurized air."; break;
                case "energy_sword": case "dagger": description = "Equip in the melee slot and bind to a number key. Left click: light attack. Right click: heavy attack. Attacks use stamina; walls block hits. With no active weapon, fists are the fallback."; break;
                case "cells": description = "Recharges up to 100 halo energy after a charging action. Keep batteries in pockets or an equipped badge. Unused charge is retained."; break;
                case "vitality_shot": description = "Inject for 3 seconds to fill surviving body parts to their current recoverable health limit. Does not revive destroyed parts or restore health lost to Bounty Clash downs."; break;
                case "regeneration_shot": description = "Inject for 3 seconds to restore 2 total HP per second for 120 seconds. Prioritizes injured surviving parts. Poison pauses healing; destroyed parts and lost health limits stay unchanged. Reuse refreshes duration without stacking."; break;
                case "stamina_shot": description = "Inject for 3 seconds to prevent sprint fatigue for 420 seconds. Does not refill hunger, thirst or oxygen, or heal injured legs."; break;
                case "antidote_shot": description = "Inject for 3 seconds to clear poison and gain 600 seconds of immunity to tactical poison clouds. Does not prevent other damage."; break;
                case "dynamite_bundle": description = "Select, then left click to throw after a 1-second action. Explodes after a 4-second fuse: up to 650 damage within 8 metres. Cover blocks the blast. Clears concertina wire; can kill teammates and the thrower."; break;
                case "frag_bomb": description = "Select, then left click to throw after a 1-second action. A 4-second fuse triggers up to 320 damage within 8 metres and 8 seconds of bleeding. Cover blocks the blast. Does not clear concertina wire."; break;
                case "fire_bomb": description = "Throw to create a 3-metre fire zone for 30 seconds. Deals 8 damage per second while exposed. Fire affects enemies, teammates and the thrower."; break;
                case "smoke_grenade": description = "3-second fuse. Creates a 5-metre smoke screen for 20 seconds, blocking vision but not sound or projectiles."; break;
                case "choke_bomb": description = "Throw to create a 4-metre choke cloud for 20 seconds. Extinguishes fire, clears tactical poison and causes coughing."; break;
                case "alert_trip_mine": description = "Place on nearby clear ground with a 3-second action. The trip line alerts when any player crosses, including teammates and the owner. Aim at it and hold the interaction to disarm."; break;
                case "concertina_trip_mine": description = "Place on nearby clear ground with a 3-second action. Any player crossing the line releases concertina wire, causing damage, bleeding and slowed movement. Dynamite can clear wire."; break;
                case "poison_trip_mine": description = "Place on nearby clear ground with a 3-second action. Any player crossing the line releases poison. Choke clouds clear tactical poison; antidote protects against it."; break;
                case "ammo_box": description = "Place with a 3-second action. Stores 400 energy; each 2-second resupply transfers up to 100, subject to inventory space. Expires after 120 seconds. Does not grant a free weapon reload."; break;
                case "energy_grenade": description = "[G] throws from pockets or an equipped badge. Bounces, then releases an energy blast after 3 seconds. Cover blocks the blast; nearby damage can kill the thrower. Move grenades out of backpacks before throwing."; break;
                case "alien_raw_meat": description = "Dissect alien remains to harvest. Restores living tissue and energy, but causes severe thirst. Cannot revive destroyed body parts."; break;
                case "alien_tissue": description = "Alien tissue harvested by dissection. Keep it as a biological specimen."; break;
                case "alien_core": description = "A boss nucleus preserved as a rare biological specimen."; break;
                case "alien_neural_core": description = "Extract and store for permanent halo research, or eat now for +30% halo damage for 60s. Causes severe thirst. Reusing refreshes duration; does not stack."; break;
                case "med_gel": description = "Compact first aid for a selected body part. Keep it in pockets or an equipped badge."; break;
                case "trauma_kit": description = "High-capacity single-use treatment for a selected body part. Use from an equipped badge."; break;
                case "energy_gel": description = "A light snack with low stomach occupancy. Consumes a little hydration."; break;
                case "protein_bar": description = "Dense field food. Plan drinking water alongside its energy recovery."; break;
                case "electrolyte": description = "Compact drinking supply with low stomach occupancy."; break;
                case "oxygen_reserve": description = "A large suit-air refill. Use from an equipped badge; excess oxygen is lost."; break;
                case "scout_rig": description = "A small enamel chest badge with three subspace compartments. Keeps medical supplies and cells accessible."; break;
                case "field_rig": description = "A large enamel chest badge with three broad subspace compartments. Keeps larger medical and oxygen supplies within reach."; break;
                case "small_pack": description = "A back-mounted scout subspace anchor with one ring, paired stabilizer fins and a 4x4 storage grid. Carried weight still applies. Move supplies to pockets or a badge before use."; break;
                case "backpack": description = "A back-mounted subspace anchor with two capacity rings and a 5x6 storage grid. Carried weight still applies. Move supplies to pockets or a badge before use."; break;
                case "sling_pack": description = "A compact back-mounted subspace anchor with one capacity ring and a 3x4 storage grid. Carried weight still applies. Move supplies to pockets or a badge before use."; break;
                case "cargo_pack": description = "A large back-mounted subspace anchor with three capacity rings and a 6x7 storage grid. Carried weight still applies. Move supplies to pockets or a badge before use."; break;
                case "circuit_board": description = "Recovered control electronics. Trade goods for the lunar supplier."; break;
                case "copper_coil": description = "A dense industrial induction coil. Its sale value comes with extra carried weight."; break;
                case "ceramic_plate": description = "Industrial ceramic stock. A trade good, not wearable armor."; break;
                case "optical_sensor": description = "An intact precision sensor. Compact trade goods recovered from lunar equipment."; break;
                case "data_drive": description = "An encrypted survey archive. Extract it and sell it for moon dust."; break;
                case "mooncore_fragment": description = "An anomalous MC00 crystal. Successfully extract it, then store it for Mooncore stabilization research and repeatable recycler fabrication at Suppliers → Halo research. Research and fabrication consume separate fragments only when confirmed. Can still be sold; carried fragments drop on death."; break;
                case "halo_mooncore_recycler": description = "Fabricated after Mooncore stabilization research. Energy per round is reduced by one third (rounded up); recovery speed -17%. Knowledge is permanent; the physical module can be lost on death."; break;
                case "mc00_archive_drive": description = "MC00 事故设备恢复的档案。成功带回自动保存调查发现。未入库前死亡会丢失，其他玩家可拾取。"; break;
                case "helium3_sample": description = "A sealed helium-3 research sample. Valuable trade goods; does not refill suit oxygen."; break;
                case "quantum_core": description = "A rare quantum processor. High-value cargo to bring back from a raid."; break;
                case "black_box": description = "A recovered survey recorder. Bulky, valuable cargo worth reserving pack space for."; break;
                default: return "";
            }
            return MoonkovLocalization.Text(description);
        }
    }
}
