// 使用氧气罐时一起检查库存版本、物品是否拿得到以及装备能否补氧。先算好补氧结果，扣库存成功后才写入角色。
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    /// <summary>Atomic server-side inventory and suit refill transaction.</summary>
    public static class RaidOxygenConsumables {
        public const string CanisterCode = "oxygen_canister";
        public const float UseSeconds = 2;

        public static bool CanConsume(in PredictedPlayerGhost state, ConsumableDefinition definition) {
            return definition != null && float.IsFinite(definition.Oxygen) && definition.Oxygen > 0 &&
                   RaidOxygen.CanRefill(state);
        }

        public static InventoryError Consume(
            ref PredictedPlayerGhost state,
            InventoryGraph graph,
            string id,
            int version) {
            if (graph == null) return InventoryError.Invalid;
            // 物品可能已经被换位、使用或丢弃。核对界面操作对应当前版本，再从实际容器检查是否可拿。
            if (version != graph.Version) return InventoryError.Stale;
            var item = graph.Find(id);
            if (item == null) return InventoryError.Missing;
            if (!graph.ConsumableAccessible(item)) return InventoryError.Inaccessible;
            var definition = ConsumableCatalog.Get(item.Code);
            if (!CanConsume(state, definition)) return InventoryError.Invalid;

            // Prepare the state change before consuming. A full/dead/invalid suit,
            // inaccessible stack or stale request leaves both owners untouched.
            // 先在角色状态副本算能补多少，不马上改本人。氧气满或角色不可补时不应扣掉这罐。
            var replenished = state;
            if (RaidOxygen.Refill(ref replenished, definition.Oxygen) <= 0) return InventoryError.Invalid;
            var error = graph.ConsumeOne(id, version);
            // 只有背包真实消费成功，才把准备好的角色状态写回。失败时氧气和库存不能只变其中一个。
            if (error == InventoryError.None) state = replenished;
            return error;
        }
    }
}