// 异形的基础声音表现。看到角色从休眠、移动、受伤或死亡进入新状态时安排相应声音，近处还有足部与地面的细节。
// 攻击命中等按事件触发的声音在 Combat 部分。
using System;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>客户端 Ghost 音频表现。复用录音音库、真实步态接触和快照变化。
    /// 每只怪无需单独生成 AudioClip，玩法判断留在服务端。</summary>
    public sealed partial class AlienCreatureFeedback : IDisposable {
        private enum Cue {
            Step,
            Warning,
            Strike,
            Hurt,
            Collapse,
            Idle,
            Motion,
            Wall,
            AttackMotion,
            AttackImpact,
            Grip,
            Proximity,
            Count
        }

        private readonly SoundSystem.SoundInfo[] m_Handles = new SoundSystem.SoundInfo[(int)Cue.Count];
        private readonly float m_Pitch;
        private readonly AlienSpecies m_Species;
        private float m_StrikeDuration = .55f;
        private readonly AlienThreatTier m_Tier;
        private readonly AlienApproach m_Approach;
        private readonly float m_ContactDistance;
        private readonly Transform m_Anchor;
        private readonly System.Random m_Random;
        private readonly MoonkovSurfaceFoley m_Wall = new();
        private float m_IdleIn, m_MotionIn, m_SurfaceTravel, m_ScrapeIn, m_ProximityIn;
        private ISoundSystem m_System;
        private readonly ISoundSystem m_LocalSoundSystem;
        private AlienAction m_PreviousAction;
        private uint m_AttackSequence, m_PlayedStrikeSequence;
        private float m_Health, m_StepCooldown, m_HurtAt, m_Travel;
        private int m_CompletedSteps;
        private Vector3 m_Position;
        private bool m_HasState, m_HasFeet, m_Detached, m_Disposed;

        public AlienCreatureFeedback(
            float scale,
            AlienSpecies species = AlienSpecies.Spider,
            Transform anchor = null,
            AlienThreatTier tier = AlienThreatTier.Common,
            AlienApproach approach = AlienApproach.Direct,
            ISoundSystem localSoundSystem = null) {
            m_LocalSoundSystem = localSoundSystem;
            m_Tier = tier;
            m_Approach = approach;
            m_Species = species;
            m_ContactDistance = tier == AlienThreatTier.Boss ? 2.5f : .8f;
            m_Anchor = anchor;
            m_Random = new System.Random(
                (anchor ? anchor.GetEntityId().GetHashCode() : UnityEngine.Random.Range(1, int.MaxValue)) ^
                ((int)species * 997));
            m_IdleIn = Range(2.5f, 7.5f);
            m_MotionIn = Range(3, 7);
            float speciesPitch = species == AlienSpecies.Slime || species == AlienSpecies.Leech ? -180 :
                species == AlienSpecies.Centipede || species == AlienSpecies.Spider ? 80 : -70;
            m_Pitch = Mathf.Clamp(-300 * Mathf.Log(Mathf.Max(.25f, scale) / .65f, 2), -350, 250) + speciesPitch +
                      UnityEngine.Random.Range(-35f, 35f) -
                      (tier == AlienThreatTier.Boss ? 450 : tier == AlienThreatTier.Elite ? 120 : 0);
        }

        // 比较上次观察与现在的动作、攻击序号、血量，挑需要新触发的声音。
        public void Observe(AlienAction action, float actionTime, uint attackSequence, float health, Vector3 position) {
            if (m_Disposed) return;
            if (!m_HasState) {
                m_HasState = true;
                m_PreviousAction = action;
                m_AttackSequence = attackSequence;
                m_Health = health;
                if (action == AlienAction.Strike) m_PlayedStrikeSequence = attackSequence;
                return; // 晚到的快照不会重演之前的攻击和死亡声。
            }
            if (attackSequence != 0 && m_AttackSequence != 0 && unchecked((int)(attackSequence - m_AttackSequence)) < 0)
                return;
            bool changed = action != m_PreviousAction;
            if (action != AlienAction.Windup && action != AlienAction.Dominance) Stop(Cue.Warning, .08f);
            if (changed && (action == AlienAction.Windup || action == AlienAction.Dominance)) {
                Stop(Cue.Idle, .1f);
                Stop(Cue.Motion, .1f);
                m_IdleIn = Mathf.Max(m_IdleIn, 3);
                m_MotionIn = Mathf.Max(m_MotionIn, 2);
                Play(Cue.Warning, position, action == AlienAction.Dominance ? 1.3f : .95f);
            }
            if (changed && action == AlienAction.Recover && m_PreviousAction == AlienAction.Strike)
                Play(Cue.Motion, position, .45f);
            bool newAttack = attackSequence != 0 && unchecked((int)(attackSequence - m_AttackSequence)) > 0;
            if (action == AlienAction.Strike && actionTime <= m_StrikeDuration && (changed || newAttack) &&
                (attackSequence == 0 || attackSequence != m_PlayedStrikeSequence)) {
                Play(Cue.Strike, position, 1);
                m_PlayedStrikeSequence = attackSequence;
            }
            if (newAttack) m_AttackSequence = attackSequence;
            if (changed && action == AlienAction.Dead) {
                Stop(Cue.Step);
                Stop(Cue.Strike, .06f);
                Stop(Cue.Hurt, .05f);
                Stop(Cue.Idle, .1f);
                Stop(Cue.Motion, .1f);
                Stop(Cue.Wall, .08f);
                Stop(Cue.Proximity, .06f);
                Stop(Cue.AttackMotion, .06f);
                Stop(Cue.AttackImpact, .08f);
                Stop(Cue.Grip, .06f);
                Play(Cue.Collapse, position, .9f);
            } else if (health > 0 && health < m_Health - .01f && Time.unscaledTime >= m_HurtAt) {
                Play(Cue.Hurt, position, .8f);
                m_HurtAt = Time.unscaledTime + .32f;
            }
            m_PreviousAction = action;
            m_Health = health;
        }

        // 根据脚真正接触或身体真实位移判断接触声。能在墙上走的物种也沿自己的表面坐标播放。
        public void Tick(
            SurfaceMotor motor,
            PredictiveFeet feet,
            AlienAction action,
            float dt,
            int completedSteps = -1,
            Vector3? footPosition = null) {
            if (m_Disposed || !m_HasState) return;
            if (dt <= 0 || dt > .25f) {
                m_Position = motor.Current.Position;
                m_Wall.Reset();
                return;
            }
            TickDetails(motor, action, dt);
            m_StepCooldown = Mathf.Max(0, m_StepCooldown - dt);
            if (!m_HasFeet) {
                m_HasFeet = true;
                m_CompletedSteps = completedSteps >= 0 ? completedSteps : feet?.CompletedSteps ?? 0;
                m_Detached = motor.Detached;
                m_Position = motor.Current.Position;
                return;
            }
            float distance = Vector3.Distance(m_Position, motor.Current.Position);
            m_Position = motor.Current.Position;
            bool landed = m_Detached && !motor.Detached;
            int steps = completedSteps >= 0 ? completedSteps : feet?.CompletedSteps ?? 0;
            bool step = steps > m_CompletedSteps;
            m_CompletedSteps = completedSteps >= 0 ? completedSteps : feet?.CompletedSteps ?? 0;
            m_Detached = motor.Detached;
            if (action == AlienAction.Dead || action == AlienAction.Ambush || action == AlienAction.Attached ||
                motor.Detached || !motor.Supported) {
                m_Travel = 0;
                return;
            }
            // 链状和分支形态没有蜘蛛的找脚解法，用身体位移决定表面的摩擦声。
            if (feet == null && completedSteps < 0 && distance < 2 && distance > .001f) {
                m_Travel += distance;
                if (m_Travel >= m_ContactDistance) {
                    step = true;
                    m_Travel %= m_ContactDistance;
                }
            }
            if (landed) {
                PlayContact(footPosition ?? motor.Current.Position - motor.Current.Up * motor.BodyHeight, 1.1f);
                m_StepCooldown = .2f;
            }
            // 身体为预警停住后，已有一只脚仍可以结束它还没走完的摆动。
            else if (step && m_StepCooldown <= 0 &&
                     (feet != null || completedSteps >= 0 || motor.Velocity.sqrMagnitude > .04f)) {
                PlayContact(footPosition ?? motor.Current.Position - motor.Current.Up * motor.BodyHeight,
                    Mathf.Lerp(.6f, .95f, Mathf.Clamp01(motor.Velocity.magnitude / 6)));
                m_StepCooldown = m_Tier == AlienThreatTier.Boss ? .65f : .15f;
            }
        }

        private float Range(float min, float max) { return min + (float)m_Random.NextDouble() * (max - min); }

        // 用距离决定要不要算更细的抓地、湿动和呼吸类声音。远处保留基本声音就能读出动作。
        private void TickDetails(SurfaceMotor motor, AlienAction action, float dt) {
            var position = motor.Current.Position;
            if (m_Health <= 0 || action == AlienAction.Dead || action == AlienAction.Ambush ||
                action == AlienAction.Attached || !CanHearDetail(position, m_Tier == AlienThreatTier.Boss ? 65 : 28)) {
                Stop(Cue.Proximity, .08f);
                m_Wall.Reset();
                m_SurfaceTravel = 0;
                return;
            }
            bool attacking = action == AlienAction.Windup || action == AlienAction.Strike ||
                             action == AlienAction.Dominance;
            // 贴着地面的小怪接近监听者后，即使停止移动也应听得见。
            // 前面已返回的静伏、附着与死亡状态仍保持安静。
            bool close = m_Tier != AlienThreatTier.Boss && m_Approach != AlienApproach.Stalk &&
                         CanHearDetail(position, 4);
            m_ProximityIn = Mathf.Max(0, m_ProximityIn - dt);
            if (close && !attacking && m_ProximityIn <= 0)
                m_ProximityIn = Play(Cue.Proximity, position, 1.1f, MoonkovAudio.Library?.AlienIdleMotion)
                    ? Range(1.8f, 2.8f)
                    : .5f;
            if (attacking) Stop(Cue.Proximity, .08f);
            if (!close) {
                m_ProximityIn = 0;
                Stop(Cue.Proximity, .1f);
            }
            if (!close && !attacking && !(m_Approach == AlienApproach.Stalk && action == AlienAction.Chase)) {
                m_IdleIn -= dt;
                m_MotionIn -= dt;
                if (m_IdleIn <= 0) {
                    bool played = Play(Cue.Idle, position, action == AlienAction.Chase ? .48f : .7f);
                    m_IdleIn = played ? Range(12, 24) : Range(.8f, 1.6f);
                }
                if (m_MotionIn <= 0 && motor.Velocity.sqrMagnitude < .09f && motor.Supported)
                    m_MotionIn = Play(Cue.Motion, position, .4f) ? Range(5, 11) : Range(1, 2);
            }
            var displacement = position - m_Position;
            m_ScrapeIn = Mathf.Max(0, m_ScrapeIn - dt);
            if (attacking || motor.Detached || !motor.Supported || !m_HasFeet || displacement.magnitude > 2 ||
                motor.Velocity.sqrMagnitude < .04f) {
                m_SurfaceTravel = 0;
                m_Wall.Reset();
                return;
            }
            RaycastHit hit;
            bool scrape = false;
            if (Vector3.Dot(motor.Current.Up, Vector3.up) < .65f) {
                m_SurfaceTravel += displacement.magnitude;
                if (m_SurfaceTravel >= .3f && m_ScrapeIn <= 0 && MoonkovSurfaceFoley.TrySupportContact(m_Anchor,
                        position, motor.Current.Up, motor.BodyHeight + .15f, out hit)) {
                    m_SurfaceTravel = 0;
                    scrape = true;
                } else { return; }
            } else {
                scrape = m_Wall.TrySlide(m_Anchor, position, displacement, motor.CollisionRadius + .13f, dt, out hit);
            }
            if (!scrape || m_ScrapeIn > 0) return;
            var library = MoonkovAudio.Library;
            bool metal = RifleImpactSurface.Classify(hit.collider) == RifleImpactKind.Metal;
            Play(Cue.Wall, hit.point, Mathf.Lerp(.35f, .7f, Mathf.Clamp01(motor.Velocity.magnitude / 5)),
                metal ? library?.AlienWallMetal : library?.AlienWallScrape);
            m_ScrapeIn = .52f;
        }

        private bool CanHearDetail(Vector3 position, float range) {
            return m_LocalSoundSystem is SoundSystem local
                ? local.IsWithinHearing(position, range)
                : MoonkovAudio.CanHearDetail(position, range);
        }

        // 同一类别用对应句柄管理。换声音时可替换旧句柄，避免多个旧循环一直叠加。
        private bool Play(
            Cue cue,
            Vector3 position,
            float volume,
            SoundDef overrideSound = null,
            bool worldPosition = false) {
            var game = GameManager.Instance;
            var library = MoonkovAudio.Library;
            if (!Application.isPlaying || !library) return false;
            m_System = m_LocalSoundSystem ?? (game && !game.IsHeadless ? game.SoundSystem : null);
            if (m_System == null) return false;
            var sound = cue == Cue.Step ? library.AlienContact :
                cue == Cue.Warning ? m_Tier == AlienThreatTier.Boss ? library.AlienBossVoice : library.AlienWarning :
                cue == Cue.Strike ? m_Tier == AlienThreatTier.Boss ? library.AlienBossVoice : library.AlienStrike :
                cue == Cue.Hurt ? library.AlienHurt :
                cue == Cue.Idle ? library.AlienIdle :
                cue == Cue.Motion ? library.AlienIdleMotion :
                cue == Cue.Collapse ? library.AlienDeath : null;
            sound = overrideSound ?? sound;
            Stop(cue, .025f);
            var info = new SoundSystem.SoundInfo {
                SoundDefinition = sound, SoundTransform = worldPosition ? null : m_Anchor,
                Position = !worldPosition && m_Anchor ? m_Anchor.InverseTransformPoint(position) : position,
                SoundDefOverrideInfo =
                    sound
                        ? new SoundEmitter.SoundDefOverrideData(sound)
                            { SpatialBlend = 1, BasePitchInCents = sound.BasePitchInCents + m_Pitch }
                        : null
            };
            m_Handles[(int)cue] = sound && m_System.PlayEmitter(info, volume) ? info : null;
            return m_Handles[(int)cue] != null;
        }

        private void Stop(Cue cue, float fade = 0) {
            int index = (int)cue;
            if (m_Handles[index] != null) m_System?.Stop(m_Handles[index], fade);
            m_Handles[index] = null;
        }

        // 怪物离场要停掉仍在播放的声音，已经生成但没再进 Tick 的实例也要清。
        public void Dispose() {
            if (m_Disposed) return;
            m_Disposed = true;
            for (int i = 0; i < m_Handles.Length; i++) Stop((Cue)i);
            if (m_System is SoundSystem system && m_Anchor) system.StopAttachedTo(m_Anchor);
        }
    }
}