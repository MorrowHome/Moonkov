using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static class DollSingerWeapons
    {
        public const uint Halo = 2, Revolver = 3, Shotgun = 4, Sniper = 5, Cicada = 6, Trine = 7, Aegis = 8, Vesper = 9, Thorn = 10, Sundown = 11, Rondo = 12, Tempest = 13, Nocturne = 14, None = uint.MaxValue;
        public static bool IsHalo(uint id) => id >= Halo && id <= Nocturne;
        public static uint WeaponId(string code) => code == "rifle" || code == "compact" ? Halo :
            code == "pistol" ? Revolver : code == "shotgun" ? Shotgun : code == "sniper" ? Sniper :
            code == "cicada" ? Cicada : code == "trine" ? Trine : code == "aegis" ? Aegis : code == "vesper" ? Vesper : code == "thorn" ? Thorn : code == "sundown" ? Sundown : code == "rondo" ? Rondo : code == "tempest" ? Tempest : code == "nocturne" ? Nocturne : None;
        private static string Slot(int slot) => slot == 1 ? "Primary" : slot == 2 ? "Secondary" : "Pistol";
        private static int Ammo(InventoryItem item, InventoryGraph graph, WeaponRegistry registry)
        {
            var weapon = registry.GetWeaponData(WeaponId(item?.Code), (ushort)HaloModules.Mask(graph, item));
            if (item == null || weapon == null) return 0;
            item.LoadedAmmo = item.LoadedAmmo < 0 ? registry.GetWeaponData(WeaponId(item.Code)).MagazineSize : Mathf.Max(0, item.LoadedAmmo);
            return item.LoadedAmmo;
        }
        public static void StoreAmmo(ref PredictedPlayerGhost state)
        {
            if (state.EquippedWeaponSlot == 1) state.PrimaryAmmo = state.CurrentAmmo;
            else if (state.EquippedWeaponSlot == 2) state.SecondaryAmmo = state.CurrentAmmo;
            else if (state.EquippedWeaponSlot == 3) state.PistolAmmo = state.CurrentAmmo;
        }
        public static void SaveActiveAmmo(ref PredictedPlayerGhost state, InventoryGraph graph)
        {
            var item = graph?.Find(state.EquippedWeaponItem.ToString());
            if (item != null && WeaponId(item.Code) == state.EquippedWeaponID) item.LoadedAmmo = state.CurrentAmmo;
            StoreAmmo(ref state);
        }
        // The authoritative item tree determines possession; backpack weapons cannot be selected.
        public static void SyncEquipment(ref PredictedPlayerGhost state, InventoryGraph graph, WeaponRegistry registry)
        {
            SaveActiveAmmo(ref state, graph);
            state.InventoryWeapons = true;
            var primary = graph?.Equipped("Primary"); var secondary = graph?.Equipped("Secondary"); var pistol = graph?.Equipped("Pistol");
            state.PrimaryWeaponID = WeaponId(primary?.Code); state.SecondaryWeaponID = WeaponId(secondary?.Code); state.PistolWeaponID = WeaponId(pistol?.Code);
            state.PrimaryAmmo = Ammo(primary, graph, registry); state.SecondaryAmmo = Ammo(secondary, graph, registry); state.PistolAmmo = Ammo(pistol, graph, registry);
            state.PrimaryModules = (ushort)HaloModules.Mask(graph, primary); state.SecondaryModules = (ushort)HaloModules.Mask(graph, secondary);
            state.PistolModules = (ushort)HaloModules.Mask(graph, pistol);
            ushort activity=state.AlienFoodEffectSeconds>0 ? (ushort)((int)state.AlienFoodEffect<<12) : (ushort)0;
            state.PrimaryModules|=activity;state.SecondaryModules|=activity;state.PistolModules|=activity;
            if(state.AlienSurgeSeconds>0)
            {
                state.PrimaryModules |= (ushort)HaloModule.AlienSurge;
                state.SecondaryModules |= (ushort)HaloModule.AlienSurge;
                state.PistolModules |= (ushort)HaloModule.AlienSurge;
            }
            int slot = state.EquippedWeaponSlot;
            var active = slot > 0 ? graph?.Equipped(Slot(slot)) : null;
            if (WeaponId(active?.Code) == None)
            {
                slot = primary != null ? 1 : secondary != null ? 2 : pistol != null ? 3 : 0;
                active = slot == 0 ? null : graph?.Equipped(Slot(slot));
            }
            state.EquippedWeaponSlot = slot;
            string id = active?.Id ?? "";
            ushort modules = (ushort)HaloModules.Mask(graph, active);
            modules|=activity;
            if(state.AlienSurgeSeconds>0)modules |= (ushort)HaloModule.AlienSurge;
            if (state.EquippedWeaponItem.ToString() != id || state.EquippedWeaponID != WeaponId(active?.Code) || state.EquippedModules != modules)
            {
                state.EquippedWeaponItem = id; state.EquippedWeaponID = WeaponId(active?.Code);
                state.CurrentAmmo = Ammo(active, graph, registry); state.EquippedModules = modules; state.ReloadTimer = 0f; state.ReloadTargetAmmo = 0;
                state.ControllerState.IsReloadingState = false;
                HaloModuleFire.BeginEquip(ref state, registry.GetWeaponData(state.EquippedWeaponID, state.EquippedModules));
            }
        }
        public static bool TryEquip(ref PredictedPlayerGhost state, in PlayerInput input, WeaponRegistry registry)
        {
            if (!state.InventoryWeapons || state.CurrentHealth <= 0f || state.ControllerState.IsReloadingState) return false;
            int slot = input.EquipPistol ? 3 : input.EquipRevolver ? 2 : input.EquipHalo ? 1 : state.EquippedWeaponSlot;
            uint requested = slot == 1 ? state.PrimaryWeaponID : slot == 2 ? state.SecondaryWeaponID : slot == 3 ? state.PistolWeaponID : None;
            if (slot == state.EquippedWeaponSlot || requested == None || registry?.GetWeaponData(requested) == null) return false;
            StoreAmmo(ref state);
            state.EquippedWeaponSlot = slot; state.EquippedWeaponItem = default;
            state.EquippedWeaponID = requested;
            state.EquippedModules = slot == 1 ? state.PrimaryModules : slot == 2 ? state.SecondaryModules : state.PistolModules;
            state.CurrentAmmo = slot == 1 ? state.PrimaryAmmo : slot == 2 ? state.SecondaryAmmo : state.PistolAmmo;
            HaloModuleFire.BeginEquip(ref state, registry.GetWeaponData(requested, state.EquippedModules));
            return true;
        }
        public static void CompleteReload(ref PredictedPlayerGhost state, WeaponData weapon)
        {
            state.ControllerState.IsReloadingState = false;
            state.ReloadTimer = 0f;
            if (weapon != null) state.CurrentAmmo = Mathf.Max(state.CurrentAmmo, Mathf.Min(state.ReloadTargetAmmo, weapon.MagazineSize));
            state.ReloadTargetAmmo = 0;
        }
    }
}
