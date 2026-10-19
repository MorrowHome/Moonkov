using System;
using Unity.MP_FPS.Missions;
using UnityEngine;

namespace Unity.MP_FPS
{
    [Serializable]
    public sealed class RaidMissionAnchor
    {
        public string Id, Label, Region = "MC00";
        public Vector3 Position, Approach;
        public float Yaw;
    }

    // Scene authoring only. All mutable operation/interaction state belongs to the server World.
    [DisallowMultipleComponent]
    public sealed class RaidMissionSite : MonoBehaviour
    {
        [SerializeField] private RaidMissionAnchor[] m_Anchors = Array.Empty<RaidMissionAnchor>();
        [SerializeField] private MissionDefinition[] m_Definitions = { MissionCatalog.Mooncore(), MissionCatalog.Archive() };
        [SerializeField, Min(0)] private int m_Seed;
        [SerializeField, Min(10)] private float m_DiscoveryRadius = 120;
        public static RaidMissionSite Active { get; private set; }
        public RaidMissionAnchor[] Anchors => m_Anchors;
        public MissionDefinition[] Definitions => m_Definitions;
        public uint Seed => (uint)m_Seed;
        public float DiscoveryRadius => m_DiscoveryRadius;
        public Vector3 Center => Anchor("sampler")?.Approach ?? transform.position;
        public RaidMissionAnchor Anchor(string id) => Array.Find(m_Anchors, a => a != null && a.Id == id);

        public string Validate()
        {
            if (m_Definitions == null || m_Definitions.Length < 2 || m_Anchors == null || m_Anchors.Length > 32) return "缺少任务模板或设备锚点";
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var a in m_Anchors)
                if (a == null || string.IsNullOrWhiteSpace(a.Id) || !ids.Add(a.Id) ||
                    !Finite(a.Position) || !Finite(a.Approach)) return "任务锚点重复或坐标无效";
            foreach (var d in m_Definitions)
            {
                if (d == null || d.Validate() != null || d.Objectives.Length > 8) return "任务模板无效或节点超过同步上限";
                foreach (var n in d.Objectives) if (Anchor(n.Anchor) == null) return "任务缺少设备: " + n.Anchor;
            }
            return null;
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        private void OnEnable()
        {
            var error = Validate();
            if (error != null) { Debug.LogWarning("[Missions] " + error, this); return; }
            if (Active && Active != this) { Debug.LogError("Only one authored mission region is supported in this scene.", this); return; }
            Active = this;
        }
        private void OnDisable() { if (Active == this) Active = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSite() => Active = null;
    }
}
