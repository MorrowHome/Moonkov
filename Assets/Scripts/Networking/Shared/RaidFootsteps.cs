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
            float.IsFinite(distance) && distance >= 0 && distance <= range * (blocked ? ObstructionMultiplier : 1);
    }
}
