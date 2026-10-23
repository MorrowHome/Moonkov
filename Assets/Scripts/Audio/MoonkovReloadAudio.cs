namespace Unity.MP_FPS
{
    [System.Flags]
    public enum ReloadAudioEvent { None = 0, Start = 1, Stop = 2, Ready = 4 }

    // Rendering can skip a short reload entirely. Its tick and ammo gain still identify completion.
    public sealed class MoonkovReloadAudio
    {
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
