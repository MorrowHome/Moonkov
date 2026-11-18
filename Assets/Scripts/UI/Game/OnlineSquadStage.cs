// 小队页共享的 3D 展示舞台。把多个 MenuCharacterView 摆进一间观察舱，用一部相机渲染席位和角色。
using System;
using Unity.MP_FPS.Online;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Unity.MP_FPS.Client {
    // 整个小队共用一套小场景、相机和目标纹理。
    // 角色沿用菜单的装备与动画代码，在独立预览场景里展示。
    public sealed partial class OnlineSquadStage : IDisposable {
        private const int StageLayer = 5;
        private const int MaxTargetEdge = 3072;
        private const int MaxTargetPixels = 3_000_000;
        private const float FullscreenBottomClearance = 138f;

        private static readonly Vector3[] SlotPositions = {
            new(-1.59f, 0, -.24f), new(-.53f, 0, .13f),
            new(.53f, 0, -.03f), new(1.59f, 0, -.32f)
        };

        private readonly Image m_Image;
        private readonly MenuCharacterView[] m_Actors = new MenuCharacterView[4];
        private readonly LobbyAppearance[] m_Appearances = new LobbyAppearance[4];
        private readonly string[] m_Ids = new string[4];
        private readonly IVisualElementScheduledItem m_Timer;
        private LobbyMember[] m_Members = new LobbyMember[4];
        private Scene m_Scene;
        private GameObject m_Root;
        private Camera m_Camera;
        private RenderTexture m_Target;
        private Vector2 m_Viewport;
        private float m_PixelsPerPoint;
        private int m_DragPointer = -1;
        private float m_LastDragX;
        private MenuCharacterView m_DragActor;
        private bool m_Disposed, m_Dirty = true, m_PreviewScene;
        private double m_LastTime;
        public event Action FramingChanged;
        internal event Action<RenderTexture> FrameChanged;
        private bool m_ReducedMotion;
        public bool ReducedMotion {
            get => m_ReducedMotion;
            set {
                if (m_ReducedMotion == value) return;
                m_ReducedMotion = value;
                m_Dirty = true;
                if (m_Root != null) Reconcile();
            }
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
#endif
        public static bool SystemReducedMotion {
            get {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN && !UNITY_EDITOR
                return SystemParametersInfo(0x1042, 0, out int enabled, 0) && enabled == 0;
#else
                return false;
#endif
            }
        }

        public RenderTexture Target => m_Target;
        public Scene DisplayScene => m_Scene;

        public int ActorCount {
            get {
                int count = 0;
                foreach (var actor in m_Actors)
                    if (actor != null)
                        count++;
                return count;
            }
        }

        public OnlineSquadStage(Image image) {
            m_Image = image ?? throw new ArgumentNullException(nameof(image));
            m_Image.pickingMode = PickingMode.Position;
            m_Image.scaleMode = ScaleMode.StretchToFill;
            m_Image.tooltip = MoonkovLocalization.Text("拖动人物旋转 / 双击人物回正");
            m_Image.RegisterCallback<PointerDownEvent>(BeginTurn);
            m_Image.RegisterCallback<PointerMoveEvent>(Turn);
            m_Image.RegisterCallback<PointerUpEvent>(EndTurn);
            m_Image.RegisterCallback<PointerCancelEvent>(CancelTurn);
            m_Image.RegisterCallback<PointerCaptureOutEvent>(TurnCaptureLost);
            m_Image.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_LastTime = Time.realtimeSinceStartupAsDouble;
            m_Timer = image.schedule.Execute(Tick).Every(33);
        }

        private void BeginTurn(PointerDownEvent evt) {
            if (m_DragPointer >= 0 || m_Target == null || evt.target != m_Image || evt.button != 0 ||
                evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
                return;
            var local = m_Image.WorldToLocal(evt.position);
            var actor = PickActor(local);
            if (actor == null) return;
            if (evt.clickCount >= 2) {
                actor.SquadInspectionYaw = 0;
                m_Dirty = true;
            } else {
                m_DragPointer = evt.pointerId;
                m_DragActor = actor;
                m_LastDragX = local.x;
                m_Image.CapturePointer(evt.pointerId);
            }
            evt.StopPropagation();
        }

        // 点击从预览相机映射到舞台中的射线，再跟角色边界比较。最终拖动的是所选人物而不是整张背景。
        private MenuCharacterView PickActor(Vector2 local) {
            var rect = m_Image.contentRect;
            if (m_Camera == null || rect.width <= 0 || rect.height <= 0 || !rect.Contains(local)) return null;
            var point = new Vector2((local.x - rect.xMin) / rect.width, (rect.yMax - local.y) / rect.height);
            var ray = m_Camera.ViewportPointToRay(point);
            MenuCharacterView selected = null;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < m_Actors.Length; i++) {
                var actor = m_Actors[i];
                if (actor == null || !actor.HitSquadModel(ray)) continue;
                // 头发和服装的边界有时会伸进相邻席位。
                float offset = Mathf.Abs(ProjectSlot(i).x - point.x);
                if (offset >= closest) continue;
                selected = actor;
                closest = offset;
            }
            return selected;
        }

        private void Turn(PointerMoveEvent evt) {
            if (m_DragActor == null || evt.pointerId != m_DragPointer || !m_Image.HasPointerCapture(evt.pointerId))
                return;
            if ((evt.pressedButtons & 1) == 0) {
                StopTurn();
                return;
            }
            float x = m_Image.WorldToLocal(evt.position).x;
            // 两个预览相机都朝 -Z 看。鼠标向右拖时，绕竖直轴的转角应减小。
            m_DragActor.SquadInspectionYaw =
                Mathf.Repeat(
                    m_DragActor.SquadInspectionYaw -
                    (x - m_LastDragX) * 360f / Mathf.Max(1, m_Image.contentRect.width) + 180f, 360f) - 180f;
            m_LastDragX = x;
            m_Dirty = true;
            evt.StopPropagation();
        }

        private void EndTurn(PointerUpEvent evt) {
            if (evt.pointerId != m_DragPointer || evt.button != 0) return;
            StopTurn();
            evt.StopPropagation();
        }

        private void CancelTurn(PointerCancelEvent evt) {
            if (evt.pointerId == m_DragPointer) StopTurn();
        }

        private void TurnCaptureLost(PointerCaptureOutEvent evt) {
            if (evt.pointerId == m_DragPointer) StopTurn();
        }

        private void StopTurn() {
            int pointer = m_DragPointer;
            m_DragPointer = -1;
            m_DragActor = null;
            if (pointer >= 0 && m_Image.HasPointerCapture(pointer)) m_Image.ReleasePointer(pointer);
        }

        public void Present(LobbyMember[] members) {
            if (m_Disposed) return;
            for (int i = 0; i < 4; i++) m_Members[i] = members != null && i < members.Length ? members[i] : null;
            m_Dirty = true;
            if (m_Root != null) Reconcile();
        }

        // 席位换人或换角色时才重建模型。成员只换装备或姿势时，继续沿用同一个模型并更新外观。
        private void Reconcile() {
            for (int i = 0; i < 4; i++) {
                var member = m_Members[i];
                var appearance = member?.Appearance;
                if (member == null || appearance == null) {
                    ReleaseActor(i);
                    continue;
                }
                if (m_Ids[i] != member.PlayerId || m_Actors[i]?.CharacterIndex != appearance.CharacterIndex) {
                    ReleaseActor(i);
                    m_Actors[i] = new MenuCharacterView(characterIndex: appearance.CharacterIndex,
                        sharedStage: m_Root.transform);
                    m_Ids[i] = member.PlayerId;
                }
                var actor = m_Actors[i];
                if (!appearance.SameAs(m_Appearances[i])) {
                    actor.SetEquipmentInventory(LobbyAppearanceInventory.PreviewInventory(appearance));
                    m_Appearances[i] = appearance.Copy();
                }
                actor.PresentSquad(appearance, i, member.Ready && !ReducedMotion);
                actor.SquadRoot.localPosition = SlotPositions[i];
                if (ReducedMotion) actor.SettleSquadPose();
            }
        }

        // 定时推进角色动画和画面。面板不可见或未完成布局时没有需要绘制的纹理。
        private void Tick() {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = Mathf.Clamp((float)(now - m_LastTime), 0, .1f);
            m_LastTime = now;
            bool visible = m_Image.panel != null;
            for (VisualElement e = m_Image; visible && e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None)
                    visible = false;
            if (m_Disposed || !visible) {
                Suspend();
                return;
            }
            bool animate = !ReducedMotion && TerminalMotion.Animate(m_Image);
            var viewport = m_Image.contentRect.size;
            if (animate || m_Dirty || m_Target == null || viewport != m_Viewport ||
                !Mathf.Approximately(m_Image.scaledPixelsPerPoint, m_PixelsPerPoint))
                RenderForEditor(viewport, animate ? dt : 0);
        }

        public void RenderForEditor(Vector2 viewport, float dt = 0) {
            if (m_Disposed || !float.IsFinite(viewport.x) || !float.IsFinite(viewport.y) || viewport.x < 2 ||
                viewport.y < 2)
                return;
            if (m_Root == null) CreateStage();
            bool settle = dt == 0 && !TerminalMotion.Animate(m_Image);
            foreach (var actor in m_Actors) {
                if (actor == null) continue;
                if (settle)
                    actor.SettleSquadPose();
                else
                    actor.Advance(Mathf.Clamp(dt, 0, .1f));
            }
            m_PixelsPerPoint = m_Image.panel != null ? m_Image.scaledPixelsPerPoint : 1;
            float edgeScale = Mathf.Min(MaxTargetEdge, SystemInfo.maxTextureSize) / Mathf.Max(viewport.x, viewport.y);
            float areaScale = Mathf.Sqrt(MaxTargetPixels / (viewport.x * viewport.y));
            float scale = Mathf.Min(Mathf.Max(1.5f, m_PixelsPerPoint), Mathf.Min(edgeScale, areaScale));
            int width = Mathf.Max(2, Mathf.FloorToInt(viewport.x * scale));
            int height = Mathf.Max(2, Mathf.FloorToInt(viewport.y * scale));
            bool framing = viewport != m_Viewport || m_Target == null;
            m_Viewport = viewport;
            if (m_Target == null || m_Target.width != width || m_Target.height != height) {
                ReleaseTarget();
                // 同一相机的输出跟随 UI 尺寸分配。用系统支持的采样数处理边缘，再把完成的画面通知磨砂层。
                var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) {
                    msaaSamples = width * height <= 1_600_000 ? 4 : 2,
                    useMipMap = false
                };
                descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
                m_Target = new RenderTexture(descriptor) {
                    name = "Moonkov shared squad stage",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                m_Target.Create();
                m_Image.image = m_Target;
                framing = true;
            }
            if (framing) {
                m_Camera.aspect = (float)width / height;
                float tangent = Mathf.Tan(m_Camera.fieldOfView * .5f * Mathf.Deg2Rad);
                float halfHeight = Mathf.Max(1.39f, 590f / viewport.y);
                float distance = Mathf.Max(halfHeight, 2.45f / m_Camera.aspect) / tangent + .23f;
                // 全屏舞台在脚下留出一些空间，供席位标签和操作栏使用。
                float floorSpace = (distance - SlotPositions[1].z) * tangent;
                float focusY = Mathf.Clamp((1f - 2f * FullscreenBottomClearance / viewport.y) * floorSpace, .3f, .87f);
                var focus = m_Root.transform.position + new Vector3(0, focusY, 0);
                m_Camera.transform.position = focus + new Vector3(0, .12f, distance);
                m_Camera.transform.LookAt(focus);
            }
            RenderPipeline.SubmitRenderRequest(m_Camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = m_Target });
            m_Image.MarkDirtyRepaint();
            m_Dirty = false;
            if (framing) FramingChanged?.Invoke();
            FrameChanged?.Invoke(m_Target);
        }

        private float SlotViewportX(int slot) { return m_Camera != null ? ProjectSlot(slot).x : 1 - (slot + .5f) / 4; }

        // 座位在 3D 场景里有固定位置。用相机投影把它换成页面坐标，姓名卡才会跟人对齐。
        private Vector3 ProjectSlot(int slot) {
            return m_Camera.WorldToViewportPoint(m_Root.transform.position + SlotPositions[slot]);
        }

        internal void AlignMemberCards(VisualElement[] cards) {
            for (int i = 0; i < cards.Length; i++) {
                float center = SlotViewportX(i);
                float before = i > 0 ? Mathf.Abs(center - SlotViewportX(i - 1)) : 1f;
                float after = i < cards.Length - 1 ? Mathf.Abs(center - SlotViewportX(i + 1)) : 1f;
                float width = Mathf.Clamp(Mathf.Min(before, after) - .012f, 0, .23f);
                if (m_Camera != null) {
                    width = Mathf.Min(width, 120f / m_Viewport.x);
                    cards[i].style.top = Length.Percent((1f - ProjectSlot(i).y + 10f / m_Viewport.y) * 100f);
                }
                cards[i].style.width = Length.Percent(width * 100f);
                cards[i].style.left = Length.Percent(Mathf.Clamp(center - width * .5f, 0, 1 - width) * 100f);
            }
        }

        private void CreateStage() {
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                m_Scene = EditorSceneManager.NewPreviewScene();
                m_PreviewScene = true;
            } else
#endif
            {
                m_Scene = SceneManager.CreateScene("Moonkov squad presentation");
            }
            m_Root = new GameObject("Shared squad stage");
            SceneManager.MoveGameObjectToScene(m_Root, m_Scene);
            if (!m_PreviewScene) m_Root.transform.position = new Vector3(18000, 1200, 1200);
            BuildObservationCabin();
            m_Camera = Child("Squad camera").AddComponent<Camera>();
            m_Camera.enabled = false;
            m_Camera.scene = m_Scene;
            m_Camera.cullingMask = 1 << StageLayer;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color32(4, 6, 10, 255);
            m_Camera.fieldOfView = 29;
            m_Camera.nearClipPlane = .05f;
            m_Camera.farClipPlane = 40;
            m_Camera.allowHDR = true;
            m_Camera.allowMSAA = true;
            var data = m_Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            data.renderShadows = true;
            Reconcile();
        }

        private GameObject Child(string name) {
            var child = new GameObject(name) { layer = StageLayer };
            child.transform.SetParent(m_Root.transform, false);
            return child;
        }

        // 离队或者角色资源变化时只释放那个座位上的预览。其他成员的动画和纹理还能继续使用。
        private void ReleaseActor(int index) {
            if (m_DragActor != null && m_DragActor == m_Actors[index]) StopTurn();
            m_Actors[index]?.Dispose();
            m_Actors[index] = null;
            m_Appearances[index] = null;
            m_Ids[index] = null;
        }

        private void Detached(DetachFromPanelEvent evt) { Suspend(); }

        private void ReleaseTarget() {
            m_Image.image = null;
            if (m_Target == null) return;
            m_Target.Release();
            Destroy(m_Target);
            m_Target = null;
            FrameChanged?.Invoke(null);
        }

        // 不可见时释放临时舞台和渲染纹理。再打开时仍可从上一次收到的成员数据还原。
        public void Suspend() {
            StopTurn();
            for (int i = 0; i < 4; i++) ReleaseActor(i);
            ReleaseTarget();
            if (m_Root != null) {
                m_Root.SetActive(false);
                Destroy(m_Root);
                m_Root = null;
            }
            ReleaseCabinAssets();
            m_Camera = null;
            m_Dirty = true;
            if (!m_Scene.IsValid()) return;
#if UNITY_EDITOR
            if (m_PreviewScene) {
                EditorSceneManager.ClosePreviewScene(m_Scene);
                m_Scene = default;
                return;
            }
#endif
            SceneManager.UnloadSceneAsync(m_Scene);
            m_Scene = default;
        }

        private static void Destroy(UnityEngine.Object value) {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }

        public void Dispose() {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Timer.Pause();
            m_Image.UnregisterCallback<PointerDownEvent>(BeginTurn);
            m_Image.UnregisterCallback<PointerMoveEvent>(Turn);
            m_Image.UnregisterCallback<PointerUpEvent>(EndTurn);
            m_Image.UnregisterCallback<PointerCancelEvent>(CancelTurn);
            m_Image.UnregisterCallback<PointerCaptureOutEvent>(TurnCaptureLost);
            m_Image.UnregisterCallback<DetachFromPanelEvent>(Detached);
            Suspend();
            FramingChanged = null;
            FrameChanged = null;
        }
    }
}