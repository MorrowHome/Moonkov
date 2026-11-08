using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    // Only the owning client presents its research devices. These models have
    // no colliders, so a LAN host's private props cannot block other players.
    internal sealed class ResearchQuestFieldView
    {
        private static readonly ResearchDevice[] s_LowerDevices =
            { ResearchDevice.LowerSeal, ResearchDevice.NadirTerminal, ResearchDevice.CredentialsLocker };
        private static readonly ResearchDevice[] s_ControlDevices =
            { ResearchDevice.ControlAccess, ResearchDevice.OriginalArchive, ResearchDevice.Shutdown };
        private readonly Dictionary<ResearchDevice, FieldDevice> m_FieldDevices = new();
        private Material m_FrameMaterial, m_PlateMaterial, m_DisplayMaterial;
        private FieldDevice m_Emitter;
        private GameObject m_Tablet;
        private int m_RaidId;

        private sealed class FieldDevice
        {
            internal GameObject Root, Cover, Artifact;
            internal Renderer Indicator;
            private readonly MaterialPropertyBlock m_Block = new();
            private Color m_Color;

            internal void Signal(Color color, float strength = 2)
            {
                Color emission = color * strength;
                if (m_Color == emission) return;
                m_Color = emission;
                m_Block.SetColor("_BaseColor", color);
                m_Block.SetColor("_EmissionColor", emission);
                Indicator.SetPropertyBlock(m_Block);
            }
        }


        internal void Present(in ResearchQuestSnapshotRpc snapshot)
        {
            var site = RaidMissionSite.Active;
            if (!site || snapshot.Active == 0) { Clear(); return; }
            if (m_RaidId != snapshot.RaidId) { Clear(); m_RaidId = snapshot.RaidId; }
            PresentTablet(snapshot.TabletAvailable);
            bool lower = ResearchQuestRules.Has(snapshot.Active, ResearchQuest.NoWayBack);
            bool central = ResearchQuestRules.Has(snapshot.Active, ResearchQuest.EndOfTransmission);
            if (!lower && !central) return;
            foreach (var device in lower ? s_LowerDevices : s_ControlDevices)
            {
                if (!m_FieldDevices.TryGetValue(device, out var field) || !field.Root)
                {
                    var anchor = ResearchQuestRuntime.Anchor(device);
                    if (anchor == null) continue;
                    field = MakeDevice(anchor, device);
                    m_FieldDevices[device] = field;
                }
                bool covered = device is ResearchDevice.NadirTerminal or ResearchDevice.CredentialsLocker ? !snapshot.LowerOpened :
                    (device is ResearchDevice.OriginalArchive or ResearchDevice.Shutdown) && !snapshot.ControlOpened;
                bool available = snapshot.Needs(device);
                if (field.Cover && field.Cover.activeSelf != covered) field.Cover.SetActive(covered);
                if (field.Artifact && field.Artifact.activeSelf != available) field.Artifact.SetActive(available);
                field.Signal(covered ? new Color(.8f, .08f, .025f) :
                    available ? new Color(.22f, .33f, 1f) : new Color(.19f, .61f, .43f));
            }
            if (m_Emitter == null || !m_Emitter.Root)
            {
                var anchor = site.ResearchAnchor(lower ? "lower_security" : "unstable_feed");
                if (anchor == null) return;
                m_Emitter = MakeDevice(anchor, null);
            }
            bool live = lower || !snapshot.SystemsDisabled;
            m_Emitter.Signal(live ? new Color(1, .13f, .035f) : new Color(.10f, .19f, .16f),
                live ? .55f + 2.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 1.6f)) : .2f);
        }

        private void PresentTablet(bool available)
        {
            if (!available) { Destroy(m_Tablet); m_Tablet = null; return; }
            if (m_Tablet) return;
            var anchor = ResearchQuestRuntime.Anchor(ResearchDevice.Tablet);
            var surface = anchor == null ? null : RaidInteractionAim.MissionSurface(anchor.Id);
            if (!surface) return;
            m_Tablet = PhysicalItemVisuals.Create(ResearchQuestRules.Tablet, surface);
            if (!m_Tablet) return;
            m_Tablet.transform.SetPositionAndRotation(ResearchQuestRuntime.TabletMount(anchor),
                Quaternion.Euler(0, anchor.Yaw + 90, 0) * m_Tablet.transform.localRotation);
            foreach (var collider in m_Tablet.GetComponentsInChildren<Collider>()) collider.enabled = false;
        }

        internal Transform Surface(ResearchDevice device, RaidMissionAnchor anchor, bool outline = false)
        {
            if (outline && device == ResearchDevice.Tablet) return m_Tablet ? m_Tablet.transform : null;
            if (m_FieldDevices.TryGetValue(device, out var field) && field.Root) return field.Root.transform;
            return RaidInteractionAim.MissionSurface(anchor.Id);
        }

        private FieldDevice MakeDevice(RaidMissionAnchor anchor, ResearchDevice? device)
        {
            if (!m_FrameMaterial)
            {
                m_FrameMaterial = DeviceMaterial("Research hardware", new Color(.085f, .103f, .133f), .62f);
                m_PlateMaterial = DeviceMaterial("Research hardware / ceramic", new Color(.26f, .29f, .34f), .25f);
                m_DisplayMaterial = DeviceMaterial("Research hardware / indicator", new Color(.14f, .2f, .35f), .15f);
                m_DisplayMaterial.EnableKeyword("_EMISSION");
            }
            string title = device switch
            {
                ResearchDevice.LowerSeal => "L21 // ISOLATION",
                ResearchDevice.NadirTerminal => "L21 // NADIR",
                ResearchDevice.CredentialsLocker => "L21 // ACCESS",
                ResearchDevice.ControlAccess => "L22 // LUCENT",
                ResearchDevice.OriginalArchive => "L22 // ARCHIVE",
                ResearchDevice.Shutdown => "L22 // E-STOP",
                _ => "HIGH VOLTAGE"
            };
            var root = new GameObject(title);
            SceneManager.MoveGameObjectToScene(root, RaidMissionSite.Active.gameObject.scene);
            root.transform.SetPositionAndRotation(anchor.Position, Quaternion.Euler(0, anchor.Yaw, 0));
            var field = new FieldDevice { Root = root };
            float height = anchor.Position.y - anchor.Approach.y + .06f;
            Part(root.transform, "Base", new Vector3(0, -height + .055f, -.045f), new Vector3(.48f, .11f, .48f), m_PlateMaterial);
            Part(root.transform, "Frame", new Vector3(0, -height * .5f, -.09f), new Vector3(.28f, height, .26f), m_FrameMaterial);
            Part(root.transform, "Head", new Vector3(0, 0, -.065f), new Vector3(.63f, .34f, .29f), m_FrameMaterial);
            Part(root.transform, "Panel", new Vector3(0, 0, .088f), new Vector3(.54f, .255f, .025f), m_PlateMaterial);
            field.Indicator = Part(root.transform, "Status bar", new Vector3(0, .075f, .104f),
                new Vector3(.45f, .031f, .012f), m_DisplayMaterial).GetComponent<Renderer>();
            var label = new GameObject("Legend");
            label.transform.SetParent(root.transform, false);
            label.transform.SetLocalPositionAndRotation(new Vector3(0, .018f, .104f), Quaternion.Euler(0, 180, 0));
            var legend = label.AddComponent<TextMesh>();
            legend.text = title; legend.anchor = TextAnchor.MiddleCenter; legend.alignment = TextAlignment.Center;
            legend.color = new Color(.82f, .85f, .89f); legend.fontSize = 42; legend.characterSize = .036f;
            for (int i = -1; i <= 1; i++)
                Part(root.transform, "Key", new Vector3(i * .07f, -.058f, .107f), new Vector3(.038f, .031f, .018f), m_FrameMaterial);

            string code = device switch
            {
                ResearchDevice.NadirTerminal => ResearchQuestRules.NadirRecords,
                ResearchDevice.CredentialsLocker => ResearchQuestRules.Credentials,
                ResearchDevice.OriginalArchive => ResearchQuestRules.OriginalArchive,
                _ => null
            };
            if (code != null)
            {
                Part(root.transform, "Evidence tray", new Vector3(0, .164f, -.02f), new Vector3(.54f, .033f, .33f), m_PlateMaterial);
                field.Artifact = PhysicalItemVisuals.Create(code, root.transform);
                if (field.Artifact)
                {
                    field.Artifact.transform.localPosition = new Vector3(0, .184f, -.018f);
                    foreach (var collider in field.Artifact.GetComponentsInChildren<Collider>()) collider.enabled = false;
                }
            }
            if (code != null || device == ResearchDevice.Shutdown)
            {
                field.Cover = Part(root.transform, "Security cover",
                    code != null ? new Vector3(0, .232f, -.021f) : new Vector3(0, -.036f, .123f),
                    code != null ? new Vector3(.56f, .102f, .34f) : new Vector3(.43f, .15f, .028f), m_FrameMaterial);
            }
            return field;
        }

        private static Material DeviceMaterial(string name, Color color, float metallic)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", .39f);
            return material;
        }

        private static GameObject Part(Transform parent, string name, Vector3 local, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = local;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            part.GetComponent<Collider>().enabled = false;
            return part;
        }

        internal void Clear()
        {
            Destroy(m_Tablet);
            m_Tablet = null;
            foreach (var field in m_FieldDevices.Values) Destroy(field.Root);
            m_FieldDevices.Clear();
            if (m_Emitter != null) Destroy(m_Emitter.Root);
            m_Emitter = null;
            Destroy(m_FrameMaterial);
            Destroy(m_PlateMaterial);
            Destroy(m_DisplayMaterial);
            m_FrameMaterial = m_PlateMaterial = m_DisplayMaterial = null;
            m_RaidId = 0;
        }

        private static void Destroy(UnityEngine.Object obj)
        {
            if (!obj) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
