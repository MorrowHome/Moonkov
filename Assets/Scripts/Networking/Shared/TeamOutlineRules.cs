// 队友识别器的装备条件和目标关系。根据是否活着、所在队伍与同伴小组来决定能标谁，极速赏金里倒地队友仍可被识别。
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public static class TeamOutlineRules {
        public const string ItemCode = "team_tracker";
        public const int MaxTeammates = 3;

        // 道具必须在三个特殊装备槽中的一个。放在普通包里时不开放队友识别器效果。
        public static bool Equipped(InventoryGraph graph) {
            return graph != null && (graph.Equipped("Special1")?.Code == ItemCode ||
                                     graph.Equipped("Special2")?.Code == ItemCode ||
                                     graph.Equipped("Special3")?.Code == ItemCode);
        }

        // 阅读者需要活着且在该玩法可行动。死亡、倒地或角色还在部署都不展示操作效果。
        public static bool CanView(in PredictedPlayerGhost viewer) {
            return EnergyShieldRules.Alive(viewer) && (!viewer.ClashEnabled || viewer.ClashCanAct);
        }

        // 同伴组编号非零时才比对 AI 队友。没有组号的普通敌人不可被识别成同伴。
        public static bool Companion(in PredictedPlayerGhost viewer, in PredictedPlayerGhost target, bool targetHuman) {
            return !targetHuman && target.CurrentHealth > 0 && viewer.CompanionSquadId != 0 &&
                   viewer.CompanionSquadId == target.CompanionSquadId;
        }

        public static bool Teammate(
            in PredictedPlayerGhost viewer,
            in PredictedPlayerGhost target,
            bool viewerHuman,
            bool targetHuman) {
            // 倒地的队友仍需救援，可以显示。战局结束者与尸体不再显示。
            // 赏金模式允许看到需要救援的倒地队友。真正退出或阵亡后，目标不再通过玩法的友方识别。
            bool present = target.ClashEnabled
                ? target.ClashLife == ClashLife.Downed || (target.ClashLife == ClashLife.Alive && target.ClashCanAct)
                : target.CurrentHealth > 0;
            return present && HaloTransferRules.Teammates(viewer, target, viewerHuman, targetHuman);
        }
    }
}