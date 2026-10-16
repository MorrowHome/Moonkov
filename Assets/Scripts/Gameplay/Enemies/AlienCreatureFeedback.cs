using System;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Client ghost presentation. Shared recorded banks, actual gait contacts and snapshot edges;
    /// no per-creature generated AudioClips and no gameplay authority.</summary>
    public sealed class AlienCreatureFeedback : IDisposable
    {
        private enum Cue { Step, Warning, Strike, Hurt, Collapse, Count }
        private readonly SoundSystem.SoundInfo[] m_Handles = new SoundSystem.SoundInfo[(int)Cue.Count];
        private readonly float m_Pitch;
        private ISoundSystem m_System;
        private AlienAction m_PreviousAction;
        private uint m_AttackSequence;
        private float m_Health, m_StepCooldown, m_HurtAt, m_Travel;
        private int m_CompletedSteps;
        private Vector3 m_Position;
        private bool m_HasState, m_HasFeet, m_Detached, m_Disposed;

        public AlienCreatureFeedback(float scale, AlienSpecies species = AlienSpecies.Spider)
        {
            float speciesPitch = species == AlienSpecies.Slime || species == AlienSpecies.Leech ? -180 :
                species == AlienSpecies.Centipede || species == AlienSpecies.Spider ? 80 : -70;
            m_Pitch = Mathf.Clamp(-300 * Mathf.Log(Mathf.Max(.25f, scale) / .65f, 2), -350, 250) +
                speciesPitch + UnityEngine.Random.Range(-65f, 65f);
        }
        public void Observe(AlienAction action, float actionTime, uint attackSequence, float health, Vector3 position)
        {
            if (m_Disposed) return;
            if (!m_HasState)
            {
                m_HasState = true; m_PreviousAction = action; m_AttackSequence = attackSequence; m_Health = health;
                return; // Late snapshots never replay historical attacks/deaths.
            }
            bool changed = action != m_PreviousAction;
            if (action != AlienAction.Windup) Stop(Cue.Warning, .08f);
            if (changed && action == AlienAction.Windup) Play(Cue.Warning, position, .95f);
            bool newAttack = attackSequence != 0 && unchecked((int)(attackSequence - m_AttackSequence)) > 0;
            if (action == AlienAction.Strike && actionTime < .25f && (changed || newAttack))
                Play(Cue.Strike, position, 1);
            if (newAttack) m_AttackSequence = attackSequence;
            if (changed && action == AlienAction.Dead)
            {
                Stop(Cue.Step); Stop(Cue.Strike, .06f); Stop(Cue.Hurt, .05f);
                Play(Cue.Collapse, position, .9f);
            }
            else if (health > 0 && health < m_Health - .01f && Time.unscaledTime >= m_HurtAt)
            {
                Play(Cue.Hurt, position, .8f); m_HurtAt = Time.unscaledTime + .32f;
            }
            m_PreviousAction = action; m_Health = health;
        }
        public void Tick(SurfaceMotor motor, PredictiveFeet feet, AlienAction action, float dt)
        {
            if (m_Disposed || !m_HasState) return;
            m_StepCooldown = Mathf.Max(0, m_StepCooldown - dt);
            if (!m_HasFeet)
            {
                m_HasFeet = true; m_CompletedSteps = feet?.CompletedSteps ?? 0;
                m_Detached = motor.Detached; m_Position = motor.Current.Position; return;
            }
            float distance = Vector3.Distance(m_Position, motor.Current.Position);
            m_Position = motor.Current.Position;
            bool landed = m_Detached && !motor.Detached;
            bool step = feet != null && feet.CompletedSteps > m_CompletedSteps;
            m_CompletedSteps = feet?.CompletedSteps ?? 0; m_Detached = motor.Detached;
            if (action == AlienAction.Dead || action == AlienAction.Ambush || motor.Detached || !motor.Supported)
            { m_Travel = 0; return; }
            // Chain and branching species have no spider foot solver: displacement drives their contact shear.
            if (feet == null && distance < 2 && distance > .001f)
            {
                m_Travel += distance;
                if (m_Travel >= .8f) { step = true; m_Travel %= .8f; }
            }
            if (landed)
            {
                Play(Cue.Step, motor.Current.Position, 1.1f); m_StepCooldown = .2f;
            }
            else if (step && m_StepCooldown <= 0 && motor.Velocity.sqrMagnitude > .04f)
            {
                Play(Cue.Step, motor.Current.Position, Mathf.Lerp(.45f, .8f, Mathf.Clamp01(motor.Velocity.magnitude / 6)));
                m_StepCooldown = .15f;
            }
        }
        private void Play(Cue cue, Vector3 position, float volume)
        {
            var game = GameManager.Instance;
            var library = MoonkovAudio.Library;
            if (!Application.isPlaying || !game || game.IsHeadless || game.SoundSystem == null || !library) return;
            m_System = game.SoundSystem;
            SoundDef sound = cue == Cue.Step ? library.AlienContact : cue == Cue.Warning ? library.AlienWarning :
                cue == Cue.Strike ? library.AlienStrike : cue == Cue.Hurt ? library.AlienHurt : library.AlienDeath;
            Stop(cue, .025f);
            m_Handles[(int)cue] = MoonkovAudio.PlayWeaponLayer(sound, position, volume, 1, m_Pitch);
        }
        private void Stop(Cue cue, float fade = 0)
        {
            int index = (int)cue;
            if (m_Handles[index] != null) m_System?.Stop(m_Handles[index], fade);
            m_Handles[index] = null;
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            for (int i = 0; i < m_Handles.Length; i++) Stop((Cue)i, .08f);
        }
    }
}
