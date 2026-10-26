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
}
