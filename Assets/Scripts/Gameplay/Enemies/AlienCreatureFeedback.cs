using System;
using System.Collections.Generic;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    /// <summary>Client-only spider foley, using the game's pooled emitters and SFX mixer.
    /// Short synthesized cues are owned by this creature and released with its ghost.</summary>
    public sealed class AlienCreatureFeedback : IDisposable
    {
        private enum Cue { Step, Warning, Strike, Hurt, Collapse, Count }
        private readonly SoundDef[] m_Sounds = new SoundDef[(int)Cue.Count];
        private readonly SoundSystem.SoundInfo[] m_Handles = new SoundSystem.SoundInfo[(int)Cue.Count];
        private readonly float m_Scale;
        private ISoundSystem m_SoundSystem;
        private AlienAction m_PreviousAction;
        private uint m_AttackSequence;
        private float m_Health, m_StepCooldown;
        private int m_CompletedSteps;
        private bool m_HasState, m_HasFeet, m_Detached, m_Disposed;

        public AlienCreatureFeedback(float scale) { m_Scale = scale; }

        public void Observe(AlienAction action, float actionTime, uint attackSequence, float health, Vector3 position)
        {
            if (m_Disposed) return;
            if (!m_HasState)
            {
                // A first snapshot may already contain an old attack or a corpse.
                m_HasState = true; m_PreviousAction = action;
                m_AttackSequence = attackSequence; m_Health = health;
                return;
            }
            bool changed = action != m_PreviousAction;
            if (action != AlienAction.Windup) Stop(Cue.Warning);
            if (changed && action == AlienAction.Windup) Play(Cue.Warning, position, .85f);
            bool newAttack = attackSequence != 0 && unchecked((int)(attackSequence - m_AttackSequence)) > 0;
            // Sequence advances during windup. The launch edge can therefore arrive
            // with the same sequence as the previous anticipation snapshot.
            if (action == AlienAction.Strike && actionTime < .25f && (changed || newAttack))
                Play(Cue.Strike, position, .75f);
            if (newAttack) m_AttackSequence = attackSequence;
            if (changed && action == AlienAction.Dead)
            {
                Stop(Cue.Step); Stop(Cue.Strike); Stop(Cue.Hurt);
                Play(Cue.Collapse, position, .8f);
            }
            else if (health > 0 && health < m_Health - .01f) Play(Cue.Hurt, position, .65f);
            m_PreviousAction = action; m_Health = health;
        }

        public void Tick(SurfaceMotor motor, PredictiveFeet feet, AlienAction action, float dt)
        {
            if (m_Disposed || !m_HasState) return;
            m_StepCooldown = Mathf.Max(0, m_StepCooldown - dt);
            if (!m_HasFeet)
            {
                m_HasFeet = true; m_CompletedSteps = feet.CompletedSteps; m_Detached = motor.Detached;
                return;
            }
            bool landed = m_Detached && !motor.Detached;
            bool step = feet.CompletedSteps > m_CompletedSteps;
            m_CompletedSteps = feet.CompletedSteps; m_Detached = motor.Detached;
            if (action == AlienAction.Dead || motor.Detached || !motor.Supported) return;
            if (landed)
            {
                Play(Cue.Collapse, motor.Current.Position, .5f);
                m_StepCooldown = .12f;
            }
            else if (step && m_StepCooldown <= 0 && motor.Velocity.sqrMagnitude > .04f)
            {
                // Real foot contacts drive the cadence, with at most one cue per 90 ms.
                Play(Cue.Step, motor.Current.Position, Mathf.Lerp(.4f, .7f, Mathf.Clamp01(motor.Velocity.magnitude / 6)));
                m_StepCooldown = .09f;
            }
        }

        private void Play(Cue cue, Vector3 position, float volume)
        {
            var game = GameManager.Instance;
            if (!Application.isPlaying || !game || game.IsHeadless || game.SoundSystem == null) return;
            m_SoundSystem = game.SoundSystem;
            int index = (int)cue;
            if (!m_Sounds[index]) m_Sounds[index] = CreateSound(cue);
            Stop(cue);
            m_Handles[index] = m_SoundSystem.CreateEmitter(m_Sounds[index], position, volume);
        }

        private SoundDef CreateSound(Cue cue)
        {
            const int sampleRate = 24000;
            float duration = cue == Cue.Step ? .065f : cue == Cue.Warning ? .36f : cue == Cue.Collapse ? .24f : .13f;
            var samples = new float[Mathf.CeilToInt(duration * sampleRate)];
            uint noise = 0x7f4a7c15u + (uint)cue * 977;
            float filtered = 0, phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)sampleRate, t = time / duration;
                noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                float white = (noise & 65535) / 32767.5f - 1;
                filtered += (white - filtered) * .16f;
                float frequency = cue == Cue.Warning ? Mathf.Lerp(170, 330, t) :
                    cue == Cue.Step ? 1300 : cue == Cue.Strike ? Mathf.Lerp(430, 110, t) : cue == Cue.Hurt ? 680 : 95;
                phase += frequency * Mathf.Lerp(1.1f, .82f, Mathf.InverseLerp(.3f, 1.2f, m_Scale)) * Mathf.PI * 2 / sampleRate;
                float attack = Mathf.Clamp01(time / .004f);
                float envelope = attack * Mathf.Pow(1 - t, cue == Cue.Warning ? .65f : 2.5f);
                float texture = filtered * 1.6f + Mathf.Sin(phase) * .28f;
                if (cue == Cue.Warning) texture *= .65f + .35f * Mathf.Sin(time * 100);
                samples[i] = Mathf.Clamp(texture * envelope * .6f, -.8f, .8f);
            }
            var clip = AudioClip.Create("Procedural spider " + cue, samples.Length, 1, sampleRate, false);
            clip.hideFlags = HideFlags.DontSave; clip.SetData(samples, 0);
            var sound = ScriptableObject.CreateInstance<SoundDef>();
            sound.name = clip.name; sound.hideFlags = HideFlags.DontSave;
            sound.Clips = new List<AudioClip> { clip };
            sound.MixerGroup = SoundMixer.SoundMixerGroup.SFX;
            sound.PlaybackType = SoundDef.PlaybackTypes.Sequential;
            sound.PlayCount = 1; sound.VolumeScale = 1;
            sound.RepeatInfo = new SoundDef.Repeat();
            sound.StartStopInfo = new SoundDef.StartStop();
            sound.PitchAndVolumeInfo = new SoundDef.PitchAndVolume
            {
                VolumeMin = cue == Cue.Warning ? -6 : -10,
                VolumeMax = cue == Cue.Warning ? -6 : -10
            };
            sound.DistanceInfo = new SoundDef.Distance
            {
                SpatialBlend = 1, VolumeDistMin = 1.2f, VolumeDistMax = cue == Cue.Warning ? 18 : 12,
                VolumeRolloffMode = AudioRolloffMode.Linear, DopplerScale = 0
            };
            sound.LowPassFilter = new SoundDef.Filter(); sound.HighPassFilter = new SoundDef.Filter();
            sound.DistortionFilter = new SoundDef.Distortion();
            return sound;
        }

        private void Stop(Cue cue)
        {
            int index = (int)cue;
            if (m_Handles[index] != null) m_SoundSystem?.Stop(m_Handles[index]);
            m_Handles[index] = null;
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            for (int i = 0; i < m_Sounds.Length; i++)
            {
                Stop((Cue)i);
                if (!m_Sounds[i]) continue;
                foreach (var clip in m_Sounds[i].Clips) Release(clip);
                Release(m_Sounds[i]);
            }
        }
        private static void Release(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value);
        }
    }
}
