using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    // Authored acceptance course, not autonomous wall navigation or a server motor.
    [RequireComponent(typeof(AlienGroundProbe))]
    public sealed class AlienAdhesionRoute : MonoBehaviour
    {
        [System.Serializable]
        public struct Pose
        {
            public Vector3 Centre, Up;
            public Pose(Vector3 centre, Vector3 up) { Centre = centre; Up = up; }
        }
        [SerializeField] private Transform m_Course;
        [SerializeField] private Pose[] m_Route;
        [SerializeField, Range(.1f, 6f)] private float m_Speed = 1.5f;
        [SerializeField, Range(15f, 240f)] private float m_TurnRate = 90f;
        [SerializeField] private bool m_Running = true;
        private AlienGroundProbe m_Probe;
        private int m_Index = 1;
        private Vector3 m_Centre;
        private bool m_Ready, m_FaceTravel;
        public bool IsComplete => m_Route != null && m_Index >= m_Route.Length;
        public bool IsBlocked { get; private set; }
        public int CurrentWaypoint => m_Index;
        public Vector3 Up => transform.up;
        public Vector3 Centre => m_Centre;
        public string Status { get; private set; } = "Awaiting supported pose";
        public Collider Contact { get; private set; }
        public Vector3 ContactPoint { get; private set; }
        public float ContactConfidence { get; private set; }
        public const float BodyOffset = .66f;
        // Circumscribes the small visible shell; rotation cannot enlarge a sphere.
        public const float ClearanceRadius = .53f;

        public void Configure(Transform course, Pose[] route) { m_Course = course; m_Route = route; }
        public void SetRunning(bool value) => m_Running = value;
        public void SetFaceTravel(bool value) => m_FaceTravel = value;
        public void ReplaceRoute(Pose[] route)
        {
            m_Route = route; m_Index = 1; IsBlocked = false; m_Running = true;
            // Deliberately retain the accepted centre/frame. Replanning never teleports.
        }
        public void SetFast(bool fast) => m_Speed = fast ? 6f : 1.5f;
        private void OnEnable() { m_Ready = false; }

        private void Start() { m_Probe = GetComponent<AlienGroundProbe>(); }

        private void Update()
        {
            if (!m_Probe || !m_Course || m_Route == null || m_Route.Length < 2) return;
            if (!m_Ready)
            {
                // On re-enable retain current pose. The builder alone places the initial spawn.
                m_Centre = transform.position + transform.up * BodyOffset;
                m_Ready = true;
            }
            if (!m_Running) { Status = "Paused at supported pose"; return; }
            float dt = Mathf.Min(Time.deltaTime, .033333f);
            if (dt <= 0f) return;
            if (m_Index >= m_Route.Length) { Status = "Course finished; no teleport/restart"; return; }
            Vector3 target = m_Course.TransformPoint(m_Route[m_Index].Centre);
            Vector3 targetUp = m_Course.TransformDirection(m_Route[m_Index].Up).normalized;
            if (targetUp.sqrMagnitude < .5f || (transform.lossyScale - Vector3.one).sqrMagnitude > .0001f)
            { Hold("Invalid normal or non-unit rig scale"); return; }
            float distanceToTarget = Vector3.Distance(m_Centre, target);
            float angleToTarget = Vector3.Angle(Up, targetUp);
            float fraction = Mathf.Min(1f, Mathf.Min(m_Speed * dt / Mathf.Max(.00001f, distanceToTarget),
                m_TurnRate * dt / Mathf.Max(.00001f, angleToTarget)));
            // Advance position and frame by the SAME fraction. Rotating first at a convex
            // edge can point the support ray into empty space and strand the creature.
            Vector3 up = Vector3.Slerp(Up, targetUp, fraction).normalized;
            Quaternion rotation = AlienSurfaceFrame.Transport(transform.rotation, up);
            if (m_FaceTravel)
            {
                Vector3 tangent = Vector3.ProjectOnPlane(target - m_Centre, up);
                if (tangent.sqrMagnitude > .001f)
                {
                    Vector3 heading = Vector3.RotateTowards(rotation * Vector3.forward, tangent.normalized,
                        m_TurnRate * Mathf.Deg2Rad * dt, 0f);
                    rotation = Quaternion.LookRotation(heading, up);
                }
            }
            Vector3 centre = Vector3.Lerp(m_Centre, target, fraction);
            float normalRemaining = Vector3.Angle(up, targetUp);
            if (!FindSupport(centre, rotation, out var support))
            { Hold("No support: hold, do not bridge gap"); return; }
            float distance = Vector3.Distance(centre, support.point);
            if (distance < ClearanceRadius || distance > 1.15f)
            { Hold("Grip/clearance distance rejected"); return; }
            // Check both swept translation and endpoint overlap even for an in-place rotation.
            // Decorative legs are not an authoritative collision hull.
            if (m_Probe.Overlaps(m_Centre, ClearanceRadius) ||
                m_Probe.Obstructed(m_Centre, centre, ClearanceRadius) || m_Probe.Overlaps(centre, ClearanceRadius))
            { Hold("Body clearance blocked"); return; }
            Vector3 nextRoot = centre - up * BodyOffset;
            m_Centre = centre;
            transform.SetPositionAndRotation(nextRoot, rotation);
            Contact = support.collider;
            ContactPoint = support.point;
            ContactConfidence = Mathf.MoveTowards(ContactConfidence, 1f, dt * 4f);
            IsBlocked = false;
            Status = "Supported authored adhesion pose";
            if (Vector3.Distance(centre, target) < .005f && normalRemaining < .5f) m_Index++;
        }

        private void OnDrawGizmosSelected()
        {
            if (!m_Course || m_Route == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < m_Route.Length; i++)
            {
                Vector3 centre = m_Course.TransformPoint(m_Route[i].Centre);
                Gizmos.DrawWireSphere(centre, .06f);
                Gizmos.DrawLine(centre, centre + m_Course.TransformDirection(m_Route[i].Up) * .5f);
                if (i > 0) Gizmos.DrawLine(m_Course.TransformPoint(m_Route[i-1].Centre), centre);
            }
            if (!Application.isPlaying || !m_Ready) return;
            Gizmos.color = ContactConfidence > 0f ? Color.green : Color.red;
            Gizmos.DrawWireSphere(m_Centre, ClearanceRadius);
            if (Contact) Gizmos.DrawLine(m_Centre, ContactPoint);
        }

        private bool FindSupport(Vector3 centre, Quaternion frame, out RaycastHit support)
        {
            support = default;
            Vector3 up = frame * Vector3.up;
            float nearest = float.PositiveInfinity;
            bool found = false;
            // A single radial ray can graze past an exact convex edge. This bounded local
            // footprint verifies adjacent physical faces without expanding the grip range.
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = i == 0 ? Vector3.zero :
                    (frame * (i < 3 ? Vector3.forward : Vector3.right)) * (i % 2 == 0 ? -.12f : .12f);
                if (!m_Probe.Surface(centre + offset, up, out var hit, .1f, 1.25f, 55f)) continue;
                float distance = Vector3.Distance(centre, hit.point);
                if (distance < ClearanceRadius || distance > 1.15f || distance >= nearest) continue;
                nearest = distance;
                support = hit;
                found = true;
            }
            return found;
        }

        private void Hold(string reason)
        {
            Status = reason;
            IsBlocked = true;
            ContactConfidence = 0f;
            // Preserve last validated centre and frame. No automatic warp or world-Y fall.
        }
    }
}
