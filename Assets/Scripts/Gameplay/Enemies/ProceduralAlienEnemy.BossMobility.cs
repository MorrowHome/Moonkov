using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private Vector3 m_BossDestination;
        private float m_NextBossMove, m_BossMoveHealth, m_BossMoveStarted = -1;
        private bool m_BossMoveCounted;
        private uint m_BossMoveAttack;
        public int BossRepositions { get; private set; }

        private bool TickBossMobility(float dt, PredictedPlayerGhost health, bool remembered)
        {
            if (!profile.Boss || profile.BossRepositionSpeed <= 0) return false;
            if (m_Action == AlienAction.Reposition)
            {
                if (!remembered || m_Clock-m_ActionStarted >= profile.BossRepositionSeconds + .8f ||
                    m_BossMoveStarted >= 0 && m_Clock-m_BossMoveStarted >= profile.BossRepositionSeconds ||
                    Vector3.ProjectOnPlane(m_BossDestination-transform.position,Vector3.up).sqrMagnitude < .2f)
                { ChangeAction(remembered ? AlienAction.Chase : AlienAction.Roam); return false; }
                Vector3 waypoint = UprightWaypoint(m_BossDestination);
                Vector3 intent=Vector3.ProjectOnPlane(waypoint-transform.position,Vector3.up).normalized;
                // A newly requested sidestep may arrive during the previous plan's
                // .75s cache window. Wait for its own checked route, without bypassing
                // the planner's rate/budget or spending the whole dash on a stale goal.
                if (intent.sqrMagnitude < .01f || Vector3.Distance(m_UprightGoal, m_BossDestination) > .1f)
                {
                    UpdateCombatPresentation(health.CurrentHealth);
                    m_Body.Tick(Vector3.zero, 0, 0, dt, 0, false);
                    return true;
                }
                if (m_BossMoveStarted < 0) m_BossMoveStarted = m_Clock;
                Vector3 facing = m_Visible ? Vector3.ProjectOnPlane(m_LastSeen-transform.position,Vector3.up).normalized : intent;
                float yaw=Mathf.Clamp(Vector3.SignedAngle(transform.forward,facing,Vector3.up)/Mathf.Max(.001f,dt),-profile.TurnSpeed,profile.TurnSpeed);
                UpdateCombatPresentation(health.CurrentHealth);
                Vector3 before = transform.position;
                m_Body.Tick(intent,profile.BossRepositionSpeed*(m_BossPhase>1?1.15f:1),yaw,dt,0,false);
                if (!m_BossMoveCounted && Vector3.Distance(before, transform.position) > .001f)
                { BossRepositions++; m_BossMoveCounted = true; }
                return true;
            }
            if (m_BossMoveHealth<=0) m_BossMoveHealth=health.CurrentHealth;
            if (!remembered || !m_Visible || m_Clock<m_NextBossMove ||
                m_Action==AlienAction.Windup || m_Action==AlienAction.Strike || m_Action==AlienAction.Recover || m_Action==AlienAction.Dominance) return false;
            Vector3 target=m_LastSeen, delta=Vector3.ProjectOnPlane(target-transform.position,Vector3.up);
            float distance=delta.magnitude;
            bool damaged=m_BossMoveHealth-health.CurrentHealth>health.MaxHealth*.08f;
            bool far=distance>profile.BossPreferredDistance*1.4f;
            bool afterAttack=m_Attacks>m_BossMoveAttack;
            if ((!far && !damaged && !afterAttack) || distance<1 || distance>profile.SightRange) return false;
            Vector3 forward=delta.normalized;
            Vector3 desired=far ? target-forward*profile.BossPreferredDistance :
                transform.position+Vector3.Cross(Vector3.up,forward)*(m_Attacks%2==0?3:-3)-forward*1.2f;
            desired=transform.position+Vector3.ClampMagnitude(desired-transform.position,profile.BossRepositionSpeed*profile.BossRepositionSeconds);
            float radius=profile.GroundBodyRadius(m_Body.Scale);
            if (!RaidSpawnSafety.TryGroundPosition(desired,radius,profile.GroundBodyHeight(m_Body.Scale),35,out var floor,out _))
            {m_NextBossMove=m_Clock+1;return false;}
            m_BossDestination=floor;
            m_BossMoveHealth=health.CurrentHealth;m_BossMoveAttack=m_Attacks;
            m_BossMoveStarted=-1;m_BossMoveCounted=false;
            m_NextBossMove=m_Clock+profile.BossRepositionCooldown*(m_BossPhase>1?.75f:1);
            m_NextAttack=Mathf.Max(m_NextAttack,m_Clock+profile.BossRepositionSeconds+.1f);
            ChangeAction(AlienAction.Reposition);
            return TickBossMobility(dt,health,remembered);
        }
    }
}
