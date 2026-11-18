// 把背包重量换成移动、冲刺消耗和恢复倍率。角色移动和体力计算都用这套规则，所以背包变重会反映到走跑节奏。
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // Weight is server-derived from the actual carried inventory. The same value
    // is replicated for owner prediction; storage and open loot are excluded.
    public static class RaidEncumbrance {
        public const float ComfortableWeight = 18;

        public static float Burden(float weight) {
            if (!float.IsFinite(weight)) return 0;
            return Mathf.Clamp01((weight - ComfortableWeight) /
                                 (InventoryCatalog.CarryWeightLimit - ComfortableWeight));
        }

        public static FirstPersonController.ControllerConsts Movement(
            in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants) {
            if (state.ClashEnabled) return constants;
            float burden = Burden(state.CarriedWeight);
            constants.Walk.Speed *= 1 - .12f * burden;
            constants.Sprint.Speed *= 1 - .18f * burden;
            constants.JumpHeight *= 1 - .12f * burden;
            return constants;
        }

        public static float SprintDrain(float weight) { return 1 + .75f * Burden(weight); }
        public static float SprintRecovery(float weight) { return 1 - .35f * Burden(weight); }
    }
}