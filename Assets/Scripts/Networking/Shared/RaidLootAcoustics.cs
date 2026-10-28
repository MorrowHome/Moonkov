namespace Unity.MP_FPS
{
    // Opening and searching use the same authoritative hearing and playback events.
    public static class RaidLootAcoustics
    {
        public const float HearingRange = 10f;
        public const double MinimumInterval = .8;
        public const double SearchInterval = 1.2;

        public static void RefreshSearch(ref PredictedPlayerGhost player,bool searching,double now,ref double nextSoundAt)
        {
            player.LootSearching=searching && player.CurrentHealth>0;
            if(!player.LootSearching || now<nextSoundAt)return;
            Mark(ref player);nextSoundAt=now+SearchInterval;
        }

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
