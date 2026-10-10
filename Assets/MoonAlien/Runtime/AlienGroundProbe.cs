using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien
{
    // Queries the owner's physics scene and explicitly limits hits to the sandbox terrain.
    // Saturation fails closed, rather than selecting an incomplete/unordered hit list.
    public sealed class AlienGroundProbe : MonoBehaviour
    {
        [SerializeField] private Transform m_Terrain;
        [SerializeField] private LayerMask m_Mask = ~0;
        [SerializeField, Range(0f, 70f)] private float m_MaxSlope = 50f;
        private readonly RaycastHit[] m_Hits = new RaycastHit[32];
        private readonly Collider[] m_Overlaps = new Collider[32];
        public void Configure(Transform terrain) => m_Terrain = terrain;

        public bool Ground(Vector3 near, out RaycastHit hit, float rise = 1.2f, float drop = 2.4f)
            => Surface(near, Vector3.up, out hit, rise, drop, m_MaxSlope);

        public bool Surface(Vector3 near, Vector3 up, out RaycastHit hit, float rise = 1.2f,
            float drop = 2.4f, float maxAngle = 55f)
        {
            hit = default;
            up = up.normalized;
            if (up.sqrMagnitude < .5f) return false;
            if (!m_Terrain || !gameObject.scene.GetPhysicsScene().IsValid()) return false;
            int count = gameObject.scene.GetPhysicsScene().Raycast(near + up * rise,
                -up, m_Hits, rise + drop, m_Mask, QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length) return false;
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var candidate = m_Hits[i];
                if (!candidate.collider || !candidate.transform.IsChildOf(m_Terrain) || candidate.distance >= nearest) continue;
                nearest = candidate.distance;
                hit = candidate;
                found = true;
            }
            // Reject steep nearest surfaces, never see through them to walkable ground below.
            return found && Vector3.Dot(hit.normal, up) >= Mathf.Cos(maxAngle * Mathf.Deg2Rad);
        }

        public bool Overlaps(Vector3 centre, float radius)
        {
            if (!m_Terrain) return true;
            int count = gameObject.scene.GetPhysicsScene().OverlapSphere(centre, radius,
                m_Overlaps, m_Mask, QueryTriggerInteraction.Ignore);
            if (count == m_Overlaps.Length) return true;
            for (int i = 0; i < count; i++)
                if (m_Overlaps[i] && m_Overlaps[i].transform.IsChildOf(m_Terrain)) return true;
            return false;
        }

        public bool Obstructed(Vector3 from, Vector3 to, float radius)
        {
            Vector3 delta = to - from;
            if (!m_Terrain || delta.sqrMagnitude < .000001f) return !m_Terrain;
            int count = gameObject.scene.GetPhysicsScene().SphereCast(from, radius, delta.normalized,
                m_Hits, delta.magnitude, m_Mask, QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length) return true;
            for (int i = 0; i < count; i++)
                if (m_Hits[i].collider && m_Hits[i].transform.IsChildOf(m_Terrain)) return true;
            return false;
        }
    }
}
