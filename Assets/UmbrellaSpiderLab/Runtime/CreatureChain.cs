using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Arc-length trail separates steering from body propagation. An actual corner
    /// remains in the trail until the tail passes it; it is never replaced by the head's heading.</summary>
    public sealed class CreatureChain
    {
        public const int Rings = 97;
        private const int HistoryCapacity = 2048;
        private readonly SurfaceMotor.Pose[] m_History = new SurfaceMotor.Pose[HistoryCapacity];
        private readonly float[] m_Distance = new float[HistoryCapacity];
        private int m_Newest, m_Count;
        private float m_Travel, m_Phase, m_Drive;
        private readonly int m_Mask;
        private readonly Vector3[] m_Smoothed = new Vector3[Rings];
        private readonly Vector3[] m_Previous = new Vector3[Rings];
        private readonly Collider[] m_Support = new Collider[Rings], m_PreviousSupport = new Collider[Rings];
        private bool m_HasPrevious;
        private bool m_SuckerInitialized, m_RearHolding;
        private Vector3 m_SuckerTail, m_SuckerAnchor;
        private Collider m_SuckerSurface;
        public bool RearSuckerHolding => m_RearHolding;
        public float SuckerSpan { get; private set; }
        public readonly Vector3[] Centres = new Vector3[Rings], Ups = new Vector3[Rings], Tangents = new Vector3[Rings];
        public readonly float[] Radii = new float[Rings];
        public float Phase => m_Phase;
        public float Drive => m_Drive;
        public int UnsupportedRings { get; private set; }
        public float MaximumBend { get; private set; }
        public int BodySweepsBlocked { get; private set; }
        public int SelfSeparations { get; private set; }

        public CreatureChain(int mask) { m_Mask = mask; }

        public void Reset(SurfaceMotor.Pose head, float length)
        {
            m_Newest = m_Count = 0; m_Travel = m_Phase = m_Drive = 0;
            m_HasPrevious = false;
            m_SuckerInitialized = false; m_SuckerSurface = null;
            int samples = Mathf.CeilToInt((length + 2) / .035f);
            for (int i = samples; i >= 0; i--)
            {
                var point = head; point.Position -= head.Forward * i * .035f;
                Push(point);
            }
        }

        private void Push(SurfaceMotor.Pose pose)
        {
            if (m_Count > 0)
            {
                float delta = Vector3.Distance(pose.Position, m_History[m_Newest].Position);
                if (delta < .018f) return;
                m_Travel += delta;
                m_Newest = (m_Newest + 1) % HistoryCapacity;
            }
            m_History[m_Newest] = pose; m_Distance[m_Newest] = m_Travel;
            m_Count = Mathf.Min(m_Count + 1, HistoryCapacity);
        }

        /// <summary>Two-anchor gait: the rear stays in collider-local coordinates while
        /// the head extends; the head then pauses while the rear contracts forward.
        /// The actor owns head displacement, so this is never a second movement authority.</summary>
        public void TickSuction(SurfaceMotor.Pose head, CreatureChainSettings s, float phase,
            float drive, float dt, bool detached, float bodyHeight = SurfaceMotor.Height)
        {
            Tick(head, s, true, drive, dt, 0, detached, bodyHeight);
            bool holding = phase < .58f;
            if (!m_SuckerInitialized || detached)
            {
                m_SuckerTail = head.Position - head.Forward * s.length * (1 - s.contraction);
                m_SuckerInitialized = true; m_SuckerSurface = null;
            }
            if (holding && m_SuckerSurface) m_SuckerTail = m_SuckerSurface.transform.TransformPoint(m_SuckerAnchor);
            else if (!holding)
                m_SuckerTail = Vector3.MoveTowards(m_SuckerTail, head.Position - head.Forward * s.length * (1 - s.contraction),
                    s.speed * 1.6f * dt * drive);
            m_RearHolding = holding && !detached;
            SuckerSpan = Vector3.Distance(head.Position, m_SuckerTail);
            UnsupportedRings = 0;
            for (int i = 0; i < Rings; i++)
            {
                float t = i / (Rings - 1f);
                Vector3 position = Vector3.Lerp(head.Position, m_SuckerTail, t);
                float radius = s.radius * Mathf.Lerp(.50f, 1, Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), .4f));
                Vector3 up = head.Up;
                if (!detached && Physics.Raycast(position + up * .6f, -up, out var hit, 1.8f, m_Mask, QueryTriggerInteraction.Ignore))
                {
                    up = hit.normal; position = hit.point + up * radius * s.verticalScale;
                    if (i == Rings - 1)
                    { m_SuckerSurface = hit.collider; m_SuckerAnchor = hit.collider.transform.InverseTransformPoint(hit.point + up * bodyHeight); }
                }
                else UnsupportedRings++;
                float arch = Mathf.Sin(t * Mathf.PI) * s.archHeight * Mathf.Sin(phase * Mathf.PI) * drive;
                Centres[i] = position + up * arch; Ups[i] = up; Radii[i] = radius;
            }
            for (int i = 0; i < Rings; i++)
            {
                Vector3 direction = Centres[Mathf.Max(0, i - 1)] - Centres[Mathf.Min(Rings - 1, i + 1)];
                Tangents[i] = direction.sqrMagnitude > .00001f ? direction.normalized : head.Forward;
            }
        }

        private SurfaceMotor.Pose At(float behind, SurfaceMotor.Pose head)
        {
            float newestGap = Vector3.Distance(head.Position, m_History[m_Newest].Position);
            float wanted = m_Travel + newestGap - behind;
            SurfaceMotor.Pose newer = head; float newerDistance = m_Travel + newestGap;
            for (int offset = 0; offset < m_Count; offset++)
            {
                int index = (m_Newest - offset + HistoryCapacity) % HistoryCapacity;
                if (m_Distance[index] <= wanted)
                {
                    float t = Mathf.InverseLerp(m_Distance[index], newerDistance, wanted);
                    return new SurfaceMotor.Pose
                    {
                        Position = Vector3.Lerp(m_History[index].Position, newer.Position, t),
                        Rotation = Quaternion.Slerp(m_History[index].Rotation, newer.Rotation, t)
                    };
                }
                newer = m_History[index]; newerDistance = m_Distance[index];
            }
            newer.Position -= newer.Forward * Mathf.Max(0, newerDistance - wanted);
            return newer;
        }

        public void Tick(SurfaceMotor.Pose head, CreatureChainSettings settings, bool caterpillar, float drive, float dt, float sprint = 0, bool detached = false, float bodyHeight = SurfaceMotor.Height)
        {
            Push(head);
            m_Drive = Mathf.MoveTowards(m_Drive, Mathf.Clamp01(drive), dt * 3);
            m_Phase = Mathf.Repeat(m_Phase + dt * settings.waveFrequency * Mathf.PI * 2 * Mathf.Clamp01(drive), Mathf.PI * 2);
            UnsupportedRings = 0; MaximumBend = 0; BodySweepsBlocked = SelfSeparations = 0;
            float spacing = settings.length / (Rings - 1), behind = 0;
            for (int i = 0; i < Rings; i++)
            {
                float t = i / (Rings - 1f);
                float wave = m_Phase - t * settings.length / settings.wavelength * Mathf.PI * 2;
                // Axial shortening bulges the larva's volume; the contraction travels through
                // the chain instead of translating a row of rigid spheres together.
                float extension = caterpillar ? 1 - settings.contraction * m_Drive * (.5f + .5f * Mathf.Sin(wave)) : 1;
                if (i > 0) behind += spacing * extension;
                var pose = At(behind, head);
                float radius = settings.radius * (caterpillar
                    ? Mathf.Lerp(.56f, 1, Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), .3f)) / Mathf.Sqrt(extension)
                    : Mathf.Lerp(1, .065f, Mathf.Pow(t, 2.8f)));
                if (caterpillar) radius *= 1 - settings.segmentGrooveDepth * Mathf.Pow(.5f + .5f * Mathf.Cos(t * settings.segments * Mathf.PI * 2), 4);
                float envelope = Mathf.Sin(t * Mathf.PI) * (caterpillar ? 1 : Mathf.Min(1, t * 12));
                float sideways = Mathf.Sin(wave) * settings.lateralAmplitude * envelope * m_Drive;
                Vector3 centre = pose.Position - pose.Up * (bodyHeight - radius * settings.verticalScale);
                Vector3 offset = pose.Right * sideways;
                if (offset.sqrMagnitude > .000001f && Physics.SphereCast(centre, radius * .65f, offset.normalized,
                    out var obstacle, offset.magnitude, m_Mask, QueryTriggerInteraction.Ignore))
                    offset = offset.normalized * Mathf.Max(0, obstacle.distance - .012f);
                centre += offset;
                if (!detached && Physics.Raycast(centre + pose.Up * .65f, -pose.Up, out var ground, 1.6f,
                    m_Mask, QueryTriggerInteraction.Ignore) && Vector3.Dot(ground.normal, pose.Up) > .4f)
                {
                    // Keep the trail's transported frame through a corner. Switching straight
                    // from floor normal to wall normal would shift adjacent rings sideways
                    // by a whole radius and fold the tube back onto itself.
                    Ups[i] = pose.Up;
                    m_Support[i] = ground.collider;
                    centre = ground.point + pose.Up * radius * settings.verticalScale;
                }
                else { Ups[i] = pose.Up; m_Support[i] = null; UnsupportedRings++; }
                float lift = caterpillar ? settings.archHeight * m_Drive * Mathf.Pow(Mathf.Max(0, Mathf.Sin(wave)), 2) : 0;
                centre += Ups[i] * (lift + (settings.headLift * m_Drive + settings.sprintHeadLift * sprint) * Mathf.Exp(-t * 12));
                Centres[i] = centre; Radii[i] = radius;
            }
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 1; i < Rings - 1; i++)
                    m_Smoothed[i] = Vector3.Lerp(Centres[i], (Centres[i - 1] + Centres[i + 1]) * .5f, settings.bendSmoothing);
                for (int i = 1; i < Rings - 1; i++) Centres[i] = m_Smoothed[i];
            }
            for (int i = 0; i < Rings; i++)
            {
                // Bounded positional constraints keep the entire tube involved in contact.
                // They correct the body only: steering remains owned by the head motor.
                if (i > 0)
                {
                    Vector3 delta = Centres[i] - Centres[i - 1];
                    float maximum = spacing * settings.maximumStretch;
                    if (delta.magnitude > maximum * 2)
                        Centres[i] += Vector3.ClampMagnitude(Centres[i - 1] + delta.normalized * maximum - Centres[i], .035f);
                }
                if (settings.selfSeparation > 0 && i > 1)
                {
                    for (int j = 0; j < i - 2; j += 3)
                    {
                        float clearance = (Radii[i] + Radii[j]) * .8f;
                        if ((i - j) * spacing < clearance * 1.5f) continue;
                        Vector3 delta = Centres[i] - Centres[j]; float distance = delta.magnitude;
                        if (distance >= clearance || distance < .0001f) continue;
                        Centres[i] += delta / distance * Mathf.Min(.008f, (clearance - distance) * settings.selfSeparation);
                        SelfSeparations++;
                    }
                }
                if (m_HasPrevious)
                {
                    Vector3 delta = Centres[i] - m_Previous[i];
                    float radius = Radii[i] * Mathf.Min(1, settings.verticalScale) * settings.collisionRadiusScale;
                    if (delta.sqrMagnitude > .000001f && Physics.SphereCast(m_Previous[i], radius, delta.normalized,
                        out var collision, delta.magnitude, m_Mask, QueryTriggerInteraction.Ignore) &&
                        collision.collider != m_Support[i] && collision.collider != m_PreviousSupport[i])
                    {
                        Vector3 contact = m_Previous[i] + delta.normalized * Mathf.Max(0, collision.distance - .006f);
                        Vector3 slide = Vector3.ProjectOnPlane(Centres[i] - contact, collision.normal);
                        if (slide.sqrMagnitude > .000001f && Physics.SphereCast(contact, radius, slide.normalized,
                            out var second, slide.magnitude, m_Mask, QueryTriggerInteraction.Ignore) &&
                            second.collider != m_Support[i] && second.collider != m_PreviousSupport[i])
                            slide = slide.normalized * Mathf.Max(0, second.distance - .006f);
                        Centres[i] += Vector3.ClampMagnitude(contact + slide - Centres[i], Radii[i] * .5f);
                        BodySweepsBlocked++;
                    }
                }
            }
            // Resolve any remaining hairpin after collision corrections. A finite turn is
            // distributed along the chain instead of letting neighbouring rings reverse
            // onto each other during a corner or emergency flight.
            for (int i = 2; i < Rings; i++)
            {
                Vector3 previous = Centres[i - 1] - Centres[i - 2];
                Vector3 delta = Centres[i] - Centres[i - 1];
                if (previous.sqrMagnitude < .000001f || delta.sqrMagnitude < .000001f) continue;
                if (Vector3.Angle(previous, delta) <= settings.maximumJointAngle) continue;
                Vector3 direction = Vector3.RotateTowards(previous.normalized, delta.normalized,
                    settings.maximumJointAngle * Mathf.Deg2Rad, 0);
                Centres[i] = Centres[i - 1] + direction * Mathf.Clamp(delta.magnitude, spacing * .3f, spacing * settings.maximumStretch);
            }
            for (int i = 0; i < Rings; i++)
            {
                Vector3 direction = Centres[Mathf.Max(0, i - 1)] - Centres[Mathf.Min(Rings - 1, i + 1)];
                Tangents[i] = direction.sqrMagnitude > .000001f ? direction.normalized : head.Forward;
                if (i > 0) MaximumBend = Mathf.Max(MaximumBend, Vector3.Angle(Tangents[i - 1], Tangents[i]));
                m_Previous[i] = Centres[i];
                m_PreviousSupport[i] = m_Support[i];
            }
            m_HasPrevious = true;
        }
        public void ReleaseContacts() { m_HasPrevious = false; }
    }
}
