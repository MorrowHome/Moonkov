using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Eight independent collider-local anchors; gait grants steps without queueing the body.</summary>
    public sealed class PredictiveFeet
    {
        [System.Serializable]
        public sealed class GaitSettings
        {
            [Range(0, 30)] public float forwardLean = 19;
            [Range(0, 10)] public float roll = 3;
            [Range(0, .2f)] public float compression = .12f;
            [Range(0, .1f)] public float bodyBob = .045f;
            [Range(1, 4)] public float strideCycleDistance = 3;
            [Range(.25f, .6f)] public float swingFraction = .40f;
            [Range(.05f, .4f)] public float footLift = .18f;
        }
        public GaitSettings Settings { get; set; } = new GaitSettings();
        public enum ContactState { Stance, Swing, Recovery }
        public sealed class Foot
        {
            public Vector3 Position, Normal, Velocity;
            public Collider Surface;
            public Vector3 LocalPoint, LocalNormal;
            public ContactState State;
            public float Reach;
            public float UnplantedTime;
            public string PlanningIssue;
            internal Vector3 Start, StartVelocity, StartNormal, End, EndNormal;
            internal Collider EndSurface;
            internal float Elapsed, Duration, Lift;
            internal int Replans;
            internal float CandidateLift;
            internal Vector3 StepOrigin;
            internal float FailedTime;
        }

        public const int Count = 8;
        public const float UpperLength = 1.05f;
        public const float LowerLength = 1.25f;
        public const float MaxReach = UpperLength + LowerLength - .055f;
        private readonly Foot[] m_Feet = new Foot[Count];
        private readonly int m_Mask;
        private readonly float m_Size;
        public float Size => m_Size;
        private float ReachLimit => MaxReach * m_Size;
        private float m_Clock;
        private Vector2 m_PreviousInput;
        private float m_PreviousYaw;
        private float m_PreviousSpeed;
        private float m_Phase, m_CycleTime, m_LeadDistance, m_StrideTotal;
        private Vector2 m_Lean;
        public SurfaceMotor.Pose BodyPose { get; private set; }
        public float RunIntensity => m_Lean.magnitude;
        public float MaxCompletedStride { get; private set; }
        public int CompletedSteps { get; private set; }
        public float AverageStride => CompletedSteps > 0 ? m_StrideTotal / CompletedSteps : 0;
        public Foot this[int i] => m_Feet[i];
        public int SwingCount { get; private set; }
        public int RecoveryCount { get; private set; }
        public int UnreachableCount { get; private set; }
        public int ObstructedSwings { get; private set; }
        public int ReleasedAnchors { get; private set; }
        public float SpeedScale { get; private set; } = 1;

        public PredictiveFeet(int mask, float scale = 1) { m_Mask = mask; m_Size = Mathf.Clamp(scale, .1f, 4); }

        public static Vector3 Radial(int index)
        {
            float a = (index * 45 + 22.5f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
        }

        public static Vector3 Hip(SurfaceMotor.Pose pose, int i, float scale = 1)
        {
            return pose.Position + pose.Rotation * (Radial(i) * .35f + Vector3.up * .04f) * scale;
        }

        private Vector3 Home(SurfaceMotor.Pose pose, int i)
        {
            Vector3 lead = (pose.Right * m_PreviousInput.x + pose.Forward * m_PreviousInput.y).normalized * m_LeadDistance;
            return pose.Position + pose.Rotation * (Radial(i) * 1.18f - Vector3.up * SurfaceMotor.Height) * m_Size + lead;
        }

        // Body posture belongs before planning/IK: the roots really lean and compress while the
        // planted toes keep their collider anchors. It is not a late rotation of already-solved legs.
        private SurfaceMotor.Pose RunningPose(SurfaceMotor.Pose surface, Vector2 lean, float phase)
        {
            float intensity = lean.magnitude;
            Vector3 direction = surface.Right * lean.x + surface.Forward * lean.y;
            Vector3 axis = Vector3.Cross(surface.Up, direction);
            var pose = surface;
            float compression = (-Settings.compression + Settings.bodyBob * Mathf.Cos(phase * Mathf.PI * 4)) * intensity;
            pose.Position += surface.Up * compression + direction * .025f * m_Size;
            if (axis.sqrMagnitude > .001f)
                pose.Rotation = Quaternion.AngleAxis(Settings.forwardLean * intensity, axis.normalized) * surface.Rotation;
            pose.Rotation *= Quaternion.AngleAxis(Mathf.Sin(phase * Mathf.PI * 2) * Settings.roll * intensity, Vector3.forward);
            return pose;
        }

        private SurfaceMotor.Pose PredictBody(SurfaceMotor.Pose surface, Vector2 targetLean, float seconds)
        {
            Vector2 lean = Vector2.MoveTowards(m_Lean, targetLean, seconds * 10);
            float phase = Mathf.Repeat(m_Phase + seconds / m_CycleTime, 1);
            return RunningPose(surface, lean, phase);
        }

        public void Reset(SurfaceMotor.Pose pose)
        {
            m_Clock = 0;
            ObstructedSwings = ReleasedAnchors = 0; SpeedScale = 1;
            m_PreviousInput = Vector2.zero; m_PreviousYaw = 0;
            m_PreviousSpeed = 0;
            m_Phase = m_LeadDistance = m_StrideTotal = 0; m_Lean = Vector2.zero; m_CycleTime = .55f;
            CompletedSteps = 0; MaxCompletedStride = 0; BodyPose = pose;
            for (int i = 0; i < Count; i++)
            {
                var foot = m_Feet[i] = new Foot();
                if (FindContact(pose, pose, i, pose.Up, out var hit))
                {
                    foot.Position = hit.point; foot.Normal = hit.normal;
                    Plant(foot, hit.collider, hit.point, hit.normal);
                }
                else
                {
                    foot.Position = Home(pose, i); foot.Normal = pose.Up;
                    foot.State = ContactState.Recovery;
                }
            }
            UpdateMetrics(pose);
        }

        public void Tick(SurfaceMotor motor, Vector2 input, float speed, float yaw, float dt)
        {
            m_Clock += dt;
            float travelSpeed = speed * input.magnitude;
            // A complete cycle covers about three metres at running speed. Longer real segments
            // and forward touchdown bias let rear feet finish a visible push instead of shuffling.
            m_CycleTime = Mathf.Clamp(Settings.strideCycleDistance / Mathf.Max(1, travelSpeed), .14f, .55f);
            m_Phase = Mathf.Repeat(m_Phase + dt / m_CycleTime, 1);
            float duration = m_CycleTime * Settings.swingFraction;
            float supportTime = Mathf.Min(.13f, .85f * m_Size / Mathf.Max(1, travelSpeed));
            Vector2 targetLean = input.normalized * Mathf.InverseLerp(1, 7, travelSpeed);
            m_Lean = Vector2.MoveTowards(m_Lean, targetLean, dt * 10);
            m_LeadDistance = Mathf.InverseLerp(1, 6, travelSpeed) * .5f * m_Size;
            var surfacePose = motor.Current;
            var pose = BodyPose = RunningPose(surfacePose, m_Lean, m_Phase);
            var landingSurface = motor.Predict(input, speed, yaw, duration);
            var landing = PredictBody(landingSurface, targetLean, duration);
            var supportEnd = PredictBody(motor.Predict(input, speed, yaw, duration + supportTime), targetLean, duration + supportTime);
            bool intentChanged = (input - m_PreviousInput).sqrMagnitude > .08f ||
                Mathf.Abs(yaw - m_PreviousYaw) > 30 || Mathf.Abs(speed - m_PreviousSpeed) > 1.5f;
            m_PreviousInput = input; m_PreviousYaw = yaw; m_PreviousSpeed = speed;
            SwingCount = 0;
            for (int i = 0; i < Count; i++)
            {
                var foot = m_Feet[i];
                foot.UnplantedTime = foot.State == ContactState.Stance && foot.Surface ? 0 : foot.UnplantedTime + dt;
                if (foot.State == ContactState.Swing)
                {
                    UpdateSwing(foot, i, motor, input, speed, yaw, intentChanged, dt);
                    if (foot.State == ContactState.Swing) SwingCount++;
                }
                else if (foot.Surface && foot.Surface.enabled && foot.Surface.gameObject.activeInHierarchy)
                {
                    Vector3 anchor = foot.Surface.transform.TransformPoint(foot.LocalPoint);
                    // Position is restored from the anchor, never reprojected onto a changing home.
                    // Local anchors also follow a platform; platform traversal itself is not validated yet.
                    foot.Position = anchor;
                    foot.Normal = foot.Surface.transform.TransformDirection(foot.LocalNormal).normalized;
                    foot.Velocity = Vector3.zero;
                    if (!SpiderLegKinematics.Solve(pose, i, foot.Position, foot.Normal, m_Mask, out _, out _, m_Size))
                        ReleaseAnchor(foot);
                }
                else foot.State = ContactState.Recovery;
                if (foot.State == ContactState.Recovery) Retract(foot, pose, i, dt);
            }
            // Rotate priority each beat. An unavailable candidate never prevents another leg stepping.
            int first = Mathf.FloorToInt(m_Clock / duration) % Count;
            for (int pass = 0; pass < 2; pass++)
            for (int offset = 0; offset < Count; offset++)
            {
                int i = (first + offset) % Count;
                var foot = m_Feet[i];
                if (foot.State == ContactState.Swing) continue;
                float reach = Vector3.Distance(Hip(pose, i, m_Size), foot.Position);
                float error = Vector3.Distance(Home(landingSurface, i), foot.Position);
                float phase = Mathf.Repeat(m_Phase + (i % 2) * .5f + (i / 2) * .015f, 1);
                bool urgent = reach > ReachLimit * .80f || foot.State == ContactState.Recovery;
                if (urgent != (pass == 0)) continue;
                if (foot.State != ContactState.Recovery && SwingCount >= (urgent ? 6 : 4)) continue;
                if (!urgent && (error < .42f * m_Size || phase > .10f)) continue;
                // An airborne recovery foot removes no support. Never prevent it from landing
                // because other feet are also recovering; that would deadlock the whole gait.
                bool support = foot.State == ContactState.Recovery || SupportDistributedWithout(i, urgent ? 2 : 4);
                if (!support) foot.PlanningIssue = "support distribution";
                float stepTime = urgent ? Mathf.Min(.095f, duration * .65f) : duration;
                var stepSurface = urgent ? motor.Predict(input, speed, yaw, stepTime) : landingSurface;
                var stepLanding = urgent ? PredictBody(stepSurface, targetLean, stepTime) : landing;
                var stepSupportEnd = urgent ? PredictBody(motor.Predict(input, speed, yaw, stepTime + supportTime), targetLean, stepTime + supportTime) : supportEnd;
                if (!support || !FindContact(stepLanding, stepSupportEnd, i, foot.Normal, out var contact, foot, stepSurface))
                {
                    foot.FailedTime += dt;
                    if (foot.State == ContactState.Stance && (reach > ReachLimit * .94f ||
                        foot.FailedTime > .12f && reach > ReachLimit * .84f)) ReleaseAnchor(foot);
                    continue;
                }
                if (Vector3.Distance(contact.point, foot.Position) < .09f && foot.Surface) continue;
                // A legal endpoint is insufficient: check a coarse toe trajectory before granting a step.
                float lift = foot.CandidateLift;
                foot.Start = foot.Position; foot.StartNormal = foot.Normal; foot.StartVelocity = foot.Velocity;
                foot.StepOrigin = foot.Position;
                foot.End = contact.point; foot.EndNormal = contact.normal; foot.EndSurface = contact.collider;
                foot.Elapsed = 0; foot.Duration = stepTime; foot.Lift = lift; foot.Replans = 0;
                foot.State = ContactState.Swing;
                foot.PlanningIssue = null; foot.FailedTime = 0;
                SwingCount++;
            }
            UpdateMetrics(pose);
        }

        public void BeginFold()
        {
            for (int i = 0; i < Count; i++) ReleaseAnchor(m_Feet[i]);
            SwingCount = 0;
        }

        /// <summary>Flight feet follow the body with no collider-local grips or gait speed gate.</summary>
        public void TickFolded(SurfaceMotor.Pose surfacePose, float dt)
        {
            m_Lean = Vector2.MoveTowards(m_Lean, Vector2.zero, dt * 10);
            BodyPose = RunningPose(surfacePose, m_Lean, m_Phase);
            for (int i = 0; i < Count; i++)
            {
                var foot = m_Feet[i];
                foot.UnplantedTime += dt;
                Retract(foot, BodyPose, i, dt);
            }
            UpdateMetrics(BodyPose); SpeedScale = 1;
        }

        public void EndFold(SurfaceMotor.Pose pose)
        {
            m_PreviousInput = Vector2.zero; m_PreviousSpeed = m_PreviousYaw = 0;
            m_Lean = Vector2.zero; BodyPose = pose;
            for (int i = 0; i < Count; i++) m_Feet[i].Normal = pose.Up;
            // The existing recovery scheduler lands each folded foot at a fresh exposed contact.
        }

        private void ReleaseAnchor(Foot foot)
        {
            foot.Surface = foot.EndSurface = null;
            foot.State = ContactState.Recovery; foot.Velocity = Vector3.zero;
            foot.FailedTime = 0; ReleasedAnchors++;
        }

        private void Retract(Foot foot, SurfaceMotor.Pose pose, int i, float dt)
        {
            Vector3 hip = Hip(pose, i, m_Size);
            // Repair a previously invalid target at the endpoint the IK was actually rendering.
            // Normal recovery releases BEFORE this limit, so valid planted contacts never jump.
            Vector3 delta = foot.Position - hip;
            if (delta.magnitude > ReachLimit) foot.Position = hip + delta.normalized * ReachLimit;
            Vector3 tucked = hip + (pose.Rotation * Radial(i) * .52f + pose.Up * .42f) * m_Size;
            Vector3 before = foot.Position;
            Vector3 next = Vector3.MoveTowards(before, tucked, 26f * dt);
            Vector3 step = next - before;
            if (step.sqrMagnitude > .000001f && UnityEngine.Physics.Raycast(before + step.normalized * .018f,
                step.normalized, out var hit, step.magnitude + .025f, m_Mask, QueryTriggerInteraction.Ignore))
            {
                next = before + step.normalized * Mathf.Max(0, hit.distance - .04f);
                // Lift along the current accessible surface instead of retrying the blocked
                // old-contact trajectory forever. This movement still sweeps against the world.
                Vector3 escape = Vector3.ProjectOnPlane(pose.Up, hit.normal).normalized * (26f * dt);
                if (ClearToe(next, next + escape)) next += escape;
            }
            foot.Position = next;
            foot.Normal = Vector3.RotateTowards(foot.Normal, pose.Up, dt * 20, 0);
            foot.Velocity = (next - before) / Mathf.Max(.0001f, dt);
        }

        private bool SupportDistributedWithout(int excluded, int minimum)
        {
            int planted = 0, first = -1, previous = -1, largestGap = 0;
            for (int i = 0; i < Count; i++)
            {
                if (i == excluded || m_Feet[i].State != ContactState.Stance || !m_Feet[i].Surface) continue;
                planted++;
                if (first < 0) first = i;
                if (previous >= 0) largestGap = Mathf.Max(largestGap, i - previous);
                previous = i;
            }
            if (planted < minimum) return false;
            largestGap = Mathf.Max(largestGap, first + Count - previous);
            // Emergency fast-running contacts retain at least two broadly opposed grips. Normal
            // gait retains four distributed contacts; the body never waits for a whole group.
            return largestGap <= (minimum == 2 ? 5 : 4);
        }

        private bool FindContact(SurfaceMotor.Pose landing, SurfaceMotor.Pose supportEnd, int index,
            Vector3 oldNormal, out RaycastHit best, Foot stepping = null, SurfaceMotor.Pose? contactFrame = null)
        {
            best = default;
            float bestScore = float.MaxValue;
            var surface = contactFrame ?? landing;
            Vector3 home = Home(surface, index), hip = Hip(landing, index, m_Size);
            if (stepping != null) stepping.PlanningIssue = "no reachable contact";
            for (int candidate = 0; candidate < 12; candidate++)
            {
                Vector3 normal = candidate < 6 ? surface.Up : oldNormal;
                if (normal.sqrMagnitude < .1f) normal = surface.Up;
                Vector3 radial = surface.Rotation * Radial(index);
                Vector3 lateral = Vector3.Cross(surface.Up, radial);
                Vector3 center = home + (radial * ((candidate % 3 - 1) * .35f) +
                    lateral * ((candidate % 6 < 3 ? -1 : 1) * .23f)) * m_Size;
                if (!UnityEngine.Physics.Raycast(center + normal * 1.25f * m_Size, -normal, out var hit,
                    2.5f * m_Size, m_Mask, QueryTriggerInteraction.Ignore)) continue;
                float reach = Vector3.Distance(hip, hit.point);
                float futureReach = Vector3.Distance(Hip(supportEnd, index, m_Size), hit.point);
                if (reach > ReachLimit * .94f || futureReach > ReachLimit) continue;
                if (!SpiderLegKinematics.Solve(landing, index, hit.point, hit.normal, m_Mask, out _, out _, m_Size) ||
                    !SpiderLegKinematics.Solve(supportEnd, index, hit.point, hit.normal, m_Mask, out _, out _, m_Size)) continue;
                // A detail collider may sit behind/inside the coarse building proxy. Its ray hit is
                // not an exposed grip merely because the toe can reach it. Keep both the departure
                // collar and the straight root-to-contact corridor on the accessible side.
                Vector3 collar = hit.point + hit.normal * .05f * m_Size;
                if (UnityEngine.Physics.CheckSphere(collar, .025f * m_Size, m_Mask, QueryTriggerInteraction.Ignore)) continue;
                Vector3 corridor = collar - hip;
                if (corridor.sqrMagnitude > .0001f && UnityEngine.Physics.Raycast(hip, corridor.normalized,
                    out _, corridor.magnitude, m_Mask, QueryTriggerInteraction.Ignore)) continue;
                // Reject crossed legs; the candidate must lie in this leg's outward half-space.
                if (Vector3.Dot(hit.point - hip, radial) < .10f * m_Size) continue;
                float score = (hit.point - home).sqrMagnitude + Mathf.Max(0, futureReach - 1.15f * m_Size) * .7f * m_Size;
                if (score >= bestScore) continue;
                float lift = Settings.footLift * m_Size;
                if (stepping != null)
                {
                    bool clear = false;
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        lift = (.18f + attempt * .25f) * m_Size;
                        if (ClearSwing(stepping.Position, hit.point, stepping.Normal, hit.normal, lift))
                        { clear = true; break; }
                    }
                    if (!clear) { stepping.PlanningIssue = "toe path obstructed"; continue; }
                }
                bestScore = score; best = hit;
                if (stepping != null) stepping.CandidateLift = lift;
            }
            return best.collider;
        }

        private void UpdateSwing(Foot foot, int i, SurfaceMotor motor, Vector2 input,
            float speed, float yaw, bool intentChanged, float dt)
        {
            float fraction = foot.Elapsed / foot.Duration;
            float remaining = foot.Duration - foot.Elapsed;
            Vector2 targetLean = input.normalized * Mathf.InverseLerp(1, 7, speed * input.magnitude);
            var landingSurface = intentChanged ? motor.Predict(input, speed, yaw, remaining) : default;
            var landing = intentChanged ? PredictBody(landingSurface, targetLean, remaining) : default;
            float supportTime = Mathf.Min(.13f, .85f * m_Size / Mathf.Max(1, speed * input.magnitude));
            var supportEnd = intentChanged ? PredictBody(motor.Predict(input, speed, yaw, remaining + supportTime), targetLean, remaining + supportTime) : default;
            // Bounded redirection after an abrupt intent change. Keep both the current velocity and
            // original touchdown deadline; correcting a landing never restarts a full-duration chase.
            if (intentChanged && fraction < .6f && foot.Replans < 2 && FindContact(landing, supportEnd, i, foot.EndNormal, out var revised, foot, landingSurface)
                && Vector3.Distance(revised.point, foot.End) > .32f &&
                ClearSwing(foot.Position, revised.point, foot.Normal, revised.normal, foot.Lift))
            {
                foot.Start = foot.Position; foot.StartVelocity = foot.Velocity; foot.StartNormal = foot.Normal;
                foot.Duration -= foot.Elapsed; foot.Elapsed = 0;
                foot.End = revised.point; foot.EndNormal = revised.normal; foot.EndSurface = revised.collider;
                foot.Replans++;
            }
            Vector3 before = foot.Position;
            foot.Elapsed = Mathf.Min(foot.Duration, foot.Elapsed + dt);
            float t = foot.Elapsed / foot.Duration;
            Vector3 next = Trajectory(foot.Start, foot.StartVelocity, foot.End,
                foot.StartNormal, foot.EndNormal, foot.Lift, foot.Duration, t);
            // A late obstruction enters recovery at the current location instead of stepping through it.
            if (!ClearToe(before, next) || !SpiderLegKinematics.Solve(BodyPose, i, next,
                Vector3.Slerp(foot.StartNormal, foot.EndNormal, t), m_Mask, out _, out _, m_Size))
            {
                ReleaseAnchor(foot);
                Retract(foot, BodyPose, i, dt);
                ObstructedSwings++;
                return;
            }
            foot.Position = next;
            foot.Normal = Vector3.Slerp(foot.StartNormal, foot.EndNormal, t);
            // Analytic Hermite derivative is retained for position/velocity continuous replanning.
            foot.Velocity = TrajectoryVelocity(foot, t);
            if (t >= 1)
            {
                float stride = Vector3.Distance(foot.StepOrigin, foot.End);
                MaxCompletedStride = Mathf.Max(MaxCompletedStride, stride);
                m_StrideTotal += stride; CompletedSteps++;
                Plant(foot, foot.EndSurface, foot.End, foot.EndNormal);
                foot.Velocity = Vector3.zero;
            }
        }

        private static Vector3 Trajectory(Vector3 start, Vector3 velocity, Vector3 end,
            Vector3 n0, Vector3 n1, float lift, float duration, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            Vector3 curve = (2 * t3 - 3 * t2 + 1) * start + (t3 - 2 * t2 + t) * velocity * duration +
                (-2 * t3 + 3 * t2) * end;
            // Zero height/velocity at each end. Departure and arrival use their OWN contact normals.
            return curve + Vector3.Lerp(n0, n1, t) * (16 * t2 * (1 - t) * (1 - t) * lift);
        }

        private static Vector3 TrajectoryVelocity(Foot f, float t)
        {
            float t2 = t * t;
            Vector3 v = (6 * t2 - 6 * t) * f.Start + (3 * t2 - 4 * t + 1) * f.StartVelocity * f.Duration +
                (-6 * t2 + 6 * t) * f.End;
            float bump = 16 * t2 * (1 - t) * (1 - t) * f.Lift;
            float derivative = 32 * t * (1 - t) * (1 - 2 * t) * f.Lift;
            v += (f.EndNormal - f.StartNormal) * bump + Vector3.Lerp(f.StartNormal, f.EndNormal, t) * derivative;
            return v / f.Duration;
        }

        private bool ClearSwing(Vector3 from, Vector3 to, Vector3 n0, Vector3 n1, float lift)
        {
            Vector3 previous = from;
            for (int j = 1; j <= 5; j++)
            {
                Vector3 next = Trajectory(from, Vector3.zero, to, n0, n1, lift, .15f, j / 5f);
                if (!ClearToe(previous, next)) return false;
                previous = next;
            }
            return true;
        }

        private bool ClearToe(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            // Ray rather than a sphere: endpoints sit on the contact plane by definition. A sphere
            // started at that contact would overlap and hide a sweep hit. The rendered toe is a marker.
            return delta.sqrMagnitude < .000001f || !UnityEngine.Physics.Raycast(from + delta.normalized * .018f,
                delta.normalized, out _, Mathf.Max(0, delta.magnitude - .04f), m_Mask, QueryTriggerInteraction.Ignore);
        }

        private static void Plant(Foot foot, Collider surface, Vector3 point, Vector3 normal)
        {
            foot.Surface = surface; foot.Position = point; foot.Normal = normal;
            foot.State = surface ? ContactState.Stance : ContactState.Recovery;
            foot.PlanningIssue = null;
            foot.FailedTime = 0;
            if (!surface) return;
            foot.LocalPoint = surface.transform.InverseTransformPoint(point);
            foot.LocalNormal = surface.transform.InverseTransformDirection(normal);
        }

        private void UpdateMetrics(SurfaceMotor.Pose pose)
        {
            RecoveryCount = 0; UnreachableCount = 0;
            float worst = 0;
            for (int i = 0; i < Count; i++)
            {
                var f = m_Feet[i];
                f.Reach = Vector3.Distance(Hip(pose, i, m_Size), f.Position);
                if (f.State == ContactState.Recovery) RecoveryCount++;
                if (f.Reach > ReachLimit) UnreachableCount++;
                worst = Mathf.Max(worst, f.Reach / ReachLimit);
            }
            // A bounded, continuous reach guard, never a boolean isStepping/wait-for-group stop.
            // At a genuine reach failure, let the recovering toe catch up before the body can
            // drag it metres away. This is continuous and leaves normal running unimpeded.
            SpeedScale = Mathf.Lerp(1, .5f, Mathf.InverseLerp(.87f, 1.07f, worst)) *
                (1 - Mathf.InverseLerp(.97f, 1.015f, worst));
        }
    }
}
