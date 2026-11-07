using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    // One actor-owned pool. Weapons, defense and later halo abilities spend here.
    // It is initialized once per spawn, never by switching or re-equipping a halo.
    public static class HaloEnergyRules
    {
        public const float Capacity = 100, RecoveryDelay = 3, RecoveryPerSecond = 10;
        public static float Maximum(in PredictedPlayerGhost state) =>
            float.IsFinite(state.HaloCapacity) && state.HaloCapacity >= Capacity ? state.HaloCapacity : Capacity;
        public static float Energy(in PredictedPlayerGhost state) =>
            float.IsFinite(state.HaloEnergy) ? Mathf.Clamp(state.HaloEnergy, 0, Maximum(state)) : 0;
        public static bool HasHalo(InventoryGraph graph) => graph != null &&
            (DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(graph.Equipped("Primary")?.Code)) ||
             DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(graph.Equipped("Secondary")?.Code)) ||
             DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(graph.Equipped("Pistol")?.Code)));

        public static void Synchronize(ref PredictedPlayerGhost state, InventoryGraph graph)
        {
            state.HaloEquipped = HasHalo(graph);
            float capacity = Capacity;
            if (graph != null)
                foreach (string slot in s_Slots)
                {
                    var item = graph.Equipped(slot);
                    if (!DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(item?.Code))) continue;
                    var modules = HaloModules.Mask(graph, item);
                    capacity = Mathf.Max(capacity, CapacityForModules(modules));
                }
            state.HaloCapacity = capacity;
            if (!state.HaloEnergyInitialized)
            {
                state.HaloEnergy = state.HaloEquipped ? capacity : 0;
                state.HaloEnergyInitialized = true;
            }
            state.HaloEnergy = Energy(state);
            if (!state.HaloEquipped || !EnergyShieldRules.Alive(state))
            {
                Release(ref state);
                if (state.HaloRechargeEnergy > 0)
                {
                    state.HaloRechargeEnergy = state.ReloadTimer = state.ReloadDuration = 0;
                    state.ReloadTargetAmmo = 0;
                    state.ControllerState.IsReloadingState = false;
                }
            }
        }
        private static readonly string[] s_Slots = { "Primary", "Secondary", "Pistol" };

        public static float CapacityForModules(HaloModule modules) => Capacity *
            ((modules & HaloModule.Capacitor) != 0 ? 1.5f : 1) * ((modules & HaloModule.BiologicalCapacity) != 0 ? 1.4f : 1);
        public static float RecoveryRate(HaloModule modules)
        {
            float rate = RecoveryPerSecond;
            if ((modules & HaloModule.Capacitor) != 0) rate /= 1.35f;
            else if ((modules & HaloModule.ChargeBridge) != 0) rate /= .6f;
            else if ((modules & HaloModule.MooncoreRecycler) != 0) rate /= 1.2f;
            if ((modules & HaloModule.BiologicalCharging) != 0) rate /= .75f;
            return rate;
        }

        public static bool CanDefend(in PredictedPlayerGhost state) => state.HaloEquipped &&
            EnergyShieldRules.Alive(state) && (!state.ClashEnabled || state.ClashCanAct) &&
            !GrenadeHandling.Occupied(state) && !state.ConsumingSupply && state.HandAction == 0 && state.MeleePhase == 0 &&
            (!state.ControllerState.IsReloadingState || state.HaloRechargeEnergy > 0) && state.WeaponEquipRemaining <= 0 && !state.ClashDarkSight && !HaloTransferRules.Occupied(state);

        public static void Release(ref PredictedPlayerGhost state)
        { state.HaloDefending = false; state.HaloDefenseHeld = false; }

        public static void SetDefense(ref PredictedPlayerGhost state, bool held)
        {
            if (!held) state.HaloDefenseBroken = false;
            bool wasDefending = state.HaloDefending;
            state.HaloDefenseHeld = held && CanDefend(state);
            state.HaloDefending = state.HaloDefenseHeld && !state.HaloDefenseBroken && Energy(state) > 0;
            if (state.HaloDefending && !wasDefending) state.ShieldRestoreSequence++;
            if (!state.HaloDefenseHeld) return;
            // Holding the barrier is free. Only an actual energy spend restarts recovery.
            HaloModuleFire.Reset(ref state);
            state.WeaponRaiseProgress = state.HipFireHoldRemaining = 0;
        }

        public static float Spend(ref PredictedPlayerGhost state, float amount)
        {
            if (!float.IsFinite(amount) || amount <= 0) return 0;
            float spent = Mathf.Min(Energy(state), amount);
            state.HaloEnergy = Energy(state) - spent;
            state.HaloRecoveryDelay = RecoveryDelay;
            state.HaloRegenerating = false;
            if (state.HaloDefending && state.HaloEnergy <= 0)
            { state.HaloDefending = false; state.HaloDefenseBroken = true; state.ShieldBreakSequence++; }
            return spent;
        }

        public static int AvailableShots(in PredictedPlayerGhost state, WeaponData weapon) => weapon == null ? 0 :
            Mathf.Min(weapon.MagazineSize, Mathf.FloorToInt((Energy(state) + .0001f) / Mathf.Max(1, weapon.EnergyPerRound)));
        public static void RefreshAmmo(ref PredictedPlayerGhost state, WeaponData weapon)
        {
            if (state.HaloEnergyInitialized && DollSingerWeapons.IsHalo(state.EquippedWeaponID))
                state.CurrentAmmo = AvailableShots(state, weapon);
        }
        public static void CommitShot(ref PredictedPlayerGhost state, WeaponData weapon)
        {
            if (!state.HaloEnergyInitialized || !DollSingerWeapons.IsHalo(state.EquippedWeaponID))
            { state.CurrentAmmo--; return; }
            Spend(ref state, Mathf.Max(1, weapon.EnergyPerRound));
            RefreshAmmo(ref state, weapon);
        }

        public static void Tick(ref PredictedPlayerGhost state, float dt, bool canRecover = true)
        {
            state.HaloRegenerating = false;
            if (!state.HaloEquipped || !EnergyShieldRules.Alive(state) || !canRecover)
            { Release(ref state); return; }
            if (!float.IsFinite(dt) || dt <= 0) return;
            state.HaloEnergy = Energy(state);
            if (HaloTransferRules.Occupied(state)) { state.HaloRecoveryDelay = RecoveryDelay; return; }
            if (state.HaloRechargeEnergy > 0) return;
            float delay = float.IsFinite(state.HaloRecoveryDelay) ? Mathf.Max(0, state.HaloRecoveryDelay) : RecoveryDelay;
            state.HaloRecoveryDelay = Mathf.Max(0, delay - dt);
            float active = Mathf.Max(0, dt - delay);
            float rate = RecoveryRate((HaloModule)state.EquippedModules);
            float before = state.HaloEnergy;
            state.HaloEnergy = Mathf.Min(Maximum(state), before + active * rate);
            state.HaloRegenerating = state.HaloEnergy > before;
        }
    }

    public static class HaloEnergyInventory
    {
        public static bool CanRecharge(in PredictedPlayerGhost state, InventoryGraph graph, InventoryItem cell) =>
            graph != null && HaloEnergyRules.HasHalo(graph) && state.HaloEquipped && EnergyShieldRules.Alive(state) &&
            (!state.ClashEnabled || state.ClashCanAct) && !GrenadeHandling.Occupied(state) && !HaloTransferRules.Occupied(state) &&
            !state.ControllerState.IsReloadingState && state.HaloRechargeEnergy <= 0 && state.WeaponEquipRemaining <= 0 &&
            !state.ConsumingSupply && state.HandAction == 0 && state.MeleePhase == 0 &&
            HaloEnergyRules.Energy(state) <= HaloEnergyRules.Maximum(state) - 1 && cell != null && BatteryEnergy.IsCell(cell) &&
            graph.Carried(cell) && (cell.Parent == InventoryCatalog.Pockets || cell.Parent == graph.Equipped("ChestRig")?.Id) &&
            BatteryEnergy.Stored(cell) > 0;

        public static InventoryError Recharge(ref PredictedPlayerGhost state, InventoryGraph graph, string cellId, int expectedVersion, WeaponData weapon = null)
        {
            if (graph == null) return InventoryError.Invalid;
            if (graph.Version != expectedVersion) return InventoryError.Stale;
            var cell = graph.Find(cellId);
            if (cell == null) return InventoryError.Missing;
            if (!CanRecharge(state, graph, cell)) return InventoryError.Inaccessible;
            int transferred = Mathf.Min(BatteryEnergy.Charge(cell),
                Mathf.FloorToInt(HaloEnergyRules.Maximum(state) - HaloEnergyRules.Energy(state) + .0001f));
            if (transferred <= 0) return InventoryError.Invalid;
            BatteryEnergy.Spend(cell, transferred);
            if (cell.Quantity == 0) graph.Items.Remove(cell);
            HaloReload.BeginEnergyCharge(ref state, weapon, transferred);
            graph.Version++;
            return InventoryError.None;
        }

        public static bool RechargeFirst(ref PredictedPlayerGhost state, InventoryGraph graph, WeaponData weapon = null)
        {
            if (graph == null) return false;
            foreach (var cell in graph.Items)
                if (CanRecharge(state, graph, cell))
                    return Recharge(ref state, graph, cell.Id, graph.Version, weapon) == InventoryError.None;
            return false;
        }
    }
}
