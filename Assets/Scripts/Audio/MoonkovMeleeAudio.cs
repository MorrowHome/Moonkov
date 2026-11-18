// 每个角色各自记住近战动作的进度，把新一轮挥击、发力和命中结果变成一次性的声音提示。由 MoonkovPlayerAudio 统一安排人声。
using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS {
    // Consumes rendered snapshots once, never the prediction replay loop.
    public sealed class MoonkovMeleeAudio {
        [System.Flags]
        public enum Cues {
            None = 0,
            Equip = 1,
            Swing = 2,
            Hit = 4,
            Effort = 8
        }

        private bool m_Initialized;
        private int m_Epoch;
        private byte m_Weapon;
        private uint m_Attack, m_Hit, m_Cancel;
        private bool m_PendingSwing;
        private SoundSystem.SoundInfo m_EffortSound;
        public bool VoicePlaying => m_EffortSound?.Handle?.IsValid() == true;

        public Cues Observe(in PredictedPlayerGhost state) {
            // 首次观察某一轮近战状态时，只记武器和各事件序号。切换时期也以新快照为起点，之前挥过的动作不重新响。
            if (!m_Initialized || m_Epoch != state.MeleeEpoch) {
                m_Initialized = true;
                m_Epoch = state.MeleeEpoch;
                m_Weapon = state.MeleeWeapon;
                m_Attack = state.MeleeAttackSequence;
                m_Hit = state.MeleeHitSequence;
                m_Cancel = state.MeleeCancelSequence;
                m_PendingSwing = false;
                return Cues.None;
            }
            bool equip = m_Weapon != state.MeleeWeapon;
            bool attack = MoonkovPlayerAudio.NewTick(state.MeleeAttackSequence, ref m_Attack);
            bool hit = MoonkovPlayerAudio.NewTick(state.MeleeHitSequence, ref m_Hit);
            bool cancel = m_Cancel != state.MeleeCancelSequence;
            m_Cancel = state.MeleeCancelSequence;
            m_Weapon = state.MeleeWeapon;
            // 服务器开始攻击后还要等前摇走完。取消、换武器、死亡或暗视等状态会放下这次等待中的挥击声。
            if (attack) m_PendingSwing = true;
            bool alive = EnergyShieldRules.Alive(state);
            if (cancel || equip || !alive || state.ClashDarkSight) m_PendingSwing = false;
            if (!alive || state.ClashDarkSight) return Cues.None;
            var cues = (equip ? Cues.Equip : Cues.None) | (hit ? Cues.Hit : Cues.None);
            if (m_PendingSwing && state.MeleeAttackSequence == m_Attack &&
                MeleeRules.TryDefinition((MeleeWeaponKind)state.MeleeAttackWeapon, out var definition)) {
                var timing = definition.Timing((MeleeAttackKind)state.MeleeAttack, state.MeleeCombo);
                // Allow a slightly delayed snapshot, but never replay an already finished attack.
                // 过前摇才响，允许网络把消息稍晚带到。完整的有效时间都过去时就把那次声音略过。
                if (state.MeleeElapsed >= timing.Windup) {
                    m_PendingSwing = false;
                    if (state.MeleeElapsed < timing.Windup + timing.Active + .1f &&
                        state.MeleePhase != (byte)MeleePhase.Idle) {
                        cues |= Cues.Swing;
                        if ((MeleeWeaponKind)state.MeleeAttackWeapon == MeleeWeaponKind.Fists) cues |= Cues.Effort;
                    }
                }
            }
            return cues;
        }

        public bool Update(in PredictedPlayerGhost state, Vector3 position, bool owned, bool allowVoice = true) {
            if (!allowVoice || !EnergyShieldRules.Alive(state) || state.ClashDarkSight || (m_Initialized &&
                    (m_Epoch != state.MeleeEpoch || m_Cancel != state.MeleeCancelSequence ||
                     m_Weapon != state.MeleeWeapon)))
                Stop();
            var cues = Observe(state);
            var library = MoonkovAudio.Library;
            if (!library) return false;
            bool effort = false;
            if ((cues & Cues.Equip) != 0 && state.MeleeWeapon != (byte)MeleeWeaponKind.None)
                MoonkovAudio.PlayWeaponLayer(
                    state.MeleeWeapon == (byte)MeleeWeaponKind.Fists ? library.InventoryCloth : library.MeleeDraw,
                    position, .8f, owned ? 0 : 1);
            if ((cues & Cues.Swing) != 0) {
                bool sword = (MeleeWeaponKind)state.MeleeAttackWeapon == MeleeWeaponKind.EnergySword;
                bool fists = (MeleeWeaponKind)state.MeleeAttackWeapon == MeleeWeaponKind.Fists;
                bool heavy = (MeleeAttackKind)state.MeleeAttack == MeleeAttackKind.Heavy;
                MoonkovAudio.PlayWeaponLayer(sword ? library.MeleeEnergySwing : library.MeleeSwing, position,
                    fists ? heavy ? .38f : .24f : heavy ? 1 : .7f, owned ? .1f : 1, heavy ? -160 : 40);
                if ((cues & Cues.Effort) != 0 && allowVoice) {
                    Stop();
                    m_EffortSound = MoonkovAudio.PlayWeaponLayer(library.MeleeEffort, position + Vector3.up * 1.4f,
                        heavy ? 1 : .82f, owned ? .05f : 1);
                    effort = m_EffortSound != null;
                }
            }
            // 碰撞音从已确认的命中位置播放，再根据击中或被挡选音色。挥击声则以发动的角色位置为基准。
            if ((cues & Cues.Hit) != 0)
                MoonkovAudio.Play(state.MeleeHitBlocked ? library.MeleeBlocked : library.MeleeImpact,
                    state.MeleeHitPoint, .8f);
            return effort;
        }

        public void Stop() {
            GameManager.Instance?.SoundSystem?.Stop(m_EffortSound, .04f);
            m_EffortSound = null;
        }
    }
}