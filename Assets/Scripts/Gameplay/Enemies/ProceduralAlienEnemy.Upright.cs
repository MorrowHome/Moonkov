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
            // Ground paths belong to the humanoid NavMesh; the spider surface graph
            // deliberately includes walls and ceilings. Full body clearance remains
            // authoritative in the upright capsule motor at every step.
            if (m_Clock >= m_NextUprightPath || Vector3.Distance(goal, m_UprightGoal) > 2)
            {
                m_NextUprightPath = m_Clock + 1; m_UprightGoal = goal; m_UprightCornerCount = 0;
                if (NavMesh.SamplePosition(transform.position, out var start, 2, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(goal, out var end, 2, NavMesh.AllAreas))
                {
                    m_UprightPath ??= new NavMeshPath();
                    if (NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, m_UprightPath) &&
                        m_UprightPath.status == NavMeshPathStatus.PathComplete)
                    {
                        m_UprightCornerCount = m_UprightPath.GetCornersNonAlloc(m_UprightCorners);
                        m_UprightCorner = m_UprightCornerCount > 1 ? 1 : 0;
                    }
                }
            }
            while (m_UprightCorner + 1 < m_UprightCornerCount &&
                Vector3.ProjectOnPlane(m_UprightCorners[m_UprightCorner] - transform.position, Vector3.up).sqrMagnitude < .4f)
                m_UprightCorner++;
            return m_UprightCorner < m_UprightCornerCount ? m_UprightCorners[m_UprightCorner] : goal;
        }
    }
}
