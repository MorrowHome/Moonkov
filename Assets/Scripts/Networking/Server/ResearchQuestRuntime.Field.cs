using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public sealed partial class ResearchQuestRuntime
    {
        private static readonly ResearchDevice[] s_LowerDevices =
            { ResearchDevice.LowerSeal, ResearchDevice.NadirTerminal, ResearchDevice.CredentialsLocker };
        private static readonly ResearchDevice[] s_ControlDevices =
            { ResearchDevice.ControlAccess, ResearchDevice.OriginalArchive, ResearchDevice.Shutdown };
        private readonly Dictionary<ResearchDevice, FieldDevice> m_FieldDevices = new();
        private readonly HashSet<ResearchDevice> m_AnnouncedDevices = new();
        private bool m_LowerOpened, m_ControlOpened;
        private Material m_FrameMaterial, m_PlateMaterial, m_DisplayMaterial;
        private FieldDevice m_Emitter;
        private RaidMissionAnchor m_EmitterAnchor;
        private double m_NextPulse;

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

        private void UpdateFieldDevices(Vector3 player, double now, uint tick)
        {
            bool lower = Active(ResearchQuest.NoWayBack);
            bool central = Active(ResearchQuest.EndOfTransmission);
            if (!lower && !central) return;
            if (!RaidMissionSite.Active) return;
            var devices = lower ? s_LowerDevices : s_ControlDevices;
            foreach (var device in devices)
            {
                if (!m_FieldDevices.TryGetValue(device, out var field) || !field.Root)
                {
                    var anchor = Anchor(device);
                    if (anchor == null) continue;
                    field = MakeDevice(anchor, device);
                    m_FieldDevices[device] = field;
                }
                bool sealedDevice = device is ResearchDevice.NadirTerminal or ResearchDevice.CredentialsLocker ? !m_LowerOpened :
                    (device is ResearchDevice.OriginalArchive or ResearchDevice.Shutdown) && !m_ControlOpened;
                if (field.Cover && field.Cover.activeSelf != sealedDevice) field.Cover.SetActive(sealedDevice);
                if (field.Artifact && field.Artifact.activeSelf == sealedDevice) field.Artifact.SetActive(!sealedDevice);
                bool available = Needs(device);
                field.Signal(sealedDevice ? new Color(.8f, .08f, .025f) : available ? new Color(.22f, .33f, 1f) : new Color(.19f, .61f, .43f));
            }
            if (m_Emitter == null || !m_Emitter.Root)
            {
                m_EmitterAnchor = RaidMissionSite.Active.ResearchAnchor(lower ? "lower_security" : "unstable_feed");
                if (m_EmitterAnchor == null) return;
                m_Emitter = MakeDevice(m_EmitterAnchor, null);
                m_NextPulse = now + 1.05;
            }
            bool live = lower || !m_Result.SystemsDisabled;
            // The small energized area is localized to this physical emitter. Damage
            // uses the normal shield and hit path and cannot affect other deployments.
            var point = m_EmitterAnchor.Position;
            var eye = player + Vector3.up;
            bool near = live && (eye - point).sqrMagnitude < (lower ? 2.25f : 3.24f) &&
                RaidInteractionAim.Visible(eye, point, m_Emitter.Root.transform);
            if (!near) m_NextPulse = now + 1.05;
            m_Emitter.Signal(live ? new Color(1, .13f, .035f) : new Color(.10f, .19f, .16f),
                live ? .55f + 2.35f * Mathf.Abs(Mathf.Sin((float)now * (near ? 5 : 1.6f))) : .2f);
            if (near && now >= m_NextPulse)
            {
                m_NextPulse = now + 2.6;
                BountyClashDamage.Apply(m_World, m_Player, default(ClashDamageSource), BodyPart.Chest,
                    lower ? 7 : 12, tick, RaidDamageKind.Fire, point);
                Notice("discharge", now);
            }
        }

        private void CompleteFieldDevice(ResearchDevice device, double now)
        {
            switch (device)
            {
                case ResearchDevice.LowerSeal:
                    m_LowerOpened = true;
                    AlertAliens(device);
                    Notice("lower-open", now);
                    break;
                case ResearchDevice.NadirTerminal:
                {
                    string id = Guid.NewGuid().ToString("D");
                    if (!Recover(ResearchQuestRules.NadirRecords, id, now)) return;
                    m_Result.NadirRecordsId = id;
                    RemoveArtifact(device);
                    Notice(m_Result.CredentialsId == null ? "credentials" : "extract", now);
                    break;
                }
                case ResearchDevice.CredentialsLocker:
                {
                    string id = Guid.NewGuid().ToString("D");
                    if (!Recover(ResearchQuestRules.Credentials, id, now)) return;
                    m_Result.CredentialsId = id;
                    RemoveArtifact(device);
                    Notice(m_Result.NadirRecordsId == null ? "nadir-records" : "extract", now);
                    break;
                }
                case ResearchDevice.ControlAccess:
                    m_ControlOpened = true;
                    m_Result.CredentialsId = m_Saved.CredentialsId;
                    AlertAliens(device);
                    Notice("control-open", now);
                    break;
                case ResearchDevice.OriginalArchive:
                {
                    string id = Guid.NewGuid().ToString("D");
                    if (!Recover(ResearchQuestRules.OriginalArchive, id, now)) return;
                    m_Result.OriginalArchiveId = id;
                    RemoveArtifact(device);
                    Notice(m_Result.SystemsDisabled ? "extract" : "shutdown", now);
                    break;
                }
                case ResearchDevice.Shutdown:
                    m_Result.SystemsDisabled = true;
                    Notice(m_Result.OriginalArchiveId == null ? "archive" : "extract", now);
                    break;
            }
        }

        private void AlertAliens(ResearchDevice device)
        {
            var anchor = Anchor(device);
            if (anchor == null || !m_AnnouncedDevices.Add(device)) return;
            const float range = 22;
            var manager = m_World.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(ProceduralAlienEnemy.State), typeof(GhostGameObjectLink), typeof(LocalTransform));
            using var nearby = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var entity in nearby)
            {
                var position = (Vector3)manager.GetComponentData<LocalTransform>(entity).Position;
                if ((position - anchor.Position).sqrMagnitude > range * range) continue;
                var link = manager.GetComponentObject<GhostGameObjectLink>(entity);
                var alien = link.LinkedInstance ? link.LinkedInstance.GetComponent<ProceduralAlienEnemy>() : null;
                if (alien && MissionAlarm.Audible(anchor.Position, alien.AimPoint, range))
                    alien.HearMissionAlarm($"moonrat-{m_RaidId}-{device}", anchor.Approach);
            }
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
            Part(root.transform, "Head", new Vector3(0, 0, -.065f), new Vector3(.63f, .34f, .29f), m_FrameMaterial, true);
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
                if (field.Artifact) field.Artifact.transform.localPosition = new Vector3(0, .184f, -.018f);
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

        private static GameObject Part(Transform parent, string name, Vector3 local, Vector3 scale, Material material, bool interactive = false)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = local;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            part.GetComponent<Collider>().enabled = interactive;
            return part;
        }

        private void RemoveArtifact(ResearchDevice device)
        {
            if (!m_FieldDevices.TryGetValue(device, out var field) || !field.Artifact) return;
            DestroyFieldObject(field.Artifact);
            field.Artifact = null;
        }

        private void RemoveFieldObjects()
        {
            RemoveTablet();
            foreach (var field in m_FieldDevices.Values) DestroyFieldObject(field.Root);
            m_FieldDevices.Clear();
            if (m_Emitter != null) DestroyFieldObject(m_Emitter.Root);
            DestroyFieldObject(m_FrameMaterial);
            DestroyFieldObject(m_PlateMaterial);
            DestroyFieldObject(m_DisplayMaterial);
        }

        private static void DestroyFieldObject(UnityEngine.Object obj)
        {
            if (!obj) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
