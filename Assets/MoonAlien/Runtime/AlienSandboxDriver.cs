using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    // Deliberately NOT a gameplay or network motor. Drives a reproducible ground fixture only.
    [RequireComponent(typeof(AlienGroundProbe))]
    public sealed class AlienSandboxDriver : MonoBehaviour
    {
        [SerializeField] private float m_Travel = 24f;
        [SerializeField, Range(0f, 6f)] private float m_MaxSpeed = 6f;
        [SerializeField] private bool m_Run = true;
        private AlienGroundProbe m_Ground;
        private Vector3 m_Start;
        private float m_Speed, m_Pause = 1f, m_Yaw, m_Time;
        private int m_Direction = 1;
        public string Status { get; private set; } = "Starting";
        public void SetRunning(bool value) => m_Run = value;

        private void Start() { m_Ground = GetComponent<AlienGroundProbe>(); m_Start = transform.position; m_Yaw = transform.eulerAngles.y; }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f);
            if (dt <= 0 || !m_Run) { m_Speed = 0f; Status = "Paused"; return; }
            m_Time += dt;
            if (m_Pause > 0f) { m_Pause -= dt; m_Speed = 0f; Status = "Stop / planted feet"; return; }
            float targetYaw = m_Direction > 0 ? 0f : 180f;
            m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, targetYaw, 180f * dt);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            if (Mathf.Abs(Mathf.DeltaAngle(m_Yaw, targetYaw)) > .1f) { m_Speed = 0f; Status = "Turn in place"; return; }
            float targetSpeed = m_Time % 8f < 3f ? Mathf.Min(1.6f, m_MaxSpeed) : m_MaxSpeed;
            m_Speed = Mathf.MoveTowards(m_Speed, targetSpeed, 5f * dt);
            Vector3 next = transform.position + transform.forward * (m_Speed * dt);
            bool atEnd = m_Direction > 0 ? next.z > m_Start.z + m_Travel : next.z < m_Start.z;
            if (atEnd) { Turn("Route end"); return; }
            if (!m_Ground.Ground(next, out var hit, .65f, 1f) || Mathf.Abs(hit.point.y - transform.position.y) > .32f)
            { Turn("Unsupported / high step"); return; }
            // Probe forward support before approaching the drop; never snap across a gap.
            if (!m_Ground.Ground(next + transform.forward * .9f, out var ahead, .8f, 1f) ||
                Mathf.Abs(ahead.point.y - hit.point.y) > .45f)
            { Turn("Ledge guard"); return; }
            next.y = hit.point.y;
            if (m_Ground.Obstructed(transform.position + Vector3.up * .7f, next + Vector3.up * .7f, .25f))
            { Turn("Body obstruction"); return; }
            transform.position = next;
            Status = targetSpeed > 2f ? "Burst (ground-only)" : "Stalk (ground-only)";
        }

        private void Turn(string reason) { m_Direction *= -1; m_Pause = 1.1f; m_Speed = 0f; Status = reason; }
    }
}
