using System;
using Unity.MP_FPS.Missions;
using UnityEngine;

namespace Unity.MP_FPS
{
    [Serializable]
    public sealed class RaidMissionAnchor
    {
        public string Id, Label, Region = "MC00";
        [TextArea] public string Inscription;
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
        [SerializeField] private RaidMissionDevice[] m_Devices = Array.Empty<RaidMissionDevice>();
        [SerializeField] private SoundDef m_CutterSound, m_ArchiveSound;
        private SoundSystem.SoundInfo m_Loop;
        private ISoundSystem m_SoundSystem;
        private SoundDef m_CurrentSound;
        private float m_NextSoundTry;
        private MissionDefinition[] m_FirstChapterDefinitions;
        private MissionDefinition[] m_SecondChapterDefinitions;
        private int m_CampaignChapter;
        public static RaidMissionSite Active { get; private set; }
        public RaidMissionAnchor[] Anchors => m_Anchors;
        public MissionDefinition[] Definitions => m_CampaignChapter == 1 ? m_FirstChapterDefinitions :
            m_CampaignChapter == 2 ? m_SecondChapterDefinitions : m_Definitions;
        public bool FirstChapter => m_CampaignChapter == 1;
        public void SetCampaignChapter(int chapter)
        {
            if (chapter < 0 || chapter > 2) throw new ArgumentOutOfRangeException(nameof(chapter));
            if (chapter == 1 && m_FirstChapterDefinitions == null)
            {
                m_FirstChapterDefinitions = Array.ConvertAll(m_Definitions, d => d.Copy());
                var definition = Array.Find(m_FirstChapterDefinitions, d => d.Id == "mc00_mooncore");
                if (definition == null || Anchor("entry") == null) throw new InvalidOperationException("第一章缺少月核模板或入口锚点。");
                var nodes = new System.Collections.Generic.List<MissionObjectiveDefinition>(definition.Objectives);
                nodes.Add(new MissionObjectiveDefinition { Id = "report", Anchor = "entry", Name = "登记清理队身份", Kind = MissionObjectiveKind.Interact, Seconds = 2 });
                definition.Objectives = nodes.ToArray(); definition.OfferSeconds = 600; definition.ActiveSeconds = 600;
                if (definition.Validate() != null || definition.Objectives.Length > 8) throw new InvalidOperationException("第一章设施模板无效。");
            }
            if (chapter == 2 && m_SecondChapterDefinitions == null)
            {
                m_SecondChapterDefinitions = Array.ConvertAll(m_Definitions, d => d.Copy());
                var definition = Array.Find(m_SecondChapterDefinitions, d => d.Id == "mc00_archive");
                if (definition == null) throw new InvalidOperationException("第二章缺少档案模板。");
                definition.OfferSeconds = 600; definition.ActiveSeconds = 600;
                if (definition.Validate() != null || definition.Objectives.Length > 8)
                    throw new InvalidOperationException("第二章设施模板无效。");
            }
            m_CampaignChapter = chapter;
        }
        public uint Seed => (uint)m_Seed;
        public float DiscoveryRadius => m_DiscoveryRadius;
        public Vector3 Center => Anchor("sampler")?.Approach ?? transform.position;
        public RaidMissionAnchor Anchor(string id) => Array.Find(m_Anchors, a => a != null && a.Id == id);

        public void Present(RaidMissionSnapshotRpc snapshot, bool playerActive)
        {
            bool known = snapshot.OperationId.Length > 0 && snapshot.DefinitionIndex >= 0 && snapshot.DefinitionIndex < Definitions.Length;
            var definition = known ? Definitions[snapshot.DefinitionIndex] : null;
            bool machineRunning = false; string machineAnchor = null;
            if (m_Devices != null) foreach (var device in m_Devices)
            {
                if (!device) continue;
                bool active = known && device.AnchorId == "entry", complete = true, running = false, hasObjective = false;
                if (definition != null) for (int i = 0; i < definition.Objectives.Length; i++)
                {
                    var n = definition.Objectives[i]; if (n.Anchor != device.AnchorId) continue;
                    active = true; hasObjective = true; complete &= (snapshot.CompletedMask & (1u << i)) != 0;
                    if (n.Kind == MissionObjectiveKind.Process && (snapshot.CompletedMask & n.Prerequisites) == n.Prerequisites &&
                        (snapshot.CompletedMask & (1u << i)) == 0 && snapshot.Phase == MissionPhase.Active)
                    { running = true; machineRunning = true; machineAnchor = n.Anchor; }
                }
                int products = known && snapshot.Phase == MissionPhase.OutputReady &&
                    device.AnchorId == (definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive") ? snapshot.OutputRemaining : 0;
                device.Present(active, hasObjective && complete && !running, running, known && snapshot.Phase == MissionPhase.Expired, products, definition?.ArtifactCode == "mooncore_fragment");
            }
            var desired = machineRunning && playerActive ? definition.ArtifactCode == "mooncore_fragment" ? m_CutterSound : m_ArchiveSound : null;
            if (desired != m_CurrentSound || (desired && Time.unscaledTime >= m_NextSoundTry && (m_Loop?.Handle == null || !m_Loop.Handle.IsValid())))
            {
                StopPresentation();
                if (desired)
                {
                    m_CurrentSound = desired; m_SoundSystem = GameManager.Instance?.SoundSystem;
                    m_NextSoundTry = Time.unscaledTime + 1;
                    m_Loop = MoonkovAudio.PlayWeaponLayer(desired, Anchor(machineAnchor).Position, 1, 1);
                }
            }
        }
        public void StopPresentation()
        {
            if (m_Loop?.Handle != null && m_Loop.Handle.IsValid()) m_SoundSystem?.Stop(m_Loop, .2f);
            m_Loop = null; m_CurrentSound = null; m_SoundSystem = null;
        }
#if UNITY_EDITOR
        public void Author(RaidMissionAnchor[] anchors, RaidMissionDevice[] devices, SoundDef cutter, SoundDef archive)
        { m_Anchors = anchors; m_Devices = devices; m_CutterSound = cutter; m_ArchiveSound = archive; }
#endif

        public string Validate()
        {
            if (m_Definitions == null || m_Definitions.Length < 2 || m_Anchors == null || m_Anchors.Length > 32) return "缺少任务模板或设备锚点";
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var a in m_Anchors)
                if (a == null || string.IsNullOrWhiteSpace(a.Id) || !ids.Add(a.Id) ||
                    !Finite(a.Position) || !Finite(a.Approach)) return "任务锚点重复或坐标无效";
            var definitionIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var d in m_Definitions)
            {
                if (d == null || d.Validate() != null || d.Objectives.Length > 8 || !definitionIds.Add(d.Id)) return "任务模板无效、重复或节点超过同步上限";
                if (d.Version != 1 || !Inventory.MissionInventory.IsArtifact(d.ArtifactCode)) return "任务产物或内容版本不受当前保存协议支持";
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
        private void OnDisable() { StopPresentation(); m_CampaignChapter = 0; if (Active == this) Active = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSite() => Active = null;
    }
}
