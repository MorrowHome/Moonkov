using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    // Four bounded bytes travel as continuous command/snapshot state. Zero keeps prefab defaults.
    public static class HaloLighting
    {
        public const float DefaultRange = 8f, MinRange = 1f, MaxRange = 24f;
        public const int MaxPercent = 800;
        private const int LegacyMaxPercent = 200, ExtendedPercentStep = 25;
        public const float MinAimRange = 5f, MaxAimRange = 160f;
        public static float AimRange(uint settings, float fallback = 80f) =>
            (settings >> 24) == 0 ? fallback : Mathf.Clamp(settings >> 24, MinAimRange, MaxAimRange);
        public static float Range(uint settings, float fallback = DefaultRange) =>
            (settings & 255u) == 0 ? fallback : Mathf.Clamp(settings & 255u, MinRange * 10, MaxRange * 10) / 10f;
        public static int IdlePercent(uint settings) => Percent(settings >> 8);
        public static int AimPercent(uint settings) => Percent(settings >> 16);
        // Keep saved 0–200% values byte-for-byte. Unused codes extend the range without widening the command/snapshot field.
        private static int Percent(uint value)
        {
            int code = (int)(value & 255u);
            if (code == 0) return 100;
            return code <= LegacyMaxPercent + 1 ? code - 1 :
                Mathf.Min(LegacyMaxPercent + (code - LegacyMaxPercent - 1) * ExtendedPercentStep, MaxPercent);
        }
        private static uint PackPercent(int percent)
        {
            percent = Mathf.Clamp(percent, 0, MaxPercent);
            return (uint)(percent <= LegacyMaxPercent ? percent + 1 :
                LegacyMaxPercent + 1 + Mathf.RoundToInt((percent - LegacyMaxPercent) / (float)ExtendedPercentStep));
        }
        public static uint Pack(float range, int idlePercent, int aimPercent, float aimRange = 80f) =>
            (uint)Mathf.RoundToInt(Mathf.Clamp(float.IsNaN(range) || float.IsInfinity(range) ? DefaultRange : range, MinRange, MaxRange) * 10f) |
            (PackPercent(idlePercent) << 8) |
            (PackPercent(aimPercent) << 16) |
            ((uint)Mathf.RoundToInt(Mathf.Clamp(float.IsNaN(aimRange) || float.IsInfinity(aimRange) ? 80f : aimRange, MinAimRange, MaxAimRange)) << 24);
        public static uint Sanitize(uint settings) => settings == 0 ? 0 : Pack(Range(settings), IdlePercent(settings), AimPercent(settings), AimRange(settings));
    }

    // Uses unscaled time; cancellation never becomes a short press.
    public struct HaloLightPress
    {
        public const float HoldSeconds = .3f;
        private bool pending, adjusting;
        private float started;
        public bool Adjusting => adjusting;
        public bool Step(bool pressed, bool held, float now, bool allowed)
        {
            if (!allowed) { Cancel(); return false; }
            if (pressed) { pending = true; started = now; }
            if (!pending) return false;
            if (!held)
            {
                bool toggle = !adjusting && now - started < HoldSeconds;
                Cancel(); return toggle;
            }
            if (now - started >= HoldSeconds) adjusting = true;
            return false;
        }
        public void Cancel() { pending = adjusting = false; }
    }
}
