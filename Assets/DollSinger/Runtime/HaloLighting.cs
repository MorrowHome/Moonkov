using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    // Four bounded bytes travel as continuous command/snapshot state. Zero keeps prefab defaults.
    public static class HaloLighting
    {
        public const float MinRange = 1f, MaxRange = 12f;
        public const int MaxPercent = 200;
        public const float MinAimRange = 5f, MaxAimRange = 160f;
        public static float AimRange(uint settings, float fallback = 80f) =>
            (settings >> 24) == 0 ? fallback : Mathf.Clamp(settings >> 24, MinAimRange, MaxAimRange);
        public static float Range(uint settings, float fallback = 3f) =>
            (settings & 255u) == 0 ? fallback : Mathf.Clamp(settings & 255u, 10f, 120f) / 10f;
        public static int IdlePercent(uint settings) => Percent(settings >> 8);
        public static int AimPercent(uint settings) => Percent(settings >> 16);
        private static int Percent(uint value) => (value & 255u) == 0 ? 100 : Mathf.Min((int)(value & 255u) - 1, MaxPercent);
        public static uint Pack(float range, int idlePercent, int aimPercent, float aimRange = 80f) =>
            (uint)Mathf.RoundToInt(Mathf.Clamp(float.IsNaN(range) || float.IsInfinity(range) ? 3f : range, MinRange, MaxRange) * 10f) |
            ((uint)(Mathf.Clamp(idlePercent, 0, MaxPercent) + 1) << 8) |
            ((uint)(Mathf.Clamp(aimPercent, 0, MaxPercent) + 1) << 16) |
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
