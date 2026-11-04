using System;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private bool m_ManualDemonstration;

        // An isolated local scene owns the clock. Production ghosts never enter this path.
        internal void ConfigureDemonstration(AlienCreatureProfile source)
        {
            if (m_Body != null) throw new InvalidOperationException("Configure before linking the demonstration actor.");
            profile = source; m_ManualDemonstration = true;
        }

        internal bool BeginDemonstration(Entity target, AlienAttackKind kind, byte phase)
        {
            if (!m_ManualDemonstration || Role != MultiplayerRole.Server) return false;
            m_Target = target; m_LastSeen = ContactPoint(target); m_LastSeenAt = m_Clock; m_Visible = true;
            m_NextSense = float.PositiveInfinity;
            m_BossIntroduced = m_EnrageDisplayed = true; m_BossPhase = phase;
            m_NextBossMove = float.PositiveInfinity;
            var pattern = AlienCombatPattern.For(profile, phase, m_Attacks + 1, kind);
            if (m_AmbushEnabled)
            {
                if (kind != AlienAttackKind.RoofPounce) return false;
                m_NextAttack = m_Clock;
                UpdateServer(1f / 60);
                m_NextAttack = float.PositiveInfinity;
                return m_Action == AlienAction.Windup;
            }
            if (!CanBeginSpeciesAttack(pattern) || !CommitAttack(pattern, phase)) return false;
            m_NextAttack = float.PositiveInfinity;
            WriteState();
            return true;
        }
    }
}
