// 可复用的 3D 角色预览。创建模型、光照和动画，由相机渲染成 UI 图片，供船舱、仓库、局内背包和小队页使用。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Unity.MP_FPS.Client {
    // 独立的渲染舞台。预览角色只需要视觉组件。
    public sealed partial class MenuCharacterView : IDisposable {
        // 项目的 PC 渲染器只画第 0 到 7 层。UI 层在这个掩码内。
        private const int ViewLayer = 5;
        private const float PortraitFieldOfView = 27;
        private EnergyShieldPresentation m_Protection;
        private InventoryGraph m_ProtectionInventory;
        public Image Element { get; }
        public RenderTexture Target => m_Target;
        public int MotionChanges { get; private set; }
        public float HaloAimWeight { get; private set; }
        public bool HaloVisible => m_Halo != null && m_Halo.gameObject.activeInHierarchy;
        public Scene DisplayScene => m_Scene;
        public int CurrentMotionIndex => m_Motion;

        public string CurrentMotion =>
            m_Settings == null ? string.Empty : m_Settings.Motion(m_Motion).Name ?? m_Settings.Motions[m_Motion].name;

        public int CharacterIndex { get; private set; } = -1;
        private MenuCharacterSettings m_Settings;
        private readonly GameSettings m_GameSettings;
        private readonly bool m_IdleOnly;
        private readonly bool m_HomePose;
        private readonly Transform m_SharedStage;
        private Animator m_Animator;
        public event Action FrameRendered;
        private readonly System.Random m_Random = new();
        private static int s_StageSequence;
        private readonly AnimationClipPlayable[] m_Clips = new AnimationClipPlayable[2];
        private readonly int[] m_Indices = new int[2];
        private readonly double[] m_Times = new double[2];
        private Scene m_Scene;
        private GameObject m_Stage, m_Model;
        private Camera m_Camera;
        private PlayableGraph m_Graph;
        private AnimationMixerPlayable m_Mixer;
        private AnimationLayerMixerPlayable m_Layers;
        private AnimationClipPlayable m_BaseIdle;
        private SecondaryBoneSpring m_Spring;
        private HumanoidTwistFollowers m_TwistFollowers;
        private SkirtHandAvoidance m_HandAvoidance;
        private DollSingerBakedGestures m_Gestures;
        private AvatarMask m_FullBodyMask;
        private Transform m_Chest;
        private SkinnedMeshRenderer[] m_Renderers;
        private Transform m_Halo, m_Head, m_Finger;
        private double m_BaseTime;
        private RenderTexture m_Target;
        private IVisualElementScheduledItem m_Timer;
        private Bounds m_Bounds;
        private int m_Slot, m_Motion, m_LastAction;
        private int[] m_ActionBag;
        private int m_BagRemaining;
        private float m_Remaining, m_Blend, m_FadeSeconds;
        private double m_LastTime;
        private Vector2 m_EditorViewport;
        private bool m_Transition, m_Disposed, m_EditorScene, m_GestureActive;
        private int m_DragPointer = -1;
        private Vector2 m_DragPosition;
        private float m_InspectionYaw, m_PoseTurn, m_FaceHeightOffset;
        private bool m_Inspected, m_ViewDirty;
        public float InspectionYaw => m_InspectionYaw;
        public float Zoom { get; private set; } = 1;

        public MenuCharacterView(
            bool idleOnly = false,
            Image element = null,
            int? characterIndex = null,
            Transform sharedStage = null,
            bool homePose = false) {
            m_IdleOnly = idleOnly || homePose;
            m_HomePose = homePose;
            m_SharedStage = sharedStage;
            Element = element ?? new Image();
            Element.pickingMode = homePose ? PickingMode.Ignore : PickingMode.Position;
            Element.scaleMode = ScaleMode.ScaleToFit;
            Element.AddToClassList("terminal-character-art");
            Element.tooltip = homePose ? string.Empty : MoonkovLocalization.Text("拖动旋转 / 滚轮缩放");
            Element.RegisterCallback<PointerDownEvent>(BeginInspect);
            Element.RegisterCallback<PointerMoveEvent>(Inspect);
            Element.RegisterCallback<PointerUpEvent>(EndInspect);
            Element.RegisterCallback<PointerCancelEvent>(CancelInspect);
            Element.RegisterCallback<PointerCaptureOutEvent>(CaptureLost);
            Element.RegisterCallback<WheelEvent>(ZoomInspect);
            Element.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_GameSettings = characterIndex.HasValue ? null : GameSettings.Instance;
            SelectCharacter(characterIndex ?? m_GameSettings?.PlayerCharacter ?? GameSettings.DollSingerCharacterIndex);
            if (m_GameSettings != null) m_GameSettings.PlayerCharacterChanged += SelectCharacter;
            try {
                m_LastTime = Time.realtimeSinceStartupAsDouble;
                if (m_SharedStage == null) m_Timer = Element.schedule.Execute(Tick).Every(33);
            } catch {
                Dispose();
                throw;
            }
        }

        // 角色编号变化后按新资源建模型。之前拍摄的纹理和动画图跟旧角色一起释放，避免换人后画面残留。
        private void SelectCharacter(int characterIndex) {
            characterIndex = GameSettings.NormalizeCharacterIndex(characterIndex);
            if (m_Disposed || CharacterIndex == characterIndex) return;
            ReleaseStage();
            CharacterIndex = characterIndex;
            m_Motion = m_LastAction = m_BagRemaining = 0;
            m_ActionBag = null;
            MotionChanges = 0;
            HaloAimWeight = 0;
            string path = GameSettings.MenuCharacterSettingsPath(CharacterIndex);
            m_Settings = Resources.Load<MenuCharacterSettings>(path);
            if (m_Settings == null || m_Settings.VisualPrefab == null || m_Settings.Motions == null ||
                m_Settings.Motions.Length == 0) {
                m_Settings = null;
                Debug.LogWarning("Moonkov menu character settings are missing: " + path);
            }
        }

        private void BeginInspect(PointerDownEvent evt) {
            // 只有点到人物图本身才开始查看，
            // 覆盖在上方的按钮和物品仍接收自己的事件和指针捕获。
            if (m_Disposed || m_HomePose || m_DragPointer >= 0 || evt.target != Element ||
                evt.pointerType != UnityEngine.UIElements.PointerType.mouse || evt.button != 0)
                return;
            m_DragPointer = evt.pointerId;
            m_DragPosition = Element.WorldToLocal(evt.position);
            if (!m_Inspected) {
                m_InspectionYaw = m_PoseTurn;
                m_Inspected = true;
            }
            Element.CapturePointer(evt.pointerId);
            m_Timer?.Every(16);
            evt.StopPropagation();
        }

        private void Inspect(PointerMoveEvent evt) {
            if (evt.pointerId != m_DragPointer || !Element.HasPointerCapture(evt.pointerId)) return;
            if ((evt.pressedButtons & 1) == 0) {
                StopInspect();
                return;
            }
            var position = Element.WorldToLocal(evt.position);
            m_InspectionYaw =
                Mathf.Repeat(
                    m_InspectionYaw -
                    (position.x - m_DragPosition.x) * 180f / Mathf.Max(1f, Element.contentRect.width) + 180f, 360f) -
                180f;
            m_DragPosition = position;
            m_ViewDirty = true;
            evt.StopPropagation();
        }

        private void ZoomInspect(WheelEvent evt) {
            if (m_Disposed || m_HomePose || evt.target != Element || !float.IsFinite(evt.delta.y) || evt.delta.y == 0)
                return;
            Zoom = Mathf.Clamp(Zoom * Mathf.Exp(-Mathf.Clamp(evt.delta.y, -40, 40) * .08f), .65f, 3f);
            m_ViewDirty = true;
            evt.StopPropagation();
            (evt.target as VisualElement)?.focusController?.IgnoreEvent(evt);
        }

        private void EndInspect(PointerUpEvent evt) {
            if (evt.pointerId != m_DragPointer || evt.button != 0) return;
            StopInspect();
            evt.StopPropagation();
        }

        private void CancelInspect(PointerCancelEvent evt) {
            if (evt.pointerId == m_DragPointer) StopInspect();
        }

        private void CaptureLost(PointerCaptureOutEvent evt) {
            if (evt.pointerId == m_DragPointer) StopInspect();
        }

        private void Detached(DetachFromPanelEvent evt) { ReleaseStage(); }

        private void StopInspect() {
            int pointer = m_DragPointer;
            m_DragPointer = -1;
            if (pointer >= 0 && Element.HasPointerCapture(pointer)) Element.ReleasePointer(pointer);
            if (pointer >= 0) m_Timer?.Every(33);
        }

        private void ApplyOrientation() {
            if (m_Model != null)
                m_Model.transform.localRotation = Quaternion.Euler(0,
                    m_HomePose ? HomeYaw : m_Settings.Yaw + (m_Inspected ? m_InspectionYaw : m_PoseTurn), 0);
        }

        public void ResetInspection() {
            StopInspect();
            m_InspectionYaw = 0;
            m_Inspected = false;
            Zoom = 1;
            m_ViewDirty = true;
        }

        // 预览可以独占一套舞台，也能挂到小队共用舞台。这里准备模型和动画，独立展示时还会配置自己的相机和灯。
        private void CreateStage() {
            m_Slot = 0;
            m_Motion = 0;
            m_Blend = 0;
            m_BaseTime = 0;
            m_Transition = false;
            m_Times[0] = m_Times[1] = 0;
            m_GestureActive = false;
#if UNITY_EDITOR
            if (m_SharedStage == null && !Application.isPlaying) {
                m_Scene = EditorSceneManager.NewPreviewScene();
                m_EditorScene = true;
            } else
#endif
            {
                m_Scene = m_SharedStage != null
                    ? m_SharedStage.gameObject.scene
                    : SceneManager.CreateScene("Moonkov menu character " + Guid.NewGuid().ToString("N"));
            }
            m_Stage = new GameObject("Menu character render stage");
            SceneManager.MoveGameObjectToScene(m_Stage, m_Scene);
            // Player 构建时把预览放远一些，避开实际场景的模型和灯光。
            if (m_SharedStage != null)
                m_Stage.transform.SetParent(m_SharedStage, false);
            else
                m_Stage.transform.position = m_EditorScene
                    ? Vector3.zero
                    : new Vector3(1000 + 64 * (s_StageSequence++ % 100), 1000, 1000);
            m_Stage.SetActive(false);
            m_Model = UnityEngine.Object.Instantiate(m_Settings.VisualPrefab, m_Stage.transform, false);
            m_Model.transform.localPosition = Vector3.zero;
            m_Model.transform.localRotation = Quaternion.Euler(0, m_Settings.Yaw, 0);
            foreach (var t in m_Model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ViewLayer;
            var animator = m_Model.GetComponentInChildren<Animator>(true);
            m_Animator = animator;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = false;
            m_Stage.SetActive(true);
            animator.Rebind();
            m_TwistFollowers = m_Model.GetComponentInChildren<HumanoidTwistFollowers>(true);
            m_TwistFollowers?.InitializePresentation();
            m_Spring = m_Model.GetComponentInChildren<SecondaryBoneSpring>(true);
            m_HandAvoidance = m_Model.GetComponentInChildren<SkirtHandAvoidance>(true);
            m_HandAvoidance?.PreparePresentation(0, 0);
            m_Spring?.InitializePresentation();
            m_Chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (m_Settings.GestureBake != null)
                m_Gestures = new DollSingerBakedGestures(animator, m_Settings.GestureBake);
            m_FullBodyMask = new AvatarMask { name = "Menu full body", hideFlags = HideFlags.HideAndDontSave };
            for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                m_FullBodyMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, true);
            m_Graph = PlayableGraph.Create("Moonkov menu motions");
            m_Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            m_Mixer = AnimationMixerPlayable.Create(m_Graph, 2);
            m_BaseIdle = AnimationClipPlayable.Create(m_Graph, m_Settings.Motions[0]);
            m_BaseIdle.SetSpeed(0);
            m_BaseIdle.SetApplyFootIK(false);
            m_BaseIdle.SetApplyPlayableIK(true);
            m_Layers = AnimationLayerMixerPlayable.Create(m_Graph, 2);
            m_Graph.Connect(m_BaseIdle, 0, m_Layers, 0);
            m_Graph.Connect(m_Mixer, 0, m_Layers, 1);
            m_Layers.SetInputWeight(0, 1);
            m_Layers.SetInputWeight(1, 1);
            m_Layers.SetLayerMaskFromAvatarMask(1, m_FullBodyMask);
            AnimationPlayableOutput.Create(m_Graph, "Character", animator).SetSourcePlayable(m_Layers);
            SetClip(0, 0);
            m_Mixer.SetInputWeight(0, 1f);
            m_Graph.Play();
            m_Graph.Evaluate(0);
            m_TwistFollowers?.ApplyPresentation();
            m_Remaining = IdleDuration();
            m_Head = animator.GetBoneTransform(HumanBodyBones.Head);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            m_FaceHeightOffset = m_Head != null && neck != null
                ? Vector3.Distance(m_Head.position, neck.position) * 1.25f
                : 0;
            m_Finger = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);
            if (m_Finger != null)
                while (m_Finger.childCount > 0)
                    m_Finger = m_Finger.GetChild(0);
            InitializeEquipmentVisuals();
            PresentMeleeEquipment();
            UpdateHalo();
            SampleEquipmentVisuals(0);
            ApplyProtectionInventory(m_ProtectionInventory);
            ApplyEquipmentProtectionSnapshot();
            m_Renderers = m_Model.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in m_Renderers) {
                renderer.updateWhenOffscreen = true;
                // 小队相机按要求绘制，编辑器一次更新可能请求多次。
                // 每次都让蒙皮更新到最终的骨骼姿势。
                if (m_SharedStage != null) renderer.forceMatrixRecalculationPerRender = true;
                renderer.forceRenderingOff = false;
            }
            if (m_Renderers.Length == 0)
                throw new InvalidOperationException("The menu visual prefab has no character renderer.");
            m_Bounds = new Bounds(m_Stage.transform.position, Vector3.zero);
            ExpandFramingBounds();
            if (m_SharedStage != null) return;
            m_Camera = Child("Menu portrait camera").AddComponent<Camera>();
            m_Camera.enabled = false;
            m_Camera.cullingMask = 1 << ViewLayer;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.clear;
            m_Camera.orthographic = false;
            m_Camera.fieldOfView = PortraitFieldOfView;
            m_Camera.nearClipPlane = .05f;
            m_Camera.farClipPlane = 30;
            m_Camera.allowHDR = true;
            m_Camera.allowMSAA = true;
            var data = m_Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            data.renderShadows = true;
            // 编辑器和 Player 都按同样的可见性限制处理。每部相机只画自己的 UI 舞台。
            m_Camera.scene = m_Scene;
            Light("Soft key", new Vector3(24, 145, 0), new Color(1f, .97f, .94f), m_HomePose ? .7f : 1.25f, true);
            Light("Cool fill", new Vector3(8, -135, 0), new Color(.82f, .89f, 1f), m_HomePose ? .3f : .5f, false);
            if (m_HomePose) Light("Window rim", new Vector3(18, -40, 0), new Color(.75f, .82f, 1), .9f, false);
            m_ViewDirty = true;
        }

        private GameObject Child(string name) {
            var child = new GameObject(name) { layer = ViewLayer };
            child.transform.SetParent(m_Stage.transform, false);
            return child;
        }

        private void Light(string name, Vector3 rotation, Color color, float intensity, bool shadows) {
            var light = Child(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << ViewLayer;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.transform.rotation = Quaternion.Euler(rotation);
        }

        private float IdleDuration() {
            return Mathf.Lerp(m_Settings.IdleInterval.x, m_Settings.IdleInterval.y, (float)m_Random.NextDouble());
        }

        private void SetClip(int slot, int index) {
            if (m_Clips[slot].IsValid()) {
                m_Graph.Disconnect(m_Mixer, slot);
                m_Graph.DestroyPlayable(m_Clips[slot]);
            }
            m_Clips[slot] = AnimationClipPlayable.Create(m_Graph, m_Settings.Motions[index]);
            m_Clips[slot].SetApplyFootIK(false);
            m_Clips[slot].SetApplyPlayableIK(true);
            // 这里自己在 Advance 中推进采样时刻。新的 Clip 不让动画系统自动走时间，切页暂停与减弱效果都能直接控制。
            m_Clips[slot].SetSpeed(0);
            m_Graph.Connect(m_Clips[slot], 0, m_Mixer, slot);
            m_Indices[slot] = index;
            m_Times[slot] = 0;
        }

        private void BeginNextMotion() {
            // 打乱动作后每个只拿一次。一轮都展示完才开始下一轮。
            int next = m_Motion == 0 && m_Settings.Motions.Length > 1 ? TakeAction() : 0;
            BeginMotion(next);
        }

        private void BeginMotion(int next) {
            if (next != 0) {
                m_LastAction = next;
                var mask = m_Settings.MotionMasks != null && next < m_Settings.MotionMasks.Length
                    ? m_Settings.MotionMasks[next]
                    : m_Settings.HaloAimMask;
                m_Layers.SetLayerMaskFromAvatarMask(1, mask != null ? mask : m_FullBodyMask);
            }
            var options = m_Settings.Motion(next);
            var clip = m_Settings.Motions[next];
            m_FadeSeconds = Mathf.Max(.01f, m_Settings.CrossfadeSeconds);
            if (next != 0 && !options.Loop && options.Weapon == 0)
                m_FadeSeconds = Mathf.Min(m_FadeSeconds, Mathf.Max(.05f, clip.length * .25f));
            // 保留还在播放的动作，在另一个槽准备下一段。过渡期间用两个槽的权重做平滑混合。
            SetClip(1 - m_Slot, next);
            m_Motion = next;
            m_Transition = true;
            m_Blend = 0;
            float hold = options.HoldSeconds > 0 ? options.HoldSeconds :
                options.Loop ? Mathf.Max(clip.length,
                    Mathf.Lerp(m_Settings.LoopInterval.x, m_Settings.LoopInterval.y, (float)m_Random.NextDouble())) :
                clip.length;
            m_Remaining = next == 0 ? IdleDuration() : hold + m_FadeSeconds;
            MotionChanges++;
        }

        private int TakeAction() {
            // 可选动作只收与当前装备兼容的条目。一轮随机列表用完再洗，避免短时间同一动作反复出现。
            if (m_ActionBag == null) {
                var actions = new List<int>();
                for (int i = 1; i < m_Settings.Motions.Length; i++)
                    if (EquipmentMotionAllowed(i))
                        actions.Add(i);
                m_ActionBag = actions.ToArray();
                m_BagRemaining = 0;
            }
            int count = m_ActionBag.Length;
            if (count == 0) return 0;
            if (m_BagRemaining == 0) {
                for (int i = count - 1; i > 0; i--) {
                    int other = m_Random.Next(i + 1);
                    (m_ActionBag[i], m_ActionBag[other]) = (m_ActionBag[other], m_ActionBag[i]);
                }
                if (count > 1 && m_ActionBag[count - 1] == m_LastAction)
                    (m_ActionBag[0], m_ActionBag[count - 1]) = (m_ActionBag[count - 1], m_ActionBag[0]);
                m_BagRemaining = count;
            }
            return m_ActionBag[--m_BagRemaining];
        }

        private void Tick() {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = Mathf.Clamp((float)(now - m_LastTime), 0, .1f);
            m_LastTime = now;
            if (m_Disposed || m_Settings == null) return;
            bool visible = Element.panel != null;
            for (VisualElement parent = Element; parent != null; parent = parent.parent)
                if (parent.resolvedStyle.display == DisplayStyle.None) {
                    visible = false;
                    break;
                }
            // 不可见视图不继续占用舞台和纹理。需要时会用同样的配置建回来。
            if (!visible) {
                ReleaseStage();
                return;
            }
            if (m_Camera == null) CreateStage();
            // 关闭动态效果时，只在首次、尺寸或状态变化时渲染静态帧。首页的明确点击演出仍有专门处理。
            if ((!TerminalMotion.Animate(Element) || HalosTerminalBackdrop.PrefersReducedMotion) &&
                !HomeReactionActive) {
                var size = Element.contentRect.size;
                if (m_Target == null || size != m_EditorViewport || m_ViewDirty) {
                    if (m_ViewDirty) Advance(0);
                    Render();
                    m_EditorViewport = size;
                }
                return;
            }
            Advance(dt);
            Render();
        }

        // 编辑器的生命周期与动画定向检查也会调用这里。
        public void Advance(float dt) {
            if (m_Disposed || m_Settings == null) return;
            if (m_Model == null) CreateStage();
            float reactionDt = dt;
            if (HomeReactionActive) {
                AdvanceHomeReaction(dt);
                if (m_HomeReducedMotion) dt = 0;
            }
            m_Remaining -= dt;
            m_BaseTime += dt;
            var idle = m_Settings.Motions[0];
            // 待机作为底层连续循环。上层动作仍能保留各自的播放时间和骨骼遮罩。
            m_BaseIdle.SetTime(m_BaseTime % idle.length);
            if (m_SharedStage != null)
                AdvanceSquadPose(dt);
            else if (!m_IdleOnly && m_Remaining <= 0 && !m_Transition) BeginNextMotion();
            for (int i = 0; i < 2; i++) {
                if (!m_Clips[i].IsValid()) continue;
                var clip = m_Settings.Motions[m_Indices[i]];
                m_Times[i] += dt;
                m_Clips[i].SetTime(m_Settings.Motion(m_Indices[i]).Loop
                    ? m_Times[i] % clip.length
                    : Math.Min(m_Times[i], clip.length));
            }
            // 两个动作的权重加起来为一。混合结束后交换当前槽，下次继续复用另一边。
            if (m_Transition) {
                m_Blend = Mathf.Min(1f, m_Blend + dt / m_FadeSeconds);
                float weight = Mathf.SmoothStep(0, 1, m_Blend);
                m_Mixer.SetInputWeight(m_Slot, 1f - weight);
                m_Mixer.SetInputWeight(1 - m_Slot, weight);
                if (m_Blend >= 1) {
                    m_Slot = 1 - m_Slot;
                    m_Transition = false;
                }
            }
            float preserveHands = 0, turn = 0, tiptoe = 0;
            bool gestureActive = false;
            for (int i = 0; i < 2; i++) {
                if (!m_Clips[i].IsValid()) continue;
                var options = m_Settings.Motion(m_Indices[i]);
                float weight = m_Mixer.GetInputWeight(i);
                if (options.PreserveHands) preserveHands += weight;
                turn += options.Turn * weight;
                if (options.Tiptoe) tiptoe += weight;
                if (options.Weapon > 0 && weight > .0001f) gestureActive = true;
            }
            m_HandAvoidance?.PreparePresentation(dt, preserveHands);
            m_EquipmentMelee?.PreparePresentation(dt);
            m_TwistFollowers?.RestoreBeforeAnimation();
            if (m_GestureActive || gestureActive || m_HomeSniperShowing) m_Gestures?.RestoreBeforeAnimation();
            m_SquadMotions?.RestoreBeforeAnimation();
            m_HomeMotions?.RestoreBeforeAnimation();
            m_GestureActive = gestureActive;
            m_Graph.Evaluate(0);
            m_Model.transform.localPosition = Vector3.zero;
            m_PoseTurn = turn * m_Settings.TurnDegrees;
            ApplyOrientation();
            m_ViewDirty = false;
            DollSingerMovement.ApplyTiptoePresentation(m_Animator, tiptoe);
            m_TwistFollowers?.ApplyPresentation();
            ApplyWeaponGesture(dt);
            m_EquipmentMelee?.SamplePreviewIdle();
            ApplySquadPose(dt);
            if (m_HomePose && m_Head != null) {
                float lookOutside = 1 - m_HomeFacing;
                m_Head.rotation = Quaternion.AngleAxis(-20 * lookOutside, Vector3.up) *
                                  Quaternion.AngleAxis(-10 * lookOutside, m_Model.transform.right) * m_Head.rotation;
            }
            UpdateHalo();
            ApplyHomeReactionPose(reactionDt);
            m_Spring?.SimulatePresentation(dt);
            SampleEquipmentVisuals(dt);
            PlayHomeShot();
            if (m_Protection) m_Protection.SetPreviewHaloColor(PreviewHaloColor);
            if (m_Protection) m_Protection.SimulatePresentation();
        }

        private void ExpandFramingBounds() {
            var root = m_Model.transform;
            var toModel = root.worldToLocalMatrix;
            var scale = root.lossyScale;
            foreach (var renderer in m_Renderers) {
                var bounds = renderer.localBounds;
                var boundsRoot = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                var toRoot = toModel * boundsRoot.localToWorldMatrix;
                for (int i = 0; i < 8; i++) {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    ExpandFramingPoint(Vector3.Scale(toRoot.MultiplyPoint3x4(corner), scale));
                }
            }
            if (m_Head != null)
                ExpandFramingPoint(Vector3.Scale(root.InverseTransformPoint(m_Head.position), scale) +
                                   Vector3.up * .4f);
            if (m_Halo != null)
                ExpandFramingPoint(Vector3.Scale(root.InverseTransformPoint(m_Halo.position), scale) +
                                   Vector3.up * .2f);
        }

        private void ExpandFramingPoint(Vector3 point) {
            float radius = new Vector2(point.x, point.z).magnitude;
            var center = m_Model.transform.position + Vector3.up * point.y;
            m_Bounds.Encapsulate(new Bounds(center, new Vector3(radius * 2, 0, radius * 2)));
        }

        private void ApplyWeaponGesture(float dt) {
            if (m_Gestures == null || m_Chest == null) return;
            for (int i = 0; i < 2; i++) {
                if (!m_Clips[i].IsValid()) continue;
                var options = m_Settings.Motion(m_Indices[i]);
                int gestureWeapon = EquipmentGestureWeapon(m_Indices[i]);
                if (gestureWeapon == 0 || m_Mixer.GetInputWeight(i) <= 0) continue;
                float age = (float)m_Times[i] - m_Settings.CrossfadeSeconds;
                m_Gestures.Apply(gestureWeapon, m_Mixer.GetInputWeight(i),
                    options.WeaponAction == MenuWeaponAction.Fire ? age : float.PositiveInfinity,
                    options.WeaponAction == MenuWeaponAction.Reload, age / Mathf.Max(.01f, options.HoldSeconds),
                    m_Settings.CrossfadeSeconds, m_Chest, m_Model.transform.forward, dt);
                return;
            }
        }

        public void SetProtectionInventory(InventoryGraph inventory) { SetEquipmentInventory(inventory); }

        private void ApplyProtectionInventory(InventoryGraph inventory) {
            m_ProtectionInventory = inventory;
            if (m_Model == null) return;
            bool equipped = EnergyShieldEquipment.IsHeadDome(inventory?.Equipped("Helmet")?.Code) ||
                            inventory?.Equipped("Shield") != null ||
                            EnergyShieldEquipment.BackpackStyle(inventory?.Equipped("Backpack")?.Code) != 0 ||
                            EnergyShieldEquipment.BadgeStyle(inventory?.Equipped("ChestRig")?.Code) != 0;
            if (!m_Protection && !equipped) return;
            if (!m_Protection)
                m_Protection = m_Model.GetComponent<EnergyShieldPresentation>() ??
                               m_Model.AddComponent<EnergyShieldPresentation>();
            m_Protection.PresentationAudioEnabled = false;
            m_Protection.PresentInventory(inventory);
            m_Protection.SetPreviewHaloColor(PreviewHaloColor);
            m_Protection.SimulatePresentation();
        }

        private void UpdateHalo() {
            HaloAimWeight = 0;
            for (int i = 0; i < 2; i++)
                if (m_Clips[i].IsValid() && (m_Settings.Motion(m_Indices[i]).Weapon > 0 || m_Indices[i] == 1))
                    HaloAimWeight += m_Mixer.GetInputWeight(i);
        }

        public void Render() { Render(Element.contentRect.size); }

        // 按 UI 上的实际尺寸分配渲染纹理，大小合适时继续复用。用放大的渲染尺寸提高预览清晰度，同时受配置上限约束。
        public void Render(Vector2 viewport) {
            if (m_Disposed || m_Camera == null) return;
            float width = viewport.x, height = viewport.y;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width < 2 || height < 2) return;
            int maximum = m_HomePose ? Mathf.Min(1536, m_Settings.MaximumResolution) : m_Settings.MaximumResolution;
            float scale = Mathf.Min(m_Settings.ResolutionScale, maximum / Mathf.Max(width, height));
            int w = Mathf.Min(maximum, Mathf.Max(16, Mathf.CeilToInt(width * scale / 16f) * 16));
            int h = Mathf.Min(maximum, Mathf.Max(16, Mathf.CeilToInt(height * scale / 16f) * 16));
            // 只有分辨率真的变了才重新分配渲染纹理。普通每帧渲染复用原来的目标。
            if (m_Target == null || m_Target.width != w || m_Target.height != h) {
                ReleaseTarget();
                var descriptor = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24)
                    { msaaSamples = 4, useMipMap = false };
                descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
                m_Target = new RenderTexture(descriptor) {
                    name = "Moonkov live character", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear
                };
                m_Target.Create();
                Element.image = m_Target;
            }
            m_Camera.aspect = (float)w / h;
            float verticalTangent = Mathf.Tan(PortraitFieldOfView * .5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(m_Bounds.extents.y * 1.08f / verticalTangent + m_Bounds.extents.z,
                m_Bounds.extents.x * 1.12f / Mathf.Sin(Mathf.Atan(verticalTangent * m_Camera.aspect)));
            m_Camera.fieldOfView = 2 * Mathf.Atan(verticalTangent / Zoom) * Mathf.Rad2Deg;
            var focus = m_Bounds.center;
            if (m_HomePose && m_Head != null) {
                // 角色转身时保持近景构图，仍能看到裙摆和大腿上半部。
                var hips = m_Animator.GetBoneTransform(HumanBodyBones.Hips);
                float torso = m_Head.position.y - hips.position.y;
                float top = Mathf.Max(m_Head.position.y + torso * .88f, m_Bounds.max.y) + torso * .07f;
                float bottom = hips.position.y - torso * .91f;
                focus.y = (top + bottom) * .5f;
                distance = (top - bottom) * .5f / verticalTangent + torso * .25f;
            } else if (m_Head != null && Zoom > 1) {
                var face = m_Head.position + Vector3.up * m_FaceHeightOffset;
                face.z = focus.z;
                focus = Vector3.Lerp(focus, face, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1, 2, Zoom)));
            }
            m_Camera.transform.position = focus + new Vector3(0, .035f, distance);
            m_Camera.transform.LookAt(focus);
            RenderPipeline.SubmitRenderRequest(m_Camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = m_Target });
            Element.MarkDirtyRepaint();
            FrameRendered?.Invoke();
        }

        public bool TryGetBodyAnchor(HumanBodyBones bone, out Vector2 world) {
            world = default;
            if (m_Camera == null || m_Animator == null || Element.panel == null) return false;
            var transform = m_Animator.GetBoneTransform(bone);
            if (transform == null) return false;
            var point = m_Camera.WorldToViewportPoint(transform.position);
            if (point.z <= 0) return false;
            world = Element.LocalToWorld(new Vector2(point.x * Element.contentRect.width,
                (1 - point.y) * Element.contentRect.height));
            return true;
        }

        private void ReleaseTarget() {
            Element.image = null;
            if (m_Target == null) return;
            m_Target.Release();
            Destroy(m_Target);
            m_Target = null;
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
            m_Timer?.Pause();
            if (m_GameSettings != null) m_GameSettings.PlayerCharacterChanged -= SelectCharacter;
            Element.UnregisterCallback<PointerDownEvent>(BeginInspect);
            Element.UnregisterCallback<PointerMoveEvent>(Inspect);
            Element.UnregisterCallback<PointerUpEvent>(EndInspect);
            Element.UnregisterCallback<PointerCancelEvent>(CancelInspect);
            Element.UnregisterCallback<PointerCaptureOutEvent>(CaptureLost);
            Element.UnregisterCallback<WheelEvent>(ZoomInspect);
            Element.UnregisterCallback<DetachFromPanelEvent>(Detached);
            ReleaseStage();
            m_ProtectionInventory = null;
        }

        // 舞台关闭时要释放 PlayableGraph、纹理和模型。下一次打开重新建立隔离的预览资源。
        private void ReleaseStage() {
            ReleaseHomeReaction();
            ReleaseEquipmentVisuals();
            StopInspect();
            m_InspectionYaw = m_PoseTurn = 0;
            m_Inspected = m_ViewDirty = false;
            Zoom = 1;
            if (m_Graph.IsValid()) m_Graph.Destroy();
            if (m_FullBodyMask != null) {
                Destroy(m_FullBodyMask);
                m_FullBodyMask = null;
            }
            ReleaseTarget();
            if (m_Stage != null) {
                m_Stage.SetActive(false);
                Destroy(m_Stage);
            }
            m_Stage = null;
            m_Camera = null;
            m_Model = null;
            m_Animator = null;
            m_Protection = null;
            m_EquipmentMelee = null;
            m_Halo = null;
            m_Head = null;
            m_Finger = null;
            m_Spring = null;
            m_HandAvoidance = null;
            m_TwistFollowers = null;
            m_Gestures = null;
            m_Chest = null;
            m_Renderers = null;
            m_SquadMotions = null;
            m_SquadPose = -1;
            m_SquadReady = m_SquadSettled = false;
            m_SquadReadyPulse = 0;
            if (m_SharedStage != null) {
                m_Scene = default;
                return;
            }
            if (!m_Scene.IsValid()) return;
#if UNITY_EDITOR
            if (m_EditorScene) {
                EditorSceneManager.ClosePreviewScene(m_Scene);
                m_Scene = default;
                return;
            }
#endif
            SceneManager.UnloadSceneAsync(m_Scene);
            m_Scene = default;
        }
    }
}