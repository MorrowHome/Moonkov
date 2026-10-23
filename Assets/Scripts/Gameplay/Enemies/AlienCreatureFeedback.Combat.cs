using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class AlienCreatureFeedback
    {
        private AlienAttackEvents m_Events;
        private bool m_HasAttackSnapshot;
        private uint m_DetailSequence;
        private AlienAction m_DetailAction;
        private int m_DetailPulse = -1;
        private float m_GripAt;

        // Committed client snapshots supply timing/positions; these sounds never infer a hit or deal damage.
        public void ObserveAttackDetails(in ProceduralAlienEnemy.State state, in AlienCombatPattern pattern, Vector3 socket)
        {
            if (m_Disposed) return;
            m_StrikeDuration = Mathf.Max(.01f, pattern.Active);
            bool launch = m_Events.Launch(state);
            bool impact = m_Events.Impact(state);
            int pulse = pattern.PulseAt(state.ActionTime);
            if (!m_HasAttackSnapshot)
            {
                m_HasAttackSnapshot = true; m_DetailSequence = state.AttackSequence; m_DetailAction = state.Action;
                m_DetailPulse = state.Action == AlienAction.Strike ? pulse : -1;
                return; // Joining an active attack must not replay its launch or old pulses.
            }
            if (state.AttackSequence != 0 && m_DetailSequence != 0 &&
                unchecked((int)(state.AttackSequence - m_DetailSequence)) < 0) return;
            bool entry = state.Action != m_DetailAction;
            bool sequence = state.AttackSequence != m_DetailSequence;
            if (sequence) m_DetailPulse = -1;
            var library = MoonkovAudio.Library;
            if (launch)
            {
                bool spit = pattern.Kind == AlienAttackKind.MucusSpit || pattern.Kind == AlienAttackKind.AcidSpit;
                Play(Cue.AttackMotion, socket, spit ? .9f : .8f, spit ? library?.AlienSpit : library?.AlienSwing);
            }
            if (impact) Play(Cue.AttackImpact, state.ImpactPoint, .9f, library?.AlienImpact, true);
            if (state.Action == AlienAction.Strike && state.AttackSequence != 0 &&
                state.ActionTime <= pattern.Active && pulse > m_DetailPulse)
            {
                m_DetailPulse = pulse; // Only the current pulse; never catch up a burst of missed snapshots.
                switch (pattern.Kind)
                {
                    case AlienAttackKind.AcidSpit:
                    case AlienAttackKind.RootSpikes:
                    case AlienAttackKind.TentacleSlam:
                        Play(Cue.AttackImpact, pattern.PulsePoint(state.AttackPoint, state.AttackDirection, pulse), m_Tier == AlienThreatTier.Boss ? 1.1f : .8f, library?.AlienImpact, true);
                        break;
                    case AlienAttackKind.SlimePulse:
                        Play(Cue.AttackImpact, state.AttackOrigin, .75f, library?.AlienWetMove);
                        break;
                    case AlienAttackKind.TentacleSweep:
                    case AlienAttackKind.StiltRake:
                        if (pulse > 0) Play(Cue.AttackMotion, socket, .65f, library?.AlienSwing);
                        break;
                    case AlienAttackKind.LeechDrain:
                        Play(Cue.Grip, socket, .6f, library?.AlienGrip);
                        break;
                }
            }
            if (state.Action == AlienAction.Attached && (entry || Time.unscaledTime >= m_GripAt))
            {
                Play(Cue.Grip, socket, entry ? .8f : .45f, library?.AlienGrip);
                m_GripAt = Time.unscaledTime + 1.25f;
            }
            if (entry && m_DetailAction == AlienAction.Attached) Stop(Cue.Grip, .1f);
            m_DetailSequence = state.AttackSequence; m_DetailAction = state.Action;
        }

        private void PlayContact(Vector3 position, float gain)
        {
            var library = MoonkovAudio.Library;
            bool wet = m_Species == AlienSpecies.Slime || m_Species == AlienSpecies.Leech ||
                m_Species == AlienSpecies.Caterpillar || m_Species == AlienSpecies.Snake;
            var sound = m_Tier == AlienThreatTier.Boss || m_Species == AlienSpecies.StiltWalker ? library?.AlienHeavyStep :
                wet ? library?.AlienWetMove : library?.AlienContact;
            float closeGain = m_Tier != AlienThreatTier.Boss && m_Species != AlienSpecies.StiltWalker &&
                MoonkovAudio.CanHearDetail(position, 4) ? 2 : 1;
            Play(Cue.Step, position, (m_Tier == AlienThreatTier.Boss ? gain * 1.1f : gain) * closeGain, sound);
        }
    }
}
