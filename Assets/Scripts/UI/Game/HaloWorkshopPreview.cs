using System;
using Unity.MP_FPS.DollSinger;
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
    // A private stage containing only the actual halo prefab and its module geometry.
    // Static views render on changes. Animation is an explicit, temporary visual example.
    public sealed class HaloWorkshopPreview : IDisposable
    {
        private const int ViewLayer = 5, MaximumResolution = 512;
        private const float FrameRadius = .50f;
        private const string ScenePrefix = "Moonkov halo workshop preview ";
        private static int s_StageSequence;
        public Image Element { get; }
        public RenderTexture Target => m_Target;
        public bool Available => !m_Disposed && !m_Failed && m_Source != null;
        public bool CanPlayShot => Available && m_ChargedRounds > 0;
        public string WeaponCode { get; private set; } = "rifle";
        public ushort Modules { get; private set; }
        public float Zoom { get; private set; } = 1;
        public Scene DisplayScene => m_Scene;
        public event Action AvailabilityChanged;

        private MenuCharacterSettings m_Settings;
        private GameObject m_Source, m_Stage, m_Model;
        private RifleHaloVisual m_Rifle;
        private ShotgunHaloVisual m_Shotgun;
        private HaloModuleVisual m_Modules;
        private Camera m_Camera;
        private RenderTexture m_Target;
        private Scene m_Scene;
        private IVisualElementScheduledItem m_Timer;
        private int m_Capacity = 30, m_ChargedRounds = 30, m_DemoRounds, m_DemoShots, m_DemoShotCount;
        private uint m_ShotTick;
        private float m_DemoElapsed, m_ChargeDuration = 2, m_ShotInterval = .1f;
        private double m_LastTime;
        private bool m_Disposed, m_Failed, m_Dirty = true, m_EditorScene, m_Warned;
        private bool? m_ReportedAvailability;
        private Vector2 m_ViewOffset;
        private Demo m_Demo;
        private enum Demo { None, Charge, Shot }

        public HaloWorkshopPreview()
        {
            Element = new Image { pickingMode = PickingMode.Position, scaleMode = ScaleMode.ScaleToFit };
            Element.AddToClassList("workshop-live-halo");
            Element.tooltip = MoonkovLocalization.Text("SCROLL TO ZOOM");
            Element.RegisterCallback<AttachToPanelEvent>(Attached);
            Element.RegisterCallback<DetachFromPanelEvent>(Detached);
            Element.RegisterCallback<GeometryChangedEvent>(GeometryChanged);
            Element.RegisterCallback<WheelEvent>(ZoomInspect);
            Element.RegisterCallback<PointerDownEvent>(ResetInspect);
            ResolveSource();
            m_LastTime = Time.realtimeSinceStartupAsDouble;
            m_Timer = Element.schedule.Execute(Tick).Every(100);
        }

        public void Present(string weaponCode, ushort modules, int capacity, int chargedRounds)
        {
            if (m_Disposed) return;
            Element.tooltip = MoonkovLocalization.Text("SCROLL TO ZOOM");
            capacity = Mathf.Max(1, capacity); chargedRounds = Mathf.Max(0, chargedRounds);
            bool changed = WeaponCode != weaponCode || Modules != modules || m_Capacity != capacity || m_ChargedRounds != chargedRounds;
            WeaponCode = weaponCode; Modules = modules; m_Capacity = capacity; m_ChargedRounds = chargedRounds;
            var previousSource = m_Source;
            ResolveSource();
            if (!Available) { ReleaseStage(); return; }
            if (changed || previousSource != m_Source)
            {
                m_Demo = Demo.None; m_Dirty = true;
                if (m_Stage != null) BuildModel();
            }
            if (Visible()) Step(0);
        }

        private void ResolveSource()
        {
            var previousSource = m_Source;
            if (m_Settings == null)
                m_Settings = Resources.Load<MenuCharacterSettings>(GameSettings.MenuCharacterSettingsPath(GameSettings.DollSingerCharacterIndex));
            m_Source = m_Settings == null ? null : WeaponCode == "shotgun" ? m_Settings.ShotgunHaloPreviewPrefab :
                WeaponCode == "rifle" || WeaponCode == "compact" ? m_Settings.RifleHaloPreviewPrefab : null;
            // A failed renderer must not retry on every inventory refresh. A new
            // source or a newly constructed preview can start a fresh attempt.
            if (m_Source != previousSource) m_Failed = false;
            if (m_Source == null) return;
            // Never instantiate a character prefab in place of a missing halo reference.
            if (WeaponCode == "shotgun" ? m_Source.GetComponent<ShotgunHaloVisual>() == null : m_Source.GetComponent<RifleHaloVisual>() == null)
            { m_Source = null; Warn("The configured source must be the original halo prefab root."); }
        }

        // Explicit calls also support an offscreen review without a UI panel.
        // Normal UI use is scheduled only while the Image is visible and laid out.
        public void Step(float dt)
        {
            if (!Available) return;
            dt = float.IsFinite(dt) ? Mathf.Max(0, dt) : 0;
            try
            {
                if (m_Stage == null) CreateStage();
                if (m_Demo != Demo.None)
                {
                    m_DemoElapsed += dt;
                    float duration = m_Demo == Demo.Charge ? m_ChargeDuration : .5f + (m_DemoShotCount - 1) * m_ShotInterval;
                    if (m_DemoElapsed >= duration)
                    {
                        m_Demo = Demo.None;
                        BuildModel();
                        m_Timer?.Every(100);
                    }
                    else
                    {
                        if (m_Demo == Demo.Shot)
                        {
                            int due = Mathf.Min(m_DemoShotCount, 1 + Mathf.FloorToInt(m_DemoElapsed / m_ShotInterval));
                            while (m_DemoShots < due) FireDemoShot();
                        }
                        ApplyFrame(dt);
                    }
                    m_Dirty = true;
                }
                Vector2 viewport = Element.contentRect.size;
                if (!float.IsFinite(viewport.x) || !float.IsFinite(viewport.y) || viewport.x < 2 || viewport.y < 2)
                    viewport = new Vector2(384, 384);
                if (EnsureTarget(viewport)) m_Dirty = true;
                if (!m_Dirty) return;
                // Same fixed front view for every mask: a shape change cannot alter framing.
                m_Camera.aspect = (float)m_Target.width / m_Target.height;
                m_Camera.orthographicSize = FrameRadius / Mathf.Min(1f, m_Camera.aspect) / Zoom;
                m_Camera.transform.localPosition = new Vector3(m_ViewOffset.x, m_ViewOffset.y, -3);
                RenderPipeline.SubmitRenderRequest(m_Camera, new UniversalRenderPipeline.SingleCameraRequest { destination = m_Target });
                m_Dirty = false;
                Element.MarkDirtyRepaint();
            }
            catch (Exception error)
            {
                m_Failed = true;
                Warn(error.Message);
                ReleaseStage();
                ReportAvailability(false);
                return;
            }
            ReportAvailability(true);
        }

        private void ReportAvailability(bool ready)
        {
            if (m_ReportedAvailability == ready) return;
            m_ReportedAvailability = ready;
            AvailabilityChanged?.Invoke();
        }

        public void PlayCharge() => PlayCharge(2f);
        public void PlayCharge(float durationSeconds)
        {
            if (!Available) return;
            m_ChargeDuration = float.IsFinite(durationSeconds) ? Mathf.Max(.1f, durationSeconds) : 2f;
            StartDemo(Demo.Charge);
        }

        public void PlayShot(float intervalSeconds = .1f)
        {
            if (!CanPlayShot) return;
            m_ShotInterval = float.IsFinite(intervalSeconds) ? Mathf.Max(.001f, intervalSeconds) : .1f;
            StartDemo(Demo.Shot);
        }

        private void StartDemo(Demo demo)
        {
            // Rebuild once to start every example from the same static pose.
            m_Demo = Demo.None; Step(0);
            if (!Available || m_Stage == null) return;
            BuildModel();
            m_Demo = demo; m_DemoElapsed = 0;
            m_DemoRounds = demo == Demo.Shot ? m_ChargedRounds : Mathf.Min(m_ChargedRounds, m_Capacity);
            m_DemoShots = 0;
            m_DemoShotCount = Mathf.Min(m_ChargedRounds, WeaponCode != "shotgun" && ((HaloModule)Modules & HaloModule.PulseController) != 0 ? 3 : 1);
            if (demo == Demo.Shot)
                FireDemoShot();
            ApplyFrame(0); m_Dirty = true;
            m_LastTime = Time.realtimeSinceStartupAsDouble;
            m_Timer?.Every(33);
            Step(0);
        }

        private void FireDemoShot()
        {
            m_DemoRounds = Mathf.Max(0, m_DemoRounds - 1); m_DemoShots++; m_ShotTick++;
            m_Rifle?.PlayShot(m_ShotTick); m_Shotgun?.PlayShot(m_ShotTick);
        }

        private void CreateStage()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("A graphics device is required for the real halo preview.");
            string name = ScenePrefix + (++s_StageSequence);
            m_EditorScene = false;
#if UNITY_EDITOR
            if (!Application.isPlaying) { m_Scene = EditorSceneManager.NewPreviewScene(); m_EditorScene = true; m_Scene.name = name; }
            else
#endif
                m_Scene = SceneManager.CreateScene(name);
            m_Stage = new GameObject(name);
            SceneManager.MoveGameObjectToScene(m_Stage, m_Scene);
            m_Stage.hideFlags = HideFlags.HideAndDontSave;
            if (!m_EditorScene) m_Stage.transform.position = new Vector3(4096 + 64 * (s_StageSequence % 100), 4096, 4096);
            m_Stage.SetActive(false);
            var cameraObject = new GameObject("Halo preview camera") { layer = ViewLayer };
            cameraObject.transform.SetParent(m_Stage.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 0, -3);
            m_Camera = cameraObject.AddComponent<Camera>();
            m_Camera.enabled = false; m_Camera.orthographic = true;
            m_Camera.clearFlags = CameraClearFlags.SolidColor; m_Camera.backgroundColor = Color.clear;
            m_Camera.cullingMask = 1 << ViewLayer; m_Camera.nearClipPlane = .1f; m_Camera.farClipPlane = 8;
            m_Camera.allowHDR = false; m_Camera.allowMSAA = true; m_Camera.useOcclusionCulling = false;
            m_Camera.scene = m_Scene;
#if UNITY_EDITOR
            if (m_EditorScene) m_Camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(m_Scene);
#endif
            var cameraData = m_Camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false; cameraData.renderShadows = false; cameraData.volumeLayerMask = 0;
            cameraData.antialiasing = AntialiasingMode.None;
            BuildModel();
            m_Stage.SetActive(true);
        }

        private void BuildModel()
        {
            if (m_Model != null) { m_Model.SetActive(false); Destroy(m_Model); }
            m_Rifle = null; m_Shotgun = null; m_Modules = null;
            bool stageActive = m_Stage.activeSelf;
            m_Stage.SetActive(false);
            m_Model = UnityEngine.Object.Instantiate(m_Source, m_Stage.transform, false);
            m_Model.name = "Actual " + WeaponCode + " halo";
            m_Model.transform.localPosition = Vector3.zero;
            m_Model.transform.localRotation = Quaternion.identity;
            m_Model.transform.localScale = Vector3.one;
            // The original artwork/materials are shared; only the private clone is posed.
            foreach (var behaviour in m_Model.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            foreach (var collider in m_Model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var renderer in m_Model.GetComponentsInChildren<Renderer>(true))
            { renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; }
            m_Rifle = m_Model.GetComponent<RifleHaloVisual>();
            m_Shotgun = m_Model.GetComponent<ShotgunHaloVisual>();
            m_Model.SetActive(false);
            m_Rifle?.SetEquipped(true, m_ChargedRounds); m_Shotgun?.SetEquipped(true, m_ChargedRounds);
            m_Rifle?.SetMagazineCapacity(m_Capacity); m_Rifle?.SetAimBlend(1);
            m_Shotgun?.SetModuleProfile(Modules, m_Capacity); m_Shotgun?.SetAimBlend(1);
            var moduleObject = new GameObject("Actual halo module geometry");
            moduleObject.transform.SetParent(m_Model.transform, false);
            m_Modules = moduleObject.AddComponent<HaloModuleVisual>();
            m_Modules.enabled = false; m_Modules.Configure(m_Rifle != null); m_Modules.SetAimBlend(1);
            foreach (var transform in m_Model.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = ViewLayer;
            m_DemoRounds = m_ChargedRounds; m_ShotTick = 0;
            // Settle the existing shotgun interpolation once; static time then stays frozen.
            m_Rifle?.SetLocalState(m_ChargedRounds, false, 0, m_Capacity);
            m_Shotgun?.SetLocalState(m_ChargedRounds, false, 0);
            m_Rifle?.Step(0); m_Shotgun?.Step(1);
            m_Modules.SetState(Modules, m_ChargedRounds, m_Capacity, false, 0, m_Capacity, 0);
            m_Modules.Step(0);
            m_Stage.SetActive(stageActive);
            m_Dirty = true;
        }

        private void ApplyFrame(float dt)
        {
            bool charge = m_Demo == Demo.Charge;
            float progress = charge ? Mathf.Clamp01(m_DemoElapsed / m_ChargeDuration) : 0;
            m_Rifle?.SetLocalState(m_DemoRounds, charge, progress, m_Capacity);
            m_Shotgun?.SetLocalState(m_DemoRounds, charge, progress);
            m_Modules.SetState(Modules, m_DemoRounds, m_Capacity, charge, progress, m_Capacity, m_ShotTick, Mathf.Max(0, m_DemoShots - 1));
            m_Rifle?.Step(dt); m_Shotgun?.Step(dt); m_Modules.Step(dt);
        }

        private bool EnsureTarget(Vector2 viewport)
        {
            float scale = Mathf.Min(2f, MaximumResolution / Mathf.Max(viewport.x, viewport.y));
            int width = Mathf.Clamp(Mathf.CeilToInt(viewport.x * scale / 8) * 8, 16, MaximumResolution);
            int height = Mathf.Clamp(Mathf.CeilToInt(viewport.y * scale / 8) * 8, 16, MaximumResolution);
            if (m_Target != null && m_Target.width == width && m_Target.height == height) return false;
            ReleaseTarget();
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24)
            { msaaSamples = 4, useMipMap = false, autoGenerateMips = false };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            m_Target = new RenderTexture(descriptor)
            { name = "Actual halo workshop preview", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            if (!m_Target.Create()) throw new InvalidOperationException("The private halo render texture could not be created.");
            Element.image = m_Target;
            return true;
        }

        private bool Visible()
        {
            Vector2 size = Element.contentRect.size;
            if (Element.panel == null || !float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x < 2 || size.y < 2) return false;
            for (VisualElement scope = Element; scope != null; scope = scope.parent)
                if (scope.resolvedStyle.display == DisplayStyle.None || scope.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }
        private void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = Mathf.Clamp((float)(now - m_LastTime), 0, .1f); m_LastTime = now;
            if (m_Disposed) return;
            if (!Visible()) { if (m_Stage != null) ReleaseStage(); return; }
            Step(dt);
        }
        private void Attached(AttachToPanelEvent evt) { m_Dirty = true; m_LastTime = Time.realtimeSinceStartupAsDouble; }
        private void Detached(DetachFromPanelEvent evt) => ReleaseStage();
        private void GeometryChanged(GeometryChangedEvent evt) => m_Dirty = true;
        private void ZoomInspect(WheelEvent evt)
        {
            if (m_Disposed || evt.target != Element || !Available || !float.IsFinite(evt.delta.y) || evt.delta.y == 0) return;
            Vector2 size = Element.contentRect.size;
            if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x < 2 || size.y < 2) return;
            float previous = Zoom;
            Zoom = Mathf.Clamp(Zoom * Mathf.Exp(-Mathf.Clamp(evt.delta.y, -40, 40) * .08f), 1f, 2f);
            if (!Mathf.Approximately(previous, Zoom))
            {
                float aspect = m_Target != null ? (float)m_Target.width / m_Target.height : size.x / size.y;
                var frame = new Vector2(aspect, 1) * (FrameRadius / Mathf.Min(1f, aspect));
                var pointer = new Vector2(evt.localMousePosition.x / size.x * 2 - 1, 1 - evt.localMousePosition.y / size.y * 2);
                // Keep the decoration under the cursor in place while zooming.
                // The same offset survives module and current/after changes.
                m_ViewOffset += Vector2.Scale(pointer, frame) * (1f / previous - 1f / Zoom);
                var limit = frame * (1f - 1f / Zoom);
                m_ViewOffset = new Vector2(Mathf.Clamp(m_ViewOffset.x, -limit.x, limit.x), Mathf.Clamp(m_ViewOffset.y, -limit.y, limit.y));
                m_Dirty = true;
            }
            evt.StopPropagation(); evt.PreventDefault();
        }
        private void ResetInspect(PointerDownEvent evt)
        {
            if (m_Disposed || evt.target != Element || evt.button != 0 || evt.clickCount != 2) return;
            Zoom = 1; m_ViewOffset = Vector2.zero; m_Dirty = true;
            evt.StopPropagation(); evt.PreventDefault();
        }
        private void Warn(string message)
        {
            if (m_Warned) return;
            m_Warned = true; Debug.LogWarning("Halo workshop real preview unavailable: " + message);
        }
        private void ReleaseTarget()
        {
            Element.image = null;
            if (m_Target == null) return;
            m_Target.Release(); Destroy(m_Target); m_Target = null;
        }
        private void ReleaseStage()
        {
            ReleaseTarget();
            if (m_Stage != null) { m_Stage.SetActive(false); Destroy(m_Stage); }
            m_Stage = m_Model = null; m_Camera = null; m_Rifle = null; m_Shotgun = null; m_Modules = null;
            m_Demo = Demo.None; m_Dirty = true; m_Timer?.Every(100);
            if (!m_Scene.IsValid()) return;
#if UNITY_EDITOR
            if (m_EditorScene) { EditorSceneManager.ClosePreviewScene(m_Scene); m_Scene = default; return; }
#endif
            SceneManager.UnloadSceneAsync(m_Scene); m_Scene = default;
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Element.UnregisterCallback<AttachToPanelEvent>(Attached);
            Element.UnregisterCallback<DetachFromPanelEvent>(Detached);
            Element.UnregisterCallback<GeometryChangedEvent>(GeometryChanged);
            Element.UnregisterCallback<WheelEvent>(ZoomInspect);
            Element.UnregisterCallback<PointerDownEvent>(ResetInspect);
            ReleaseStage();
            m_Timer?.Pause();
        }
    }
}
