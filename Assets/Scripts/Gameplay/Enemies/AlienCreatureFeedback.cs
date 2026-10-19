using System;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Client ghost presentation. Shared recorded banks, actual gait contacts and snapshot edges;
    /// no per-creature generated AudioClips and no gameplay authority.</summary>
    public sealed partial class AlienCreatureFeedback : IDisposable
    {
        private enum Cue { Step, Warning, Strike, Hurt, Collapse, Idle, Motion, Wall, AttackMotion, AttackImpact, Grip, Count }
        private readonly SoundSystem.SoundInfo[] m_Handles = new SoundSystem.SoundInfo[(int)Cue.Count];
        private readonly float m_Pitch;
        private readonly AlienSpecies m_Species;
        private float m_StrikeDuration = .55f;
        private readonly AlienThreatTier m_Tier;
        private readonly AlienApproach m_Approach;
        private readonly float m_ContactDistance;
        private readonly Transform m_Anchor;
        private readonly System.Random m_Random;
        private readonly MoonkovSurfaceFoley m_Wall = new MoonkovSurfaceFoley();
        private float m_IdleIn, m_MotionIn, m_SurfaceTravel, m_ScrapeIn;
        private ISoundSystem m_System;
        private AlienAction m_PreviousAction;
        private uint m_AttackSequence, m_PlayedStrikeSequence;
        private float m_Health, m_StepCooldown, m_HurtAt, m_Travel;
        private int m_CompletedSteps;
        private Vector3 m_Position;
        private bool m_HasState, m_HasFeet, m_Detached, m_Disposed;

        public AlienCreatureFeedback(float scale, AlienSpecies species = AlienSpecies.Spider, Transform anchor = null,
            AlienThreatTier tier = AlienThreatTier.Common, AlienApproach approach = AlienApproach.Direct)
        {
            m_Tier = tier; m_Approach = approach; m_Species = species;
            m_ContactDistance = tier == AlienThreatTier.Boss ? 2.5f : .8f;
            m_Anchor = anchor;
            m_Random = new System.Random((anchor ? anchor.GetEntityId().GetHashCode() : UnityEngine.Random.Range(1, int.MaxValue)) ^ (int)species * 997);
            m_IdleIn = Range(2.5f, 7.5f); m_MotionIn = Range(3, 7);
            float speciesPitch = species == AlienSpecies.Slime || species == AlienSpecies.Leech ? -180 :
                species == AlienSpecies.Centipede || species == AlienSpecies.Spider ? 80 : -70;
            m_Pitch = Mathf.Clamp(-300 * Mathf.Log(Mathf.Max(.25f, scale) / .65f, 2), -350, 250) +
                speciesPitch + UnityEngine.Random.Range(-35f, 35f) - (tier == AlienThreatTier.Boss ? 450 : tier == AlienThreatTier.Elite ? 120 : 0);
        }
        public void Observe(AlienAction action, float actionTime, uint attackSequence, float health, Vector3 position)
        {
            if (m_Disposed) return;
            if (!m_HasState)
            {
                m_HasState = true; m_PreviousAction = action; m_AttackSequence = attackSequence; m_Health = health;
                if (action == AlienAction.Strike) m_PlayedStrikeSequence = attackSequence;
                return; // Late snapshots never replay historical attacks/deaths.
            }
            if (attackSequence != 0 && m_AttackSequence != 0 && unchecked((int)(attackSequence - m_AttackSequence)) < 0) return;
            bool changed = action != m_PreviousAction;
            if (action != AlienAction.Windup && action != AlienAction.Dominance) Stop(Cue.Warning, .08f);
            if (changed && (action == AlienAction.Windup || action == AlienAction.Dominance))
            {
                Stop(Cue.Idle, .1f); Stop(Cue.Motion, .1f);
                m_IdleIn = Mathf.Max(m_IdleIn, 3); m_MotionIn = Mathf.Max(m_MotionIn, 2);
                Play(Cue.Warning, position, action == AlienAction.Dominance ? 1.3f : .95f);
            }
            if (changed && action == AlienAction.Recover && m_PreviousAction == AlienAction.Strike)
                Play(Cue.Motion, position, .45f);
            bool newAttack = attackSequence != 0 && unchecked((int)(attackSequence - m_AttackSequence)) > 0;
            if (action == AlienAction.Strike && actionTime <= m_StrikeDuration && (changed || newAttack) &&
                (attackSequence == 0 || attackSequence != m_PlayedStrikeSequence))
            { Play(Cue.Strike, position, 1); m_PlayedStrikeSequence = attackSequence; }
            if (newAttack) m_AttackSequence = attackSequence;
            if (changed && action == AlienAction.Dead)
            {
                Stop(Cue.Step); Stop(Cue.Strike, .06f); Stop(Cue.Hurt, .05f);
                Stop(Cue.Idle, .1f); Stop(Cue.Motion, .1f); Stop(Cue.Wall, .08f);
                Stop(Cue.AttackMotion, .06f); Stop(Cue.AttackImpact, .08f); Stop(Cue.Grip, .06f);
                Play(Cue.Collapse, position, .9f);
            }
            else if (health > 0 && health < m_Health - .01f && Time.unscaledTime >= m_HurtAt)
            {
                Play(Cue.Hurt, position, .8f); m_HurtAt = Time.unscaledTime + .32f;
            }
            m_PreviousAction = action; m_Health = health;
        }
        public void Tick(SurfaceMotor motor, PredictiveFeet feet, AlienAction action, float dt, int completedSteps = -1, Vector3? footPosition = null)
        {
            if (m_Disposed || !m_HasState) return;
            if (dt <= 0 || dt > .25f) { m_Position = motor.Current.Position; m_Wall.Reset(); return; }
            TickDetails(motor, action, dt);
            m_StepCooldown = Mathf.Max(0, m_StepCooldown - dt);
            if (!m_HasFeet)
            {
                m_HasFeet = true; m_CompletedSteps = completedSteps >= 0 ? completedSteps : feet?.CompletedSteps ?? 0;
                m_Detached = motor.Detached; m_Position = motor.Current.Position; return;
            }
            float distance = Vector3.Distance(m_Position, motor.Current.Position);
            m_Position = motor.Current.Position;
            bool landed = m_Detached && !motor.Detached;
            int steps = completedSteps >= 0 ? completedSteps : feet?.CompletedSteps ?? 0;
            bool step = steps > m_CompletedSteps;
            m_CompletedSteps = completedSteps >= 0 ? completedSteps : feet?.CompletedSteps ?? 0; m_Detached = motor.Detached;
            if (action == AlienAction.Dead || action == AlienAction.Ambush || action == AlienAction.Attached || motor.Detached || !motor.Supported)
            { m_Travel = 0; return; }
            // Chain and branching species have no spider foot solver: displacement drives their contact shear.
            if (feet == null && completedSteps < 0 && distance < 2 && distance > .001f)
            {
                m_Travel += distance;
                if (m_Travel >= m_ContactDistance) { step = true; m_Travel %= m_ContactDistance; }
            }
            if (landed)
            {
                PlayContact(footPosition ?? motor.Current.Position - motor.Current.Up * motor.BodyHeight, 1.1f); m_StepCooldown = .2f;
            }
            else if (step && m_StepCooldown <= 0 && motor.Velocity.sqrMagnitude > .04f)
            {
                PlayContact(footPosition ?? motor.Current.Position - motor.Current.Up * motor.BodyHeight,
                    Mathf.Lerp(.6f, .95f, Mathf.Clamp01(motor.Velocity.magnitude / 6)));
                m_StepCooldown = m_Tier == AlienThreatTier.Boss ? .65f : .15f;
            }
        }
        private float Range(float min, float max) => min + (float)m_Random.NextDouble() * (max - min);

        private void TickDetails(SurfaceMotor motor, AlienAction action, float dt)
        {
            Vector3 position = motor.Current.Position;
            if (m_Health <= 0 || action == AlienAction.Dead || action == AlienAction.Ambush || action == AlienAction.Attached ||
                !MoonkovAudio.CanHearDetail(position, m_Tier == AlienThreatTier.Boss ? 65 : 28))
            { m_Wall.Reset(); m_SurfaceTravel = 0; return; }
            bool attacking = action == AlienAction.Windup || action == AlienAction.Strike || action == AlienAction.Dominance;
            if (!attacking && !(m_Approach == AlienApproach.Stalk && action == AlienAction.Chase))
            {
                m_IdleIn -= dt; m_MotionIn -= dt;
                if (m_IdleIn <= 0)
                {
                    bool played = Play(Cue.Idle, position, action == AlienAction.Chase ? .48f : .7f);
                    m_IdleIn = played ? Range(12, 24) : Range(.8f, 1.6f);
                }
                if (m_MotionIn <= 0 && motor.Velocity.sqrMagnitude < .09f && motor.Supported)
                {
                    m_MotionIn = Play(Cue.Motion, position, .4f) ? Range(5, 11) : Range(1, 2);
                }
            }
            Vector3 displacement = position - m_Position;
            m_ScrapeIn = Mathf.Max(0, m_ScrapeIn - dt);
            if (attacking || motor.Detached || !motor.Supported || !m_HasFeet || displacement.magnitude > 2 ||
                motor.Velocity.sqrMagnitude < .04f)
            { m_SurfaceTravel = 0; m_Wall.Reset(); return; }
            RaycastHit hit;
            bool scrape = false;
            if (Vector3.Dot(motor.Current.Up, Vector3.up) < .65f)
            {
                m_SurfaceTravel += displacement.magnitude;
                if (m_SurfaceTravel >= .3f && m_ScrapeIn <= 0 &&
                    MoonkovSurfaceFoley.TrySupportContact(m_Anchor, position, motor.Current.Up, motor.BodyHeight + .15f, out hit))
                { m_SurfaceTravel = 0; scrape = true; }
                else return;
            }
            else scrape = m_Wall.TrySlide(m_Anchor, position, displacement, motor.CollisionRadius + .13f, dt, out hit);
            if (!scrape || m_ScrapeIn > 0) return;
            var library = MoonkovAudio.Library;
            bool metal = RifleImpactSurface.Classify(hit.collider) == RifleImpactKind.Metal;
            Play(Cue.Wall, hit.point, Mathf.Lerp(.35f, .7f, Mathf.Clamp01(motor.Velocity.magnitude / 5)),
                metal ? library?.AlienWallMetal : library?.AlienWallScrape);
            m_ScrapeIn = .52f;
        }

        private bool Play(Cue cue, Vector3 position, float volume, SoundDef overrideSound = null, bool worldPosition = false)
        {
            var game = GameManager.Instance;
            var library = MoonkovAudio.Library;
            if (!Application.isPlaying || !game || game.IsHeadless || game.SoundSystem == null || !library) return false;
            m_System = game.SoundSystem;
            SoundDef sound = cue == Cue.Step ? library.AlienContact : cue == Cue.Warning ?
                (m_Tier == AlienThreatTier.Boss ? library.AlienBossVoice : library.AlienWarning) :
                cue == Cue.Strike ? (m_Tier == AlienThreatTier.Boss ? library.AlienBossVoice : library.AlienStrike) : cue == Cue.Hurt ? library.AlienHurt :
                cue == Cue.Idle ? library.AlienIdle : cue == Cue.Motion ? library.AlienIdleMotion :
                cue == Cue.Collapse ? library.AlienDeath : null;
            sound = overrideSound ?? sound;
            Stop(cue, .025f);
            var info = new SoundSystem.SoundInfo
            {
                SoundDefinition = sound, SoundTransform = worldPosition ? null : m_Anchor,
                Position = !worldPosition && m_Anchor ? m_Anchor.InverseTransformPoint(position) : position,
                SoundDefOverrideInfo = sound ? new SoundEmitter.SoundDefOverrideData(sound)
                    { SpatialBlend = 1, BasePitchInCents = sound.BasePitchInCents + m_Pitch } : null
            };
            m_Handles[(int)cue] = sound && m_System.PlayEmitter(info, volume) ? info : null;
            return m_Handles[(int)cue] != null;
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
            for (int i = 0; i < m_Handles.Length; i++) Stop((Cue)i);
            if (m_System is SoundSystem system && m_Anchor) system.StopAttachedTo(m_Anchor);
        }
    }
}
