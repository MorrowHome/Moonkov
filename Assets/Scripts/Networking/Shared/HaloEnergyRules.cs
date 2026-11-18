// 射击、主动护盾和补能用同一个光环能量池。根据已装模块更新容量和回充速度，并把能量换成当前可打的弹数。
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS {
    // One actor-owned pool. Weapons, defense and later halo abilities spend here.
    // It is initialized once per spawn, never by switching or re-equipping a halo.
    public static class HaloEnergyRules {
        public const float Capacity = 100, RecoveryDelay = 3, RecoveryPerSecond = 10;

        public static float Maximum(in PredictedPlayerGhost state) {
            return float.IsFinite(state.HaloCapacity) && state.HaloCapacity >= Capacity ? state.HaloCapacity : Capacity;
        }

        public static float Energy(in PredictedPlayerGhost state) {
            return float.IsFinite(state.HaloEnergy) ? Mathf.Clamp(state.HaloEnergy, 0, Maximum(state)) : 0;
        }

        public static bool HasHalo(InventoryGraph graph) {
            return graph != null &&
                   (DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(graph.Equipped("Primary")?.Code)) ||
                    DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(graph.Equipped("Secondary")?.Code)) ||
                    DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(graph.Equipped("Pistol")?.Code)));
        }

        public static void Synchronize(ref PredictedPlayerGhost state, InventoryGraph graph) {
            // 从真实装备槽算是否拥有光环，再比较多个已装光环的模块容量，角色只保存一份能量池。
            state.HaloEquipped = HasHalo(graph);
            float capacity = Capacity;
            if (graph != null)
                foreach (string slot in s_Slots) {
                    var item = graph.Equipped(slot);
                    if (!DollSingerWeapons.IsHalo(DollSingerWeapons.WeaponId(item?.Code))) continue;
                    var modules = HaloModules.Mask(graph, item);
                    capacity = Mathf.Max(capacity, CapacityForModules(modules));
                }
            state.HaloCapacity = capacity;
            // 首次载入才设为满能量，后续根据当下容量收紧原余量。换武器或拆模块不会给一池新电。
            if (!state.HaloEnergyInitialized) {
                state.HaloEnergy = state.HaloEquipped ? capacity : 0;
                state.HaloEnergyInitialized = true;
            }
            state.HaloEnergy = Energy(state);
            if (!state.HaloEquipped || !EnergyShieldRules.Alive(state)) {
                Release(ref state);
                if (state.HaloRechargeEnergy > 0) {
                    // 角色死亡或失去光环时取消待完成的补能。读条状态不能等到以后拿回光环又偷偷补一次。
                    state.HaloRechargeEnergy = state.ReloadTimer = state.ReloadDuration = 0;
                    state.ReloadTargetAmmo = 0;
                    state.ControllerState.IsReloadingState = false;
                }
            }
        }

        private static readonly string[] s_Slots = { "Primary", "Secondary", "Pistol" };

        public static float CapacityForModules(HaloModule modules) {
            return Capacity * ((modules & HaloModule.Capacitor) != 0 ? 1.5f : 1) *
                   ((modules & HaloModule.BiologicalCapacity) != 0 ? 1.4f : 1);
        }

        // 容量、接桥、回收类模块各改回充速度，再考虑研究模块。没有这些条件时使用基础回充。
        public static float RecoveryRate(HaloModule modules) {
            float rate = RecoveryPerSecond;
            if ((modules & HaloModule.Capacitor) != 0)
                rate /= 1.35f;
            else if ((modules & HaloModule.ChargeBridge) != 0)
                rate /= .6f;
            else if ((modules & HaloModule.MooncoreRecycler) != 0) rate /= 1.2f;
            if ((modules & HaloModule.BiologicalCharging) != 0) rate /= .75f;
            return rate;
        }

        // 护盾可以和部分搜索状态共存，拿雷、近战或外放等占手时要停。是否有能量在举盾状态计算时再正式裁决。
        public static bool CanDefend(in PredictedPlayerGhost state) {
            return state.HaloEquipped && EnergyShieldRules.Alive(state) && (!state.ClashEnabled || state.ClashCanAct) &&
                   !GrenadeHandling.Occupied(state) && !state.ConsumingSupply &&
                   (state.HandAction == 0 || state.HandAction == (byte)Unity.MP_FPS.DollSinger.HandAction.Search) &&
                   state.MeleePhase == 0 && (!state.ControllerState.IsReloadingState || state.HaloRechargeEnergy > 0) &&
                   state.WeaponEquipRemaining <= 0 && !state.ClashDarkSight && !HaloTransferRules.Occupied(state);
        }

        public static void Release(ref PredictedPlayerGhost state) {
            state.HaloDefending = false;
            state.HaloDefenseHeld = false;
        }

        public static void SetDefense(ref PredictedPlayerGhost state, bool held) {
            // 能量耗尽破盾后，必须松手才清破盾状态。即使回了一点电也不会在持续按住期间反复开合。
            if (!held) state.HaloDefenseBroken = false;
            bool wasDefending = state.HaloDefending;
            state.HaloDefenseHeld = held && CanDefend(state);
            state.HaloDefending = state.HaloDefenseHeld && !state.HaloDefenseBroken && Energy(state) > 0;
            if (state.HaloDefending && !wasDefending) state.ShieldRestoreSequence++;
            if (!state.HaloDefenseHeld) return;
            // Holding the barrier is free. Only an actual energy spend restarts recovery.
            // 举盾本身不耗能，但清当前射击准备。屏障挡到真实伤害才从共享池扣。
            HaloModuleFire.Reset(ref state);
            state.WeaponRaiseProgress = state.HipFireHoldRemaining = 0;
        }

        public static float Spend(ref PredictedPlayerGhost state, float amount) {
            if (!float.IsFinite(amount) || amount <= 0) return 0;
            // 最多扣当前实际有的能量，任何有效消耗重启恢复延迟。护盾恰好耗尽会留下破盾通知。
            float spent = Mathf.Min(Energy(state), amount);
            state.HaloEnergy = Energy(state) - spent;
            state.HaloRecoveryDelay = RecoveryDelay;
            state.HaloRegenerating = false;
            if (state.HaloDefending && state.HaloEnergy <= 0) {
                state.HaloDefending = false;
                state.HaloDefenseBroken = true;
                state.ShieldBreakSequence++;
            }
            return spent;
        }

        // 武器的弹数其实是可用能量除以该武器每发成本，再被最大弹容量限制。能量变化后重算弹数。
        public static int AvailableShots(in PredictedPlayerGhost state, WeaponData weapon) {
            return weapon == null
                ? 0
                : Mathf.Min(weapon.MagazineSize,
                    Mathf.FloorToInt((Energy(state) + .0001f) / Mathf.Max(1, weapon.EnergyPerRound)));
        }

        public static void RefreshAmmo(ref PredictedPlayerGhost state, WeaponData weapon) {
            if (state.HaloEnergyInitialized && DollSingerWeapons.IsHalo(state.EquippedWeaponID))
                state.CurrentAmmo = AvailableShots(state, weapon);
        }

        public static void CommitShot(ref PredictedPlayerGhost state, WeaponData weapon) {
            // 普通枪械仍直接扣自己的弹数。光环发射才扣共享池并同步该把武器还能打几次。
            if (!state.HaloEnergyInitialized || !DollSingerWeapons.IsHalo(state.EquippedWeaponID)) {
                state.CurrentAmmo--;
                return;
            }
            Spend(ref state, Mathf.Max(1, weapon.EnergyPerRound));
            RefreshAmmo(ref state, weapon);
        }

        public static void Tick(ref PredictedPlayerGhost state, float dt, bool canRecover = true) {
            state.HaloRegenerating = false;
            if (!state.HaloEquipped || !EnergyShieldRules.Alive(state) || !canRecover) {
                Release(ref state);
                return;
            }
            if (!float.IsFinite(dt) || dt <= 0) return;
            state.HaloEnergy = Energy(state);
            // 光环外放期间不自动回充，也不同时叠加电池装填。回收后再从恢复延迟开始。
            if (HaloTransferRules.Occupied(state)) {
                state.HaloRecoveryDelay = RecoveryDelay;
                return;
            }
            if (state.HaloRechargeEnergy > 0) return;
            float delay = float.IsFinite(state.HaloRecoveryDelay)
                ? Mathf.Max(0, state.HaloRecoveryDelay)
                : RecoveryDelay;
            state.HaloRecoveryDelay = Mathf.Max(0, delay - dt);
            // 恢复延迟可能在这帧中间刚好用完。只用过了延迟的那一小段进电，避免按整帧多加。
            float active = Mathf.Max(0, dt - delay);
            float rate = RecoveryRate((HaloModule)state.EquippedModules);
            float before = state.HaloEnergy;
            state.HaloEnergy = Mathf.Min(Maximum(state), before + active * rate);
            state.HaloRegenerating = state.HaloEnergy > before;
        }
    }

    public static class HaloEnergyInventory {
        // 补能从口袋或当前胸挂找可访问电池。满电、正在装填或另一动作占手时不可重复开始。
        public static bool CanRecharge(in PredictedPlayerGhost state, InventoryGraph graph, InventoryItem cell) {
            return graph != null && HaloEnergyRules.HasHalo(graph) && state.HaloEquipped &&
                   EnergyShieldRules.Alive(state) && (!state.ClashEnabled || state.ClashCanAct) &&
                   !GrenadeHandling.Occupied(state) && !HaloTransferRules.Occupied(state) &&
                   !state.ControllerState.IsReloadingState && state.HaloRechargeEnergy <= 0 &&
                   state.WeaponEquipRemaining <= 0 && !state.ConsumingSupply && state.HandAction == 0 &&
                   state.MeleePhase == 0 && HaloEnergyRules.Energy(state) <= HaloEnergyRules.Maximum(state) - 1 &&
                   cell != null && BatteryEnergy.IsCell(cell) && graph.Carried(cell) &&
                   (cell.Parent == InventoryCatalog.Pockets || cell.Parent == graph.Equipped("ChestRig")?.Id) &&
                   BatteryEnergy.Stored(cell) > 0;
        }

        public static InventoryError Recharge(
            ref PredictedPlayerGhost state,
            InventoryGraph graph,
            string cellId,
            int expectedVersion,
            WeaponData weapon = null) {
            if (graph == null) return InventoryError.Invalid;
            if (graph.Version != expectedVersion) return InventoryError.Stale;
            var cell = graph.Find(cellId);
            if (cell == null) return InventoryError.Missing;
            if (!CanRecharge(state, graph, cell)) return InventoryError.Inaccessible;
            // 一笔补能顶多用一节电池余量，也不能超过当前池缺的量。先从真实背包扣这部分。
            int transferred = Mathf.Min(BatteryEnergy.Charge(cell),
                Mathf.FloorToInt(HaloEnergyRules.Maximum(state) - HaloEnergyRules.Energy(state) + .0001f));
            if (transferred <= 0) return InventoryError.Invalid;
            BatteryEnergy.Spend(cell, transferred);
            if (cell.Quantity == 0) graph.Items.Remove(cell);
            // 真正给光环加电留到装填动作完成。角色同步的是待加能量和读条，客户端不能先自行加一份。
            HaloReload.BeginEnergyCharge(ref state, weapon, transferred);
            graph.Version++;
            return InventoryError.None;
        }

        public static bool RechargeFirst(
            ref PredictedPlayerGhost state,
            InventoryGraph graph,
            WeaponData weapon = null) {
            if (graph == null) return false;
            foreach (var cell in graph.Items)
                if (CanRecharge(state, graph, cell))
                    return Recharge(ref state, graph, cell.Id, graph.Version, weapon) == InventoryError.None;
            return false;
        }
    }
}