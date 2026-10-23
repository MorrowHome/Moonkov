using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private Entity m_MotionTarget;
        private Vector3 m_ObservedTargetPosition, m_ObservedTargetVelocity;
        private float m_TargetSampleAt = -100;
        private float m_NextBossSweep, m_NextBossArea, m_NextBossSpit;

        // Sample only the final visible perception result, never a hidden live transform.
        private void ObserveTargetMotion()
        {
            float elapsed = m_Clock - m_TargetSampleAt;
            if (!m_Visible) { m_MotionTarget = Entity.Null; m_ObservedTargetVelocity = Vector3.zero; return; }
            if (m_MotionTarget == m_Target && elapsed >= .1f && elapsed <= .6f)
            {
                Vector3 velocity = Vector3.ProjectOnPlane((m_LastSeen - m_ObservedTargetPosition) / elapsed, Vector3.up);
                m_ObservedTargetVelocity = velocity.magnitude <= 12 ? Vector3.Lerp(m_ObservedTargetVelocity, velocity, .65f) : Vector3.zero;
            }
            else m_ObservedTargetVelocity = Vector3.zero;
            m_MotionTarget = m_Target; m_ObservedTargetPosition = m_LastSeen; m_TargetSampleAt = m_Clock;
        }

        private bool TryChooseAttack(byte phase, out AlienCombatPattern next)
        {
            next = AlienCombatPattern.For(profile, phase, m_Attacks + 1);
            if (!profile.Boss)
            {
                // A pulse outside its actual radius used to strand the slime in empty attacks.
                if (profile.Species == AlienSpecies.Slime && !CanBeginSpeciesAttack(next) && profile.MucusEveryAttacks > 0)
                    next = AlienCombatPattern.For(profile, phase, m_Attacks + 1, AlienAttackKind.MucusSpit);
                return CanBeginSpeciesAttack(next);
            }
            if (profile.Species == AlienSpecies.Roots)
            {
                if (TryBossPattern(AlienAttackKind.SlimePulse, phase, m_NextBossSweep, out next)) return true;
                if (TryBossPattern(AlienAttackKind.RootSpikes, phase, m_NextBossArea, out next)) return true;
            }
            else
            {
                if (TryBossPattern(AlienAttackKind.TentacleSweep, phase, m_NextBossSweep, out next)) return true;
                if (TryBossPattern(AlienAttackKind.TentacleSlam, phase, m_NextBossArea, out next)) return true;
            }
            return TryBossPattern(AlienAttackKind.MucusSpit, phase, m_NextBossSpit, out next);
        }

        private bool TryBossPattern(AlienAttackKind kind, byte phase, float readyAt, out AlienCombatPattern pattern)
        {
            pattern = AlienCombatPattern.For(profile, phase, m_Attacks + 1, kind);
            if (m_Clock < readyAt) return false;
            // Ground strikes must not lock onto an unrelated floor below an elevated player.
            if (kind != AlienAttackKind.MucusSpit &&
                Mathf.Abs(ContactPoint(m_Target).y - (transform.position.y - m_Body.Motor.BodyHeight + 1)) > 1.5f) return false;
            return CanBeginSpeciesAttack(pattern);
        }

        private bool CommitAttack(AlienCombatPattern next, byte phase)
        {
            bool head = next.Kind == AlienAttackKind.FaceLeap || next.Kind == AlienAttackKind.MucusSpit;
            Vector3 target = head ? TargetHead(m_Target).position : m_LastSeen - Vector3.up;
            Vector3 origin = next.Kind == AlienAttackKind.MucusSpit ? MucusSocket() : transform.position;
            bool aimed = next.Kind == AlienAttackKind.RootSpikes || next.Kind == AlienAttackKind.TentacleSlam ||
                next.Kind == AlienAttackKind.AcidSpit || next.Kind == AlienAttackKind.MucusSpit ||
                next.Kind == AlienAttackKind.Charge || next.Kind == AlienAttackKind.Bite || next.Kind == AlienAttackKind.FaceLeap;
            if (aimed && m_MotionTarget == m_Target && m_Visible && m_Clock - m_TargetSampleAt <= .6f)
            {
                float leadTime = next.Kind == AlienAttackKind.MucusSpit ? Mathf.Min(.7f, next.Windup * .45f +
                    AlienMucusTrajectory.FlightTime(origin, target, profile.MucusSpeed) * .5f) : Mathf.Min(.6f, next.Windup * .45f);
                Vector3 proposed = target + Vector3.ClampMagnitude(m_ObservedTargetVelocity * leadTime, profile.Boss ? 3 : 1.2f);
                int mask = LayerMask.GetMask("Default", "Ground");
                // Predictive aim cannot route through a wall the target has not crossed.
                if (!UnityEngine.Physics.Linecast(target + (head ? Vector3.zero : Vector3.up),
                    proposed + (head ? Vector3.zero : Vector3.up), mask, QueryTriggerInteraction.Ignore)) target = proposed;
            }
            if (next.Kind == AlienAttackKind.MucusSpit && !PrepareMucusArc(target)) return false;
            if (!head && UnityEngine.Physics.Raycast(target + Vector3.up * 2, Vector3.down,
                out var floor, 6, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) target = floor.point + floor.normal * .04f;
            m_Pattern = next; m_BossPhase = phase; m_Attacks++;
            m_CombatOrigin = origin; m_CombatPoint = target;
            m_CombatDirection = Vector3.ProjectOnPlane(target - transform.position, Vector3.up).normalized;
            if (next.Kind == AlienAttackKind.RootSpikes && m_ObservedTargetVelocity.sqrMagnitude > 1 && m_MotionTarget == m_Target)
                m_CombatDirection = m_ObservedTargetVelocity.normalized;
            if (m_CombatDirection.sqrMagnitude < .01f) m_CombatDirection = transform.forward;
            ChangeAction(AlienAction.Windup);
            float cycle = next.Windup + next.Active + next.Recovery + .3f;
            m_NextAttack = m_Clock + Mathf.Max(profile.AttackCooldown, cycle);
            if (profile.Boss)
            {
                // Each tool has its own recovery budget. When it is unavailable, use a
                // different legal tool or move; never bypass a cooldown by repeating a shot.
                float ready = m_Clock + cycle + (phase > 1 ? 1.2f : 2.5f);
                if (next.Kind == AlienAttackKind.MucusSpit) m_NextBossSpit = ready;
                else if (next.Kind == AlienAttackKind.TentacleSweep || next.Kind == AlienAttackKind.SlimePulse) m_NextBossSweep = ready;
                else m_NextBossArea = ready;
            }
            return true;
        }
    }
}
