// 把气闸的压力变化和门状态变成阀门、泵、电机和锁的声音。每种声音再按空气、结构接触、头盔或无线电的传播方式算音量。
// 门和气压由 AirlockController 提供。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    public enum AirlockSoundPath : byte {
        Airborne,
        StructureContact,
        HelmetInternal,
        Radio
    }

    public enum AirlockSoundEvent : byte {
        ValveFlow,
        Pump,
        OuterMotor,
        InnerMotor,
        OuterUnlock,
        InnerUnlock,
        OuterLocked,
        InnerLocked
    }

    public static class AirlockAcoustics {
        public static float Gain(
            AirlockSoundPath path,
            AtmosphereSample source,
            AtmosphereSample listener,
            bool structureContact,
            bool radioLinked = true) {
            // 头盔内声一直存在，无线电看连接，结构传声看是否踩到舱体。空气声还要看两点气压和之间有没有隔断。
            switch (path) {
                case AirlockSoundPath.HelmetInternal: return 1;
                case AirlockSoundPath.Radio: return radioLinked ? 1 : 0;
                case AirlockSoundPath.StructureContact: return structureContact ? .35f : 0;
                default:
                    return source.PressureKPa > .01f && listener.PressureKPa > .01f &&
                           AirlockController.AirConnected(source.AirspaceId, listener.AirspaceId)
                        ? Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(source.PressureKPa, listener.PressureKPa) / 101.325f))
                        : 0;
            }
        }
    }

    // Uses the existing pooled SoundSystem and mixer; never modifies the global mute or AudioListener volume.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AirlockController))]
    public sealed class AirlockAudio : MonoBehaviour {
        [SerializeField] private AirlockController m_Owner;
        [SerializeField] private Transform m_Listener, m_ValveAnchor, m_PumpAnchor, m_OuterAnchor, m_InnerAnchor;
        [SerializeField] private Transform m_OuterLatchAnchor, m_InnerLatchAnchor;
        [SerializeField] private Transform m_StructureRoot;
        [SerializeField] private SoundDef m_Valve, m_Pump, m_DoorMotor, m_Lock, m_Unlock;
        private ISoundSystem m_Sound;
        private SoundSystem.SoundInfo[] m_Loops = new SoundSystem.SoundInfo[4];
        private readonly bool[] m_CueActive = new bool[4];
        private readonly bool[] m_ContactRoute = new bool[4];
        private AirlockState m_Previous;
        private bool m_HasPrevious;
        private readonly RaycastHit[] m_Contacts = new RaycastHit[16];
        public bool ValveActive { get; private set; }
        public bool PumpActive { get; private set; }
        public bool OuterMotorActive { get; private set; }
        public bool InnerMotorActive { get; private set; }
        public float AirGain { get; private set; }
        public float ContactGain { get; private set; }

        public event Action<AirlockSoundEvent, bool>
            EventRaised; // bool: a real clip is assigned, false: explicit pending cue.

        public void Configure(
            AirlockController owner,
            Transform listener,
            Transform valve,
            Transform pump,
            Transform outer,
            Transform inner,
            SoundDef valveSound = null,
            SoundDef pumpSound = null,
            SoundDef doorSound = null,
            SoundDef lockSound = null,
            SoundDef unlockSound = null,
            Transform outerLatch = null,
            Transform innerLatch = null) {
            m_Owner = owner;
            m_Listener = listener;
            m_ValveAnchor = valve;
            m_PumpAnchor = pump;
            m_OuterAnchor = outer;
            m_InnerAnchor = inner;
            m_Valve = valveSound;
            m_Pump = pumpSound;
            m_DoorMotor = doorSound;
            m_Lock = lockSound;
            m_Unlock = unlockSound;
            m_OuterLatchAnchor = outerLatch;
            m_InnerLatchAnchor = innerLatch;
        }

        public void BindSoundSystem(ISoundSystem sound) { m_Sound = sound; }
        public void BindListener(Transform listener) { m_Listener = listener; }
        public void ConfigureStructure(Transform structureRoot) { m_StructureRoot = structureRoot; }

        private void Awake() {
            if (!m_Owner) m_Owner = GetComponent<AirlockController>();
        }

        private void OnDisable() {
            for (int i = 0; i < m_Loops.Length; i++) StopLoop(i);
            m_HasPrevious = false;
        }

        private void Update() {
            if (!m_Owner || !m_Owner.Ready) return;
            if (m_Sound == null && GameManager.Instance) m_Sound = GameManager.Instance.SoundSystem;
            if (!m_Listener && Camera.main) m_Listener = Camera.main.transform;
            // 气阀声来自仍有压差的调压；只有朝舱内加压时，泵才和气阀一起工作。
            var s = m_Owner.State;
            ValveActive = s.Equalizing && Mathf.Abs(s.PressureKPa -
                                                    (s.TargetInside
                                                        ? m_Owner.Settings.InsidePressureKPa
                                                        : m_Owner.Settings.OutsidePressureKPa)) > .0001f;
            PumpActive = ValveActive && s.TargetInside;
            // A requested direction can remain set while a panel is held by an actor.
            // Drive each motor from its observed motion, independently of shared feedback.
            // 用两帧开度差判断门板真的在走。若门发了打开命令但被人卡住，电机声不该继续假装它在走。
            OuterMotorActive = m_HasPrevious && Mathf.Abs(s.OuterFraction - m_Previous.OuterFraction) > .000001f;
            InnerMotorActive = m_HasPrevious && Mathf.Abs(s.InnerFraction - m_Previous.InnerFraction) > .000001f;
            // 呼吸区也提供音频传播需要的空气区和气压。走出舱内后，即使声音源还很近，真空也会切断空气声。
            AtmosphereSample listener = default;
            if (m_Listener) BreathableZone.TrySample(m_Listener.position, out listener);
            var source = new AtmosphereSample(m_Owner.AirlockId + ".CHAMBER", s.PressureKPa,
                m_Owner.Settings.SupplyOxygenFraction, false);
            bool contact = HasStructureContact();
            AirGain = AirlockAcoustics.Gain(AirlockSoundPath.Airborne, source, listener, contact);
            ContactGain = AirlockAcoustics.Gain(AirlockSoundPath.StructureContact, source, listener, contact);
            // One localized emitter represents its two possible physical propagation paths; a contact path is low-passed.
            // 同一机械声选更强的一条传播路径，避免空气声和结构声叠成两份。
            float mechanismGain = Mathf.Max(AirGain, ContactGain);
            float delta =
                Mathf.Abs((s.TargetInside ? m_Owner.Settings.InsidePressureKPa : m_Owner.Settings.OutsidePressureKPa) -
                          s.PressureKPa);
            Loop(0, ValveActive, m_Valve, m_ValveAnchor,
                mechanismGain * Mathf.Sqrt(Mathf.Clamp01(delta / m_Owner.Settings.InsidePressureKPa)),
                AirlockSoundEvent.ValveFlow);
            Loop(1, PumpActive, m_Pump, m_PumpAnchor, mechanismGain, AirlockSoundEvent.Pump);
            Loop(2, OuterMotorActive, m_DoorMotor, m_OuterAnchor, mechanismGain, AirlockSoundEvent.OuterMotor);
            Loop(3, InnerMotorActive, m_DoorMotor, m_InnerAnchor, mechanismGain, AirlockSoundEvent.InnerMotor);
            // 锁声只在门板刚离开零点或刚回到零点时播一次。组件刚打开还没有前一帧，不补播。
            if (m_HasPrevious) {
                if (s.OuterFraction > 0 && m_Previous.OuterFraction == 0)
                    OneShot(AirlockSoundEvent.OuterUnlock, m_Unlock,
                        m_OuterLatchAnchor ? m_OuterLatchAnchor : m_OuterAnchor, mechanismGain);
                if (s.InnerFraction > 0 && m_Previous.InnerFraction == 0)
                    OneShot(AirlockSoundEvent.InnerUnlock, m_Unlock,
                        m_InnerLatchAnchor ? m_InnerLatchAnchor : m_InnerAnchor, mechanismGain);
                if (s.OuterFraction == 0 && m_Previous.OuterFraction > 0)
                    OneShot(AirlockSoundEvent.OuterLocked, m_Lock,
                        m_OuterLatchAnchor ? m_OuterLatchAnchor : m_OuterAnchor, mechanismGain);
                if (s.InnerFraction == 0 && m_Previous.InnerFraction > 0)
                    OneShot(AirlockSoundEvent.InnerLocked, m_Lock,
                        m_InnerLatchAnchor ? m_InnerLatchAnchor : m_InnerAnchor, mechanismGain);
            }
            m_Previous = s;
            m_HasPrevious = true;
        }

        // 监听器附近要有落地的角色胶囊。再向脚下探测，确认实际接触的是这个气闸的结构。
        private bool HasStructureContact() {
            if (!m_Listener) return false;
            UnityEngine.CharacterController capsule = null;
            foreach (var candidate in m_Listener.GetComponentsInParent<UnityEngine.CharacterController>())
                if (candidate.enabled) {
                    capsule = candidate;
                    break;
                }
            if (!capsule || !capsule.isGrounded) return false;
            var origin = capsule.transform.position + Vector3.up * .2f;
            int n = UnityEngine.Physics.RaycastNonAlloc(origin, Vector3.down, m_Contacts, .5f, ~0,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) {
                var hit = m_Contacts[i].collider;
                if (hit && (hit.GetComponentInParent<AirlockController>() == m_Owner ||
                            (m_StructureRoot && hit.transform.IsChildOf(m_StructureRoot))))
                    return true;
            }
            return false;
        }

        private static bool HasClip(SoundDef sound) {
            return sound && sound.Clips != null && sound.Clips.Count > 0 && sound.Clips[0];
        }

        private void Loop(int index, bool active, SoundDef sound, Transform anchor, float gain, AirlockSoundEvent cue) {
            if (!active) {
                m_CueActive[index] = false;
                StopLoop(index);
                return;
            }
            if (!m_CueActive[index]) {
                EventRaised?.Invoke(cue, HasClip(sound));
                m_CueActive[index] = true;
            }
            var handle = m_Loops[index];
            bool contactRoute = ContactGain > AirGain;
            // 声音从空气传播切到结构传播时，重新开循环以应用低通。装置没停时则用同一个句柄随压力改音量。
            if (handle?.Handle != null && handle.Handle.IsValid() && m_ContactRoute[index] != contactRoute) {
                StopLoop(index);
                handle = null;
            }
            if (handle == null || handle.Handle == null || !handle.Handle.IsValid()) {
                if (!HasClip(sound) || m_Sound == null || gain <= 0) return;
                handle = new SoundSystem.SoundInfo {
                    SoundDefinition = sound, SoundTransform = anchor ? anchor : transform,
                    SoundDefOverrideInfo = new SoundEmitter.SoundDefOverrideData(sound)
                };
                if (contactRoute && sound.LowPassFilter != null)
                    handle.SoundDefOverrideInfo.BaseLowPassCutoff = 1200 - sound.LowPassFilter.CutoffMin;
                if (!m_Sound.PlayEmitter(handle, gain)) return;
                m_ContactRoute[index] = contactRoute;
                m_Loops[index] = handle;
            }
            if (handle?.Handle != null && handle.Handle.IsValid()) handle.Handle.Emitter.SetVolume(gain);
        }

        private void StopLoop(int index) {
            if (m_Loops[index] != null && m_Sound != null) m_Sound.Stop(m_Loops[index], .05f);
            m_Loops[index] = null;
        }

        private void OneShot(AirlockSoundEvent cue, SoundDef sound, Transform anchor, float gain) {
            EventRaised?.Invoke(cue, HasClip(sound));
            if (HasClip(sound) && m_Sound != null && gain > 0)
                m_Sound.CreateEmitter(sound, anchor ? anchor : transform, gain);
        }
    }
}