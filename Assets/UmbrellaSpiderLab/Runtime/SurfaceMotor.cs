using UnityEngine;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>
    /// Kinematic, local surface traversal. Owns the body pose and transports its tangent frame.
    /// Static, thick colliders and corners below 135 degrees are the initial contract.
    /// Missing support enters a swept fall rather than retaining a floating, jammed surface pose.
    /// </summary>
    public sealed class SurfaceMotor
    {
        public struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Up => Rotation * Vector3.up;
            public Vector3 Forward => Rotation * Vector3.forward;
            public Vector3 Right => Rotation * Vector3.right;
        }

        private struct Corner
        {
            public bool Active;
            public bool OnArc;
            public Vector3 Entry, Center, Radius, Axis, Incoming;
            public Vector3 FromNormal, ToNormal;
            public Quaternion EntryRotation;
            public Collider FromSurface, ToSurface;
            public float Angle, Progress;
        }

        private struct State
        {
            public Pose Pose;
            public Vector3 Velocity;
            public Collider Surface;
            public Corner Corner;
            public bool Supported;
            public int Transitions, Blocked;
            public float Travel;
            public bool Detached;
            public float DetachedTime;
            public Vector3 FlightVelocity;
            public int Escapes;
        }

        public const float Height = .56f;
        public const float BodyRadius = .23f;
        private const float TurnRadius = .42f;
        private const float Skin = .012f;
        private readonly int m_Mask;
        private readonly float m_Size;
        public float Size => m_Size;
        public float BodyHeight => Height * m_Size;
        public float CollisionRadius => BodyRadius * m_Size;
        private float CornerRadius => TurnRadius * m_Size;
        private float ContactSkin => Skin * m_Size;
        private State m_State;
        public Pose Current => m_State.Pose;
        public Vector3 Velocity => m_State.Velocity;
        public Collider Surface => m_State.Surface;
        public bool Supported => m_State.Supported;
        public bool Transitioning => m_State.Corner.Active;
        public int Transitions => m_State.Transitions;
        public int Blocked => m_State.Blocked;
        public float Travel => m_State.Travel;
        public bool Detached => m_State.Detached;
        public int Escapes => m_State.Escapes;
        public string Mode => Detached ? "DETACHED / FOLDED" : !Supported ? "NO SUPPORT" : !Transitioning ? "SURFACE" :
            m_State.Corner.OnArc ? "CORNER ARC" : "CORNER APPROACH";

        public SurfaceMotor(Vector3 position, Quaternion rotation, int mask, float scale = 1)
        {
            m_Mask = mask; m_Size = Mathf.Clamp(scale, .1f, 4);
            Reset(position, rotation);
        }

        public void Reset(Vector3 position, Quaternion rotation)
        {
            m_State = new State { Pose = new Pose { Position = position, Rotation = rotation } };
            Attach(ref m_State);
        }

        public void Tick(Vector2 input, float speed, float yawDegrees, float dt)
        {
            Advance(ref m_State, input, speed, yawDegrees, dt);
        }

        /// <summary>Ground-only locomotion for an upright body. Sweeps its complete
        /// height and checks support before moving; never climbs walls or ceilings.</summary>
        public void TickGroundedCapsule(Vector2 input, float speed, float yawDegrees, float dt, float height, float radius)
        {
            if (dt <= 0) return;
            Vector3 before = Current.Position;
            Quaternion rotation = Quaternion.Euler(0, Current.Rotation.eulerAngles.y + yawDegrees * dt, 0);
            Vector3 foot = before - Vector3.up * BodyHeight;
            Vector3 movement = rotation * new Vector3(input.x, 0, input.y) * (speed * dt);
            bool supported = UnityEngine.Physics.Raycast(foot + Vector3.up * .45f, Vector3.down, out var floor, .9f, m_Mask,
                QueryTriggerInteraction.Ignore) && floor.normal.y >= .75f;
            bool blocked = false;
            if (supported)
            {
                foot = floor.point;
                float distance = movement.magnitude;
                float clearance = .025f + radius * Mathf.Tan(Vector3.Angle(floor.normal, Vector3.up) * Mathf.Deg2Rad);
                Vector3 lower = foot + Vector3.up * (radius + clearance), upper = foot + Vector3.up * (height - radius + clearance);
                blocked = UnityEngine.Physics.CheckCapsule(lower, upper, radius, m_Mask, QueryTriggerInteraction.Ignore) ||
                    (distance > .0001f && UnityEngine.Physics.CapsuleCast(lower, upper, radius, movement / distance, distance + .025f,
                        m_Mask, QueryTriggerInteraction.Ignore));
                if (!blocked && distance > .0001f)
                {
                    Vector3 next = foot + movement;
                    if (UnityEngine.Physics.Raycast(next + Vector3.up * .45f, Vector3.down, out var support, .9f, m_Mask,
                        QueryTriggerInteraction.Ignore) && support.normal.y >= .75f)
                    {
                        float nextClearance = .025f + radius * Mathf.Tan(Vector3.Angle(support.normal, Vector3.up) * Mathf.Deg2Rad);
                        if (!UnityEngine.Physics.CheckCapsule(support.point + Vector3.up * (radius + nextClearance),
                            support.point + Vector3.up * (height - radius + nextClearance), radius, m_Mask, QueryTriggerInteraction.Ignore))
                        { foot = support.point; floor = support; }
                        else blocked = true;
                    }
                    else blocked = true;
                }
            }
            m_State.Corner = default; m_State.Detached = false; m_State.Supported = supported;
            m_State.Surface = supported ? floor.collider : null;
            m_State.Pose = new Pose { Position = supported ? foot + Vector3.up * BodyHeight : before, Rotation = rotation };
            m_State.Velocity = (Current.Position - before) / dt;
            m_State.Travel += Vector3.Distance(before, Current.Position);
            if (blocked) m_State.Blocked++;
        }

        /// <summary>Presentation-only pose from an authoritative snapshot. Never call
        /// this on the server motor: clients use its value-copy predictor for feet only.</summary>
        public void Observe(Pose pose, Vector3 velocity, bool detached)
        {
            m_State.Pose = pose; m_State.Velocity = velocity;
            m_State.Detached = detached; m_State.Supported = !detached;
            m_State.Corner = default;
        }

        /// <summary>Emergency release owns a real swept flight, not a reset to the spawn point.
        /// Clearing the pending corner also releases its locked heading/arc when it is obstructed.</summary>
        public void Detach(Vector3 intendedDirection)
        {
            if (Detached) return;
            if (intendedDirection.sqrMagnitude < .01f) intendedDirection = Current.Forward;
            m_State.Corner = default; m_State.Surface = null; m_State.Supported = false;
            m_State.Detached = true; m_State.DetachedTime = 0; m_State.Escapes++;
            m_State.FlightVelocity = (intendedDirection.normalized * 6.5f + Current.Up * 4.5f) * m_Size;
            m_State.Velocity = m_State.FlightVelocity;
        }

        /// <summary>Server-selected ballistic attack. Shares the existing swept flight
        /// and contact recovery; the target is locked before launch.</summary>
        public void Launch(Vector3 velocity)
        {
            m_State.Corner = default; m_State.Surface = null; m_State.Supported = false;
            m_State.Detached = true; m_State.DetachedTime = 0;
            m_State.FlightVelocity = velocity;
            m_State.Velocity = velocity;
        }

        public void SetAttachedPose(Vector3 position, Quaternion rotation)
        {
            m_State.Corner = default; m_State.Surface = null; m_State.Supported = false;
            m_State.Detached = true; m_State.Velocity = Vector3.zero;
            m_State.Pose = new Pose { Position = position, Rotation = rotation };
        }

        // The predictor runs the exact same queries/transition geometry on a value copy. It cannot
        // mutate the actor, contact anchors, counters, or the live transition when planning feet.
        public Pose Predict(Vector2 input, float speed, float yawDegrees, float seconds)
        {
            var prediction = m_State;
            Advance(ref prediction, input, speed, yawDegrees, seconds);
            return prediction.Pose;
        }

        private void Advance(ref State state, Vector2 input, float speed, float yaw, float dt)
        {
            input = Vector2.ClampMagnitude(input, 1);
            float maxDt = Mathf.Min(.0125f, .065f / Mathf.Max(1, speed));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / maxDt));
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                var before = state.Pose.Position;
                if (!state.Detached && !state.Supported && !state.Corner.Active && !Attach(ref state))
                {
                    state.Detached = true; state.DetachedTime = .24f;
                    state.FlightVelocity = state.Velocity; state.Surface = null;
                }
                if (state.Detached)
                {
                    MoveDetached(ref state, input, yaw, h);
                    state.Velocity = h > 0 ? (state.Pose.Position - before) / h : Vector3.zero;
                    state.Travel += Vector3.Distance(before, state.Pose.Position);
                    continue;
                }
                // Rotation is a separate intent: sideways travel never waits for facing alignment.
                if (!state.Corner.Active)
                    state.Pose.Rotation *= Quaternion.AngleAxis(yaw * h, Vector3.up);
                Vector3 direction = state.Pose.Right * input.x + state.Pose.Forward * input.y;
                if (direction.sqrMagnitude > .0001f)
                {
                    direction.Normalize();
                    if (!state.Corner.Active) PlanCorner(ref state, direction, speed);
                    if (state.Corner.Active) MoveCorner(ref state, direction, speed * input.magnitude * h);
                    else MoveSurface(ref state, direction * (speed * input.magnitude * h));
                }
                else if (!state.Corner.Active) Attach(ref state);
                state.Velocity = h > 0 ? (state.Pose.Position - before) / h : Vector3.zero;
                state.Travel += Vector3.Distance(before, state.Pose.Position);
            }
        }

        private void MoveDetached(ref State state, Vector2 input, float yaw, float dt)
        {
            state.DetachedTime += dt;
            var upright = Quaternion.FromToRotation(state.Pose.Up, Vector3.up) * state.Pose.Rotation;
            state.Pose.Rotation = Quaternion.Slerp(state.Pose.Rotation, upright, 1 - Mathf.Exp(-4 * dt));
            state.Pose.Rotation = Quaternion.AngleAxis(yaw * dt, Vector3.up) * state.Pose.Rotation;
            if (input.sqrMagnitude > .01f)
            {
                Vector3 intent = Vector3.ProjectOnPlane(state.Pose.Right * input.x + state.Pose.Forward * input.y, Vector3.up);
                if (intent.sqrMagnitude > .01f)
                {
                    var horizontal = Vector3.ProjectOnPlane(state.FlightVelocity, Vector3.up);
                    horizontal = Vector3.MoveTowards(horizontal, intent.normalized * (6.5f * input.magnitude), dt * 10);
                    state.FlightVelocity = horizontal + Vector3.up * state.FlightVelocity.y;
                }
            }
            state.FlightVelocity += Vector3.down * (20 * dt);
            // A release grace period prevents immediately re-gripping the same jammed surface.
            if (state.DetachedTime >= .24f && state.FlightVelocity.sqrMagnitude > .01f &&
                Ray(state.Pose.Position, state.FlightVelocity.normalized, 2.5f, out var ahead) &&
                Vector3.Dot(state.FlightVelocity, ahead.normal) < -.2f &&
                Vector3.Dot(state.Pose.Position - ahead.point, ahead.normal) <= BodyHeight + .025f &&
                Land(ref state, ahead)) return;
            Vector3 remaining = state.FlightVelocity * dt;
            // Bounded sweep/slide handles tight corners without advancing through a blocking wall.
            for (int slide = 0; slide < 3 && remaining.sqrMagnitude > .000001f; slide++)
            {
                float distance = remaining.magnitude;
                if (!UnityEngine.Physics.SphereCast(state.Pose.Position, CollisionRadius, remaining / distance,
                    out var hit, distance + ContactSkin, m_Mask, QueryTriggerInteraction.Ignore))
                { state.Pose.Position += remaining; break; }
                float travelled = Mathf.Min(distance, Mathf.Max(0, hit.distance - ContactSkin));
                state.Pose.Position += remaining / distance * travelled;
                state.Blocked++;
                // SphereCast normals guide sliding only. Attachment still uses an actual ray hit.
                if (state.DetachedTime >= .24f && Ray(hit.point + hit.normal * .12f, -hit.normal, .25f,
                    out var contact) && Land(ref state, contact)) return;
                remaining = Vector3.ProjectOnPlane(remaining * (1 - travelled / distance), hit.normal);
                state.FlightVelocity = Vector3.ProjectOnPlane(state.FlightVelocity, hit.normal);
            }
        }

        private bool Land(ref State state, RaycastHit contact)
        {
            float gap = Vector3.Dot(state.Pose.Position - contact.point, contact.normal);
            if (gap < CollisionRadius - .02f || gap > BodyHeight + .025f) return false;
            Vector3 target = state.Pose.Position + contact.normal * (BodyHeight - gap);
            if (!Clear(state.Pose.Position, target) || UnityEngine.Physics.CheckSphere(target,
                CollisionRadius - .005f, m_Mask, QueryTriggerInteraction.Ignore)) return false;
            state.Pose.Position = target;
            state.Pose.Rotation = Quaternion.FromToRotation(state.Pose.Up, contact.normal) * state.Pose.Rotation;
            state.Surface = contact.collider; state.Supported = true; state.Detached = false;
            state.FlightVelocity = Vector3.zero; state.Corner = default;
            return true;
        }

        private bool Ray(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            return UnityEngine.Physics.Raycast(origin, direction, out hit, distance, m_Mask,
                QueryTriggerInteraction.Ignore);
        }

        private bool Attach(ref State state)
        {
            Vector3 normal = state.Pose.Up;
            if (!Ray(state.Pose.Position + normal * .16f, -normal, BodyHeight + .55f, out var hit))
            {
                state.Supported = false;
                return false;
            }
            if (Vector3.Dot(normal, hit.normal) < .6f) return false;
            var transport = Quaternion.FromToRotation(normal, hit.normal);
            state.Pose.Rotation = transport * state.Pose.Rotation;
            state.Pose.Position = hit.point + hit.normal * BodyHeight;
            state.Surface = hit.collider;
            state.Supported = true;
            return true;
        }

        private void PlanCorner(ref State state, Vector3 direction, float speed)
        {
            Vector3 p = state.Pose.Position, n = state.Pose.Up;
            float look = Mathf.Max(.85f, speed * .2f);
            // Concave: a wall is visible BEFORE current support is lost.
            if (Ray(p, direction, look + BodyHeight, out var front) &&
                Vector3.Dot(front.normal, direction) < -.25f && Vector3.Dot(front.normal, n) < .94f)
            {
                MakeCorner(ref state, direction, front, false);
                return;
            }
            // Convex: only search around an edge when future support is missing. The fan aims back
            // into the departing solid; it is not a global nearest-surface or downward-only search.
            Vector3 ahead = p + direction * look;
            if (Ray(ahead + n * .12f, -n, BodyHeight + .25f, out _)) return;
            for (int i = 0; i < 6; i++)
            {
                float angle = (15 + i * 13) * Mathf.Deg2Rad;
                Vector3 ray = -n * Mathf.Cos(angle) - direction * Mathf.Sin(angle);
                if (Ray(ahead + n * .12f, ray, look * 3 + BodyHeight, out var side) &&
                    Vector3.Dot(side.normal, direction) > .25f && Vector3.Dot(side.normal, n) < .94f)
                {
                    if (MakeCorner(ref state, direction, side, true)) return;
                }
            }
        }

        private bool MakeCorner(ref State state, Vector3 direction, RaycastHit target, bool convex)
        {
            var n0 = state.Pose.Up;
            var n1 = target.normal;
            float dot = Vector3.Dot(n0, n1);
            float degrees = Vector3.Angle(n0, n1);
            if (degrees < 15 || degrees > 135 || !state.Surface) return false;
            // Two local planes yield their intersection; collider names/types never select a route.
            Vector3 p = state.Pose.Position;
            float a = convex ? -BodyHeight : 0;
            float b = Vector3.Dot(target.point - p, n1) + (convex ? 0 : BodyHeight);
            float denominator = 1 - dot * dot;
            Vector3 intersection = p + n0 * ((a - dot * b) / denominator) +
                n1 * ((b - dot * a) / denominator);
            Vector3 axis = Vector3.Cross(n0, n1).normalized;
            Vector3 incoming = Vector3.Cross(axis, n0).normalized;
            if (Vector3.Dot(incoming, direction) < 0) { incoming = -incoming; axis = -axis; }
            // Parallel motion along an edge belongs to the flat surface, not a corner transition.
            if (Vector3.Dot(incoming, direction) < .35f) return false;
            float signedAngle = Vector3.SignedAngle(n0, n1, axis);
            Vector3 entry, center, radius;
            if (convex)
            {
                center = intersection;
                radius = n0 * BodyHeight;
                entry = center + radius;
            }
            else
            {
                entry = intersection - incoming * (CornerRadius * Mathf.Tan(degrees * .5f * Mathf.Deg2Rad));
                Vector3 bend = Quaternion.AngleAxis(signedAngle > 0 ? 90 : -90, axis) * incoming;
                center = entry + bend * CornerRadius;
                radius = entry - center;
            }
            float approach = Vector3.Dot(entry - p, incoming);
            if (approach < -.10f || approach > 1.8f) return false;
            var corner = new Corner
            {
                Active = true, Entry = entry, Center = center, Radius = radius, Axis = axis,
                Incoming = incoming, FromNormal = n0, ToNormal = n1,
                EntryRotation = state.Pose.Rotation, FromSurface = state.Surface,
                ToSurface = target.collider, Angle = signedAngle
            };
            // Reject paths that the body volume cannot traverse. Arc normals are transported from
            // actual ray hits; SphereCast normals are deliberately not used as surface orientation.
            Vector3 previous = entry;
            if (!Clear(p, entry)) return false;
            for (int i = 1; i <= 8; i++)
            {
                var next = center + Quaternion.AngleAxis(signedAngle * i / 8f, axis) * radius;
                if (!Clear(previous, next)) return false;
                previous = next;
            }
            state.Corner = corner;
            return true;
        }

        private bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            return delta.sqrMagnitude < .000001f || !UnityEngine.Physics.SphereCast(from,
                CollisionRadius, delta.normalized, out _, delta.magnitude, m_Mask, QueryTriggerInteraction.Ignore);
        }

        private void MoveCorner(ref State state, Vector3 direction, float distance)
        {
            var c = state.Corner;
            float sideways = Vector3.Dot(direction, c.Axis) * distance;
            Vector3 shift = c.Axis * sideways;
            c.Entry += shift; c.Center += shift;
            float forward = Vector3.Dot(direction, c.OnArc ?
                Quaternion.AngleAxis(c.Angle * c.Progress, c.Axis) * c.Incoming : c.Incoming);
            if (!c.OnArc)
            {
                if (forward < -.1f)
                {
                    state.Corner = default;
                    MoveSurface(ref state, direction * distance);
                    return;
                }
                float remaining = Mathf.Max(0, Vector3.Dot(c.Entry - state.Pose.Position, c.Incoming));
                float travel = Mathf.Max(0, forward * distance);
                if (travel < remaining)
                {
                    SetPosition(ref state, state.Pose.Position + c.Incoming * travel + shift);
                    state.Corner = c;
                    return;
                }
                distance = travel - remaining;
                c.OnArc = true;
                c.EntryRotation = state.Pose.Rotation;
                if (!SetPosition(ref state, c.Entry)) { state.Corner = default; return; }
                forward = 1;
            }
            float arcLength = c.Radius.magnitude * Mathf.Abs(c.Angle) * Mathf.Deg2Rad;
            float nextProgress = c.Progress + forward * distance / arcLength;
            float progress = Mathf.Clamp01(nextProgress);
            var q = Quaternion.AngleAxis(c.Angle * progress, c.Axis);
            var next = c.Center + q * c.Radius;
            if (!SetPosition(ref state, next)) { state.Corner = c; return; }
            state.Pose.Rotation = q * c.EntryRotation;
            c.Progress = progress;
            state.Supported = true;
            if (nextProgress >= 1 || nextProgress <= 0 && forward < 0)
            {
                state.Surface = nextProgress >= 1 ? c.ToSurface : c.FromSurface;
                state.Transitions++;
                state.Corner = default;
                // Consume the remainder in this SAME tick. Finishing a corner is never a zero-speed frame.
                float leftover = Mathf.Abs(nextProgress - progress) * arcLength;
                if (leftover > 0)
                    MoveSurface(ref state, (q * c.Incoming) * (forward >= 0 ? leftover : -leftover));
            }
            else state.Corner = c;
        }

        private void MoveSurface(ref State state, Vector3 delta)
        {
            var candidate = state;
            candidate.Pose.Position += delta;
            if (!Attach(ref candidate))
            {
                state.Supported = false;
                state.Blocked++;
                return;
            }
            if (SetPosition(ref state, candidate.Pose.Position))
            {
                state.Pose.Rotation = candidate.Pose.Rotation;
                state.Surface = candidate.Surface;
                state.Supported = true;
            }
        }

        private bool SetPosition(ref State state, Vector3 target)
        {
            Vector3 delta = target - state.Pose.Position;
            if (delta.sqrMagnitude < .0000001f) return true;
            if (UnityEngine.Physics.SphereCast(state.Pose.Position, CollisionRadius, delta.normalized,
                out var hit, delta.magnitude + ContactSkin, m_Mask, QueryTriggerInteraction.Ignore))
            {
                state.Pose.Position += delta.normalized * Mathf.Max(0, hit.distance - ContactSkin);
                state.Blocked++;
                return false;
            }
            state.Pose.Position = target;
            return true;
        }
    }
}
