using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private NavMeshPath m_UprightPath;
        private readonly Vector3[] m_UprightCorners = new Vector3[32];
        private float m_NextUprightPath;
        private int m_UprightCorner, m_UprightCornerCount;
        private Vector3 m_UprightGoal, m_BlockedGroundGoal;
        private float m_BlockedGroundUntil;
        private int m_GroundQueriesLeft;
        private const int GroundQueryBudget = 144;
        public int LastNavigationQueries { get; private set; }
        public int NavigationPlans { get; private set; }

        private Vector3 UprightWaypoint(Vector3 goal)
        {
            // NavMesh positions are feet, while the ghost transform is the elevated body.
            Vector3 foot = transform.position - Vector3.up * m_Body.Motor.BodyHeight;
            if (m_Clock >= m_NextUprightPath)
            {
                NavigationPlans++; m_GroundQueriesLeft = GroundQueryBudget;
                m_NextUprightPath = m_Clock + .75f; m_UprightGoal = goal; m_UprightCornerCount = 0;
                m_UprightCorner = 0;
                if (NavMesh.SamplePosition(foot, out var start, 1.2f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(goal, out var end, 2, NavMesh.AllAreas) && Mathf.Abs(start.position.y - foot.y) < .6f &&
                    Mathf.Abs(end.position.y - goal.y) < 1.5f)
                {
                    m_UprightPath ??= new NavMeshPath();
                    if (NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, m_UprightPath) &&
                        m_UprightPath.status != NavMeshPathStatus.PathInvalid)
                    {
                        m_UprightCornerCount = m_UprightPath.GetCornersNonAlloc(m_UprightCorners);
                        m_UprightCorner = m_UprightCornerCount > 1 ? 1 : 0;
                    }
                }
                bool rememberedBlock = m_Clock < m_BlockedGroundUntil &&
                    Vector3.Distance(goal, m_BlockedGroundGoal) < Mathf.Max(3, profile.GroundBodyRadius(m_Body.Scale) * 2);
                // Verify a bounded prefix in the giant's real capsule, not the humanoid bake.
                // A distant corner becomes a shorter supported waypoint; future prefixes replan.
                if (m_UprightCornerCount > 1 && !rememberedBlock)
                {
                    Vector3 previous = foot; float remaining = 12; int accepted = 0;
                    for (int i = 1; i < m_UprightCornerCount && remaining > .1f; i++)
                    {
                        Vector3 requested = previous + Vector3.ClampMagnitude(m_UprightCorners[i] - previous, remaining);
                        if (!ClearGroundRoute(previous, requested, out var supported) || Vector3.Distance(previous, supported) < .5f) break;
                        remaining -= Vector3.Distance(previous, supported); previous = supported;
                        m_UprightCorners[accepted++] = supported;
                    }
                    m_UprightCornerCount = accepted; m_UprightCorner = 0;
                }
                else m_UprightCornerCount = 0;
                if (m_UprightCornerCount == 0 && !rememberedBlock)
                {
                    Vector3 requested = foot + Vector3.ClampMagnitude(goal - foot, 12);
                    if (ClearGroundRoute(foot, requested, out var groundedGoal))
                    { m_UprightCorners[0] = groundedGoal; m_UprightCornerCount = 1; }
                }
                if (m_UprightCornerCount == 0)
                {
                    if (!rememberedBlock) { m_BlockedGroundGoal = goal; m_BlockedGroundUntil = m_Clock + 3; }
                    // Small, reachable side positions can change the firing angle around a blockage.
                    // Never use an unchecked target vector or teleport to the candidate.
                    Vector3 toward = Vector3.ProjectOnPlane(goal - foot, Vector3.up).normalized;
                    float side = (GhostGameObject.Owner & 1) == 0 ? 1 : -1;
                    for (int i = 0; i < 4 && m_GroundQueriesLeft >= 12; i++)
                    {
                        float angle = (i % 2 == 0 ? side : -side) * (i < 2 ? 65 : 110);
                        Vector3 candidate = foot + Quaternion.AngleAxis(angle, Vector3.up) * toward *
                            Mathf.Max(3, profile.GroundBodyRadius(m_Body.Scale) * 2.5f);
                        if (!ClearGroundRoute(foot, candidate, out var supported)) continue;
                        if (!SpendGroundQuery() || UnityEngine.Physics.Linecast(supported + Vector3.up * m_Body.Motor.BodyHeight,
                            goal + Vector3.up * .5f, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                        m_UprightCorners[0] = supported; m_UprightCornerCount = 1; m_UprightCorner = 0; break;
                    }
                }
                LastNavigationQueries = GroundQueryBudget - m_GroundQueriesLeft;
            }
            while (m_UprightCorner < m_UprightCornerCount &&
                Vector3.ProjectOnPlane(m_UprightCorners[m_UprightCorner] - foot, Vector3.up).sqrMagnitude < .4f &&
                Mathf.Abs(m_UprightCorners[m_UprightCorner].y - foot.y) < .6f) m_UprightCorner++;
            return m_UprightCorner < m_UprightCornerCount ?
                m_UprightCorners[m_UprightCorner] + Vector3.up * m_Body.Motor.BodyHeight : transform.position;
        }

        private bool SpendGroundQuery()
        {
            if (m_GroundQueriesLeft <= 0) return false;
            m_GroundQueriesLeft--; return true;
        }

        private bool ClearGroundRoute(Vector3 foot, Vector3 goal, out Vector3 groundedGoal)
        {
            groundedGoal = foot;
            int mask = LayerMask.GetMask("Default", "Ground");
            float radius = profile.GroundBodyRadius(m_Body.Scale), height = profile.GroundBodyHeight(m_Body.Scale);
            int steps = Mathf.CeilToInt(Vector3.ProjectOnPlane(goal - foot, Vector3.up).magnitude / .75f);
            if (steps > 16) return false;
            Vector3 previous = foot;
            for (int i = 1; i <= Mathf.Max(1, steps); i++)
            {
                Vector3 probe = Vector3.Lerp(foot, goal, i / (float)Mathf.Max(1, steps));
                if (!SpendGroundQuery() || !UnityEngine.Physics.Raycast(probe + Vector3.up * 1.6f, Vector3.down, out var support, 3,
                    mask, QueryTriggerInteraction.Ignore) || support.normal.y < .75f || Mathf.Abs(support.point.y - previous.y) > .4f) return false;
                float clearance = .03f + radius * Mathf.Tan(Vector3.Angle(support.normal, Vector3.up) * Mathf.Deg2Rad);
                Vector3 lower = support.point + Vector3.up * (radius + clearance);
                Vector3 upper = support.point + Vector3.up * (height - radius + clearance);
                Vector3 delta = support.point - previous;
                if (!SpendGroundQuery() || UnityEngine.Physics.CheckCapsule(lower, upper, radius, mask, QueryTriggerInteraction.Ignore) ||
                    delta.sqrMagnitude > .0001f && (!SpendGroundQuery() || UnityEngine.Physics.CapsuleCast(
                        previous + Vector3.up * (radius + clearance), previous + Vector3.up * (height - radius + clearance),
                        radius, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore))) return false;
                previous = support.point;
            }
            groundedGoal = previous;
            return true;
        }
    }
}
