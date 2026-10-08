using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static class DollSingerWeapons
    {
        public const uint Halo = 2, Revolver = 3, Shotgun = 4, Sniper = 5, None = uint.MaxValue;
        public static bool IsHalo(uint id) => id == Halo || id == Revolver || id == Shotgun || id == Sniper;
        public static uint WeaponId(string code) => code == "rifle" || code == "compact" ? Halo :
            code == "pistol" ? Revolver : code == "shotgun" ? Shotgun : code == "sniper" ? Sniper : None;
        private static string Slot(int slot) => slot == 1 ? "Primary" : slot == 2 ? "Secondary" : "Pistol";
        private static int Ammo(InventoryItem item, WeaponRegistry registry)
        {
            var weapon = registry.GetWeaponData(WeaponId(item?.Code));
            if (item == null || weapon == null) return 0;
            item.LoadedAmmo = item.LoadedAmmo < 0 ? weapon.MagazineSize : Mathf.Clamp(item.LoadedAmmo, 0, weapon.MagazineSize);
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
            state.PrimaryAmmo = Ammo(primary, registry); state.SecondaryAmmo = Ammo(secondary, registry); state.PistolAmmo = Ammo(pistol, registry);
            int slot = state.EquippedWeaponSlot;
            var active = slot > 0 ? graph?.Equipped(Slot(slot)) : null;
            if (WeaponId(active?.Code) == None)
            {
                slot = primary != null ? 1 : secondary != null ? 2 : pistol != null ? 3 : 0;
                active = slot == 0 ? null : graph?.Equipped(Slot(slot));
            }
            string id = active?.Id ?? "";
            if (state.EquippedWeaponItem.ToString() != id || state.EquippedWeaponID != WeaponId(active?.Code))
            {
                state.EquippedWeaponItem = id; state.EquippedWeaponID = WeaponId(active?.Code);
                state.CurrentAmmo = Ammo(active, registry); state.ReloadTimer = 0f; state.ReloadTargetAmmo = 0;
                state.ControllerState.IsReloadingState = false; state.WeaponCooldown = 0f;
            }
            state.EquippedWeaponSlot = slot;
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
            state.CurrentAmmo = slot == 1 ? state.PrimaryAmmo : slot == 2 ? state.SecondaryAmmo : state.PistolAmmo;
            state.WeaponCooldown = 0f;
            return true;
        }
        public static void CompleteReload(ref PredictedPlayerGhost state, WeaponData weapon)
        {
            state.ControllerState.IsReloadingState = false;
            if (weapon != null) state.CurrentAmmo = Mathf.Clamp(state.ReloadTargetAmmo, state.CurrentAmmo, weapon.MagazineSize);
            state.ReloadTargetAmmo = 0;
        }
    }
}
