// 把背包重量换成移动、冲刺消耗和恢复倍率。角色移动和体力计算都用这套规则，所以背包变重会反映到走跑节奏。
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // 服务器从实际随身背包算重量。同一个数值也同步给
    // 本机预测，仓库和当前打开的地上物不计入负重。
    public static class RaidEncumbrance {
        public const float ComfortableWeight = 18;

        // 先把重量归一化为负担比例，所有速度和耐力倍率都读同一份比例，极端数值不继续无限增长。
        public static float Burden(float weight) {
            if (!float.IsFinite(weight)) return 0;
            return Mathf.Clamp01((weight - ComfortableWeight) /
                                 (InventoryCatalog.CarryWeightLimit - ComfortableWeight));
        }

        // 远征中调整控制器的移动参数，临时竞技等模式可保留默认移动。玩家仍经过同一碰撞解算。
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

        // 负重增加时耐力更快消耗、恢复更慢。它与缺氧或受伤的限制分开，最后共同影响角色。
        public static float SprintDrain(float weight) { return 1 + .75f * Burden(weight); }
        public static float SprintRecovery(float weight) { return 1 - .35f * Burden(weight); }
    }
}