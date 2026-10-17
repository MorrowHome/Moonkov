using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private bool m_AmbushEnabled, m_StrikeHit;
        private float m_LastHealth, m_RepositionUntil, m_NextPerch;
        private Vector3 m_StrikePoint, m_StrikeOrigin, m_RepositionGoal;
        private AlienSurfaceNavigation m_Navigation;
        private readonly AlienSurfaceNavigation.Route m_Route = new AlienSurfaceNavigation.Route();
        public bool AmbushEnabled => m_AmbushEnabled;
        public string NavigationFailure => m_Route.LastFailure;
        public Vector3 RepositionGoal => m_RepositionGoal;

        private void UpdateCombatPresentation(float health)
        {
            m_Body.SetCombatPresentation(m_Action, m_Clock - m_ActionStarted, m_Attacks, health,
                m_AmbushEnabled ? profile.SpiderWindup : Mathf.Max(.1f, m_Pattern.Windup),
                m_AmbushEnabled ? profile.SpiderRecovery : Mathf.Max(.1f, m_Pattern.Recovery),
                m_AmbushEnabled ? profile.SpiderStrikeSeconds : Mathf.Max(.1f, m_Pattern.Active));
        }

        private void UpdateAmbush(float dt, float health)
        {
            if (!m_Navigation) m_Navigation = AlienSurfaceNavigation.Find(m_Spawn);
            bool hurt = health < m_LastHealth - .01f;
            m_LastHealth = health;
            var manager = GhostGameObject.EntityManager;
            if (m_Clock >= m_NextSense && m_Action != AlienAction.Windup && m_Action != AlienAction.Strike)
            { Sense(); m_NextSense = m_Clock + .15f; }
            bool living = IsLivingHumanoid(manager, m_Target);
            if (!living) { m_Target = Entity.Null; m_Visible = false; }
            bool remembered = living && m_Clock - m_LastSeenAt <= profile.MemorySeconds;
            // The encounter has a physical navigation boundary; do not pursue through
            // walls outside it, or acquire the precise position of an unseen target.
            if (remembered && m_Navigation && !m_Navigation.Contains(m_LastSeen)) remembered = false;
            float age = m_Clock - m_ActionStarted;
            float distance = living && m_Visible ? Vector3.Distance(AimPoint, ContactPoint(m_Target)) : float.MaxValue;

            if ((m_Action == AlienAction.Windup || m_Action == AlienAction.Strike) && !living)
                ChangeAction(AlienAction.Recover);
            else if (m_Action == AlienAction.Windup && age >= Mathf.Max(.3f, profile.SpiderWindup))
            {
                ChangeAction(AlienAction.Strike); m_StrikeHit = false;
                m_StrikeOrigin = m_Body.Motor.Current.Position;
                float flightTime = Mathf.Max(.2f, profile.SpiderStrikeSeconds);
                Vector3 velocity = (m_StrikePoint - m_StrikeOrigin) / flightTime + Vector3.up * (10 * flightTime);
                m_Body.Launch(Vector3.ClampMagnitude(velocity, 18));
            }
            else if (m_Action == AlienAction.Strike && age >= Mathf.Max(.2f, profile.SpiderStrikeSeconds))
                ChangeAction(AlienAction.Recover);
            else if (m_Action == AlienAction.Recover && age >= Mathf.Max(.3f, profile.SpiderRecovery) && !m_Body.Motor.Detached)
            {
                BeginReposition(remembered ? m_LastSeen : m_Spawn);
            }
            else if (m_Action != AlienAction.Windup && m_Action != AlienAction.Strike && m_Action != AlienAction.Recover)
            {
                if (m_Action == AlienAction.Reposition &&
                    (m_Body.Motor.Current.Up.y > -.65f || Vector3.Distance(transform.position, m_RepositionGoal) > .8f))
                {
                    if (m_Clock >= m_RepositionUntil) BeginReposition(remembered ? m_LastSeen : m_Spawn);
                }
                else if (remembered && m_Visible && distance <= profile.SpiderAmbushRange && m_Clock >= m_NextAttack)
                {
                    // Commit to a point at the start of the visible windup. A player
                    // can evade it; flight never steers towards their new position.
                    m_StrikePoint = ContactPoint(m_Target);
                    m_Pattern = AlienCombatPattern.For(profile, 1, m_Attacks + 1);
                    m_CombatPoint = m_StrikePoint; m_CombatOrigin = transform.position;
                    m_CombatDirection = (m_StrikePoint - transform.position).normalized;
                    ChangeAction(AlienAction.Windup); m_Attacks++;
                    m_NextAttack = m_Clock + Mathf.Max(profile.AttackCooldown,
                        profile.SpiderWindup + profile.SpiderStrikeSeconds + profile.SpiderRecovery + .6f);
                }
                else if (hurt && remembered) BeginReposition(m_LastSeen);
                else if (remembered) ChangeAction(m_Visible ? AlienAction.Chase : AlienAction.Search);
                else if (Vector3.Distance(transform.position, m_Spawn) > .6f)
                { m_RepositionGoal = m_Spawn; ChangeAction(AlienAction.Reposition); }
                else ChangeAction(AlienAction.Ambush);
            }

            Vector3 intent = Vector3.zero;
            float speed = 0, yaw = 0;
            if (m_Action == AlienAction.Chase || m_Action == AlienAction.Search || m_Action == AlienAction.Reposition)
            {
                Vector3 destination = m_Action == AlienAction.Reposition ? m_RepositionGoal : m_LastSeen;
                if (m_Navigation && m_Navigation.TryGetSteering(m_Route, m_Body.Motor, destination, m_Clock, out var waypoint))
                {
                    intent = Vector3.ProjectOnPlane(waypoint - m_Body.Motor.Current.Position, m_Body.Motor.Current.Up).normalized;
                    speed = profile.Speed * m_Body.Scale * (m_Action == AlienAction.Search ? .55f : .85f);
                    yaw = Mathf.Clamp(Vector3.SignedAngle(m_Body.Motor.Current.Forward, intent, m_Body.Motor.Current.Up) /
                        Mathf.Max(.001f, dt), -profile.TurnSpeed, profile.TurnSpeed);
                }
                // No straight-line fallback: an unreachable door/wall ends movement.
            }
            UpdateCombatPresentation(health);
            m_Body.Tick(intent, speed, yaw, dt, ActionPulse(), false);
            if (m_Action == AlienAction.Strike && !m_StrikeHit && m_Clock - m_ActionStarted >= .06f && living)
            {
                Vector3 committed = m_StrikePoint - m_StrikeOrigin;
                Vector3 current = ContactPoint(m_Target) - m_StrikeOrigin;
                if (Vector3.Angle(committed, current) < 50)
                {
                    uint before = m_Hits; TryStrike(); m_StrikeHit = m_Hits != before;
                }
            }
        }

        private void BeginReposition(Vector3 target)
        {
            m_RepositionGoal = m_Spawn;
            if (m_Navigation && m_Clock >= m_NextPerch)
            {
                if (m_Navigation.TryFindPerch(transform.position, target, 12, out var perch, m_Body.Scale))
                    m_RepositionGoal = perch;
                m_NextPerch = m_Clock + 2;
            }
            m_RepositionUntil = m_Clock + 12;
            ChangeAction(AlienAction.Reposition);
        }
    }
}
