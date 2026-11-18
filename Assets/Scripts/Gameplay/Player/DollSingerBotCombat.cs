// 给机器人共用的短横移和火线几何检查。用 NavMesh 确认可落脚的一侧，用场景射线和烟雾数据知道枪线或视线是否被挡。
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS {
    internal static class DollSingerBotCombat {
        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");

        public static Vector3 SideStep(
            Vector3 position,
            Vector3 toward,
            float side,
            bool backAway,
            NavMeshQueryFilter filter) {
            toward.y = 0;
            if (toward.sqrMagnitude < .01f) return Vector3.zero;
            toward.Normalize();
            // 用目标方向的垂线算左或右，退开时再掺入少量后退量。
            var step = Vector3.Cross(Vector3.up, toward) * side;
            if (backAway) step -= toward * .5f;
            step.Normalize();
            // 首选侧不通时试另一侧。两边都没导航面就停步，别往墙或平台之外移动。
            if (CanStep(position, step, filter)) return step;
            if (backAway)
                step = -Vector3.Cross(Vector3.up, toward) * side - toward * .5f;
            else
                step = -step;
            step.Normalize();
            return CanStep(position, step, filter) ? step : Vector3.zero;
        }

        public static bool CanStep(Vector3 position, Vector3 direction, NavMeshQueryFilter filter) {
            return NavMesh.SamplePosition(position, out var from, .85f, filter) &&
                   !NavMesh.Raycast(from.position, from.position + direction * .9f, out _, filter);
        }

        public static bool WallBetween(PhysicsScene scene, Vector3 from, Vector3 to) {
            var delta = to - from;
            float distance = delta.magnitude;
            return distance > .05f && scene.Raycast(from, delta / distance, distance, s_WorldMask,
                QueryTriggerInteraction.Ignore);
        }

        // 烟雾可能挡住目视但不会挡子弹，实墙两样都挡。需要判断看不看得见时同时问两者。
        public static bool Obscured(PhysicsScene scene, TacticalServerWorld tactical, Vector3 from, Vector3 to) {
            return WallBetween(scene, from, to) || SmokeVisibility.Blocked(tactical, scene, from, to);
        }
    }
}