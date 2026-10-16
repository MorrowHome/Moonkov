using UnityEngine;

// Shared gain envelope, applied by the existing pooled SoundGameObjects. No extra mixer graph or sources.
public static class MoonkovSoundMix
{
    private static float s_WeaponUntil = -1;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => s_WeaponUntil = -1;
    public static void NotifyWeapon() => s_WeaponUntil = Time.unscaledTime + .08f;
    public static float Gain(SoundDef.MixRole role)
    {
        float recovery = Mathf.Clamp01((Time.unscaledTime - s_WeaponUntil) / .35f);
        if (role == SoundDef.MixRole.Ambience) return Mathf.Lerp(.38f, 1, recovery);
        if (role == SoundDef.MixRole.Foley) return Mathf.Lerp(.78f, 1, recovery);
        return 1; // Creature telegraphs and suit warnings remain readable during combat.
    }
}
