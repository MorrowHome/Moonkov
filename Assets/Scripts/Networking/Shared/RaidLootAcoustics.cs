// 打开容器、持续搜箱和使用补给会让服务端角色状态上的动作序号变化。客户端由此播声，AI 用同一事件做局内听觉判断。
namespace Unity.MP_FPS {
    // Opening and searching use the same authoritative hearing and playback events.
    public static class RaidLootAcoustics {
        public const float HearingRange = 10f;
        public const double MinimumInterval = .8;
        public const double SearchInterval = 1.2;

        public static void RefreshSearch(
            ref PredictedPlayerGhost player,
            bool searching,
            double now,
            ref double nextSoundAt) {
            // 搜刮状态首先由活角色当前动作判断。未搜索或间隔没到时不发新声音序号。
            player.LootSearching = searching && player.CurrentHealth > 0;
            if (!player.LootSearching || now < nextSoundAt) return;
            Mark(ref player);
            nextSoundAt = now + SearchInterval;
        }

        public static void Mark(ref PredictedPlayerGhost player) {
            // 搜索声作为递增事件传给其他系统，零留给从未触发。回绕时跳过零。
            player.LootSearchSequence = unchecked(player.LootSearchSequence + 1);
            if (player.LootSearchSequence == 0) player.LootSearchSequence = 1;
        }
    }

    // A real timed consumable or self-treatment produces a short equipment sound.
    public static class RaidConsumableAcoustics {
        public const float HearingRange = 8f;

        public static void Mark(ref PredictedPlayerGhost player) {
            // 真实开始吃喝或治疗时独立增加另一个事件流。音频若发现新序号即可播一次短声。
            player.ConsumableUseSequence = unchecked(player.ConsumableUseSequence + 1);
            if (player.ConsumableUseSequence == 0) player.ConsumableUseSequence = 1;
        }
    }
}