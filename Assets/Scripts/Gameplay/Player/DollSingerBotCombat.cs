using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS
{
    internal static class DollSingerBotCombat
    {
        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");

        public static Vector3 SideStep(Vector3 position, Vector3 toward, float side, bool backAway, NavMeshQueryFilter filter)
        {
            toward.y = 0;
            if (toward.sqrMagnitude < .01f) return Vector3.zero;
            toward.Normalize();
            Vector3 step = Vector3.Cross(Vector3.up, toward) * side;
            if (backAway) step -= toward * .5f;
            step.Normalize();
            if (CanStep(position, step, filter)) return step;
            if (backAway) step = -Vector3.Cross(Vector3.up, toward) * side - toward * .5f;
            else step = -step;
            step.Normalize();
            return CanStep(position, step, filter) ? step : Vector3.zero;
        }

        public static bool CanStep(Vector3 position, Vector3 direction, NavMeshQueryFilter filter) =>
            NavMesh.SamplePosition(position, out var from, .85f, filter) &&
            !NavMesh.Raycast(from.position, from.position + direction * .9f, out _, filter);

        public static bool WallBetween(PhysicsScene scene, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            return distance > .05f && scene.Raycast(from, delta / distance, distance, s_WorldMask, QueryTriggerInteraction.Ignore);
        }

        public static bool Obscured(PhysicsScene scene, TacticalServerWorld tactical, Vector3 from, Vector3 to) =>
            WallBetween(scene, from, to) || SmokeVisibility.Blocked(tactical, scene, from, to);
    }
}
