namespace Unity.MP_FPS
{
    // Sustained suffering is a slow state cue, never an audio event for every damage tick.
    public sealed class MoonkovDistressAudio
    {
        public const float FirstDelay = 3.5f, RepeatDelay = 9f;
        public bool Active { get; private set; }
        private float m_Next;
        public static bool PainfulStatus(in PredictedPlayerGhost state)
        {
            const SurvivalEffects painful = SurvivalEffects.Hypoxia | SurvivalEffects.Starving |
                SurvivalEffects.Dehydrated | SurvivalEffects.ArmDisabled | SurvivalEffects.LegDisabled |
                SurvivalEffects.AbdomenDisabled;
            return (RaidNutrition.Effects(state) & painful) != 0 ||
                state.TacticalBleeding || state.TacticalPoisonSeconds > 0 || state.TacticalWireSeconds > 0;
        }
        public bool Observe(float healthFraction, bool alive, bool painfulStatus, bool hurtCry, float now)
        {
            bool active = alive && (painfulStatus || healthFraction <= (Active ? .35f : .28f));
            if (!active) { Reset(); return false; }
            if (!Active) { Active = true; m_Next = now + FirstDelay; }
            // Let the short impact cry finish without silencing an entire short bleeding episode.
            if (hurtCry) m_Next = now + FirstDelay;
            if (now < m_Next) return false;
            m_Next = now + RepeatDelay;
            return true;
        }
        public void Reset() { Active = false; m_Next = 0; }
    }
}
