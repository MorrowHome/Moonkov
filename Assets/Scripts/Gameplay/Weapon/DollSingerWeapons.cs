// 把背包里的武器和配件映射到角色的网络状态，供客户端预测和服务端结算射击。
// SyncEquipment 同步物品编号、装备槽和配件。计时完成后，CompleteReload 应用待充能量或补足相应模式的弹药。
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS {
    public static class DollSingerWeapons {
        public const uint Halo = 2,
            Revolver = 3,
            Shotgun = 4,
            Sniper = 5,
            Cicada = 6,
            Trine = 7,
            Aegis = 8,
            Vesper = 9,
            Thorn = 10,
            Sundown = 11,
            Rondo = 12,
            Tempest = 13,
            Nocturne = 14,
            None = uint.MaxValue;

        public static bool IsHalo(uint id) { return id >= Halo && id <= Nocturne; }

        public static uint WeaponId(string code) {
            return code == "rifle" || code == "compact" ? Halo :
                code == "pistol" ? Revolver :
                code == "shotgun" ? Shotgun :
                code == "sniper" ? Sniper :
                code == "cicada" ? Cicada :
                code == "trine" ? Trine :
                code == "aegis" ? Aegis :
                code == "vesper" ? Vesper :
                code == "thorn" ? Thorn :
                code == "sundown" ? Sundown :
                code == "rondo" ? Rondo :
                code == "tempest" ? Tempest :
                code == "nocturne" ? Nocturne : None;
        }

        // 玩法状态存紧凑的槽位数字，库存用文字区域名。同一映射用于找道具和校验装填期间的移动请求。
        private static string Slot(int slot) {
            return slot == 1 ? "Primary" :
                slot == 2 ? "Secondary" :
                slot == MeleeInventory.EquipmentIndex ? MeleeInventory.Slot : "Pistol";
        }

        // 旧物品可能没存弹药，负数时按基础武器容量初始化。显式的零表示确实打空了。
        private static int Ammo(InventoryItem item, InventoryGraph graph, WeaponRegistry registry) {
            var weapon = registry.GetWeaponData(WeaponId(item?.Code), (ushort)HaloModules.Mask(graph, item));
            if (item == null || weapon == null) return 0;
            item.LoadedAmmo = item.LoadedAmmo < 0
                ? registry.GetWeaponData(WeaponId(item.Code)).MagazineSize
                : Mathf.Max(0, item.LoadedAmmo);
            return item.LoadedAmmo;
        }

        public static void StoreAmmo(ref PredictedPlayerGhost state) {
            if (state.EquippedWeaponSlot == 1)
                state.PrimaryAmmo = state.CurrentAmmo;
            else if (state.EquippedWeaponSlot == 2)
                state.SecondaryAmmo = state.CurrentAmmo;
            else if (state.EquippedWeaponSlot == 3) state.PistolAmmo = state.CurrentAmmo;
        }

        // 切换或入库前，先用实例 ID 找正在拿的那一把。传统弹药保存到道具，使用共享能量池时则留在角色能量状态。
        public static void SaveActiveAmmo(ref PredictedPlayerGhost state, InventoryGraph graph) {
            var item = graph?.Find(state.EquippedWeaponItem.ToString());
            if (!state.HaloEnergyInitialized && item != null &&
                MeleeInventory.Kind(item.Code) == DollSinger.MeleeWeaponKind.None &&
                WeaponId(item.Code) == state.EquippedWeaponID)
                item.LoadedAmmo = state.CurrentAmmo;
            StoreAmmo(ref state);
        }

        // 食物增益和强化效果会改伤害、散布和射速。
        // 实体武器和已支付的弹药不受影响。增益到期时继续现有的充能或取枪进度。
        public static bool RefreshTemporaryModules(
            ref PredictedPlayerGhost state,
            string itemId,
            uint weaponId,
            ushort modules) {
            const ushort temporary = (ushort)(HaloModule.AlienSurge | HaloModule.FoodEffectMask);
            if (state.EquippedWeaponItem.ToString() != (itemId ?? "") || state.EquippedWeaponID != weaponId ||
                ((state.EquippedModules ^ modules) & ~temporary) != 0)
                return false;
            state.EquippedModules = modules;
            return true;
        }

        // 计时充能开始时先预留能量。
        // 在动作完成之前禁止通过库存事务把武器实体换到别处或换成另一把。
        // 充能开始后已经预留资源。若库存能移走当前武器或把另一把塞进同槽，完成时资源就可能结给错的武器。
        public static bool BlocksReloadWeaponMove(
            in PredictedPlayerGhost state,
            InventoryOperation operation,
            string itemId,
            string parent,
            string region) {
            if (!state.InventoryWeapons || !state.ControllerState.IsReloadingState ||
                operation != InventoryOperation.Move)
                return false;
            string active = state.EquippedWeaponItem.ToString();
            if (string.IsNullOrEmpty(active)) return false;
            if (itemId == active) return true;
            return state.EquippedWeaponSlot >= 1 && state.EquippedWeaponSlot <= 3 &&
                   parent == InventoryCatalog.Equipment && region == Slot(state.EquippedWeaponSlot);
        }

        // 以服务端库存树为准。背包里的武器要先装备到槽位才能选中。
        public static void SyncEquipment(
            ref PredictedPlayerGhost state,
            InventoryGraph graph,
            WeaponRegistry registry) {
            // 能量和装备来源以库存为准。只读已经装备的槽位，背包里的武器还不能直接切出来。
            HaloEnergyRules.Synchronize(ref state, graph);
            SaveActiveAmmo(ref state, graph);
            state.InventoryWeapons = true;
            var primary = graph?.Equipped("Primary");
            var secondary = graph?.Equipped("Secondary");
            var pistol = graph?.Equipped("Pistol");
            state.PrimaryWeaponID = WeaponId(primary?.Code);
            state.SecondaryWeaponID = WeaponId(secondary?.Code);
            state.PistolWeaponID = WeaponId(pistol?.Code);
            state.PrimaryAmmo = HaloEnergyRules.AvailableShots(state,
                registry.GetWeaponData(state.PrimaryWeaponID, (ushort)HaloModules.Mask(graph, primary)));
            state.SecondaryAmmo = HaloEnergyRules.AvailableShots(state,
                registry.GetWeaponData(state.SecondaryWeaponID, (ushort)HaloModules.Mask(graph, secondary)));
            state.PistolAmmo = HaloEnergyRules.AvailableShots(state,
                registry.GetWeaponData(state.PistolWeaponID, (ushort)HaloModules.Mask(graph, pistol)));
            state.PrimaryModules = (ushort)HaloModules.Mask(graph, primary);
            state.SecondaryModules = (ushort)HaloModules.Mask(graph, secondary);
            state.PistolModules = (ushort)HaloModules.Mask(graph, pistol);
            ushort activity = state.AlienFoodEffectSeconds > 0 ? (ushort)((int)state.AlienFoodEffect << 12) : (ushort)0;
            state.PrimaryModules |= activity;
            state.SecondaryModules |= activity;
            state.PistolModules |= activity;
            if (state.AlienSurgeSeconds > 0) {
                state.PrimaryModules |= (ushort)HaloModule.AlienSurge;
                state.SecondaryModules |= (ushort)HaloModule.AlienSurge;
                state.PistolModules |= (ushort)HaloModule.AlienSurge;
            }
            // 当前槽失去有效武器时，按剩余装备顺序挑能用的一个。没有枪时仍可落到近战或空手。
            int slot = state.EquippedWeaponSlot;
            var active = slot > 0 ? graph?.Equipped(Slot(slot)) : null;
            if (slot != MeleeInventory.EquipmentIndex && WeaponId(active?.Code) == None) {
                slot = state.PrimaryWeaponID != None ? 1 :
                    state.SecondaryWeaponID != None ? 2 :
                    state.PistolWeaponID != None ? 3 :
                    MeleeInventory.Equipped(graph) != null ? MeleeInventory.EquipmentIndex : 0;
                active = slot == 0 ? null : graph?.Equipped(Slot(slot));
            }
            state.EquippedWeaponSlot = slot;
            string id = active?.Id ?? "";
            ushort modules = (ushort)HaloModules.Mask(graph, active);
            modules |= activity;
            if (state.AlienSurgeSeconds > 0) modules |= (ushort)HaloModule.AlienSurge;
            // 仅食物增益开始或消失时保留当前装填和抬手进度。换了具体武器或实体配件才重新开始取武器。
            if (!RefreshTemporaryModules(ref state, id, WeaponId(active?.Code), modules)) {
                state.EquippedWeaponItem = id;
                state.EquippedWeaponID = WeaponId(active?.Code);
                state.CurrentAmmo =
                    HaloEnergyRules.AvailableShots(state, registry.GetWeaponData(WeaponId(active?.Code), modules));
                state.EquippedModules = modules;
                state.ReloadTimer = 0f;
                state.ReloadTargetAmmo = 0;
                state.ControllerState.IsReloadingState = false;
                state.ReloadDuration = 0;
                state.HaloRechargeEnergy = 0;
                HaloModuleFire.BeginEquip(ref state,
                    registry.GetWeaponData(state.EquippedWeaponID, state.EquippedModules));
            }
            HaloEnergyRules.RefreshAmmo(ref state,
                registry.GetWeaponData(state.EquippedWeaponID, state.EquippedModules));
        }

        // 只在允许换手时响应快捷输入。不同槽位的武器状态分开，先保存旧槽再加载新槽，避免两把武器共用同一份弹药读数。
        public static bool TryEquip(ref PredictedPlayerGhost state, in PlayerInput input, WeaponRegistry registry) {
            if (!state.InventoryWeapons || state.CurrentHealth <= 0f || state.ControllerState.IsReloadingState ||
                HaloTransferRules.Occupied(state) || input.HaloTransfer)
                return false;
            int slot = input.EquipPistol ? 3 : input.EquipRevolver ? 2 : input.EquipHalo ? 1 : state.EquippedWeaponSlot;
            uint requested = slot == 1 ? state.PrimaryWeaponID :
                slot == 2 ? state.SecondaryWeaponID :
                slot == 3 ? state.PistolWeaponID : None;
            if (slot == state.EquippedWeaponSlot || requested == None || registry?.GetWeaponData(requested) == null)
                return false;
            StoreAmmo(ref state);
            state.EquippedWeaponSlot = slot;
            state.EquippedWeaponItem = default;
            state.EquippedWeaponID = requested;
            state.EquippedModules = slot == 1 ? state.PrimaryModules :
                slot == 2 ? state.SecondaryModules : state.PistolModules;
            state.CurrentAmmo = slot == 1 ? state.PrimaryAmmo : slot == 2 ? state.SecondaryAmmo : state.PistolAmmo;
            HaloModuleFire.BeginEquip(ref state, registry.GetWeaponData(requested, state.EquippedModules));
            return true;
        }

        public static bool EquipMelee(
            ref PredictedPlayerGhost state,
            InventoryGraph graph,
            WeaponRegistry registry,
            string itemId) {
            if (!state.InventoryWeapons || state.CurrentHealth <= 0 || state.ControllerState.IsReloadingState ||
                HaloTransferRules.Occupied(state))
                return false;
            var item = MeleeInventory.Equipped(graph);
            // 空手是一种装备状态，无需在库存里造物品。
            // 近战请求也要和库存实际装备的实例对应。两边都空才代表空手模式。
            if ((item?.Id ?? "") != (itemId ?? "")) return false;
            SaveActiveAmmo(ref state, graph);
            state.EquippedWeaponSlot = MeleeInventory.EquipmentIndex;
            state.EquippedWeaponItem = default;
            SyncEquipment(ref state, graph, registry);
            state.WeaponEquipRemaining = Mathf.Max(state.WeaponEquipRemaining, .25f);
            return true;
        }

        // 待充能量只在角色仍有效时加入共用能量池，随后清零，防止重复结算。
        private static void CompleteReload(ref PredictedPlayerGhost state, int magazineSize) {
            if (state.HaloRechargeEnergy > 0 && state.HaloEquipped && EnergyShieldRules.Alive(state) &&
                (!state.ClashEnabled || state.ClashCanAct)) {
                state.HaloEnergy = Mathf.Min(HaloEnergyRules.Maximum(state),
                    HaloEnergyRules.Energy(state) + state.HaloRechargeEnergy);
                state.HaloRechargeSequence++;
            }
            state.HaloRechargeEnergy = 0;
            state.ControllerState.IsReloadingState = false;
            state.ReloadTimer = 0f;
            state.ReloadDuration = 0f;
            if (magazineSize > 0)
                state.CurrentAmmo = Mathf.Max(state.CurrentAmmo, Mathf.Min(state.ReloadTargetAmmo, magazineSize));
            state.ReloadTargetAmmo = 0;
        }

        public static void CompleteReload(ref PredictedPlayerGhost state, WeaponData weapon) {
            CompleteReload(ref state,
                state.HaloEnergyInitialized && IsHalo(state.EquippedWeaponID) ? 0 :
                weapon != null ? weapon.MagazineSize : 0);
            HaloEnergyRules.RefreshAmmo(ref state, weapon);
        }

        // 成功撤离即结束能操作的战局。
        // 销毁角色和保存装备前，先结算已经付费的装填量。
        // 旧武器弹仓模式下，成功离场前可以把已支付的装填量结算到正确的物品。物品 ID、槽位或类型对不上就放弃。
        public static bool CompleteExtractedCharge(
            ref PredictedPlayerGhost state,
            InventoryGraph graph,
            int magazineSize) {
            if (state.HaloEnergyInitialized || !state.InventoryWeapons || !state.ControllerState.IsReloadingState ||
                state.CurrentHealth <= 0 || !IsHalo(state.EquippedWeaponID) || state.EquippedWeaponSlot < 1 ||
                state.EquippedWeaponSlot > 3 || state.ReloadTargetAmmo <= state.CurrentAmmo || magazineSize <= 0)
                return false;
            var active = graph?.Equipped(Slot(state.EquippedWeaponSlot));
            if (active == null || active.Id != state.EquippedWeaponItem.ToString() ||
                WeaponId(active.Code) != state.EquippedWeaponID)
                return false;
            CompleteReload(ref state, magazineSize);
            SaveActiveAmmo(ref state, graph);
            return true;
        }
    }
}