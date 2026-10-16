using System;
using UnityEngine;



namespace Unity.MP_FPS
{
    public enum AirlockSoundPath : byte { Airborne, StructureContact, HelmetInternal, Radio }
    public enum AirlockSoundEvent : byte { ValveFlow, Pump, OuterMotor, InnerMotor, OuterUnlock, InnerUnlock, OuterLocked, InnerLocked }
    public static class AirlockAcoustics
    {
        public static float Gain(AirlockSoundPath path, AtmosphereSample source, AtmosphereSample listener, bool structureContact, bool radioLinked = true)
        {
            switch (path)
            {
                case AirlockSoundPath.HelmetInternal: return 1;
                case AirlockSoundPath.Radio: return radioLinked ? 1 : 0;
                case AirlockSoundPath.StructureContact: return structureContact ? .35f : 0;
                default:
                    return source.PressureKPa > .01f && listener.PressureKPa > .01f &&
                        AirlockController.AirConnected(source.AirspaceId, listener.AirspaceId) ?
                        Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(source.PressureKPa, listener.PressureKPa) / 101.325f)) : 0;
            }
        }
    }

    // Uses the existing pooled SoundSystem and mixer; never modifies the global mute or AudioListener volume.
    [DisallowMultipleComponent, RequireComponent(typeof(AirlockController))]
    public sealed class AirlockAudio : MonoBehaviour
    {
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
        public event Action<AirlockSoundEvent, bool> EventRaised; // bool: a real clip is assigned, false: explicit pending cue.

        public void Configure(AirlockController owner, Transform listener, Transform valve, Transform pump, Transform outer, Transform inner,
            SoundDef valveSound = null, SoundDef pumpSound = null, SoundDef doorSound = null, SoundDef lockSound = null, SoundDef unlockSound = null,
            Transform outerLatch = null, Transform innerLatch = null)
        {
            m_Owner = owner; m_Listener = listener; m_ValveAnchor = valve; m_PumpAnchor = pump; m_OuterAnchor = outer; m_InnerAnchor = inner;
            m_Valve = valveSound; m_Pump = pumpSound; m_DoorMotor = doorSound; m_Lock = lockSound; m_Unlock = unlockSound;
            m_OuterLatchAnchor = outerLatch; m_InnerLatchAnchor = innerLatch;
        }
        public void BindSoundSystem(ISoundSystem sound) => m_Sound = sound;
        public void BindListener(Transform listener) => m_Listener = listener;
        public void ConfigureStructure(Transform structureRoot) => m_StructureRoot = structureRoot;
        private void Awake() { if (!m_Owner) m_Owner = GetComponent<AirlockController>(); }
        private void OnDisable()
        {
            for (int i = 0; i < m_Loops.Length; i++) StopLoop(i);
            m_HasPrevious = false;
        }
        private void Update()
        {
            if (!m_Owner || !m_Owner.Ready) return;
            if (m_Sound == null && GameManager.Instance) m_Sound = GameManager.Instance.SoundSystem;
            if (!m_Listener && Camera.main) m_Listener = Camera.main.transform;
            var s = m_Owner.State;
            ValveActive = s.Equalizing && Mathf.Abs(s.PressureKPa - (s.TargetInside ? m_Owner.Settings.InsidePressureKPa : m_Owner.Settings.OutsidePressureKPa)) > .0001f;
            PumpActive = ValveActive && s.TargetInside;
            // A requested direction can remain set while a panel is held by an actor.
            // Drive each motor from its observed motion, independently of shared feedback.
            OuterMotorActive = m_HasPrevious && Mathf.Abs(s.OuterFraction - m_Previous.OuterFraction) > .000001f;
            InnerMotorActive = m_HasPrevious && Mathf.Abs(s.InnerFraction - m_Previous.InnerFraction) > .000001f;
            AtmosphereSample listener = default;
            if (m_Listener) BreathableZone.TrySample(m_Listener.position, out listener);
            var source = new AtmosphereSample(m_Owner.AirlockId + ".CHAMBER", s.PressureKPa, m_Owner.Settings.SupplyOxygenFraction, false);
            bool contact = HasStructureContact();
            AirGain = AirlockAcoustics.Gain(AirlockSoundPath.Airborne, source, listener, contact);
            ContactGain = AirlockAcoustics.Gain(AirlockSoundPath.StructureContact, source, listener, contact);
            // One localized emitter represents its two possible physical propagation paths; a contact path is low-passed.
            float mechanismGain = Mathf.Max(AirGain, ContactGain);
            float delta = Mathf.Abs((s.TargetInside ? m_Owner.Settings.InsidePressureKPa : m_Owner.Settings.OutsidePressureKPa) - s.PressureKPa);
            Loop(0, ValveActive, m_Valve, m_ValveAnchor, mechanismGain * Mathf.Sqrt(Mathf.Clamp01(delta / m_Owner.Settings.InsidePressureKPa)), AirlockSoundEvent.ValveFlow);
            Loop(1, PumpActive, m_Pump, m_PumpAnchor, mechanismGain, AirlockSoundEvent.Pump);
            Loop(2, OuterMotorActive, m_DoorMotor, m_OuterAnchor, mechanismGain, AirlockSoundEvent.OuterMotor);
            Loop(3, InnerMotorActive, m_DoorMotor, m_InnerAnchor, mechanismGain, AirlockSoundEvent.InnerMotor);
            if (m_HasPrevious)
            {
                if (s.OuterFraction > 0 && m_Previous.OuterFraction == 0) OneShot(AirlockSoundEvent.OuterUnlock, m_Unlock, m_OuterLatchAnchor ? m_OuterLatchAnchor : m_OuterAnchor, mechanismGain);
                if (s.InnerFraction > 0 && m_Previous.InnerFraction == 0) OneShot(AirlockSoundEvent.InnerUnlock, m_Unlock, m_InnerLatchAnchor ? m_InnerLatchAnchor : m_InnerAnchor, mechanismGain);
                if (s.OuterFraction == 0 && m_Previous.OuterFraction > 0) OneShot(AirlockSoundEvent.OuterLocked, m_Lock, m_OuterLatchAnchor ? m_OuterLatchAnchor : m_OuterAnchor, mechanismGain);
                if (s.InnerFraction == 0 && m_Previous.InnerFraction > 0) OneShot(AirlockSoundEvent.InnerLocked, m_Lock, m_InnerLatchAnchor ? m_InnerLatchAnchor : m_InnerAnchor, mechanismGain);
            }
            m_Previous = s; m_HasPrevious = true;
        }
        private bool HasStructureContact()
        {
            if (!m_Listener) return false;
            UnityEngine.CharacterController capsule = null;
            foreach (var candidate in m_Listener.GetComponentsInParent<UnityEngine.CharacterController>())
                if (candidate.enabled) { capsule = candidate; break; }
            if (!capsule || !capsule.isGrounded) return false;
            var origin = capsule.transform.position + Vector3.up * .2f;
            int n = UnityEngine.Physics.RaycastNonAlloc(origin, Vector3.down, m_Contacts, .5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = m_Contacts[i].collider;
                if (hit && (hit.GetComponentInParent<AirlockController>() == m_Owner ||
                    m_StructureRoot && hit.transform.IsChildOf(m_StructureRoot))) return true;
            }
            return false;
        }
        private static bool HasClip(SoundDef sound) => sound && sound.Clips != null && sound.Clips.Count > 0 && sound.Clips[0];
        private void Loop(int index, bool active, SoundDef sound, Transform anchor, float gain, AirlockSoundEvent cue)
        {
            if (!active) { m_CueActive[index] = false; StopLoop(index); return; }
            if (!m_CueActive[index]) { EventRaised?.Invoke(cue, HasClip(sound)); m_CueActive[index] = true; }
            var handle = m_Loops[index];
            bool contactRoute = ContactGain > AirGain;
            if (handle?.Handle != null && handle.Handle.IsValid() && m_ContactRoute[index] != contactRoute)
            { StopLoop(index); handle = null; }
            if (handle == null || handle.Handle == null || !handle.Handle.IsValid())
            {
                if (!HasClip(sound) || m_Sound == null || gain <= 0) return;
                handle = new SoundSystem.SoundInfo { SoundDefinition = sound, SoundTransform = anchor ? anchor : transform,
                    SoundDefOverrideInfo = new SoundEmitter.SoundDefOverrideData(sound) };
                if (contactRoute && sound.LowPassFilter != null) handle.SoundDefOverrideInfo.BaseLowPassCutoff = 1200 - sound.LowPassFilter.CutoffMin;
                if (!m_Sound.PlayEmitter(handle, gain)) return;
                m_ContactRoute[index] = contactRoute;
                m_Loops[index] = handle;
            }
            if (handle?.Handle != null && handle.Handle.IsValid())
            {
                handle.Handle.Emitter.SetVolume(gain);
            }
        }
        private void StopLoop(int index)
        { if (m_Loops[index] != null && m_Sound != null) m_Sound.Stop(m_Loops[index], .05f); m_Loops[index] = null; }
        private void OneShot(AirlockSoundEvent cue, SoundDef sound, Transform anchor, float gain)
        {
            EventRaised?.Invoke(cue, HasClip(sound));
            if (HasClip(sound) && m_Sound != null && gain > 0) m_Sound.CreateEmitter(sound, anchor ? anchor : transform, gain);
        }
    }
}
