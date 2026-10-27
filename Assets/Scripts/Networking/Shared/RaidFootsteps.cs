using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    // A movement clue is local and imprecise. It does not grant line of sight
    // or permission to shoot; the listener's existing perception owns that.
    public static class RaidFootsteps
    {
        public const float StepDistance = 2.2f;
        public const float ObstructionMultiplier = .55f;
        public const float AlienMovementRangeScale = .6f;

        public static float HearingRange(float baseRange, DollSingerStance stance, bool fast, float weight, bool clash)
        {
            if (!float.IsFinite(baseRange) || baseRange <= 0) return 0;
            float posture = stance == DollSingerStance.Tiptoe ? .25f :
                stance == DollSingerStance.Crouching ? .45f : stance == DollSingerStance.Prone ? .3f : fast ? 1.3f : .65f;
            return baseRange * posture * LoadMultiplier(weight, clash);
        }

        public static float LoadMultiplier(float weight, bool clash) =>
            1 + .25f * (clash ? 0 : RaidEncumbrance.Burden(weight));

        public static float LandingRange(float baseRange, float weight, bool clash) =>
            float.IsFinite(baseRange) && baseRange > 0 ? baseRange * LoadMultiplier(weight, clash) : 0;

        public static bool Audible(float distance, float range, bool blocked) =>
            float.IsFinite(distance) && float.IsFinite(range) && distance >= 0 && range > 0 &&
            distance <= range * (blocked ? ObstructionMultiplier : 1);
    }

    // Coughs are discrete, local sounds. Keep the cadence while a short break
    // in exposure occurs so leaving and re-entering cannot spam extra events.
    public static class RaidCoughSound
    {
        public const float Range = 25;
        public const double Interval = 3.2;
        public static bool TryEmit(ref double next, double now, float remaining, bool clash)
        {
            if (clash || !double.IsFinite(now) || !float.IsFinite(remaining) || remaining <= 0 || now < next) return false;
            next = now + Interval;
            return true;
        }
    }

    // Alien hearing produces a search clue, not sight. A stable bearing keeps a
    // burst of shots from averaging into the exact position of a hidden actor.
    public static class RaidAlienHearing
    {
        public static Vector3 Estimate(Vector3 source, float distance, float range, bool blocked, int listener, int target)
        {
            float angle = (listener * 137.5f + target * 67f) * Mathf.Deg2Rad;
            float error = Mathf.Lerp(1.5f, 4, distance / Mathf.Max(.1f, range)) + (blocked ? 2 : 0);
            return source + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * error;
        }
    }
}
