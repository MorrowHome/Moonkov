using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public static class TeamOutlineRules
    {
        public const string ItemCode = "team_tracker";
        public const int MaxTeammates = 3;

        public static bool Equipped(InventoryGraph graph) => graph != null &&
            (graph.Equipped("Special1")?.Code == ItemCode || graph.Equipped("Special2")?.Code == ItemCode ||
             graph.Equipped("Special3")?.Code == ItemCode);

        public static bool CanView(in PredictedPlayerGhost viewer) => EnergyShieldRules.Alive(viewer) &&
            (!viewer.ClashEnabled || viewer.ClashCanAct);

        public static bool Teammate(in PredictedPlayerGhost viewer, in PredictedPlayerGhost target,
            bool viewerHuman, bool targetHuman)
        {
            // Downed allies remain useful rescue targets. Finished participants and corpses do not.
            bool present = target.ClashEnabled
                ? target.ClashLife == ClashLife.Downed || target.ClashLife == ClashLife.Alive && target.ClashCanAct
                : target.CurrentHealth > 0;
            return present && HaloTransferRules.Teammates(viewer, target, viewerHuman, targetHuman);
        }
    }
}
