#nullable disable
namespace Unity.MP_FPS.Inventory
{
    public static class SpecialEquipmentRules
    {
        public static bool Equipped(InventoryGraph graph, string code) => graph != null &&
            (graph.Equipped("Special1")?.Code == code || graph.Equipped("Special2")?.Code == code ||
             graph.Equipped("Special3")?.Code == code);

        // Multiple copies never multiply a passive bonus. Stored items grant nothing.
        public static float OxygenDrainMultiplier(InventoryGraph graph) => Equipped(graph, "oxygen_recycler") ? .75f : 1;
        public static float GlareMultiplier(InventoryGraph graph) => Equipped(graph, "glare_filter") ? .4f : 1;
        public static bool Compass(string code) => code == "survey_compass" || code == "survey_multitool";
        public static bool Rangefinder(string code) => code == "laser_rangefinder" || code == "survey_multitool";
    }
}
