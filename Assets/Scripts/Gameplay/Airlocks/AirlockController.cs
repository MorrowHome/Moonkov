using System;
using System.Collections.Generic;
using UnityEngine;



namespace Unity.MP_FPS
{
    [Serializable]
    public sealed class AirlockPanelPose
    {
        public string PanelId;
        public Transform Motion;
        public Vector3 ClosedPosition, OpenPosition;
        public Quaternion ClosedRotation = Quaternion.identity, OpenRotation = Quaternion.identity;
        [NonSerialized] public BoxCollider[] Colliders;
        public void Apply(float fraction)
        {
            if (!Motion) return;
            Motion.SetLocalPositionAndRotation(Vector3.Lerp(ClosedPosition, OpenPosition, fraction),
                Quaternion.Slerp(ClosedRotation, OpenRotation, fraction));
        }
    }

    [DisallowMultipleComponent]
    public sealed class AirlockController : MonoBehaviour
    {
        private static readonly List<AirlockController> s_Active = new List<AirlockController>();
        [SerializeField] private string m_AirlockId = "EVA_EAST_01";
        [SerializeField] private AirlockSettings m_Settings = new AirlockSettings();
        [SerializeField] private AirlockPanelPose[] m_OuterPanels = Array.Empty<AirlockPanelPose>();
        [SerializeField] private AirlockPanelPose[] m_InnerPanels = Array.Empty<AirlockPanelPose>();
        [SerializeField] private BoxCollider m_OuterAperture, m_InnerAperture;
        [SerializeField] private BreathableZone m_ChamberZone, m_InsideZone;
        [SerializeField] private BreathableZone[] m_ChamberSegments = Array.Empty<BreathableZone>();
        [SerializeField] private BreathableZone[] m_InsideSegments = Array.Empty<BreathableZone>();
        private readonly Collider[] m_Overlap = new Collider[32];
        private readonly RaycastHit[] m_Sweep = new RaycastHit[64];
        [SerializeField] private BreathableZone m_OuterBridge, m_InnerBridge;
        private AirlockRules m_Rules;
        private float m_LastOuter = -1, m_LastInner = -1;
        private uint m_LastRevision = uint.MaxValue;
        public static IReadOnlyList<AirlockController> Active => s_Active;
        public string AirlockId => m_AirlockId;
        public AirlockSettings Settings => m_Settings;
        public AirlockState State => m_Rules == null ? default : m_Rules.State;
        public bool Ready => m_Rules != null && m_Settings.IsValid && m_OuterPanels.Length > 0 && m_InnerPanels.Length > 0 && m_OuterAperture && m_InnerAperture;
        public event Action StateChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => s_Active.Clear();
        private void OnEnable()
        {
            if (m_Rules == null && m_Settings.IsValid) m_Rules = new AirlockRules(m_Settings);
            if (!s_Active.Contains(this)) s_Active.Add(this);
            WireRooms(); Present();
        }
        private void OnDisable() => s_Active.Remove(this);
        public void Configure(string id, AirlockSettings settings, AirlockPanelPose[] outer, AirlockPanelPose[] inner,
            BoxCollider outerSensor, BoxCollider innerSensor, BreathableZone chamber, BreathableZone inside)
        {
            if (string.IsNullOrWhiteSpace(id) || settings == null || !settings.IsValid) throw new ArgumentException("Invalid airlock configuration");
            m_AirlockId = id; m_Settings = settings; m_OuterPanels = outer ?? Array.Empty<AirlockPanelPose>();
            m_InnerPanels = inner ?? Array.Empty<AirlockPanelPose>(); m_OuterAperture = outerSensor; m_InnerAperture = innerSensor;
            m_ChamberZone = chamber; m_InsideZone = inside; m_Rules = new AirlockRules(settings);
            m_LastOuter = m_LastInner = -1; m_LastRevision = uint.MaxValue; WireRooms(); Present();
        }
        private void WireRooms()
        {
            if (m_ChamberZone) m_ChamberZone.Configure(m_AirlockId + ".CHAMBER", 0, m_Settings.SupplyOxygenFraction, 100, this);
            if (m_InsideZone) m_InsideZone.Configure(m_AirlockId + ".INSIDE", m_Settings.InsidePressureKPa, m_Settings.SupplyOxygenFraction, 10);
            foreach (var zone in m_ChamberSegments) if (zone) zone.Configure(m_AirlockId + ".CHAMBER", 0, m_Settings.SupplyOxygenFraction, 100, this);
            foreach (var zone in m_InsideSegments) if (zone) zone.Configure(m_AirlockId + ".INSIDE", m_Settings.InsidePressureKPa, m_Settings.SupplyOxygenFraction, 10);
        }
        public void ConfigureVolumeSegments(BreathableZone[] chamber, BreathableZone[] inside)
        {
            m_ChamberSegments = chamber ?? Array.Empty<BreathableZone>();
            m_InsideSegments = inside ?? Array.Empty<BreathableZone>(); WireRooms();
        }
        public void ConfigureApertureBridges(BreathableZone outer, BreathableZone inner)
        {
            m_OuterBridge = outer; m_InnerBridge = inner;
            if (outer) outer.Configure(m_AirlockId + ".CHAMBER",0,m_Settings.SupplyOxygenFraction,110,this);
            if (inner) inner.Configure(m_AirlockId + ".CHAMBER",0,m_Settings.SupplyOxygenFraction,110,this);
            Present();
        }
        private void Update()
        {
            if (!AirlockNetworkContext.HasNetworkWorlds) TickAuthoritative(Time.deltaTime);
            Present();
        }
        public void TickAuthoritative(float dt)
        {
            if (!Ready) return;
            var s = State; float step = dt / m_Settings.DoorTravelSeconds;
            bool outerClose = !s.OuterOpening && s.OuterFraction > 0 &&
                (Obstructed(m_OuterAperture) || ActorSweep(m_OuterPanels,s.OuterFraction,Mathf.Max(0,s.OuterFraction-step)));
            bool innerClose = !s.InnerOpening && s.InnerFraction > 0 &&
                (Obstructed(m_InnerAperture) || ActorSweep(m_InnerPanels,s.InnerFraction,Mathf.Max(0,s.InnerFraction-step)));
            bool outerOpen = (s.OuterOpening || outerClose) && s.OuterFraction < 1 && ActorSweep(m_OuterPanels,s.OuterFraction,Mathf.Min(1,s.OuterFraction+step));
            bool innerOpen = (s.InnerOpening || innerClose) && s.InnerFraction < 1 && ActorSweep(m_InnerPanels,s.InnerFraction,Mathf.Min(1,s.InnerFraction+step));
            m_Rules.Tick(dt,outerClose,innerClose,outerOpen,innerOpen);
            Present();
        }
        public AirlockFeedback Check(AirlockCommand command) => Ready ?
            m_Rules.Check(command, command == AirlockCommand.CloseOuter && Obstructed(m_OuterAperture),
                command == AirlockCommand.CloseInner && Obstructed(m_InnerAperture)) : AirlockFeedback.Unavailable;
        public AirlockFeedback RequestAuthoritative(AirlockCommand command)
        {
            if (!Ready) return AirlockFeedback.Unavailable;
            var result = m_Rules.Request(command, command == AirlockCommand.CloseOuter && Obstructed(m_OuterAperture),
                command == AirlockCommand.CloseInner && Obstructed(m_InnerAperture));
            Present(); return result;
        }
        public bool ApplyNetworkState(AirlockState state, bool newSession)
        {
            if (m_Rules == null || !m_Rules.ApplySnapshot(state, newSession)) return false;
            Present(); return true;
        }
        private bool Obstructed(BoxCollider sensor)
        {
            if (!sensor || !sensor.enabled) return true;
            var scale = sensor.transform.lossyScale;
            var half = Vector3.Scale(sensor.size * .5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            int count = UnityEngine.Physics.OverlapBoxNonAlloc(sensor.transform.TransformPoint(sensor.center), half, m_Overlap,
                sensor.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == m_Overlap.Length) return true;
            for (int i = 0; i < count; i++)
            {
                var hit = m_Overlap[i];
                if (!hit || hit.transform.IsChildOf(transform)) continue;
                if (hit.GetComponentInParent<UnityEngine.CharacterController>() || hit.GetComponentInParent<PlayerGhost>() || hit.attachedRigidbody) return true;
            }
            return false;
        }
        private bool Actor(Collider hit) => hit && !hit.transform.IsChildOf(transform) &&
            (hit.GetComponentInParent<UnityEngine.CharacterController>() || hit.GetComponentInParent<PlayerGhost>() || hit.attachedRigidbody);
        private bool ActorSweep(AirlockPanelPose[] panels,float from,float to)
        {
            if (Mathf.Abs(to-from)<.000001f) return false;
            foreach(var panel in panels)
            {
                if (panel == null || !panel.Motion) continue;
                if (panel.Colliders == null) panel.Colliders = panel.Motion.GetComponentsInChildren<BoxCollider>();
                var parent=panel.Motion.parent; var start=parent.TransformPoint(Vector3.Lerp(panel.ClosedPosition,panel.OpenPosition,from));
                var end=parent.TransformPoint(Vector3.Lerp(panel.ClosedPosition,panel.OpenPosition,to));var delta=end-start;
                var rotation=parent.rotation*Quaternion.Slerp(panel.ClosedRotation,panel.OpenRotation,from);
                foreach(var box in panel.Colliders)
                {
                    if (!box || box.isTrigger || !box.enabled) continue;
                    var center=box.transform.TransformPoint(box.center);var scale=box.transform.lossyScale;
                    var half=Vector3.Scale(box.size*.5f,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
                    int n=UnityEngine.Physics.BoxCastNonAlloc(center,half,delta.normalized,m_Sweep,rotation,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
                    if(n==m_Sweep.Length)return true;
                    for(int i=0;i<n;i++)if(Actor(m_Sweep[i].collider))return true;
                    n=UnityEngine.Physics.OverlapBoxNonAlloc(center+delta,half,m_Overlap,rotation,~0,QueryTriggerInteraction.Ignore);
                    if(n==m_Overlap.Length)return true;
                    for(int i=0;i<n;i++)if(Actor(m_Overlap[i]))return true;
                }
            }
            return false;
        }
        private void Present()
        {
            if (m_Rules == null) return;
            var s = State;
            if (m_OuterBridge) m_OuterBridge.enabled = s.OuterFraction > m_Settings.SealedFractionTolerance;
            if (m_InnerBridge) m_InnerBridge.enabled = s.InnerFraction > m_Settings.SealedFractionTolerance;
            bool moved = false;
            if (s.OuterFraction != m_LastOuter) { foreach (var p in m_OuterPanels) p?.Apply(s.OuterFraction); m_LastOuter = s.OuterFraction; moved = true; }
            if (s.InnerFraction != m_LastInner) { foreach (var p in m_InnerPanels) p?.Apply(s.InnerFraction); m_LastInner = s.InnerFraction; moved = true; }
            if (moved && Application.isPlaying) UnityEngine.Physics.SyncTransforms();
            if (s.Revision != m_LastRevision) { m_LastRevision = s.Revision; StateChanged?.Invoke(); }
        }
        public static AirlockController Find(string id)
        {
            AirlockController match = null;
            foreach (var c in s_Active) if (c && c.isActiveAndEnabled && c.AirlockId == id)
            { if (match) return null; match = c; }
            return match;
        }
        public static bool AirConnected(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;
            foreach (var c in s_Active)
                if (c && c.Ready && c.State.InnerFraction > c.Settings.SealedFractionTolerance &&
                    (a == c.AirlockId + ".CHAMBER" && b == c.AirlockId + ".INSIDE" || b == c.AirlockId + ".CHAMBER" && a == c.AirlockId + ".INSIDE")) return true;
            return false;
        }
    }
}
