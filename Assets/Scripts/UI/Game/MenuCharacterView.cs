using System;
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

namespace Unity.MP_FPS.Client
{
    // A private rendering stage: no player input, networking, or gameplay controller.
    public sealed class MenuCharacterView : IDisposable
    {
        // The project's PC renderer only draws layers 0–7. UI is within that mask.
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
        public string CurrentMotion => m_Settings == null ? string.Empty :
            m_Settings.Motion(m_Motion).Name ?? m_Settings.Motions[m_Motion].name;
        public int CharacterIndex { get; private set; } = -1;
        private MenuCharacterSettings m_Settings;
        private readonly GameSettings m_GameSettings;
        private readonly bool m_IdleOnly;
        private Animator m_Animator;
        public event Action FrameRendered;
        private readonly System.Random m_Random = new System.Random();
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
        private LineRenderer[] m_HaloStrokes;
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

        public MenuCharacterView(bool idleOnly = false, Image element = null, int? characterIndex = null)
        {
            m_IdleOnly = idleOnly;
            Element = element ?? new Image();
            Element.pickingMode = PickingMode.Position; Element.scaleMode = ScaleMode.ScaleToFit;
            Element.AddToClassList("terminal-character-art");
            Element.tooltip = MoonkovLocalization.Text("左键拖动旋转 · 滚轮缩放");
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
            try
            {
                m_LastTime = Time.realtimeSinceStartupAsDouble;
                m_Timer = Element.schedule.Execute(Tick).Every(33);
            }
            catch { Dispose(); throw; }
        }

        private void SelectCharacter(int characterIndex)
        {
            characterIndex = GameSettings.NormalizeCharacterIndex(characterIndex);
            if (m_Disposed || CharacterIndex == characterIndex) return;
            ReleaseStage();
            CharacterIndex = characterIndex;
            m_Motion = m_LastAction = m_BagRemaining = 0;
            m_ActionBag = null;
            MotionChanges = 0; HaloAimWeight = 0;
            string path = GameSettings.MenuCharacterSettingsPath(CharacterIndex);
            m_Settings = Resources.Load<MenuCharacterSettings>(path);
            if (m_Settings == null || m_Settings.VisualPrefab == null || m_Settings.Motions == null || m_Settings.Motions.Length == 0)
            {
                m_Settings = null;
                Debug.LogWarning("Moonkov menu character settings are missing: " + path);
            }
        }

        private void BeginInspect(PointerDownEvent evt)
        {
            // Only a press on the portrait starts inspection. Buttons/items layered
            // above it retain their own event target and pointer capture.
            if (m_Disposed || m_DragPointer >= 0 || evt.target != Element ||
                evt.pointerType != UnityEngine.UIElements.PointerType.mouse || evt.button != 0) return;
            m_DragPointer = evt.pointerId;
            m_DragPosition = Element.WorldToLocal(evt.position);
            if (!m_Inspected) { m_InspectionYaw = m_PoseTurn; m_Inspected = true; }
            Element.CapturePointer(evt.pointerId);
            m_Timer?.Every(16);
            evt.StopPropagation();
        }
        private void Inspect(PointerMoveEvent evt)
        {
            if (evt.pointerId != m_DragPointer || !Element.HasPointerCapture(evt.pointerId)) return;
            if ((evt.pressedButtons & 1) == 0) { StopInspect(); return; }
            Vector2 position = Element.WorldToLocal(evt.position);
            m_InspectionYaw = Mathf.Repeat(m_InspectionYaw +
                (position.x - m_DragPosition.x) * 180f / Mathf.Max(1f, Element.contentRect.width) + 180f, 360f) - 180f;
            m_DragPosition = position;
            m_ViewDirty = true;
            evt.StopPropagation();
        }
        private void ZoomInspect(WheelEvent evt)
        {
            if (m_Disposed || evt.target != Element || !float.IsFinite(evt.delta.y) || evt.delta.y == 0) return;
            Zoom = Mathf.Clamp(Zoom * Mathf.Exp(-Mathf.Clamp(evt.delta.y, -40, 40) * .08f), .65f, 3f);
            m_ViewDirty = true;
            evt.StopPropagation();
            evt.PreventDefault();
        }
        private void EndInspect(PointerUpEvent evt)
        {
            if (evt.pointerId != m_DragPointer || evt.button != 0) return;
            StopInspect(); evt.StopPropagation();
        }
        private void CancelInspect(PointerCancelEvent evt) { if (evt.pointerId == m_DragPointer) StopInspect(); }
        private void CaptureLost(PointerCaptureOutEvent evt) { if (evt.pointerId == m_DragPointer) StopInspect(); }
        private void Detached(DetachFromPanelEvent evt) => ReleaseStage();
        private void StopInspect()
        {
            int pointer = m_DragPointer; m_DragPointer = -1;
            if (pointer >= 0 && Element.HasPointerCapture(pointer)) Element.ReleasePointer(pointer);
            if (pointer >= 0) m_Timer?.Every(33);
        }
        private void ApplyOrientation()
        {
            if (m_Model != null)
                m_Model.transform.localRotation = Quaternion.Euler(0, m_Settings.Yaw +
                    (m_Inspected ? m_InspectionYaw : m_PoseTurn), 0);
        }

        private void CreateStage()
        {
            m_Slot = 0; m_Motion = 0; m_Blend = 0; m_BaseTime = 0; m_Transition = false;
            m_Times[0] = m_Times[1] = 0;
            m_GestureActive = false;
#if UNITY_EDITOR
            if (!Application.isPlaying) { m_Scene = EditorSceneManager.NewPreviewScene(); m_EditorScene = true; }
            else
#endif
                m_Scene = SceneManager.CreateScene("Moonkov menu character " + Guid.NewGuid().ToString("N"));
            m_Stage = new GameObject("Menu character render stage");
            SceneManager.MoveGameObjectToScene(m_Stage, m_Scene);
            // Far from gameplay geometry and lights in a player build.
            m_Stage.transform.position = m_EditorScene ? Vector3.zero : new Vector3(1000 + 64 * (s_StageSequence++ % 100), 1000, 1000);
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
            if (m_Settings.GestureBake != null) m_Gestures = new DollSingerBakedGestures(animator, m_Settings.GestureBake);
            m_FullBodyMask = new AvatarMask { name = "Menu full body", hideFlags = HideFlags.HideAndDontSave };
            for (int part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                m_FullBodyMask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, true);
            m_Graph = PlayableGraph.Create("Moonkov menu motions");
            m_Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            m_Mixer = AnimationMixerPlayable.Create(m_Graph, 2);
            m_BaseIdle = AnimationClipPlayable.Create(m_Graph, m_Settings.Motions[0]);
            m_BaseIdle.SetSpeed(0); m_BaseIdle.SetApplyFootIK(false); m_BaseIdle.SetApplyPlayableIK(true);
            m_Layers = AnimationLayerMixerPlayable.Create(m_Graph, 2);
            m_Graph.Connect(m_BaseIdle, 0, m_Layers, 0);
            m_Graph.Connect(m_Mixer, 0, m_Layers, 1);
            m_Layers.SetInputWeight(0, 1); m_Layers.SetInputWeight(1, 1);
            m_Layers.SetLayerMaskFromAvatarMask(1, m_FullBodyMask);
            AnimationPlayableOutput.Create(m_Graph, "Character", animator).SetSourcePlayable(m_Layers);
            SetClip(0, 0);
            m_Mixer.SetInputWeight(0, 1f);
            m_Graph.Play(); m_Graph.Evaluate(0);
            m_TwistFollowers?.ApplyPresentation();
            m_Remaining = IdleDuration();
            foreach (var t in m_Model.GetComponentsInChildren<Transform>(true))
                if (t.name == "HaloCrosshair") { m_Halo = t; break; }
            m_Head = animator.GetBoneTransform(HumanBodyBones.Head);
            var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            m_FaceHeightOffset = m_Head != null && neck != null ? Vector3.Distance(m_Head.position, neck.position) * 1.25f : 0;
            m_Finger = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);
            if (m_Finger != null) while (m_Finger.childCount > 0) m_Finger = m_Finger.GetChild(0);
            if (m_Halo != null)
            {
                m_Halo.gameObject.SetActive(true);
                m_HaloStrokes = m_Halo.GetComponentsInChildren<LineRenderer>(true);
            }
            UpdateHalo();
            SetProtectionInventory(m_ProtectionInventory);

            m_Renderers = m_Model.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var renderer in m_Renderers)
            {
                renderer.updateWhenOffscreen = true;
                renderer.forceRenderingOff = false;
            }
            if (m_Renderers.Length == 0) throw new InvalidOperationException("The menu visual prefab has no character renderer.");
            m_Bounds = new Bounds(m_Stage.transform.position, Vector3.zero);
            ExpandFramingBounds();
            m_Camera = Child("Menu portrait camera").AddComponent<Camera>();
            m_Camera.enabled = false;
            m_Camera.cullingMask = 1 << ViewLayer;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.clear;
            m_Camera.orthographic = false;
            m_Camera.fieldOfView = PortraitFieldOfView;
            m_Camera.nearClipPlane = .05f; m_Camera.farClipPlane = 30;
            m_Camera.allowHDR = true; m_Camera.allowMSAA = true;
            var data = m_Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.volumeLayerMask = 0;
            data.renderShadows = true;
            // Apply in both editor and player: a camera must never draw a second UI stage.
            m_Camera.scene = m_Scene;
            Light("Soft key", new Vector3(24, 145, 0), new Color(1f, .97f, .94f), 1.25f, true);
            Light("Cool fill", new Vector3(8, -135, 0), new Color(.82f, .89f, 1f), .5f, false);
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name) { layer = ViewLayer };
            child.transform.SetParent(m_Stage.transform, false); return child;
        }
        private void Light(string name, Vector3 rotation, Color color, float intensity, bool shadows)
        {
            var light = Child(name).AddComponent<Light>();
            light.type = LightType.Directional; light.color = color; light.intensity = intensity;
            light.cullingMask = 1 << ViewLayer;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.transform.rotation = Quaternion.Euler(rotation);
        }
        private float IdleDuration() => Mathf.Lerp(m_Settings.IdleInterval.x, m_Settings.IdleInterval.y, (float)m_Random.NextDouble());
        private void SetClip(int slot, int index)
        {
            if (m_Clips[slot].IsValid()) { m_Graph.Disconnect(m_Mixer, slot); m_Graph.DestroyPlayable(m_Clips[slot]); }
            m_Clips[slot] = AnimationClipPlayable.Create(m_Graph, m_Settings.Motions[index]);
            m_Clips[slot].SetApplyFootIK(false);
            m_Clips[slot].SetApplyPlayableIK(true);
            m_Clips[slot].SetSpeed(0);
            m_Graph.Connect(m_Clips[slot], 0, m_Mixer, slot);
            m_Indices[slot] = index; m_Times[slot] = 0;
        }
        private void BeginNextMotion()
        {
            // Shuffle without replacement: every configured action gets a turn before any repeat.
            int next = m_Motion == 0 && m_Settings.Motions.Length > 1 ? TakeAction() : 0;
            if (next != 0)
            {
                m_LastAction = next;
                var mask = m_Settings.MotionMasks != null && next < m_Settings.MotionMasks.Length ? m_Settings.MotionMasks[next] : m_Settings.HaloAimMask;
                m_Layers.SetLayerMaskFromAvatarMask(1, mask != null ? mask : m_FullBodyMask);
            }
            var options = m_Settings.Motion(next);
            var clip = m_Settings.Motions[next];
            m_FadeSeconds = Mathf.Max(.01f, m_Settings.CrossfadeSeconds);
            if (next != 0 && !options.Loop && options.Weapon == 0)
                m_FadeSeconds = Mathf.Min(m_FadeSeconds, Mathf.Max(.05f, clip.length * .25f));
            SetClip(1 - m_Slot, next);
            m_Motion = next; m_Transition = true; m_Blend = 0;
            float hold = options.HoldSeconds > 0 ? options.HoldSeconds : options.Loop
                ? Mathf.Max(clip.length, Mathf.Lerp(m_Settings.LoopInterval.x, m_Settings.LoopInterval.y, (float)m_Random.NextDouble())) : clip.length;
            m_Remaining = next == 0 ? IdleDuration() : hold + m_FadeSeconds;
            MotionChanges++;
        }

        private int TakeAction()
        {
            int count = m_Settings.Motions.Length - 1;
            if (m_ActionBag == null || m_ActionBag.Length != count) { m_ActionBag = new int[count]; m_BagRemaining = 0; }
            if (m_BagRemaining == 0)
            {
                for (int i = 0; i < count; i++) m_ActionBag[i] = i + 1;
                for (int i = count - 1; i > 0; i--)
                {
                    int other = m_Random.Next(i + 1);
                    (m_ActionBag[i], m_ActionBag[other]) = (m_ActionBag[other], m_ActionBag[i]);
                }
                if (count > 1 && m_ActionBag[count - 1] == m_LastAction)
                    (m_ActionBag[0], m_ActionBag[count - 1]) = (m_ActionBag[count - 1], m_ActionBag[0]);
                m_BagRemaining = count;
            }
            return m_ActionBag[--m_BagRemaining];
        }

        private void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = Mathf.Clamp((float)(now - m_LastTime), 0, .1f); m_LastTime = now;
            if (m_Disposed || m_Settings == null) return;
            bool visible = Element.panel != null;
            for (VisualElement parent = Element; parent != null; parent = parent.parent)
                if (parent.resolvedStyle.display == DisplayStyle.None) { visible = false; break; }
            if (!visible) { ReleaseStage(); return; }
            if (m_Camera == null) CreateStage();
            if(!TerminalMotion.Animate(Element))
            {
                var size=Element.contentRect.size;
                if(m_Target==null || size!=m_EditorViewport || m_ViewDirty)
                {
                    if (m_ViewDirty) Advance(0);
                    Render(); m_EditorViewport=size;
                }
                return;
            }
            Advance(dt);
            Render();
        }
        // Also used by the editor's single targeted lifecycle/animation check.
        public void Advance(float dt)
        {
            if (m_Disposed || m_Settings == null) return;
            if (m_Camera == null) CreateStage();
            m_Remaining -= dt;
            m_BaseTime += dt;
            var idle = m_Settings.Motions[0];
            m_BaseIdle.SetTime(m_BaseTime % idle.length);
            if (!m_IdleOnly && m_Remaining <= 0 && !m_Transition) BeginNextMotion();
            for (int i = 0; i < 2; i++)
            {
                if (!m_Clips[i].IsValid()) continue;
                var clip = m_Settings.Motions[m_Indices[i]];
                m_Times[i] += dt;
                m_Clips[i].SetTime(m_Settings.Motion(m_Indices[i]).Loop ? m_Times[i] % clip.length : Math.Min(m_Times[i], clip.length));
            }
            if (m_Transition)
            {
                m_Blend = Mathf.Min(1f, m_Blend + dt / m_FadeSeconds);
                float weight = Mathf.SmoothStep(0, 1, m_Blend);
                m_Mixer.SetInputWeight(m_Slot, 1f - weight); m_Mixer.SetInputWeight(1 - m_Slot, weight);
                if (m_Blend >= 1) { m_Slot = 1 - m_Slot; m_Transition = false; }
            }
            float preserveHands = 0, turn = 0, tiptoe = 0;
            bool gestureActive = false;
            for (int i = 0; i < 2; i++)
            {
                if (!m_Clips[i].IsValid()) continue;
                var options = m_Settings.Motion(m_Indices[i]);
                float weight = m_Mixer.GetInputWeight(i);
                if (options.PreserveHands) preserveHands += weight;
                turn += options.Turn * weight;
                if (options.Tiptoe) tiptoe += weight;
                if (options.Weapon > 0 && weight > .0001f) gestureActive = true;
            }
            m_HandAvoidance?.PreparePresentation(dt, preserveHands);
            m_TwistFollowers?.RestoreBeforeAnimation();
            if (m_GestureActive || gestureActive) m_Gestures?.RestoreBeforeAnimation();
            m_GestureActive = gestureActive;
            m_Graph.Evaluate(0);
            m_Model.transform.localPosition = Vector3.zero;
            m_PoseTurn = turn * m_Settings.TurnDegrees;
            ApplyOrientation();
            m_ViewDirty = false;
            DollSingerMovement.ApplyTiptoePresentation(m_Animator, tiptoe);
            m_TwistFollowers?.ApplyPresentation();
            ApplyWeaponGesture(dt);
            m_Spring?.SimulatePresentation(dt);
            UpdateHalo();
            if(m_Protection)m_Protection.SimulatePresentation();
        }

        private void ExpandFramingBounds()
        {
            var root = m_Model.transform;
            var toModel = root.worldToLocalMatrix;
            var scale = root.lossyScale;
            foreach (var renderer in m_Renderers)
            {
                var bounds = renderer.localBounds;
                var boundsRoot = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                var toRoot = toModel * boundsRoot.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    ExpandFramingPoint(Vector3.Scale(toRoot.MultiplyPoint3x4(corner), scale));
                }
            }
            if (m_Head != null) ExpandFramingPoint(Vector3.Scale(root.InverseTransformPoint(m_Head.position), scale) + Vector3.up * .4f);
            if (m_Halo != null) ExpandFramingPoint(Vector3.Scale(root.InverseTransformPoint(m_Halo.position), scale) + Vector3.up * .2f);
        }

        private void ExpandFramingPoint(Vector3 point)
        {
            float radius = new Vector2(point.x, point.z).magnitude;
            var center = m_Model.transform.position + Vector3.up * point.y;
            m_Bounds.Encapsulate(new Bounds(center, new Vector3(radius * 2, 0, radius * 2)));
        }

        private void ApplyWeaponGesture(float dt)
        {
            if (m_Gestures == null || m_Chest == null) return;
            for (int i = 0; i < 2; i++)
            {
                if (!m_Clips[i].IsValid()) continue;
                var options = m_Settings.Motion(m_Indices[i]);
                if (options.Weapon == 0 || m_Mixer.GetInputWeight(i) <= 0) continue;
                float age = (float)m_Times[i] - m_Settings.CrossfadeSeconds;
                m_Gestures.Apply(options.Weapon, m_Mixer.GetInputWeight(i),
                    options.WeaponAction == MenuWeaponAction.Fire ? age : float.PositiveInfinity,
                    options.WeaponAction == MenuWeaponAction.Reload, age / Mathf.Max(.01f, options.HoldSeconds),
                    m_Settings.CrossfadeSeconds, m_Chest, m_Model.transform.forward, dt);
                return;
            }
        }
        public void SetProtectionInventory(InventoryGraph inventory)
        {
            m_ProtectionInventory=inventory;
            if(m_Model==null)return;
            bool equipped=EnergyShieldEquipment.IsHeadDome(inventory?.Equipped("Helmet")?.Code)||inventory?.Equipped("Shield")!=null||EnergyShieldEquipment.BackpackStyle(inventory?.Equipped("Backpack")?.Code)!=0;
            if(!m_Protection&&!equipped)return;
            if(!m_Protection)m_Protection=m_Model.GetComponent<EnergyShieldPresentation>()??m_Model.AddComponent<EnergyShieldPresentation>();
            m_Protection.PresentInventory(inventory);
            m_Protection.SimulatePresentation();
            Render();
        }
        private void UpdateHalo()
        {
            HaloAimWeight = 0;
            for (int i = 0; i < 2; i++)
                if (m_Clips[i].IsValid() && (m_Settings.Motion(m_Indices[i]).Weapon > 0 || m_Indices[i] == 1))
                    HaloAimWeight += m_Mixer.GetInputWeight(i);
            if (m_Halo == null || m_Head == null || m_Finger == null) return;
            // Same head-to-fingertip path and ring orientation as DollSingerHaloAim.LateUpdate.
            var root = m_Model.transform;
            float t = HaloAimWeight * HaloAimWeight * (3f - 2f * HaloAimWeight);
            var head = m_Head.position + root.up * .27f + root.right * Mathf.Sin((float)m_BaseTime * 1.8f) * .008f;
            var finger = m_Finger.position + root.forward * .075f;
            var control = head + root.up * .17f + root.right * .16f;
            m_Halo.position = (1 - t) * (1 - t) * head + 2 * (1 - t) * t * control + t * t * finger;
            m_Halo.rotation = Quaternion.Slerp(Quaternion.LookRotation(root.up, root.forward), Quaternion.LookRotation(root.forward, root.up), t);
            float size = Mathf.Lerp(.83f, .45f, t);
            var scale = root.lossyScale;
            m_Halo.localScale = new Vector3(size / Mathf.Max(.001f, scale.x), size / Mathf.Max(.001f, scale.y), size / Mathf.Max(.001f, scale.z));
            Color color = Color.Lerp(m_Settings.IdleHaloColor, m_Settings.AimedHaloColor, t);
            foreach (var stroke in m_HaloStrokes)
            {
                var tint = color; if (stroke.name.EndsWith("_Glow", StringComparison.Ordinal)) tint.a = .6f;
                stroke.startColor = tint; stroke.endColor = tint;
            }
        }
        public void Render() => Render(Element.contentRect.size);

        public void Render(Vector2 viewport)
        {
            if (m_Disposed || m_Camera == null) return;
            float width = viewport.x, height = viewport.y;
            if (!float.IsFinite(width) || !float.IsFinite(height) || width < 2 || height < 2) return;
            float scale = Mathf.Min(m_Settings.ResolutionScale, m_Settings.MaximumResolution / Mathf.Max(width, height));
            int w = Mathf.Max(16, Mathf.CeilToInt(width * scale / 16f) * 16);
            int h = Mathf.Max(16, Mathf.CeilToInt(height * scale / 16f) * 16);
            if (m_Target == null || m_Target.width != w || m_Target.height != h)
            {
                ReleaseTarget();
                var descriptor = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, useMipMap = false };
                descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
                m_Target = new RenderTexture(descriptor) { name = "Moonkov live character", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                m_Target.Create(); Element.image = m_Target;
            }
            m_Camera.aspect = (float)w / h;
            float verticalTangent = Mathf.Tan(PortraitFieldOfView * .5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(m_Bounds.extents.y * 1.08f / verticalTangent + m_Bounds.extents.z,
                m_Bounds.extents.x * 1.12f / Mathf.Sin(Mathf.Atan(verticalTangent * m_Camera.aspect)));
            m_Camera.fieldOfView = 2 * Mathf.Atan(verticalTangent / Zoom) * Mathf.Rad2Deg;
            var focus = m_Bounds.center;
            if (m_Head != null && Zoom > 1)
            {
                var face = m_Head.position + Vector3.up * m_FaceHeightOffset;
                face.z = focus.z;
                focus = Vector3.Lerp(focus, face, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1, 2, Zoom)));
            }
            m_Camera.transform.position = focus + new Vector3(0, .035f, distance);
            m_Camera.transform.LookAt(focus);
            RenderPipeline.SubmitRenderRequest(m_Camera, new UniversalRenderPipeline.SingleCameraRequest { destination = m_Target });
            Element.MarkDirtyRepaint();
            FrameRendered?.Invoke();
        }
        public bool TryGetBodyAnchor(HumanBodyBones bone, out Vector2 world)
        {
            world = default;
            if (m_Camera == null || m_Animator == null || Element.panel == null) return false;
            var transform = m_Animator.GetBoneTransform(bone); if (transform == null) return false;
            var point = m_Camera.WorldToViewportPoint(transform.position);
            if (point.z <= 0) return false;
            world = Element.LocalToWorld(new Vector2(point.x * Element.contentRect.width, (1 - point.y) * Element.contentRect.height));
            return true;
        }
        private void ReleaseTarget()
        {
            Element.image = null;
            if (m_Target == null) return;
            m_Target.Release(); Destroy(m_Target); m_Target = null;
        }
        private static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Timer?.Pause();
            if (m_GameSettings != null) m_GameSettings.PlayerCharacterChanged -= SelectCharacter;
            Element.UnregisterCallback<PointerDownEvent>(BeginInspect);
            Element.UnregisterCallback<PointerMoveEvent>(Inspect);
            Element.UnregisterCallback<PointerUpEvent>(EndInspect);
            Element.UnregisterCallback<PointerCancelEvent>(CancelInspect);
            Element.UnregisterCallback<PointerCaptureOutEvent>(CaptureLost);
            Element.UnregisterCallback<WheelEvent>(ZoomInspect);
            Element.UnregisterCallback<DetachFromPanelEvent>(Detached);
            ReleaseStage();
            m_ProtectionInventory=null;
        }
        private void ReleaseStage()
        {
            StopInspect(); m_InspectionYaw = m_PoseTurn = 0; m_Inspected = m_ViewDirty = false; Zoom = 1;
            if (m_Graph.IsValid()) m_Graph.Destroy();
            if (m_FullBodyMask != null) { Destroy(m_FullBodyMask); m_FullBodyMask = null; }
            ReleaseTarget();
            if (m_Stage != null) { m_Stage.SetActive(false); Destroy(m_Stage); }
            m_Stage = null; m_Camera = null; m_Model = null; m_Animator = null;
            m_Protection=null;
            m_Halo = null; m_Head = null; m_Finger = null; m_HaloStrokes = null;
            m_Spring = null; m_HandAvoidance = null;
            m_TwistFollowers = null;
            m_Gestures = null; m_Chest = null; m_Renderers = null;
            if (!m_Scene.IsValid()) return;
#if UNITY_EDITOR
            if (m_EditorScene) { EditorSceneManager.ClosePreviewScene(m_Scene); m_Scene = default; return; }
#endif
            SceneManager.UnloadSceneAsync(m_Scene);
            m_Scene = default;
        }
    }
}
