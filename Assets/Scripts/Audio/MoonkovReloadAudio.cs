namespace Unity.MP_FPS
{
    [System.Flags]
    public enum ReloadAudioEvent { None = 0, Start = 1, Stop = 2, Ready = 4 }

    public enum ReloadSoundPhase { None, Open, Feed, Lock }

    // Rendering can skip a short reload entirely. Its tick and ammo gain still identify completion.
    public sealed class MoonkovReloadAudio
    {
        private bool m_PhaseInitialized;
        private uint m_PhaseTick, m_PhaseWeapon;
        private int m_PhaseIndex;

        // Observe the server's timeline, outside prediction replay. A skipped frame
        // plays only the latest mechanism; it never dumps missed pulses at once.
        public ReloadSoundPhase ObservePhase(in PredictedPlayerGhost state, WeaponData weapon)
        {
            // Ignore an older snapshot without rewinding the observed sequence.
            if (m_PhaseInitialized && state.LastReloadTick != m_PhaseTick && m_PhaseTick != 0 &&
                unchecked((int)(state.LastReloadTick - m_PhaseTick)) <= 0) return ReloadSoundPhase.None;
            int pulses = UnityEngine.Mathf.Clamp(state.ReloadTargetAmmo - state.CurrentAmmo, 1, 6);
            float progress = HaloReload.Progress(state, weapon);
            int index = !state.ControllerState.IsReloadingState || state.CurrentHealth <= 0 ? -1 :
                progress < .18f ? 0 : progress >= .84f ? pulses + 1 :
                1 + UnityEngine.Mathf.Min(pulses - 1, UnityEngine.Mathf.FloorToInt((progress - .18f) / .66f * pulses));
            bool sameReload = state.LastReloadTick == m_PhaseTick && state.EquippedWeaponID == m_PhaseWeapon;
            bool changed = m_PhaseInitialized && index >= 0 &&
                (sameReload ? index > m_PhaseIndex : state.LastReloadTick != 0 && state.LastReloadTick != m_PhaseTick);
            // Retain a high-water mark through repeated/corrected snapshots.
            m_PhaseIndex = sameReload ? UnityEngine.Mathf.Max(m_PhaseIndex, index) : index;
            m_PhaseInitialized = true; m_PhaseTick = state.LastReloadTick; m_PhaseWeapon = state.EquippedWeaponID;
            return !changed ? ReloadSoundPhase.None : index == 0 ? ReloadSoundPhase.Open :
                index == pulses + 1 ? ReloadSoundPhase.Lock : ReloadSoundPhase.Feed;
        }

        public static UnityEngine.Vector2 Window(SoundDef sound, ReloadSoundPhase phase)
        {
            float length = sound && sound.Clips != null && sound.Clips.Count > 0 && sound.Clips[0] ? sound.Clips[0].length : 1;
            // These banks place recorded gestures at 3%, 28%, 57% and 84%.
            // Preserve the recording's pitch when an injury/module changes timing.
            return phase switch
            {
                ReloadSoundPhase.Open => new UnityEngine.Vector2(0, UnityEngine.Mathf.Min(.28f, length * .24f)),
                ReloadSoundPhase.Feed => new UnityEngine.Vector2(.27f, UnityEngine.Mathf.Min(.2f, length * .22f)),
                ReloadSoundPhase.Lock => new UnityEngine.Vector2(.83f, length * .17f),
                _ => UnityEngine.Vector2.zero
            };
        }

        // All variants in a bank have equal duration. Speed follows authoritative remaining time.
        public static float PitchForDuration(SoundDef sound, float remaining)
        {
            if (!sound || sound.Clips == null || sound.Clips.Count == 0 || !sound.Clips[0] ||
                !float.IsFinite(remaining) || remaining <= .05f) return 0;
            float rate = UnityEngine.Mathf.Clamp(sound.Clips[0].length / remaining, .25f, 3f);
            return 1200 * UnityEngine.Mathf.Log(rate, 2);
        }
        private bool m_Initialized, m_Reloading;
        private uint m_Tick, m_Weapon;
        private int m_Ammo;
        public ReloadAudioEvent Observe(uint tick, uint weapon, int ammo, bool reloading, bool alive)
        {
            var result = ReloadAudioEvent.None;
            if (m_Initialized)
            {
                bool started = MoonkovPlayerAudio.NewTick(tick, ref m_Tick);
                bool same = weapon == m_Weapon;
                if (started || !same || !alive || m_Reloading && !reloading) result |= ReloadAudioEvent.Stop;
                if (started && reloading && alive) result |= ReloadAudioEvent.Start;
                if (same && alive && (m_Reloading || started) && !reloading && ammo > m_Ammo)
                    result |= ReloadAudioEvent.Ready;
            }
            else { m_Initialized = true; m_Tick = tick; }
            m_Weapon = weapon; m_Ammo = ammo; m_Reloading = reloading;
            return result;
        }
    }
}
