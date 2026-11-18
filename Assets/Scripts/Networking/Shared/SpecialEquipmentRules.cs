// 特殊装备槽的共用条件。只有装备起来才使氧气循环器、防眩滤镜、罗盘和测距功能生效，同一类被动效果不会因多装一件而叠加。
#nullable disable
namespace Unity.MP_FPS.Inventory {
    public static class SpecialEquipmentRules {
        // 只按三个特殊装备槽查物品码。背包或仓库里同款道具不会让被动效果生效。
        public static bool Equipped(InventoryGraph graph, string code) {
            return graph != null && (graph.Equipped("Special1")?.Code == code ||
                                     graph.Equipped("Special2")?.Code == code ||
                                     graph.Equipped("Special3")?.Code == code);
        }

        // 同类被动效果只能生效一份。只放在库存中不会获得效果。
        // 多个氧气循环器也只用一个固定耗氧折减。防眩装备效力同理。
        public static float OxygenDrainMultiplier(InventoryGraph graph) {
            return Equipped(graph, "oxygen_recycler") ? .75f : 1;
        }

        public static float GlareMultiplier(InventoryGraph graph) { return Equipped(graph, "glare_filter") ? .4f : 1; }
        // 独立罗盘与测距仪分别开放各自工具。组合工具同时支持两个能力。
        public static bool Compass(string code) { return code == "survey_compass" || code == "survey_multitool"; }

        public static bool Rangefinder(string code) {
            return code == "laser_rangefinder" || code == "survey_multitool";
        }
    }
}