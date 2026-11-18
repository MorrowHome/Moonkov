// AI 从脚步、落地和咳嗽获得线索时的计算。姿势、速度和负重影响可听距离，障碍压低范围，异形只拿到带偏差的位置。
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS {
    // 移动声音只提供附近的大致线索。是否看得见目标或能否开火，
    // 仍由听者自己的感知判断。
    public static class RaidFootsteps {
        public const float StepDistance = 2.2f;
        public const float ObstructionMultiplier = .55f;
        public const float AlienMovementRangeScale = .6f;

        // 站姿、蹲、趴和踮脚有不同的基础动静，再乘负重。玩法可以关掉重量影响。
        public static float HearingRange(
            float baseRange,
            DollSingerStance stance,
            bool fast,
            float weight,
            bool clash) {
            if (!float.IsFinite(baseRange) || baseRange <= 0) return 0;
            float posture = stance == DollSingerStance.Tiptoe ? .25f :
                stance == DollSingerStance.Crouching ? .45f :
                stance == DollSingerStance.Prone ? .3f :
                fast ? 1.3f : .65f;
            return baseRange * posture * LoadMultiplier(weight, clash);
        }

        public static float LoadMultiplier(float weight, bool clash) {
            return 1 + .25f * (clash ? 0 : RaidEncumbrance.Burden(weight));
        }

        public static float LandingRange(float baseRange, float weight, bool clash) {
            return float.IsFinite(baseRange) && baseRange > 0 ? baseRange * LoadMultiplier(weight, clash) : 0;
        }

        // 同一位置只在有效听距内才形成线索。墙挡着时缩短听距，音源没变成 AI 的可见目标。
        public static bool Audible(float distance, float range, bool blocked) {
            return float.IsFinite(distance) && float.IsFinite(range) && distance >= 0 && range > 0 &&
                   distance <= range * (blocked ? ObstructionMultiplier : 1);
        }
    }

    // 咳嗽作为离散的近处声响播放。短时间避开影响后再进来时
    // 沿用原间隔，不能连续触发额外的咳声。
    public static class RaidCoughSound {
        public const float Range = 25;
        public const double Interval = 3.2;

        // 持续呛咳只按间隔发离散事件。短暂离开影响区再回来也不会瞬间多咳几次。
        public static bool TryEmit(ref double next, double now, float remaining, bool clash) {
            if (clash || !double.IsFinite(now) || !float.IsFinite(remaining) || remaining <= 0 || now < next)
                return false;
            next = now + Interval;
            return true;
        }
    }

    // 异形听觉只给搜索线索。按大致方向保留稳定偏差，
    // 避免许多发连续枪声平均后变成藏匿角色的精确坐标。
    public static class RaidAlienHearing {
        // 听到动静后给 AI 一个带误差的搜寻位置。误差由听者、目标、距离及遮挡决定，连续声音不会平均出精确藏身点。
        public static Vector3 Estimate(
            Vector3 source,
            float distance,
            float range,
            bool blocked,
            int listener,
            int target) {
            float angle = (listener * 137.5f + target * 67f) * Mathf.Deg2Rad;
            float error = Mathf.Lerp(1.5f, 4, distance / Mathf.Max(.1f, range)) + (blocked ? 2 : 0);
            return source + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * error;
        }
    }
}