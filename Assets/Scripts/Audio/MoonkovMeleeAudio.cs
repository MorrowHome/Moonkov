using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS
{
    // Consumes rendered snapshots once, never the prediction replay loop.
    public sealed class MoonkovMeleeAudio
    {
        [System.Flags] public enum Cues { None = 0, Equip = 1, Swing = 2, Hit = 4 }
        private bool m_Initialized;
        private int m_Epoch;
        private byte m_Weapon;
        private uint m_Attack, m_Hit, m_Cancel;
        private bool m_PendingSwing;

        public Cues Observe(in PredictedPlayerGhost state)
        {
            if (!m_Initialized || m_Epoch != state.MeleeEpoch)
            {
                m_Initialized = true; m_Epoch = state.MeleeEpoch; m_Weapon = state.MeleeWeapon;
                m_Attack = state.MeleeAttackSequence; m_Hit = state.MeleeHitSequence;
                m_Cancel = state.MeleeCancelSequence; m_PendingSwing = false;
                return Cues.None;
            }
            bool equip = m_Weapon != state.MeleeWeapon;
            bool attack = MoonkovPlayerAudio.NewTick(state.MeleeAttackSequence, ref m_Attack);
            bool hit = MoonkovPlayerAudio.NewTick(state.MeleeHitSequence, ref m_Hit);
            bool cancel = m_Cancel != state.MeleeCancelSequence;
            m_Cancel = state.MeleeCancelSequence; m_Weapon = state.MeleeWeapon;
            if (attack) m_PendingSwing = true;
            bool alive = EnergyShieldRules.Alive(state);
            if (cancel || equip || !alive || state.ClashDarkSight) m_PendingSwing = false;
            if (!alive || state.ClashDarkSight) return Cues.None;
            var cues = (equip ? Cues.Equip : Cues.None) | (hit ? Cues.Hit : Cues.None);
            if (m_PendingSwing && state.MeleeAttackSequence == m_Attack &&
                MeleeRules.TryDefinition((MeleeWeaponKind)state.MeleeAttackWeapon, out var definition))
            {
                var timing = definition.Timing((MeleeAttackKind)state.MeleeAttack, state.MeleeCombo);
                // Allow a slightly delayed snapshot, but never replay an already finished attack.
                if (state.MeleeElapsed >= timing.Windup)
                {
                    m_PendingSwing = false;
                    if (state.MeleeElapsed < timing.Windup + timing.Active + .1f &&
                        state.MeleePhase != (byte)MeleePhase.Idle) cues |= Cues.Swing;
                }
            }
            return cues;
        }

        public void Update(in PredictedPlayerGhost state, Vector3 position, bool owned)
        {
            var cues = Observe(state);
            var library = MoonkovAudio.Library;
            if (!library) return;
            if ((cues & Cues.Equip) != 0)
                MoonkovAudio.PlayWeaponLayer(library.Equipment, position, .65f, owned ? 0 : 1);
            if ((cues & Cues.Swing) != 0)
            {
                bool sword = (MeleeWeaponKind)state.MeleeAttackWeapon == MeleeWeaponKind.EnergySword;
                bool heavy = (MeleeAttackKind)state.MeleeAttack == MeleeAttackKind.Heavy;
                MoonkovAudio.PlayWeaponLayer(sword ? library.MeleeEnergySwing : library.MeleeSwing,
                    position, heavy ? 1 : .7f, owned ? .1f : 1, heavy ? -160 : 40);
            }
            if ((cues & Cues.Hit) != 0)
                MoonkovAudio.Play(state.MeleeHitBlocked ? library.MeleeBlocked : library.MeleeImpact,
                    state.MeleeHitPoint, .8f);
        }
    }
}
