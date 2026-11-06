using System;
using System.Collections.Generic;
using Unity.MP_FPS.Online;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Unity.MP_FPS.Client
{
    // One small scene, one camera and one target for the entire squad. These
    // actors reuse the menu's equipment/animation code, with no gameplay World.
    public sealed class OnlineSquadStage : IDisposable
    {
        private static readonly float[] SlotX = { -1.95f, -.65f, .65f, 1.95f };
        private static readonly float[] SlotZ = { -.26f, .16f, .02f, -.34f };
        private readonly Image m_Image;
        private readonly MenuCharacterView[] m_Actors = new MenuCharacterView[4];
        private readonly LobbyAppearance[] m_Appearances = new LobbyAppearance[4];
        private readonly string[] m_Ids = new string[4];
        private readonly List<Material> m_Materials = new List<Material>();
        private readonly IVisualElementScheduledItem m_Timer;
        private LobbyMember[] m_Members = new LobbyMember[4];
        private Scene m_Scene;
        private GameObject m_Root;
        private Camera m_Camera;
        private RenderTexture m_Target;
        private bool m_Disposed, m_Dirty = true, m_PreviewScene;
        private double m_LastTime;
        public event Action FramingChanged;
        private bool m_ReducedMotion;
        public bool ReducedMotion
        {
            get => m_ReducedMotion;
            set { if (m_ReducedMotion == value) return; m_ReducedMotion = value; m_Dirty = true; if (m_Root != null) Reconcile(); }
        }
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
#endif
        public static bool SystemReducedMotion
        {
            get
            {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                return SystemParametersInfo(0x1042, 0, out int enabled, 0) && enabled == 0;
#else
                return false;
#endif
            }
        }
        public RenderTexture Target => m_Target;
        public Scene DisplayScene => m_Scene;
        public int ActorCount { get { int count = 0; foreach (var actor in m_Actors) if (actor != null) count++; return count; } }

        public OnlineSquadStage(Image image)
        {
            m_Image = image ?? throw new ArgumentNullException(nameof(image));
            m_Image.pickingMode = PickingMode.Ignore;
            m_Image.scaleMode = ScaleMode.ScaleToFit;
            m_Image.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_LastTime = Time.realtimeSinceStartupAsDouble;
            m_Timer = image.schedule.Execute(Tick).Every(33);
        }

        public void Present(LobbyMember[] members)
        {
            if (m_Disposed) return;
            for (int i = 0; i < 4; i++) m_Members[i] = members != null && i < members.Length ? members[i] : null;
            m_Dirty = true;
            if (m_Root != null) Reconcile();
        }

        private void Reconcile()
        {
            for (int i = 0; i < 4; i++)
            {
                var member = m_Members[i];
                var appearance = member?.Appearance;
                if (member == null || appearance == null)
                { ReleaseActor(i); continue; }
                if (m_Ids[i] != member.PlayerId || m_Actors[i]?.CharacterIndex != appearance.CharacterIndex)
                {
                    ReleaseActor(i);
                    m_Actors[i] = new MenuCharacterView(characterIndex: appearance.CharacterIndex, sharedStage: m_Root.transform);
                    m_Ids[i] = member.PlayerId;
                }
                var actor = m_Actors[i];
                if (!appearance.SameAs(m_Appearances[i]))
                {
                    actor.SetEquipmentInventory(LobbyAppearanceInventory.PreviewInventory(appearance));
                    m_Appearances[i] = appearance.Copy();
                }
                actor.PresentSquad(appearance, i, member.Ready && !ReducedMotion);
                actor.SquadRoot.localPosition = new Vector3(SlotX[i], 0, SlotZ[i]);
                if (ReducedMotion) actor.SettleSquadPose();
            }
        }

        private void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = Mathf.Clamp((float)(now - m_LastTime), 0, .1f); m_LastTime = now;
            bool visible = m_Image.panel != null;
            for (VisualElement e = m_Image; visible && e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) visible = false;
            if (m_Disposed || !visible) { Suspend(); return; }
            bool animate = !ReducedMotion && TerminalMotion.Animate(m_Image);
            if (animate || m_Dirty || m_Target == null || (int)m_Image.contentRect.width != m_Viewport.x || (int)m_Image.contentRect.height != m_Viewport.y)
                RenderForEditor(m_Image.contentRect.size, animate ? dt : 0);
        }

        private Vector2Int m_Viewport;
        public void RenderForEditor(Vector2 viewport, float dt = 0)
        {
            if (m_Disposed || !float.IsFinite(viewport.x) || !float.IsFinite(viewport.y) || viewport.x < 2 || viewport.y < 2) return;
            if (m_Root == null) CreateStage();
            if (dt == 0 && !TerminalMotion.Animate(m_Image))
                foreach (var actor in m_Actors) actor?.SettleSquadPose();
            foreach (var actor in m_Actors) actor?.Advance(Mathf.Clamp(dt, 0, .1f));
            float scale = Mathf.Min(1f, 1600 / Mathf.Max(viewport.x, viewport.y));
            int width = Mathf.Max(16, Mathf.CeilToInt(viewport.x * scale / 16f) * 16);
            int height = Mathf.Max(16, Mathf.CeilToInt(viewport.y * scale / 16f) * 16);
            bool framing = m_Viewport.x != (int)viewport.x || m_Viewport.y != (int)viewport.y || m_Target == null;
            m_Viewport = new Vector2Int((int)viewport.x, (int)viewport.y);
            if (m_Target == null || m_Target.width != width || m_Target.height != height)
            {
                ReleaseTarget();
                var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 2, useMipMap = false };
                descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
                m_Target = new RenderTexture(descriptor) { name = "Moonkov shared squad stage", hideFlags = HideFlags.HideAndDontSave };
                m_Target.Create(); m_Image.image = m_Target;
            }
            m_Camera.aspect = (float)width / height;
            float tangent = Mathf.Tan(m_Camera.fieldOfView * .5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(1.35f / tangent, 2.8f / (tangent * m_Camera.aspect)) + .4f;
            Vector3 focus = m_Root.transform.position + new Vector3(0, 1.1f, 0);
            m_Camera.transform.position = focus + new Vector3(0, .12f, distance);
            m_Camera.transform.LookAt(focus);
            RenderPipeline.SubmitRenderRequest(m_Camera, new UniversalRenderPipeline.SingleCameraRequest { destination = m_Target });
            m_Image.MarkDirtyRepaint(); m_Dirty = false;
            if (framing) FramingChanged?.Invoke();
        }

        public float SlotViewportX(int slot)
        {
            if (m_Camera == null || slot < 0 || slot >= 4) return (slot + .5f) / 4;
            return m_Camera.WorldToViewportPoint(m_Root.transform.position + new Vector3(SlotX[slot], 0, SlotZ[slot])).x;
        }

        private void CreateStage()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { m_Scene = EditorSceneManager.NewPreviewScene(); m_PreviewScene = true; }
            else
#endif
                m_Scene = SceneManager.CreateScene("Moonkov squad presentation");
            m_Root = new GameObject("Shared squad stage");
            SceneManager.MoveGameObjectToScene(m_Root, m_Scene);
            if (!m_PreviewScene) m_Root.transform.position = new Vector3(18000, 1200, 1200);
            var deck = Material("Deck", new Color(.12f, .135f, .15f), .45f, .38f);
            var wall = Material("Bulkhead", new Color(.06f, .07f, .085f), .3f, .3f);
            var steel = Material("Brushed steel", new Color(.22f, .25f, .29f), .65f, .48f);
            var light = Material("Airlock light", new Color(.48f, .69f, .79f), 0, .2f, true);
            Block("Deck", new Vector3(0, -.09f, 0), new Vector3(12, .16f, 7), deck);
            Block("Rear bulkhead", new Vector3(0, 1.8f, -2.25f), new Vector3(12, 4, .18f), wall);
            for (int i = -3; i <= 3; i++)
            {
                Block("Deck seam", new Vector3(i * 1.25f, .001f, 0), new Vector3(.012f, .004f, 6), steel);
                Block("Bulkhead brace", new Vector3(i * 1.6f, 1.8f, -2.07f), new Vector3(.07f, 3.6f, .14f), steel);
            }
            Block("Airlock recess", new Vector3(0, 1.65f, -2.05f), new Vector3(3.9f, 3.3f, .08f), deck);
            foreach (float x in new[] { -2.02f, 2.02f })
                Block("Airlock jamb", new Vector3(x, 1.62f, -1.91f), new Vector3(.14f, 3.24f, .18f), steel);
            Block("Airlock lintel", new Vector3(0, 3.2f, -1.91f), new Vector3(4.18f, .14f, .18f), steel);
            foreach (float x in new[] { -1.92f, 1.92f })
                Block("Airlock strip", new Vector3(x, 1.6f, -1.8f), new Vector3(.028f, 2.9f, .025f), light);
            Block("Airlock seam", new Vector3(0, 1.6f, -1.98f), new Vector3(.016f, 3.2f, .025f), steel);
            for (int i = 0; i < 4; i++)
                Block("Stand marker", new Vector3(SlotX[i], .005f, SlotZ[i] + .22f), new Vector3(.72f, .008f, .018f), steel);
            Block("Cargo left", new Vector3(-4, .32f, -.8f), new Vector3(.95f, .64f, .8f), deck);
            Block("Cargo right", new Vector3(3.7f, .4f, -1.25f), new Vector3(.85f, .8f, .75f), steel);
            Directional("Soft key", new Vector3(27, 150, 0), new Color(1, .95f, .87f), 1.4f, true);
            Directional("Cool fill", new Vector3(12, -125, 0), new Color(.65f, .8f, 1), .65f, false);
            Directional("Rim", new Vector3(20, 15, 0), new Color(.65f, .8f, 1), .9f, false);
            m_Camera = Child("Squad camera").AddComponent<Camera>();
            m_Camera.enabled = false; m_Camera.scene = m_Scene;
            m_Camera.cullingMask = 1 << 5; m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(17 / 255f, 17 / 255f, 19 / 255f);
            m_Camera.fieldOfView = 29; m_Camera.nearClipPlane = .05f; m_Camera.farClipPlane = 40;
            m_Camera.allowHDR = true; m_Camera.allowMSAA = true;
            var data = m_Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.volumeLayerMask = 0; data.renderShadows = true;
            Reconcile();
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name) { layer = 5 };
            child.transform.SetParent(m_Root.transform, false); return child;
        }
        private Material Material(string name, Color color, float metal, float smoothness, bool emission = false)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Squad " + name, hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", metal); material.SetFloat("_Smoothness", smoothness);
            if (emission) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.7f); }
            m_Materials.Add(material); return material;
        }
        private void Block(string name, Vector3 position, Vector3 scale, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = name; block.layer = 5;
            block.transform.SetParent(m_Root.transform, false); block.transform.localPosition = position; block.transform.localScale = scale;
            block.GetComponent<MeshRenderer>().sharedMaterial = material;
            var collider = block.GetComponent<Collider>(); if (collider) Destroy(collider);
        }
        private void Directional(string name, Vector3 angles, Color color, float intensity, bool shadows)
        {
            var light = Child(name).AddComponent<Light>(); light.type = LightType.Directional;
            light.transform.localRotation = Quaternion.Euler(angles); light.color = color; light.intensity = intensity;
            light.cullingMask = 1 << 5; light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }
        private void ReleaseActor(int index)
        { m_Actors[index]?.Dispose(); m_Actors[index] = null; m_Appearances[index] = null; m_Ids[index] = null; }
        private void Detached(DetachFromPanelEvent evt) => Suspend();
        private void ReleaseTarget()
        { m_Image.image = null; if (m_Target == null) return; m_Target.Release(); Destroy(m_Target); m_Target = null; }
        public void Suspend()
        {
            for (int i = 0; i < 4; i++) ReleaseActor(i);
            ReleaseTarget();
            if (m_Root != null) { m_Root.SetActive(false); Destroy(m_Root); m_Root = null; }
            foreach (var material in m_Materials) Destroy(material); m_Materials.Clear();
            m_Camera = null; m_Dirty = true;
            if (!m_Scene.IsValid()) return;
#if UNITY_EDITOR
            if (m_PreviewScene) { EditorSceneManager.ClosePreviewScene(m_Scene); m_Scene = default; return; }
#endif
            SceneManager.UnloadSceneAsync(m_Scene); m_Scene = default;
        }
        private static void Destroy(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Timer.Pause(); m_Image.UnregisterCallback<DetachFromPanelEvent>(Detached);
            Suspend(); FramingChanged = null;
        }
    }
}
