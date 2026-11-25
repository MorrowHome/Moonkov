using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS.Client {
    public sealed partial class StashItemArt {
        public static StashArtKind KindFor(string code) {
            int organ = Array.FindIndex(AlienBiology.Organs, o => o.Code == code);
            if (organ >= 0) return (StashArtKind)((int)StashArtKind.AlienSpider + organ);
            switch (code) {
                case "regeneration_shot": return StashArtKind.RegenerationShot;
                case "oxygen_recycler": return StashArtKind.OxygenRecycler;
                case "glare_filter": return StashArtKind.GlareFilter;
                case "survey_multitool": return StashArtKind.SurveyMultitool;
                case "hemostatic_bandage": return StashArtKind.HemostaticBandage;
                case "rescue_kit": return StashArtKind.RescueKit;
                case "hydration_gel": return StashArtKind.HydrationGel;
                case "compressed_ration": return StashArtKind.CompressedRation;
                case "emergency_oxygen": return StashArtKind.EmergencyOxygen;
                case "flash_grenade": return StashArtKind.FlashGrenade;
                case "poison_bomb": return StashArtKind.PoisonBomb;
                case "team_tracker": return StashArtKind.TeamTracker;
                case "survey_compass": return StashArtKind.SurveyCompass;
                case "laser_rangefinder": return StashArtKind.Rangefinder;
                case "secure_case": return StashArtKind.SecureCase;
                case "dust": return StashArtKind.Dust;
                case "alloy": return StashArtKind.Alloy;
                case "cells": return StashArtKind.Cell;
                case "medkit": return StashArtKind.Medical;
                case "alien_raw_meat": return StashArtKind.AlienMeat;
                case "alien_neural_core":
                case "alien_core":
                    return StashArtKind.AlienOrgan;
                case "alien_tissue": return StashArtKind.AlienTissue;
                case "energy_head_dome": return StashArtKind.LifeSupportShroud;
                case "energy_shield_generator": return StashArtKind.ShieldGenerator;
                case "energy_sword": return StashArtKind.EnergySword;
                case "dagger": return StashArtKind.CombatKnife;
                case "helmet": return StashArtKind.Helmet;
                case "rifle":
                case "compact":
                    return StashArtKind.HaloRifle;
                case "pistol": return StashArtKind.HaloRevolver;
                case "ration": return StashArtKind.Ration;
                case "water": return StashArtKind.Water;
                case "energy_grenade": return StashArtKind.EnergyGrenade;
                case "oxygen_canister": return StashArtKind.OxygenCanister;
                case "med_gel": return StashArtKind.MedicalGel;
                case "trauma_kit": return StashArtKind.TraumaKit;
                case "energy_gel": return StashArtKind.EnergyGel;
                case "protein_bar": return StashArtKind.ProteinBar;
                case "electrolyte": return StashArtKind.Electrolyte;
                case "oxygen_reserve": return StashArtKind.OxygenReserve;
                case "scout_rig": return StashArtKind.ScoutRig;
                case "field_rig": return StashArtKind.FieldRig;
                case "sling_pack": return StashArtKind.SlingPack;
                case "cargo_pack": return StashArtKind.CargoPack;
                case "circuit_board": return StashArtKind.CircuitBoard;
                case "copper_coil": return StashArtKind.CopperCoil;
                case "ceramic_plate": return StashArtKind.CeramicPlate;
                case "optical_sensor": return StashArtKind.OpticalSensor;
                case "data_drive": return StashArtKind.DataDrive;
                case "helium3_sample": return StashArtKind.HeliumSample;
                case "mooncore_fragment": return StashArtKind.MooncoreFragment;
                case "mc00_archive_drive": return StashArtKind.MissionArchive;
                case "quantum_core": return StashArtKind.QuantumCore;
                case "black_box": return StashArtKind.BlackBox;
                case "shotgun": return StashArtKind.HaloShotgun;
                case "sniper": return StashArtKind.HaloSniper;
                case "cicada": return StashArtKind.HaloCicada;
                case "trine": return StashArtKind.HaloTrine;
                case "aegis": return StashArtKind.HaloAegis;
                case "vesper": return StashArtKind.HaloVesper;
                case "thorn": return StashArtKind.HaloThorn;
                case "sundown": return StashArtKind.HaloSundown;
                case "rondo": return StashArtKind.HaloRondo;
                case "tempest": return StashArtKind.HaloTempest;
                case "nocturne": return StashArtKind.HaloNocturne;
                case "halo_capacitor": return StashArtKind.HaloCapacitor;
                case "halo_charge_bridge": return StashArtKind.HaloChargeBridge;
                case "halo_focus_lens": return StashArtKind.HaloFocusLens;
                case "halo_fan_array": return StashArtKind.HaloFanArray;
                case "halo_pulse_controller": return StashArtKind.HaloPulseController;
                case "halo_hit_marker": return StashArtKind.HaloHitMarker;
                case "halo_mooncore_recycler": return StashArtKind.HaloMooncoreRecycler;
                case "rig": return StashArtKind.ChestRig;
                case "backpack": return StashArtKind.Backpack;
                case "small_pack": return StashArtKind.ScoutPack;
                default: return StashArtKind.None;
            }
        }
    }
}
