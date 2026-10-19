using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private bool m_BossIntroduced, m_EnrageDisplayed;
        private float m_StalkUntil, m_NextStalk, m_DirectUntil, m_ApproachBlocked;

        private bool TickBossDisplay(float dt, PredictedPlayerGhost health, bool remembered)
        {
            if (!profile.Boss) return false;
            bool enraged = health.CurrentHealth <= health.MaxHealth * profile.EnrageHealthFraction;
            if (m_Action == AlienAction.Dominance)
            {
                if (m_Clock - m_ActionStarted >= profile.BossDisplaySeconds)
                { ChangeAction(remembered ? AlienAction.Chase : AlienAction.Roam); return false; }
                UpdateCombatPresentation(health.CurrentHealth);
                m_Body.Tick(Vector3.zero, 0, 0, dt, .65f * Mathf.Sin(Mathf.Clamp01(
                    (m_Clock - m_ActionStarted) / profile.BossDisplaySeconds) * Mathf.PI), false);
                return true;
            }
            // Never interrupt a committed warning or hit window. Each display happens once.
            if (!remembered || !m_Visible || m_Action == AlienAction.Windup || m_Action == AlienAction.Strike ||
                m_Action == AlienAction.Recover || m_Action == AlienAction.Reposition) return false;
            if (m_BossIntroduced && (!enraged || m_EnrageDisplayed)) return false;
            m_BossIntroduced = true;
            if (enraged) { m_EnrageDisplayed = true; m_BossPhase = 2; }
            ChangeAction(AlienAction.Dominance);
            m_NextAttack = Mathf.Max(m_NextAttack, m_Clock + profile.BossDisplaySeconds + .3f);
            return TickBossDisplay(dt, health, remembered);
        }

        private Vector3 HuntGoal(Vector3 target, out bool pause)
        {
            pause = false;
            if (!m_Visible || profile.Boss || m_Clock < m_DirectUntil) return target;
            Vector3 offset = Vector3.ProjectOnPlane(target - transform.position, Vector3.up);
            float distance = offset.magnitude;
            if (distance < .01f) return target;
            Vector3 toward = offset / distance;
            float side = (GhostGameObject.Owner & 1) == 0 ? 1 : -1;
            Vector3 flank = Vector3.Cross(Vector3.up, toward) * side * profile.FlankDistance;
            if (profile.Approach == AlienApproach.KeepDistance)
            {
                if (distance < profile.PreferredDistance * .7f)
                    return transform.position - toward * 3 + flank * .35f;
                if (distance <= profile.PreferredDistance && m_Clock < m_NextAttack)
                { pause = true; return transform.position; }
            }
            if (profile.Approach == AlienApproach.Stalk && distance > profile.AttackReach + 1.5f)
            {
                var health = GhostGameObject.EntityManager.GetComponentData<PredictedPlayerGhost>(m_Target);
                var view = health.ControllerState;
                Vector3 looking = Quaternion.Euler(view.PitchDegrees + view.FreeLookOffset.y,
                    view.YawDegrees + view.FreeLookOffset.x, 0) * Vector3.forward;
                bool watched = Vector3.Dot(looking, -toward) > .65f;
                if (watched && m_Clock >= m_NextStalk)
                { m_StalkUntil = m_Clock + profile.StalkPauseSeconds; m_NextStalk = m_Clock + profile.StalkPauseSeconds + 5; }
                if (watched && m_Clock < m_StalkUntil) { pause = true; return transform.position; }
            }
            if ((profile.Approach == AlienApproach.Flank || profile.Approach == AlienApproach.Stalk) && distance > 3)
                return target + flank * Mathf.Clamp01((distance - 3) / 5);
            return target;
        }

        private void ObserveHuntMovement(Vector3 before, float speed, float dt)
        {
            if (m_Action != AlienAction.Chase || speed <= .1f) { m_ApproachBlocked = 0; return; }
            m_ApproachBlocked = Vector3.Distance(before, transform.position) < speed * dt * .15f ? m_ApproachBlocked + dt : 0;
            if (m_ApproachBlocked < 1.2f) return;
            // Bounded fallback prevents a flanking goal pinning the creature to a wall.
            m_DirectUntil = m_Clock + 2; m_ApproachBlocked = 0;
        }
    }
}
