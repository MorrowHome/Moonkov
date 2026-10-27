namespace Unity.MP_FPS
{
    // Both the real player and PMC scavengers produce one authoritative cue.
    public static class RaidLootAcoustics
    {
        public const float HearingRange = 10f;
        public const double MinimumInterval = .8;

        public static void Mark(ref PredictedPlayerGhost player)
        {
            player.LootSearchSequence = unchecked(player.LootSearchSequence + 1);
            if (player.LootSearchSequence == 0) player.LootSearchSequence = 1;
        }
    }

    // A real timed consumable or self-treatment produces a short equipment sound.
    public static class RaidConsumableAcoustics
    {
        public const float HearingRange = 8f;
        public static void Mark(ref PredictedPlayerGhost player)
        {
            player.ConsumableUseSequence = unchecked(player.ConsumableUseSequence + 1);
            if (player.ConsumableUseSequence == 0) player.ConsumableUseSequence = 1;
        }
    }
}
