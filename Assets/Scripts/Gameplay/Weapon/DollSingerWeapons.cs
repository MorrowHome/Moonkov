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
        private static string Slot(int slot) => slot == 1 ? "Primary" : slot == 2 ? "Secondary" :
            slot == MeleeInventory.EquipmentIndex ? MeleeInventory.Slot : "Pistol";
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
            if (item != null && MeleeInventory.Kind(item.Code) == DollSinger.MeleeWeaponKind.None &&
                WeaponId(item.Code) == state.EquippedWeaponID) item.LoadedAmmo = state.CurrentAmmo;
            StoreAmmo(ref state);
        }
        // Food activity and surge change damage/spread/cadence, not the physical
        // weapon or paid magazine. Their expiry must not restart a charge/equip.
        public static bool RefreshTemporaryModules(ref PredictedPlayerGhost state,string itemId,uint weaponId,ushort modules)
        {
            const ushort temporary=(ushort)(HaloModule.AlienSurge | HaloModule.FoodEffectMask);
            if(state.EquippedWeaponItem.ToString()!=(itemId ?? "") || state.EquippedWeaponID!=weaponId ||
                ((state.EquippedModules ^ modules) & ~temporary)!=0) return false;
            state.EquippedModules=modules;
            return true;
        }
        // Energy is reserved at the start of a timed charge. Do not let an
        // inventory transaction replace its physical weapon before completion.
        public static bool BlocksReloadWeaponMove(in PredictedPlayerGhost state,InventoryOperation operation,string itemId,string parent,string region)
        {
            if(!state.InventoryWeapons || !state.ControllerState.IsReloadingState || operation!=InventoryOperation.Move) return false;
            string active=state.EquippedWeaponItem.ToString();
            if(string.IsNullOrEmpty(active)) return false;
            if(itemId==active) return true;
            return state.EquippedWeaponSlot>=1 && state.EquippedWeaponSlot<=3 &&
                parent==InventoryCatalog.Equipment && region==Slot(state.EquippedWeaponSlot);
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
            if (slot != MeleeInventory.EquipmentIndex && WeaponId(active?.Code) == None)
            {
                slot = state.PrimaryWeaponID != None ? 1 : state.SecondaryWeaponID != None ? 2 :
                    state.PistolWeaponID != None ? 3 : MeleeInventory.Equipped(graph) != null ? MeleeInventory.EquipmentIndex : 0;
                active = slot == 0 ? null : graph?.Equipped(Slot(slot));
            }
            state.EquippedWeaponSlot = slot;
            string id = active?.Id ?? "";
            ushort modules = (ushort)HaloModules.Mask(graph, active);
            modules|=activity;
            if(state.AlienSurgeSeconds>0)modules |= (ushort)HaloModule.AlienSurge;
            if (!RefreshTemporaryModules(ref state,id,WeaponId(active?.Code),modules))
            {
                state.EquippedWeaponItem = id; state.EquippedWeaponID = WeaponId(active?.Code);
                state.CurrentAmmo = Ammo(active, graph, registry); state.EquippedModules = modules; state.ReloadTimer = 0f; state.ReloadTargetAmmo = 0;
                state.ControllerState.IsReloadingState = false;
                state.ReloadDuration = 0;
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
        public static bool EquipMelee(ref PredictedPlayerGhost state, InventoryGraph graph, WeaponRegistry registry, string itemId)
        {
            if (!state.InventoryWeapons || state.CurrentHealth <= 0 || state.ControllerState.IsReloadingState) return false;
            var item = MeleeInventory.Equipped(graph);
            // Empty hands are an equipment mode, never a fabricated inventory item.
            if ((item?.Id ?? "") != (itemId ?? "")) return false;
            SaveActiveAmmo(ref state, graph);
            state.EquippedWeaponSlot = MeleeInventory.EquipmentIndex;
            state.EquippedWeaponItem = default;
            SyncEquipment(ref state, graph, registry);
            state.WeaponEquipRemaining = Mathf.Max(state.WeaponEquipRemaining, .25f);
            return true;
        }
        private static void CompleteReload(ref PredictedPlayerGhost state,int magazineSize)
        {
            state.ControllerState.IsReloadingState = false;
            state.ReloadTimer = 0f;
            state.ReloadDuration = 0f;
            if (magazineSize>0) state.CurrentAmmo = Mathf.Max(state.CurrentAmmo, Mathf.Min(state.ReloadTargetAmmo, magazineSize));
            state.ReloadTargetAmmo = 0;
        }
        public static void CompleteReload(ref PredictedPlayerGhost state, WeaponData weapon) =>
            CompleteReload(ref state,weapon != null ? weapon.MagazineSize : 0);

        // Successful extraction ends the playable raid immediately. Settle rounds
        // already paid for before destroying the player and saving their loadout.
        public static bool CompleteExtractedCharge(ref PredictedPlayerGhost state,InventoryGraph graph,int magazineSize)
        {
            if(!state.InventoryWeapons || !state.ControllerState.IsReloadingState || state.CurrentHealth<=0 ||
                !IsHalo(state.EquippedWeaponID) || state.EquippedWeaponSlot<1 || state.EquippedWeaponSlot>3 ||
                state.ReloadTargetAmmo<=state.CurrentAmmo || magazineSize<=0) return false;
            var active=graph?.Equipped(Slot(state.EquippedWeaponSlot));
            if(active==null || active.Id!=state.EquippedWeaponItem.ToString() || WeaponId(active.Code)!=state.EquippedWeaponID) return false;
            CompleteReload(ref state,magazineSize);
            SaveActiveAmmo(ref state,graph);
            return true;
        }
    }
}
