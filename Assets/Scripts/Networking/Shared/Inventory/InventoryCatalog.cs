#nullable disable
using System.Collections.Generic;

namespace Unity.MP_FPS.Inventory {
    public enum ItemKind {
        Root,
        Material,
        Cell,
        Helmet,
        LongGun,
        Pistol,
        Rig,
        Backpack,
        Medical,
        Food,
        Drink,
        StorageModule,
        ShapingModule,
        UtilityModule,
        Oxygen,
        Throwable,
        Shield,
        Melee,
        SecureContainer,
        SpecialEquipment
    }

    public sealed class InventoryRegion {
        public readonly string Id;
        public readonly int Width, Height;
        public readonly ItemKind? SlotKind;

        public InventoryRegion(string id, int width, int height, ItemKind? slot = null) {
            Id = id;
            Width = width;
            Height = height;
            SlotKind = slot;
        }
    }

    public sealed class ItemDefinition {
        public readonly string Code, Name;
        public readonly ItemKind Kind;
        public readonly int Width, Height, MaxStack;
        public readonly float Weight;
        public readonly InventoryRegion[] Regions;
        // 物品类型静态定义它占多少格、能堆多少、内部有没有容器区。单件实际物品再记录所处的格子和数量。
        public bool Container => Regions.Length > 0;

        public ItemDefinition(
            string code,
            string name,
            ItemKind kind,
            int w,
            int h,
            int maxStack,
            float kg,
            params InventoryRegion[] regions) {
            Code = code;
            Name = name;
            Kind = kind;
            Width = w;
            Height = h;
            MaxStack = maxStack;
            Weight = kg;
            Regions = regions;
        }
    }

    // Unity 和数据服务共用一份目录。尺寸表示外部占格，
    // Regions 表示独立内部区块，里面的物品仍只有一份。
    public static class InventoryCatalog {
        public const string Stash = "stash", Equipment = "equipment", Pockets = "pockets";
        public const int MaxDepth = 8, MaxItems = 2048;
        public const int StartingDust = 10_000;
        public const float CarryWeightLimit = 45;
        public static readonly InventoryRegion HaloCargoRegion = new("Halo cargo", 1, 1);

        public static bool CanStoreHaloCargo(string code) {
            return Get(code)?.Kind is ItemKind.LongGun or ItemKind.Pistol;
        }

        public static bool IsResearchSupply(string code) {
            return code is "rq_tablet" or "rq_flash" or "rq_drive" or "rq_sample" or "rq_package" or "rq_sample_kit"
                or "rq_nadir_records" or "rq_lucent_access" or "rq_original_archive";
        }

        public static bool IsHaloResearchCargo(string code) { return code is "rq_package" or "rq_original_archive"; }

        private static readonly string[] LootCodes = {
            "dust", "alloy", "cells", "small_pack", "rig", "ration", "water", "halo_capacitor", "halo_charge_bridge",
            "halo_focus_lens", "halo_fan_array", "halo_pulse_controller", "halo_hit_marker", "oxygen_canister"
        };

        public static string LootCode(int index) { return LootCodes[index % LootCodes.Length]; }
        public static readonly IReadOnlyDictionary<string, ItemDefinition> Definitions = Build();

        // 物品码为存档与网络共用的身份。这里声明基础类型、尺寸及容量，翻译名不会影响已保存物品。
        private static Dictionary<string, ItemDefinition> Build() {
            var defs = new[] {
                new ItemDefinition("stash", "Personal storage", ItemKind.Root, 0, 0, 1, 0,
                    new InventoryRegion("main", 10, 80)),
                new ItemDefinition("equipment", "Equipment", ItemKind.Root, 0, 0, 1, 0,
                    new InventoryRegion("Helmet", 1, 1, ItemKind.Helmet),
                    new InventoryRegion("Primary", 1, 1, ItemKind.LongGun),
                    new InventoryRegion("Secondary", 1, 1, ItemKind.LongGun),
                    new InventoryRegion("Pistol", 1, 1, ItemKind.Pistol),
                    new InventoryRegion("ChestRig", 1, 1, ItemKind.Rig),
                    new InventoryRegion("Backpack", 1, 1, ItemKind.Backpack),
                    new InventoryRegion("Shield", 1, 1, ItemKind.Shield),
                    new InventoryRegion("Melee", 1, 1, ItemKind.Melee),
                    new InventoryRegion("SecureContainer", 1, 1, ItemKind.SecureContainer),
                    new InventoryRegion("Special1", 1, 1, ItemKind.SpecialEquipment),
                    new InventoryRegion("Special2", 1, 1, ItemKind.SpecialEquipment),
                    new InventoryRegion("Special3", 1, 1, ItemKind.SpecialEquipment)),
                new ItemDefinition("pockets", "Pockets", ItemKind.Root, 0, 0, 1, 0, new InventoryRegion("1", 1, 1),
                    new InventoryRegion("2", 1, 1), new InventoryRegion("3", 1, 1), new InventoryRegion("4", 1, 1)),
                new ItemDefinition("loot", "Supply cache", ItemKind.Root, 0, 0, 1, 0,
                    new InventoryRegion("main", 6, 5)),
                new ItemDefinition("dust", "Moon dust", ItemKind.Material, 1, 1, 20, .15f),
                new ItemDefinition("alien_tissue", "Sealed alien tissue", ItemKind.Material, 1, 1, 10, .12f),
                new ItemDefinition("alien_core", "Alien boss nucleus", ItemKind.Material, 1, 1, 2, .4f),
                new ItemDefinition("alien_raw_meat", "Raw alien meat", ItemKind.Food, 1, 1, 4, .25f),
                new ItemDefinition("alien_neural_core", "Rare alien neural organ", ItemKind.Food, 1, 1, 1, .35f),
                new ItemDefinition("alloy", "Lunar alloy", ItemKind.Material, 2, 1, 10, .6f),
                new ItemDefinition("cells", "Energy cell", ItemKind.Cell, 1, 1, 12, .2f),
                new ItemDefinition("energy_grenade", "E-01 Energy grenade", ItemKind.Throwable, 1, 1, 3, .25f),
                new ItemDefinition("vitality_shot", "Vitality shot", ItemKind.Throwable, 1, 1, 3, 0.15f),
                new ItemDefinition("stamina_shot", "Stamina shot", ItemKind.Throwable, 1, 1, 3, 0.15f),
                new ItemDefinition("antidote_shot", "Antidote shot", ItemKind.Throwable, 1, 1, 3, 0.15f),
                new ItemDefinition("regeneration_shot", "Regeneration shot", ItemKind.Throwable, 1, 1, 3, 0.15f),
                new ItemDefinition("dynamite_bundle", "Dynamite bundle", ItemKind.Throwable, 1, 2, 2, 0.6f),
                new ItemDefinition("frag_bomb", "Fragmentation bomb", ItemKind.Throwable, 1, 1, 2, 0.4f),
                new ItemDefinition("fire_bomb", "Fire bomb", ItemKind.Throwable, 1, 2, 2, 0.5f),
                new ItemDefinition("smoke_grenade", "Smoke grenade", ItemKind.Throwable, 1, 1, 3, 0.35f),
                new ItemDefinition("choke_bomb", "Choke bomb", ItemKind.Throwable, 1, 1, 3, 0.3f),
                new ItemDefinition("alert_trip_mine", "Alert trip mine", ItemKind.Throwable, 1, 1, 3, 0.25f),
                new ItemDefinition("concertina_trip_mine", "Concertina trip mine", ItemKind.Throwable, 1, 2, 2, 0.5f),
                new ItemDefinition("poison_trip_mine", "Poison trip mine", ItemKind.Throwable, 1, 1, 3, 0.3f),
                new ItemDefinition("ammo_box", "Energy supply box", ItemKind.Throwable, 2, 2, 1, 1.0f),
                new ItemDefinition("medkit", "M-40 Medical injector", ItemKind.Medical, 1, 1, 4, .15f),
                new ItemDefinition("ration", "Expedition ration", ItemKind.Food, 1, 1, 3, .25f),
                new ItemDefinition("water", "Sealed drinking water", ItemKind.Drink, 1, 2, 2, .5f),
                new ItemDefinition("oxygen_canister", "O-40 Emergency oxygen canister", ItemKind.Oxygen, 1, 1, 3, .35f),
                new ItemDefinition("med_gel", "Sealed medical gel", ItemKind.Medical, 1, 1, 6, .08f),
                new ItemDefinition("trauma_kit", "Field trauma kit", ItemKind.Medical, 1, 2, 2, .45f),
                new ItemDefinition("energy_gel", "Energy gel", ItemKind.Food, 1, 1, 5, .08f),
                new ItemDefinition("protein_bar", "Protein bar", ItemKind.Food, 1, 1, 4, .15f),
                new ItemDefinition("electrolyte", "Electrolyte pouch", ItemKind.Drink, 1, 1, 3, .3f),
                new ItemDefinition("oxygen_reserve", "O-80 Reserve oxygen cylinder", ItemKind.Oxygen, 1, 2, 2, .8f),
                new ItemDefinition("circuit_board", "Control circuit board", ItemKind.Material, 1, 1, 4, .18f),
                new ItemDefinition("copper_coil", "Copper induction coil", ItemKind.Material, 2, 1, 3, .65f),
                new ItemDefinition("ceramic_plate", "Technical ceramic plate", ItemKind.Material, 2, 2, 2, .9f),
                new ItemDefinition("optical_sensor", "Optical sensor", ItemKind.Material, 1, 1, 2, .22f),
                new ItemDefinition("data_drive", "Encrypted data drive", ItemKind.Material, 1, 1, 1, .12f),
                new ItemDefinition("mooncore_fragment", "Mooncore fragment", ItemKind.Material, 1, 1, 1, .3f),
                new ItemDefinition("mc00_archive_drive", "MC00 事故档案盘", ItemKind.Material, 1, 1, 1, .12f),
                new ItemDefinition("rq_tablet", "Personnel information tablet", ItemKind.Material, 1, 2, 1, .42f),
                new ItemDefinition("rq_flash", "Encrypted extraction drive", ItemKind.Material, 1, 1, 1, .04f),
                new ItemDefinition("rq_drive", "Research storage drive", ItemKind.Material, 1, 1, 1, .25f),
                new ItemDefinition("rq_sample", "Sealed biological sample", ItemKind.Material, 1, 1, 1, .12f),
                new ItemDefinition("rq_package", "Sealed research package", ItemKind.Material, 1, 1, 1, .18f),
                new ItemDefinition("rq_sample_kit", "Biological sample kit", ItemKind.Material, 1, 1, 1, .14f),
                new ItemDefinition("rq_nadir_records", "Nadir's research records", ItemKind.Material, 1, 2, 1, .42f),
                new ItemDefinition("rq_lucent_access", "LUCENT access credentials", ItemKind.Material, 1, 1, 1, .04f),
                new ItemDefinition("rq_original_archive", "Original LUCENT archive", ItemKind.Material, 1, 1, 1, .27f),
                new ItemDefinition("helium3_sample", "Helium-3 sample", ItemKind.Material, 1, 2, 1, .6f),
                new ItemDefinition("quantum_core", "Quantum processor", ItemKind.Material, 2, 1, 1, .4f),
                new ItemDefinition("black_box", "Survey black box", ItemKind.Material, 2, 2, 1, 1.6f),
                new ItemDefinition("helmet", "L-01 Standard life-support collar", ItemKind.Helmet, 2, 2, 1, 1.2f),
                new ItemDefinition("energy_head_dome", "L-02 Neko life-support collar", ItemKind.Helmet, 2, 2, 1, .45f),
                new ItemDefinition("energy_shield_generator", "S-100 Energy shield generator", ItemKind.Shield, 2, 2, 1,
                    1.2f),
                new ItemDefinition("energy_sword", "Energy sword", ItemKind.Melee, 1, 3, 1, 1.8f),
                new ItemDefinition("dagger", "Dagger", ItemKind.Melee, 1, 2, 1, .35f),
                new ItemDefinition("rifle", "Halo Rifle", ItemKind.LongGun, 2, 2, 1, 3.2f),
                new ItemDefinition("compact", "Halo Rifle (Compact)", ItemKind.LongGun, 2, 2, 1, 2.4f),
                new ItemDefinition("pistol", "Halo Revolver", ItemKind.Pistol, 1, 1, 1, .8f),
                new ItemDefinition("shotgun", "Halo Shotgun", ItemKind.LongGun, 2, 2, 1, 3.2f),
                new ItemDefinition("sniper", "Halo Sniper", ItemKind.LongGun, 2, 2, 1, 4.5f),
                new ItemDefinition("cicada", "Cicada / 蝉鸣", ItemKind.LongGun, 2, 2, 1, 2.2f),
                new ItemDefinition("trine", "Trine / 三律", ItemKind.LongGun, 2, 2, 1, 3.1f),
                new ItemDefinition("aegis", "Aegis / 天枢", ItemKind.LongGun, 2, 2, 1, 6.2f),
                new ItemDefinition("vesper", "Vesper / 望舒", ItemKind.LongGun, 2, 2, 1, 3.8f),
                new ItemDefinition("thorn", "Thorn / 星棘", ItemKind.LongGun, 2, 2, 1, 3.3f),
                new ItemDefinition("nocturne", "Nocturne / 夜曲", ItemKind.LongGun, 2, 2, 1, 3.7f),
                new ItemDefinition("tempest", "Tempest / 岚轮", ItemKind.LongGun, 2, 2, 1, 4.1f),
                new ItemDefinition("rondo", "Rondo / 回环", ItemKind.LongGun, 2, 2, 1, 3.0f),
                new ItemDefinition("sundown", "Sundown / 坠日", ItemKind.LongGun, 2, 2, 1, 5.2f),
                new ItemDefinition("halo_capacitor", "Expansion capacitor", ItemKind.StorageModule, 1, 1, 1, .2f),
                new ItemDefinition("halo_charge_bridge", "Rapid charge bridge", ItemKind.StorageModule, 1, 1, 1, .15f),
                new ItemDefinition("halo_mooncore_recycler", "Mooncore recycler", ItemKind.StorageModule, 1, 1, 1, .2f),
                new ItemDefinition("halo_focus_lens", "Focus lens", ItemKind.ShapingModule, 1, 1, 1, .15f),
                new ItemDefinition("halo_fan_array", "Fan array", ItemKind.ShapingModule, 1, 1, 1, .2f),
                new ItemDefinition("halo_pulse_controller", "Pulse controller", ItemKind.ShapingModule, 1, 1, 1, .15f),
                new ItemDefinition("halo_hit_marker", "Hit marker", ItemKind.UtilityModule, 1, 1, 1, .1f),
                new ItemDefinition("rig", "R-04 Subspace badge", ItemKind.Rig, 2, 3, 1, .9f,
                    new InventoryRegion("left", 1, 2), new InventoryRegion("center", 2, 2),
                    new InventoryRegion("right", 1, 2)),
                new ItemDefinition("backpack", "B-08 Expedition subspace pack", ItemKind.Backpack, 3, 3, 1, 1.3f,
                    new InventoryRegion("main", 5, 6)),
                new ItemDefinition("secure_case", "S-04 个人保险箱", ItemKind.SecureContainer, 2, 2, 1, .8f,
                    new InventoryRegion("main", 2, 2)),
                new ItemDefinition("survey_compass", "C-01 月面指南针", ItemKind.SpecialEquipment, 1, 1, 1, .15f),
                new ItemDefinition("laser_rangefinder", "R-500 激光测距仪", ItemKind.SpecialEquipment, 1, 1, 1, .35f),
                new ItemDefinition("team_tracker", "T-01 Squad tracker", ItemKind.SpecialEquipment, 1, 1, 1, .25f),
                new ItemDefinition("oxygen_recycler", "O-25 Oxygen recycler", ItemKind.SpecialEquipment, 1, 1, 1, 0.4f),
                new ItemDefinition("glare_filter", "G-60 Glare filter", ItemKind.SpecialEquipment, 1, 1, 1, 0.15f),
                new ItemDefinition("survey_multitool", "SR-500 Survey instrument", ItemKind.SpecialEquipment, 1, 1, 1,
                    0.45f),
                new ItemDefinition("hemostatic_bandage", "Hemostatic bandage", ItemKind.Medical, 1, 1, 5, 0.06f),
                new ItemDefinition("rescue_kit", "Rescue medical kit", ItemKind.Medical, 2, 1, 2, 0.65f),
                new ItemDefinition("hydration_gel", "Hydration gel", ItemKind.Food, 1, 1, 4, 0.12f),
                new ItemDefinition("compressed_ration", "Compressed expedition ration", ItemKind.Food, 1, 2, 3, 0.4f),
                new ItemDefinition("emergency_oxygen", "O-20 Rapid oxygen flask", ItemKind.Oxygen, 1, 1, 4, 0.18f),
                new ItemDefinition("flash_grenade", "Flash grenade", ItemKind.Throwable, 1, 1, 3, 0.25f),
                new ItemDefinition("poison_bomb", "Poison gas bomb", ItemKind.Throwable, 1, 1, 2, 0.4f),
                new ItemDefinition("small_pack", "B-04 Scout subspace pack", ItemKind.Backpack, 2, 3, 1, .7f,
                    new InventoryRegion("main", 4, 4)),
                new ItemDefinition("scout_rig", "R-02 Scout badge", ItemKind.Rig, 2, 2, 1, .5f,
                    new InventoryRegion("left", 1, 2), new InventoryRegion("center", 1, 2),
                    new InventoryRegion("right", 1, 2)),
                new ItemDefinition("field_rig", "R-06 Field badge", ItemKind.Rig, 3, 3, 1, 1.4f,
                    new InventoryRegion("left", 2, 2), new InventoryRegion("center", 2, 2),
                    new InventoryRegion("right", 2, 2)),
                new ItemDefinition("sling_pack", "B-03 Light subspace pack", ItemKind.Backpack, 2, 2, 1, .45f,
                    new InventoryRegion("main", 3, 4)),
                new ItemDefinition("cargo_pack", "B-12 Cargo subspace pack", ItemKind.Backpack, 4, 4, 1, 2.8f,
                    new InventoryRegion("main", 6, 7))
            };
            var result = new Dictionary<string, ItemDefinition>();
            foreach (var def in defs) result.Add(def.Code, def);
            foreach (var organ in AlienBiology.Organs)
                result.Add(organ.Code, new ItemDefinition(organ.Code, organ.Name, ItemKind.Food, 1, 1, 3, .2f));
            return result;
        }

        public static ItemDefinition Get(string code) {
            return code != null && Definitions.TryGetValue(code, out var value) ? value : null;
        }
    }
}
