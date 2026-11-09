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
            if (profile.IsSpiderBoss)
            {
                if (TryBossPattern(AlienAttackKind.SpiderRake, phase, m_NextBossSweep, out next)) return true;
                Vector3 target = ContactPoint(m_Target);
                float distance = GroundDistance(target, transform.position);
                bool outsideMelee = distance > profile.AttackReach * m_Body.Scale;
                if (outsideMelee &&
                    TryBossPattern(AlienAttackKind.SpiderRush, phase, m_NextBossArea, out next)) return true;
                // One distant opener, then use the ground route to close. A blocked
                // route or a target on another floor still permits a ranged attack.
                bool canSpit = !GroundStrikeLevel(target) || outsideMelee &&
                    (m_Clock < m_BlockedGroundUntil || m_Attacks == 0 && distance > profile.SpiderAmbushRange);
                return canSpit && TryBossPattern(AlienAttackKind.MucusSpit, phase, m_NextBossSpit, out next);
            }
            else if (profile.Species == AlienSpecies.Roots)
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
            if (kind != AlienAttackKind.MucusSpit && !GroundStrikeLevel(ContactPoint(m_Target))) return false;
            return CanBeginSpeciesAttack(pattern) &&
                (kind != AlienAttackKind.SpiderRush || TrySpiderRushLanding(transform.position, m_LastSeen - Vector3.up, out _));
        }

        private bool GroundStrikeLevel(Vector3 target) =>
            Mathf.Abs(target.y - (transform.position.y - m_Body.Motor.BodyHeight + 1)) <= 1.5f;

        private bool TrySpiderRushLanding(Vector3 origin, Vector3 target, out Vector3 landing)
        {
            landing = default;
            Vector3 direction = Vector3.ProjectOnPlane(target - origin, Vector3.up).normalized;
            float distance = GroundDistance(target, origin);
            if (distance > profile.SpiderAmbushRange || distance < .1f) return false;
            Vector3 foot = origin - Vector3.up * m_Body.Motor.BodyHeight;
            float radius = profile.GroundBodyRadius(m_Body.Scale), height = profile.GroundBodyHeight(m_Body.Scale);
            int mask = LayerMask.GetMask("Default", "Ground");
            // Reject blocked lanes before selecting this attack, so another legal
            // skill can be chosen. The grounded motor rechecks moving obstacles.
            if (UnityEngine.Physics.CapsuleCast(foot + Vector3.up * (radius + .04f),
                foot + Vector3.up * (height - radius + .04f), radius, direction, distance, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!RaidSpawnSafety.TryGroundPosition(target, radius, height, 35, out landing, out _) ||
                Mathf.Abs(landing.y - foot.y) > .4f) return false;
            // Bounded samples avoid committing a rush across a hole in the floor.
            for (int i = 1; i <= 4; i++)
                if (!UnityEngine.Physics.Raycast(Vector3.Lerp(foot, landing, i / 5f) + Vector3.up * .45f,
                    Vector3.down, out var floor, .9f, mask, QueryTriggerInteraction.Ignore) || floor.normal.y < .75f) return false;
            return true;
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
            if (next.Kind == AlienAttackKind.SpiderRush && !TrySpiderRushLanding(origin, target, out target)) return false;
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
                float ready = m_Clock + cycle + (next.Kind == AlienAttackKind.SpiderRake ? .45f : phase > 1 ? 1.2f : 2.5f);
                if (next.Kind == AlienAttackKind.MucusSpit) m_NextBossSpit = ready;
                else if (next.Kind == AlienAttackKind.TentacleSweep || next.Kind == AlienAttackKind.SlimePulse || next.Kind == AlienAttackKind.SpiderRake) m_NextBossSweep = ready;
                else m_NextBossArea = ready;
            }
            return true;
        }
    }
}
