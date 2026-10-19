using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private Vector3 m_BossDestination;
        private float m_NextBossMove, m_BossMoveHealth;
        private uint m_BossMoveAttack;
        public int BossRepositions { get; private set; }

        private bool TickBossMobility(float dt, PredictedPlayerGhost health, bool remembered)
        {
            if (!profile.Boss || profile.BossRepositionSpeed <= 0) return false;
            if (m_Action == AlienAction.Reposition)
            {
                if (!remembered || m_Clock-m_ActionStarted >= profile.BossRepositionSeconds ||
                    Vector3.ProjectOnPlane(m_BossDestination-transform.position,Vector3.up).sqrMagnitude < .2f)
                { ChangeAction(remembered ? AlienAction.Chase : AlienAction.Roam); return false; }
                Vector3 intent=Vector3.ProjectOnPlane(m_BossDestination-transform.position,Vector3.up).normalized;
                float yaw=Mathf.Clamp(Vector3.SignedAngle(transform.forward,intent,Vector3.up)/Mathf.Max(.001f,dt),-profile.TurnSpeed,profile.TurnSpeed);
                UpdateCombatPresentation(health.CurrentHealth);
                m_Body.Tick(intent,profile.BossRepositionSpeed*(m_BossPhase>1?1.15f:1),yaw,dt,0,false);
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
            m_BossDestination=floor+Vector3.up*m_Body.Motor.BodyHeight;
            m_BossMoveHealth=health.CurrentHealth;m_BossMoveAttack=m_Attacks;BossRepositions++;
            m_NextBossMove=m_Clock+profile.BossRepositionCooldown*(m_BossPhase>1?.75f:1);
            m_NextAttack=Mathf.Max(m_NextAttack,m_Clock+profile.BossRepositionSeconds+.1f);
            ChangeAction(AlienAction.Reposition);
            return TickBossMobility(dt,health,remembered);
        }
    }
}
