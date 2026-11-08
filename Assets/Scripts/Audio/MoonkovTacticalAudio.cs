using UnityEngine;

namespace Unity.MP_FPS
{
    // One track per accepted world object; the caller owns network ordering and expiry.
    public sealed class MoonkovTacticalAudio
    {
        private bool m_Initialized;
        private TacticalWorldPhase m_Phase;
        private TacticalItemKind m_Kind;
        private Vector3 m_Position;
        private SoundDef m_LoopBank;
        private SoundSystem.SoundInfo m_Start, m_Loop;
        private float m_RetryAt;

        public void Present(in TacticalWorldStateRpc state)
        {
            m_Position = state.Position;
            if (m_Initialized && m_Phase == state.Phase && m_Kind == state.Kind) return;
            Stop(); m_Initialized = true; m_Phase = state.Phase; m_Kind = state.Kind;
            var lib = MoonkovAudio.Library;
            if (!lib) return;
            m_LoopBank = state.Phase == TacticalWorldPhase.Fire ? lib.TacticalFireLoop :
                state.Phase == TacticalWorldPhase.Smoke ? lib.TacticalSmokeLoop :
                state.Phase == TacticalWorldPhase.Choke ? lib.TacticalChokeLoop :
                state.Phase == TacticalWorldPhase.Poison ? lib.TacticalPoisonLoop :
                state.Phase == TacticalWorldPhase.Alarm ? lib.TacticalAlarm : null;
            var cue = state.Phase == TacticalWorldPhase.Explosion ?
                    (state.Kind == TacticalItemKind.Flash ? lib.TacticalFlash : lib.TacticalExplosion) :
                state.Phase == TacticalWorldPhase.Fire ? lib.TacticalFireStart :
                state.Phase == TacticalWorldPhase.Smoke || state.Phase == TacticalWorldPhase.Choke ? lib.TacticalGas :
                state.Phase == TacticalWorldPhase.Armed ? lib.TacticalTrapArm :
                state.Phase == TacticalWorldPhase.Wire ? lib.TacticalWireRelease :
                state.Phase == TacticalWorldPhase.Supply ? lib.TacticalSupplyDeploy :
                state.Phase == TacticalWorldPhase.Flying ? lib.TacticalDeploy : null;
            // Late arrivals resume the area bed, without replaying its original ignition or deployment.
            if (cue && Fresh(state) && MoonkovAudio.CanHearDetail(m_Position, cue.DistanceInfo.VolumeDistMax))
                m_Start = MoonkovAudio.Play(cue, m_Position);
            Update();
        }

        public void Update()
        {
            if (!m_LoopBank) return;
            if (!MoonkovAudio.CanHearDetail(m_Position, m_LoopBank.DistanceInfo.VolumeDistMax))
            { Stop(ref m_Loop); return; }
            if (m_Loop?.Handle?.IsValid() == true || Time.unscaledTime < m_RetryAt) return;
            m_RetryAt = Time.unscaledTime + .5f;
            m_Loop = MoonkovAudio.Play(m_LoopBank, m_Position);
        }

        private static bool Fresh(in TacticalWorldStateRpc state)
        {
            float lifetime = state.Phase == TacticalWorldPhase.Alarm ? 8 :
                state.Phase == TacticalWorldPhase.Wire ? 45 :
                state.Phase == TacticalWorldPhase.Poison && state.Kind == TacticalItemKind.PoisonTrap ? 30 :
                state.Phase == TacticalWorldPhase.Explosion ? .35f : 0;
            if (lifetime == 0)
                foreach (var definition in TacticalItemRules.Definitions)
                    if (definition.Kind == state.Kind)
                    { lifetime = state.Phase == TacticalWorldPhase.Flying ? definition.Fuse : definition.Lifetime; break; }
            return state.Remaining > 0 && state.Remaining >= lifetime - .5f;
        }

        private static void Stop(ref SoundSystem.SoundInfo sound)
        { GameManager.Instance?.SoundSystem?.Stop(sound, .08f); sound = null; }

        public void Stop(bool finishExplosion = false)
        {
            // The server removes an explosion after 0.35 s. Its finite acoustic tail
            // still belongs to the sound pool and must finish after the visual vanishes.
            if (finishExplosion && m_Phase == TacticalWorldPhase.Explosion) m_Start = null;
            else Stop(ref m_Start);
            Stop(ref m_Loop);
            m_LoopBank = null; m_Initialized = false; m_RetryAt = 0;
        }
    }
}
