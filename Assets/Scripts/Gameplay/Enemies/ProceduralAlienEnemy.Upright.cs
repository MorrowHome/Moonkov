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
        private Vector3 m_UprightGoal;

        private Vector3 UprightWaypoint(Vector3 goal)
        {
            // NavMesh positions are feet, while the ghost transform is the elevated body.
            Vector3 foot = transform.position - Vector3.up * m_Body.Motor.BodyHeight;
            if (m_Clock >= m_NextUprightPath || Vector3.Distance(goal, m_UprightGoal) > 2)
            {
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
                // Unbaked open terrain is allowed only with continuous ground and full-body
                // clearance. Missing/partial routes never become an unchecked wall vector.
                if (m_UprightCornerCount == 0 && ClearGroundRoute(foot, goal, out var groundedGoal))
                { m_UprightCorners[0] = groundedGoal; m_UprightCornerCount = 1; }
            }
            while (m_UprightCorner < m_UprightCornerCount &&
                Vector3.ProjectOnPlane(m_UprightCorners[m_UprightCorner] - foot, Vector3.up).sqrMagnitude < .4f &&
                Mathf.Abs(m_UprightCorners[m_UprightCorner].y - foot.y) < .6f) m_UprightCorner++;
            return m_UprightCorner < m_UprightCornerCount ?
                m_UprightCorners[m_UprightCorner] + Vector3.up * m_Body.Motor.BodyHeight : transform.position;
        }

        private bool ClearGroundRoute(Vector3 foot, Vector3 goal, out Vector3 groundedGoal)
        {
            groundedGoal = foot;
            int mask = LayerMask.GetMask("Default", "Ground");
            float radius = profile.GroundBodyRadius(m_Body.Scale), height = profile.GroundBodyHeight(m_Body.Scale);
            int steps = Mathf.CeilToInt(Vector3.ProjectOnPlane(goal - foot, Vector3.up).magnitude / .75f);
            if (steps > 96 || Mathf.Abs(goal.y - foot.y) > 1.5f) return false;
            Vector3 previous = foot;
            for (int i = 1; i <= Mathf.Max(1, steps); i++)
            {
                Vector3 probe = Vector3.Lerp(foot, goal, i / (float)Mathf.Max(1, steps));
                if (!UnityEngine.Physics.Raycast(probe + Vector3.up * 1.6f, Vector3.down, out var support, 3,
                    mask, QueryTriggerInteraction.Ignore) || support.normal.y < .75f || Mathf.Abs(support.point.y - previous.y) > .4f) return false;
                float clearance = .03f + radius * Mathf.Tan(Vector3.Angle(support.normal, Vector3.up) * Mathf.Deg2Rad);
                Vector3 lower = support.point + Vector3.up * (radius + clearance);
                Vector3 upper = support.point + Vector3.up * (height - radius + clearance);
                Vector3 delta = support.point - previous;
                if (UnityEngine.Physics.CheckCapsule(lower, upper, radius, mask, QueryTriggerInteraction.Ignore) ||
                    delta.sqrMagnitude > .0001f && UnityEngine.Physics.CapsuleCast(
                        previous + Vector3.up * (radius + clearance), previous + Vector3.up * (height - radius + clearance),
                        radius, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore)) return false;
                previous = support.point;
            }
            groundedGoal = previous;
            return true;
        }
    }
}
