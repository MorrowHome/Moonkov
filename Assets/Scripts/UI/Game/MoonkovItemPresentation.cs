using System.Collections.Generic;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client
{
    internal static class MoonkovItemPresentation
    {
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
            if (item.Code == "energy_shield_generator")
            {
                if (item.ShieldCharge < 0 || item.ShieldEnergy < 0) return MoonkovLocalization.Text("NEW GENERATOR / INITIAL CHARGE ON FIRST DEPLOYMENT");
                return MoonkovLocalization.Format($"SHIELD {item.ShieldCharge:0}/100 / ENERGY {item.ShieldEnergy:0}/100");
            }
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

        private static string Description(string code)
        {
            var organ=AlienBiology.Organ(code);if(organ!=null)return MoonkovLocalization.Text(organ.Description);
            string description;
            switch (code)
            {
                case "secure_case": description = "装备在中栏保险箱槽后，箱体与内部 2×2 格物品在死亡或超时结算时保留。仅保护仍在箱内的物品；不计成功撤离或任务首次回收。禁止武器、穿戴装备、嵌套容器和任务产物。箱内补给须先移至衣兜或胸挂才能使用。"; break;
                case "energy_shield_generator": description = "A visible waist-mounted shield projector with charge indicators. Equip in the shield slot. Absorbs combat damage before health; excess damage passes through. After 6 seconds without a hit, restores 8 shield per second and spends 0.5 energy per shield. Recharge using charged cells in pockets or an equipped rig. Does not block suffocation, hunger or thirst."; break;
                case "energy_head_dome": description = "A neck-fitted life-support collar with a lilac bow and a gold cat bell. Follows the neck bones and uses the existing oxygen reserve, canisters and pressurized air. Does not provide ballistic protection or combat shielding."; break;
                case "helmet": description = "A plain neck-fitted life-support collar. Projects a spherical helmet without cat ears and uses the existing oxygen reserve, canisters and pressurized air."; break;
                case "energy_sword": case "dagger": description = "Equip in the melee slot and bind to a number key. Left click: light attack. Right click: heavy attack. Attacks use stamina; walls block hits. With no active weapon, fists are the fallback."; break;
                case "cells": description = "Stored battery energy powers weapon reloading and shield recharging. Move cells to pockets or an equipped rig before use. Recharging the shield consumes only the energy transferred."; break;
                case "vitality_shot": description = "Inject for 3 seconds to fill surviving body parts to their current recoverable health limit. Does not revive destroyed parts or restore health lost to Bounty Clash downs."; break;
                case "stamina_shot": description = "Inject for 3 seconds to prevent sprint fatigue for 420 seconds. Does not refill hunger, thirst or oxygen, or heal injured legs."; break;
                case "antidote_shot": description = "Inject for 3 seconds to clear poison and gain 600 seconds of immunity to tactical poison clouds. Does not prevent other damage."; break;
                case "dynamite_bundle": description = "Select, then left click to throw after a 1-second action. Explodes after a 4-second fuse: up to 650 damage within 8 metres. Cover blocks the blast. Clears concertina wire; can kill teammates and the thrower."; break;
                case "frag_bomb": description = "Select, then left click to throw after a 1-second action. A 4-second fuse triggers up to 320 damage within 8 metres and 8 seconds of bleeding. Cover blocks the blast. Does not clear concertina wire."; break;
                case "fire_bomb": description = "Throw to create a 3-metre fire zone for 30 seconds. Deals 8 damage per second while exposed. Fire affects enemies, teammates and the thrower."; break;
                case "choke_bomb": description = "Throw to create a 4-metre choke cloud for 20 seconds. Extinguishes fire, clears tactical poison and causes coughing."; break;
                case "alert_trip_mine": description = "Place on nearby clear ground with a 3-second action. The trip line alerts when any player crosses, including teammates and the owner. Aim at it and hold the interaction to disarm."; break;
                case "concertina_trip_mine": description = "Place on nearby clear ground with a 3-second action. Any player crossing the line releases concertina wire, causing damage, bleeding and slowed movement. Dynamite can clear wire."; break;
                case "poison_trip_mine": description = "Place on nearby clear ground with a 3-second action. Any player crossing the line releases poison. Choke clouds clear tactical poison; antidote protects against it."; break;
                case "ammo_box": description = "Place with a 3-second action. Stores 400 energy; each 2-second resupply transfers up to 100, subject to inventory space. Expires after 120 seconds. Does not grant a free weapon reload."; break;
                case "energy_grenade": description = "[G] throws from pockets or an equipped rig. Bounces, then releases an energy blast after 3 seconds. Cover blocks the blast; nearby damage can kill the thrower. Move grenades out of backpacks before throwing."; break;
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
                case "small_pack": description = "A back-mounted scout subspace anchor with one ring, paired stabilizer fins and a 4x4 storage grid. Carried weight still applies. Move supplies to pockets or a rig before use."; break;
                case "backpack": description = "A back-mounted subspace anchor with two capacity rings and a 5x6 storage grid. Carried weight still applies. Move supplies to pockets or a rig before use."; break;
                case "sling_pack": description = "A compact back-mounted subspace anchor with one capacity ring and a 3x4 storage grid. Carried weight still applies. Move supplies to pockets or a rig before use."; break;
                case "cargo_pack": description = "A large back-mounted subspace anchor with three capacity rings and a 6x7 storage grid. Carried weight still applies. Move supplies to pockets or a rig before use."; break;
                case "circuit_board": description = "Recovered control electronics. Trade goods for the lunar supplier."; break;
                case "copper_coil": description = "A dense industrial induction coil. Its sale value comes with extra carried weight."; break;
                case "ceramic_plate": description = "Industrial ceramic stock. A trade good, not wearable armor."; break;
                case "optical_sensor": description = "An intact precision sensor. Compact trade goods recovered from lunar equipment."; break;
                case "data_drive": description = "An encrypted survey archive. Extract it and sell it for moon dust."; break;
                case "mooncore_fragment": description = "An anomalous MC00 crystal. Successfully extract it, then store it for Mooncore stabilization research and repeatable recycler fabrication at Suppliers → Halo research. Research and fabrication consume separate fragments only when confirmed. Can still be sold; carried fragments drop on death."; break;
                case "halo_mooncore_recycler": description = "Fabricated after Mooncore stabilization research. Install in a rifle or shotgun storage slot: energy per round reduced by one third (rounded up), charge time +20%. Knowledge is permanent; this physical module can be lost on death."; break;
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
