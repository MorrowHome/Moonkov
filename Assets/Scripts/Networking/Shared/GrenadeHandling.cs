// 手榴弹动作共用的阶段表。拿出、拉环、投出、收手的时长供服务器和手部动画共用，并把取物和引信规则分给对应物品。
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public enum GrenadePhase : byte {
        None,
        Drawing,
        Safe,
        Priming,
        Live,
        Throwing,
        Recovering,
        Stowing
    }

    public enum GrenadeCommand : byte {
        Equip,
        Prime,
        Release,
        Stow,
        Drop
    }

    // 权威投掷释放与烘焙手部动画共用同一套时间。
    public static class GrenadeHandling {
        public const float DrawSeconds = .35f, PrimeSeconds = .55f, ReleaseSeconds = .32f;
        public const float RecoverySeconds = .42f, StowSeconds = .3f, LowSpeed = 6f;
        // 任一非空闲阶段都会占双手。准备引信、可扔和正在放手合称实弹，意味着后续不能退原物品。
        public static bool Occupied(in PredictedPlayerGhost state) { return state.GrenadePhase != 0; }

        public static bool Live(GrenadePhase phase) {
            return phase == GrenadePhase.Priming || phase == GrenadePhase.Live || phase == GrenadePhase.Throwing;
        }

        // 实际物品码分能量手雷和战术投掷物。背包可达性由各自规则处理。
        public static bool Throwable(string code) {
            return code == EnergyGrenadeRules.Code || TacticalItemRules.Find(code)?.Throwable == true;
        }

        public static bool Accessible(InventoryGraph graph, InventoryItem item) {
            return item != null && Throwable(item.Code) && (item.Code == EnergyGrenadeRules.Code
                ? EnergyGrenadeRules.Accessible(graph, item)
                : TacticalItemRules.Accessible(graph, item));
        }

        public static float Fuse(string code) {
            return code == EnergyGrenadeRules.Code ? EnergyGrenadeRules.Fuse : TacticalItemRules.Find(code)?.Fuse ?? 0;
        }

        // 服务端阶段用共同的动画时长，放手时才生成飞行状态，拉环和收回不同。
        public static float Duration(GrenadePhase phase) {
            return phase switch {
                GrenadePhase.Drawing => DrawSeconds,
                GrenadePhase.Priming => PrimeSeconds,
                GrenadePhase.Throwing => ReleaseSeconds,
                GrenadePhase.Recovering => RecoverySeconds,
                GrenadePhase.Stowing => StowSeconds,
                _ => 0
            };
        }

        // 真正扣物品时依种类调用背包规则。调用方在拉环阶段用同一库存版本，不能拿切换后的另一个堆。
        public static InventoryError Take(InventoryGraph graph, string itemId, int version) {
            return graph?.Find(itemId)?.Code == EnergyGrenadeRules.Code
                ? EnergyGrenadeRules.Take(graph, itemId, version)
                : TacticalItemRules.Take(graph, itemId, version);
        }
    }
}