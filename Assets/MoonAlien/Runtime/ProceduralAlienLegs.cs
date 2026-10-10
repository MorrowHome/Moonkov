using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    [DisallowMultipleComponent]
    public sealed class ProceduralAlienLegs : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Leg
        {
            public Transform Upper, Lower, Toe;
            public Vector3 Hip, RestFoot;
            [System.NonSerialized] public Vector3 Position, From, Target, Normal, SwingUp;
            [System.NonSerialized] public Collider Support;
            [System.NonSerialized] public float Progress;
            [System.NonSerialized] public bool Planted, Swinging;
        }

        [SerializeField] private Transform m_Body;
        [SerializeField] private AlienGroundProbe m_Ground;
        [SerializeField] private Leg[] m_Legs = new Leg[4];
        [SerializeField, Min(.1f)] private float m_UpperLength = .95f;
        [SerializeField, Min(.1f)] private float m_LowerLength = 1.05f;
        [SerializeField, Min(.05f)] private float m_StepDistance = .36f;
        [SerializeField, Min(.05f)] private float m_StepHeight = .34f;
        private Vector3 m_LastPosition, m_Velocity, m_LastForward, m_LastUp;
        private AlienAdhesionRoute m_Surface;
        private Vector3 Up => m_Surface ? m_Surface.Up : Vector3.up;
        private float m_LastYaw, m_Duration;
        private bool m_Ready;
        private int m_ActivePair = -1, m_LastPair = 1;
        public int PlantedFeet { get; private set; }
        public int ReachClamps { get; private set; }
        public float Speed => m_Velocity.magnitude;

        public void Configure(Transform body, AlienGroundProbe ground, Leg[] legs)
        { m_Body = body; m_Ground = ground; m_Legs = legs; m_Surface = GetComponent<AlienAdhesionRoute>(); m_Ready = false; }

        private void OnEnable() => m_Ready = false;
        private void OnDisable() => m_Ready = false;

        private bool Initialize()
        {
            if (!m_Body || !m_Ground || m_Legs == null || m_Legs.Length != 4) return false;
            foreach (var leg in m_Legs) if (leg == null || !leg.Upper || !leg.Lower || !leg.Toe) return false;
            m_LastPosition = transform.position;
            m_LastYaw = transform.eulerAngles.y;
            m_LastForward = transform.forward;
            m_LastUp = Up;
            m_Velocity = Vector3.zero;
            m_ActivePair = -1;
            m_LastPair = 1;
            m_Body.position = transform.position + Up * .66f;
            m_Body.rotation = transform.rotation;
            foreach (var leg in m_Legs)
            {
                leg.Swinging = false;
                leg.Planted = ProbeFoot(transform.TransformPoint(leg.RestFoot), Up, out var hit);
                leg.Position = leg.Planted ? hit.point + hit.normal * .035f : transform.TransformPoint(leg.RestFoot);
                leg.Normal = leg.Planted ? hit.normal : Up;
                leg.Support = leg.Planted ? hit.collider : null;
            }
            m_Ready = true;
            return true;
        }

        private void LateUpdate()
        {
            if (!m_Ready && !Initialize()) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 delta = transform.position - m_LastPosition;
            // Network teleports/respawns must reset contacts; ordinary fast motion must not.
            if (delta.sqrMagnitude > 9f) { m_Ready = false; Initialize(); return; }
            m_Velocity = Vector3.Lerp(m_Velocity, delta / dt, AlienLegMath.Damping(14f, dt));
            float yawDelta = m_Surface ? Vector3.SignedAngle(
                Quaternion.FromToRotation(m_LastUp, Up) * m_LastForward, transform.forward, Up) :
                Mathf.DeltaAngle(m_LastYaw, transform.eulerAngles.y);
            float yawRate = Mathf.Clamp(yawDelta / dt, -240f, 240f);
            m_LastForward = transform.forward;
            m_LastUp = Up;
            m_LastPosition = transform.position;
            m_LastYaw = transform.eulerAngles.y;
            UpdateBody(dt);
            AdvanceSwing(dt);
            if (m_ActivePair < 0) BeginStep(yawRate);
            PlantedFeet = 0;
            ReachClamps = 0;
            foreach (var leg in m_Legs)
            {
                if (!leg.Support || !leg.Support.enabled || !leg.Support.gameObject.activeInHierarchy) leg.Planted = false;
                if (leg.Planted && !leg.Swinging) PlantedFeet++;
                Vector3 hip = m_Body.TransformPoint(leg.Hip);
                Vector3 bend = m_Body.right * Mathf.Sign(leg.Hip.x) + m_Body.up * .65f;
                AlienLegMath.Solve(hip, leg.Position, bend, m_UpperLength, m_LowerLength, out var knee, out var foot);
                if ((foot - leg.Position).sqrMagnitude > .0025f) ReachClamps++;
                SetSegment(leg.Upper, hip, knee, .23f);
                SetSegment(leg.Lower, knee, foot, .12f);
                leg.Toe.position = foot;
                leg.Toe.rotation = Quaternion.FromToRotation(Vector3.up, leg.Normal);
            }
        }

        private void UpdateBody(float dt)
        {
            if (m_Surface)
            {
                // The adhesion motor validated a sphere enclosing the shell at this exact centre.
                // Position smoothing here would invalidate that clearance check during corners.
                m_Body.position = transform.position + Up * AlienAdhesionRoute.BodyOffset;
                m_Body.rotation = transform.rotation;
                return;
            }
            Vector3 normals = Vector3.zero;
            float height = transform.position.y + .66f;
            int count = 0;
            // Actual body footprint probes avoid deriving clearance solely from widely spread toes.
            for (int i = 0; i < 5; i++)
            {
                Vector3 offset = i == 0 ? Vector3.zero : new Vector3(i % 2 == 0 ? .38f : -.38f, 0f, i < 3 ? .65f : -.65f);
                if (!m_Ground.Ground(transform.TransformPoint(offset), out var hit)) continue;
                height = Mathf.Max(height, hit.point.y + .48f);
                normals += hit.normal;
                count++;
            }
            Vector3 up = count > 0 ? AlienLegMath.SafeUp(normals / count, 40f) : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            Quaternion rotation = Quaternion.LookRotation(forward, up);
            m_Body.rotation = Quaternion.Slerp(m_Body.rotation, rotation, AlienLegMath.Damping(12f, dt));
            Vector3 position = transform.position;
            position.y = Mathf.Lerp(m_Body.position.y, height, AlienLegMath.Damping(16f, dt));
            // Rising terrain is a hard clearance floor; falling terrain eases down.
            position.y = Mathf.Max(position.y, height - .08f);
            m_Body.position = position;
        }

        private void AdvanceSwing(float dt)
        {
            if (m_ActivePair < 0) return;
            bool any = false;
            foreach (var leg in m_Legs)
            {
                if (!leg.Swinging) continue;
                leg.Progress = Mathf.Min(1f, leg.Progress + dt / m_Duration);
                leg.Position = AlienLegMath.Swing(leg.From, leg.Target, leg.SwingUp, leg.Progress, m_StepHeight);
                if (leg.Progress >= 1f)
                {
                    leg.Swinging = false;
                    // A missing ledge is never accepted as a planted contact.
                    leg.Planted = ProbeFoot(leg.Target, leg.Normal, out var hit, .15f, .2f) &&
                        (hit.point - leg.Target).sqrMagnitude < .04f;
                    leg.Support = leg.Planted ? hit.collider : null;
                }
                else any = true;
            }
            if (!any) { m_LastPair = m_ActivePair; m_ActivePair = -1; }
        }

        private void BeginStep(float yawRate)
        {
            m_Duration = Mathf.Lerp(.24f, .105f, Mathf.InverseLerp(0f, 6f, Speed));
            int pair = 1 - m_LastPair;
            if (!NeedsStep(pair, yawRate)) { pair = m_LastPair; if (!NeedsStep(pair, yawRate)) return; }
            // Each diagonal pair is lifted together only if both destinations have real support.
            int first = pair == 0 ? 0 : 1;
            int second = pair == 0 ? 3 : 2;
            if (!Destination(m_Legs[first], yawRate, out var a) || !Destination(m_Legs[second], yawRate, out var b)) return;
            StartSwing(m_Legs[first], a);
            StartSwing(m_Legs[second], b);
            m_ActivePair = pair;
        }

        private bool NeedsStep(int pair, float yawRate)
        {
            for (int i = 0; i < 4; i++)
            {
                if ((i == 0 || i == 3 ? 0 : 1) != pair) continue;
                var leg = m_Legs[i];
                if (!leg.Planted) return true;
                if (Destination(leg, yawRate, out var hit) && (hit.point - leg.Position).sqrMagnitude > m_StepDistance * m_StepDistance) return true;
            }
            return false;
        }

        private bool Destination(Leg leg, float yawRate, out RaycastHit hit)
        {
            Vector3 rest = Quaternion.AngleAxis(yawRate * m_Duration * .6f, Up) *
                (transform.rotation * leg.RestFoot);
            Vector3 lead = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(m_Velocity, Up) * m_Duration * .8f, .8f);
            Vector3 target = transform.position + rest + lead;
            if (!ProbeFoot(target, Up, out hit)) return false;
            // Avoid reaching through a floor or down a cliff. The driver stops independently.
            return Mathf.Abs(Vector3.Dot(hit.point - transform.position, Up)) <= .7f &&
                Vector3.Distance(m_Body.TransformPoint(leg.Hip), hit.point) < m_UpperLength + m_LowerLength - .08f;
        }

        private bool ProbeFoot(Vector3 point, Vector3 up, out RaycastHit hit, float rise = 1.2f, float drop = 2.4f)
        {
            // Preserve the original configured ground slope contract in the foundation scene.
            return m_Surface ? m_Ground.Surface(point, up, out hit, rise, drop) :
                m_Ground.Ground(point, out hit, rise, drop);
        }

        private void StartSwing(Leg leg, RaycastHit hit)
        {
            leg.SwingUp = Up;
            leg.From = leg.Position;
            leg.Target = hit.point + hit.normal * .035f;
            leg.Normal = hit.normal;
            leg.Progress = 0f;
            leg.Swinging = true;
            leg.Planted = false;
            leg.Support = hit.collider;
        }

        private static void SetSegment(Transform segment, Vector3 from, Vector3 to, float width)
        {
            segment.position = (from + to) * .5f;
            segment.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
            segment.localScale = new Vector3(width, Vector3.Distance(from, to) * .5f, width);
        }
    }
}
