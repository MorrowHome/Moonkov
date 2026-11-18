// 异形声音里和攻击直接相关的部分。按快照中的攻击编号、命中事件和当前节拍播挥击、喷吐、打中和抓附声。
// 第一次收到快照只记录进度，晚加入时不会把之前的声音补播一遍。
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class AlienCreatureFeedback {
        private AlienAttackEvents m_Events;
        private bool m_HasAttackSnapshot;
        private uint m_DetailSequence;
        private AlienAction m_DetailAction;
        private int m_DetailPulse = -1;
        private float m_GripAt;

        // 音效用客户端已确认的快照时间和位置。是否命中和扣血由服务端决定。
        public void ObserveAttackDetails(
            in ProceduralAlienEnemy.State state,
            in AlienCombatPattern pattern,
            Vector3 socket) {
            if (m_Disposed) return;
            m_StrikeDuration = Mathf.Max(.01f, pattern.Active);
            bool launch = m_Events.Launch(state);
            bool impact = m_Events.Impact(state);
            int pulse = pattern.PulseAt(state.ActionTime);
            // 首包只用来记服务器走到了第几拍，随后新到的变化才算事件。
            if (!m_HasAttackSnapshot) {
                m_HasAttackSnapshot = true;
                m_DetailSequence = state.AttackSequence;
                m_DetailAction = state.Action;
                m_DetailPulse = state.Action == AlienAction.Strike ? pulse : -1;
                return; // 中途加入时跳过已播过的发射和命中节拍。
            }
            // 判断序号前后顺序，防止较旧的快照重新触发已经消费过的攻击。
            if (state.AttackSequence != 0 && m_DetailSequence != 0 &&
                unchecked((int)(state.AttackSequence - m_DetailSequence)) < 0)
                return;
            bool entry = state.Action != m_DetailAction;
            bool sequence = state.AttackSequence != m_DetailSequence;
            if (sequence) m_DetailPulse = -1;
            var library = MoonkovAudio.Library;
            if (launch) {
                bool spit = pattern.Kind == AlienAttackKind.MucusSpit || pattern.Kind == AlienAttackKind.AcidSpit;
                Play(Cue.AttackMotion, socket, spit ? .9f : .8f, spit ? library?.AlienSpit : library?.AlienSwing);
            }
            if (impact) Play(Cue.AttackImpact, state.ImpactPoint, .9f, library?.AlienImpact, true);
            // 只播放眼前这次节拍。丢了中间的包也不把错过的多声命中堆在同一帧。
            if (state.Action == AlienAction.Strike && state.AttackSequence != 0 && state.ActionTime <= pattern.Active &&
                pulse > m_DetailPulse) {
                m_DetailPulse = pulse; // 只播目前这一拍，漏掉的多个快照不用追着补播。
                switch (pattern.Kind) {
                    case AlienAttackKind.AcidSpit:
                    case AlienAttackKind.RootSpikes:
                    case AlienAttackKind.TentacleSlam:
                        Play(Cue.AttackImpact, pattern.PulsePoint(state.AttackPoint, state.AttackDirection, pulse),
                            m_Tier == AlienThreatTier.Boss ? 1.1f : .8f, library?.AlienImpact, true);
                        break;
                    case AlienAttackKind.SlimePulse:
                        Play(Cue.AttackImpact, state.AttackOrigin, .75f, library?.AlienWetMove);
                        break;
                    case AlienAttackKind.TentacleSweep:
                    case AlienAttackKind.StiltRake:
                        if (pulse > 0) Play(Cue.AttackMotion, socket, .65f, library?.AlienSwing);
                        break;
                    case AlienAttackKind.LeechDrain: Play(Cue.Grip, socket, .6f, library?.AlienGrip); break;
                }
            }
            // 覆面虫抓住时可按间隔追加抓附声，脱离时把这一循环收掉。
            if (state.Action == AlienAction.Attached && (entry || Time.unscaledTime >= m_GripAt)) {
                Play(Cue.Grip, socket, entry ? .8f : .45f, library?.AlienGrip);
                m_GripAt = Time.unscaledTime + 1.25f;
            }
            if (entry && m_DetailAction == AlienAction.Attached) Stop(Cue.Grip, .1f);
            m_DetailSequence = state.AttackSequence;
            m_DetailAction = state.Action;
        }

        // 重型、湿滑和普通表面的接触声从各自音色取，再按听距调整。
        private void PlayContact(Vector3 position, float gain) {
            var library = MoonkovAudio.Library;
            bool wet = m_Species == AlienSpecies.Slime || m_Species == AlienSpecies.Leech ||
                       m_Species == AlienSpecies.Caterpillar || m_Species == AlienSpecies.Snake;
            var sound = m_Tier == AlienThreatTier.Boss || m_Species == AlienSpecies.StiltWalker
                ?
                library?.AlienHeavyStep
                : wet
                    ? library?.AlienWetMove
                    : library?.AlienContact;
            float closeGain = m_Tier != AlienThreatTier.Boss && m_Species != AlienSpecies.StiltWalker &&
                              CanHearDetail(position, 4)
                ? 2
                : 1;
            Play(Cue.Step, position, (m_Tier == AlienThreatTier.Boss ? gain * 1.1f : gain) * closeGain, sound);
        }
    }
}