using System;
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
    // One isolated render stage is shared by the background and its observer.
    public sealed class MenuMoonView : IDisposable
    {
        public const double SiderealDays = 27.3217, YearDays = 365.256;
        public Image Element { get; } = new Image { scaleMode = ScaleMode.StretchToFill };
        public bool Observing { get; set; }
        public bool Paused { get; set; }
        public double Days { get; private set; }
        public float Zoom { get; private set; } = 1;
        public Vector3 SunDirection { get; private set; }
        public RenderTexture Target => m_Target;
        public Scene DisplayScene => m_Scene;
        private readonly MoonDisplaySettings m_Settings;
        private readonly IVisualElementScheduledItem m_Timer;
        private Scene m_Scene;
        private GameObject m_Stage, m_Moon;
        private Camera m_Camera;
        private Light m_Sun;
        private Material m_Material;
        private RenderTexture m_Target;
        private double m_LastTime, m_NextFrame;
        private float m_Yaw, m_Pitch;
        private bool m_EditorScene, m_Disposed, m_Dirty = true;
        private static int s_Sequence;

        public MenuMoonView(Action inspect)
        {
            m_Settings = Resources.Load<MoonDisplaySettings>("Moonkov/MoonDisplaySettings");
            Element.AddToClassList("moon-backdrop-view");
            Element.tooltip = "Inspect the Moon";
            Element.AddManipulator(new OrbitDrag(this, inspect));
            m_LastTime = Time.realtimeSinceStartupAsDouble;
            m_Timer = Element.schedule.Execute(Tick).Every(33);
        }
        public void Advance(float seconds)
        {
            if (m_Disposed || m_Settings == null) return;
            if (!Paused) Days += Math.Max(0, seconds) * m_Settings.DaysPerSecond;
            m_Dirty = true;
        }
        public void Rotate(Vector2 delta)
        {
            m_Yaw = Mathf.Repeat(m_Yaw + delta.x * .3f, 360);
            m_Pitch = Mathf.Clamp(m_Pitch + delta.y * .25f, -80, 80);
            m_Dirty = true;
        }
        public void SetZoom(float zoom) { Zoom = Mathf.Clamp(zoom, .65f, 2.4f); m_Dirty = true; }
        public void ResetView() { m_Yaw = m_Pitch = 0; Zoom = 1; m_Dirty = true; }

        private void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = Mathf.Clamp((float)(now - m_LastTime), 0, .2f); m_LastTime = now;
            bool visible = Element.panel != null;
            for (var parent = (VisualElement)Element; parent != null; parent = parent.parent)
                if (parent.resolvedStyle.display == DisplayStyle.None) { visible = false; break; }
            if (!visible) { ReleaseStage(); return; }
            if (m_Disposed || m_Settings == null || m_Settings.Mesh == null || m_Settings.Material == null) return;
            if (!Paused) Advance(dt);
            if (now < m_NextFrame) return;
            m_NextFrame = now + (Observing ? 1.0 / 30 : 1.0 / 15);
            if (m_Camera == null) CreateStage();
            Render();
        }
        private void CreateStage()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) { m_Scene = EditorSceneManager.NewPreviewScene(); m_EditorScene = true; }
            else
#endif
                m_Scene = SceneManager.CreateScene("Moonkov lunar display " + Guid.NewGuid().ToString("N"));
            m_Stage = new GameObject("Lunar display stage");
            SceneManager.MoveGameObjectToScene(m_Stage, m_Scene);
            m_Stage.transform.position = m_EditorScene ? Vector3.zero : new Vector3(20000 + 8 * (s_Sequence++ % 100), 20000, 20000);
            m_Moon = Child("NASA Moon / display LOD");
            m_Moon.AddComponent<MeshFilter>().sharedMesh = m_Settings.Mesh;
            var renderer = m_Moon.AddComponent<MeshRenderer>();
            m_Material = new Material(m_Settings.Material);
            renderer.sharedMaterial = m_Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            m_Sun = Child("Sun / parallel rays").AddComponent<Light>();
            m_Sun.type = LightType.Directional; m_Sun.color = new Color(1, .98f, .95f); m_Sun.intensity = 1;
            m_Sun.shadows = LightShadows.None; m_Sun.cullingMask = 1 << 5;
            m_Camera = Child("Lunar display camera").AddComponent<Camera>();
            m_Camera.enabled = false; m_Camera.scene = m_Scene; m_Camera.cullingMask = 1 << 5;
            m_Camera.clearFlags = CameraClearFlags.SolidColor; m_Camera.backgroundColor = Color.clear;
            m_Camera.orthographic = true; m_Camera.aspect = 1;
            m_Camera.nearClipPlane = .05f; m_Camera.farClipPlane = 10;
            m_Camera.transform.localPosition = new Vector3(0, 0, -4);
            m_Camera.allowHDR = false; m_Camera.allowMSAA = true;
            var data = m_Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false; data.renderShadows = false; data.volumeLayerMask = 0;
            m_Dirty = true;
        }
        private GameObject Child(string name)
        {
            var child = new GameObject(name) { layer = 5 };
            child.transform.SetParent(m_Stage.transform, false); return child;
        }
        public void Render()
        {
            if (m_Disposed || m_Camera == null) return;
            var viewport = Element.contentRect.size;
            if (!float.IsFinite(viewport.x) || !float.IsFinite(viewport.y) || viewport.x < 2 || viewport.y < 2) return;
            int maximum = Observing ? m_Settings.ObserverResolution : m_Settings.BackgroundResolution;
            float longestEdge = Mathf.Max(viewport.x, viewport.y);
            float renderScale = Mathf.Clamp(longestEdge * 1.25f, 256, maximum) / longestEdge;
            int width = Mathf.Max(1, Mathf.RoundToInt(viewport.x * renderScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(viewport.y * renderScale));
            float aspect = viewport.x / viewport.y;
            if (!Mathf.Approximately(m_Camera.aspect, aspect)) { m_Camera.aspect = aspect; m_Dirty = true; }
            if (m_Target == null || m_Target.width != width || m_Target.height != height)
            {
                ReleaseTarget();
                var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 2 };
                descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
                m_Target = new RenderTexture(descriptor) { name = "Moonkov lunar display", filterMode = FilterMode.Bilinear };
                m_Target.Create(); Element.image = m_Target; m_Dirty = true;
            }
            if (!m_Dirty) return;
            float orbit = (float)(Days / SiderealDays % 1) * 360;
            float year = (float)(Days / YearDays % 1) * 360;
            // The spacecraft tracks 35% of the lunar orbit, allowing both faces and phase to change.
            var orbitPlane = Quaternion.Euler(0, 0, 5.145f);
            var observer = orbitPlane * Quaternion.AngleAxis(orbit * .35f, Vector3.up) * Quaternion.Inverse(orbitPlane) * Quaternion.Euler(m_Pitch - 18, m_Yaw, 0);
            var inverse = Quaternion.Inverse(observer);
            var spinAxis = Quaternion.Euler(0, 0, 1.54f);
            m_Moon.transform.localRotation = inverse * spinAxis * Quaternion.AngleAxis(orbit, Vector3.up) * m_Settings.SourceOrientation;
            SunDirection = inverse * (Quaternion.AngleAxis(year, Vector3.up) * new Vector3(.78f, 0, -.625f).normalized);
            m_Sun.transform.localRotation = Quaternion.LookRotation(-SunDirection);
            m_Material.SetVector("_SunDirection", -m_Sun.transform.forward);
            m_Material.SetColor("_SunColor", m_Sun.color * m_Sun.intensity);
            m_Camera.orthographicSize = 1.18f / (Observing ? Zoom : 1) * Mathf.Max(1, 1 / aspect);
            RenderPipeline.SubmitRenderRequest(m_Camera, new UniversalRenderPipeline.SingleCameraRequest { destination = m_Target });
            Element.MarkDirtyRepaint(); m_Dirty = false;
        }
        private void ReleaseTarget()
        {
            Element.image = null;
            if (m_Target != null) { m_Target.Release(); Destroy(m_Target); m_Target = null; }
        }
        private void ReleaseStage()
        {
            ReleaseTarget();
            if (m_Stage != null) { m_Stage.SetActive(false); Destroy(m_Stage); }
            if (m_Material != null) Destroy(m_Material);
            m_Stage = m_Moon = null; m_Camera = null; m_Sun = null; m_Material = null;
            if (!m_Scene.IsValid()) return;
#if UNITY_EDITOR
            if (m_EditorScene) { EditorSceneManager.ClosePreviewScene(m_Scene); m_Scene = default; return; }
#endif
            SceneManager.UnloadSceneAsync(m_Scene); m_Scene = default;
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose() { if (m_Disposed) return; m_Disposed = true; m_Timer.Pause(); ReleaseStage(); }

        private sealed class OrbitDrag : PointerManipulator
        {
            private readonly MenuMoonView m_View;
            private readonly Action m_Inspect;
            private Vector2 m_Last;
            private int m_Pointer = -1;
            public OrbitDrag(MenuMoonView view, Action inspect) { m_View = view; m_Inspect = inspect; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCaptureOutEvent>(Lost);
                target.RegisterCallback<WheelEvent>(Wheel);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
                target.UnregisterCallback<WheelEvent>(Wheel);
            }
            private void Down(PointerDownEvent e)
            {
                if (e.button != 0) return;
                if (!m_View.Observing)
                {
                    var point = target.WorldToLocal(e.position);
                    if (Vector2.Distance(point, target.contentRect.center) > Mathf.Min(target.contentRect.width, target.contentRect.height) * .43f) return;
                    m_Inspect(); e.StopPropagation(); return;
                }
                m_Last = e.position; m_Pointer = e.pointerId; target.CapturePointer(m_Pointer); e.StopPropagation();
            }
            private void Move(PointerMoveEvent e)
            {
                if (m_Pointer != e.pointerId) return;
                Vector2 point = e.position; m_View.Rotate(point - m_Last); m_Last = point; e.StopPropagation();
            }
            private void Up(PointerUpEvent e) { if (e.pointerId != m_Pointer) return; target.ReleasePointer(m_Pointer); m_Pointer = -1; e.StopPropagation(); }
            private void Lost(PointerCaptureOutEvent e) { m_Pointer = -1; }
            private void Wheel(WheelEvent e) { if (!m_View.Observing) return; m_View.SetZoom(m_View.Zoom * Mathf.Exp(-e.delta.y * .08f)); e.StopPropagation(); }
        }
    }
}
