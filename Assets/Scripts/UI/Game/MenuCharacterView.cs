using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Unity.MP_FPS.DollSinger;
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
        public Image Element { get; }
        public RenderTexture Target => m_Target;
        public int MotionChanges { get; private set; }
        public float HaloAimWeight { get; private set; }
        public bool HaloVisible => m_Halo != null && m_Halo.gameObject.activeInHierarchy;
        public Scene DisplayScene => m_Scene;
        public string CurrentMotion => m_Settings.Motions[m_Motion].name;
        private readonly MenuCharacterSettings m_Settings;
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
        private SkirtHandAvoidance m_HandAvoidance;
        private Transform m_Halo, m_Head, m_Finger;
        private LineRenderer[] m_HaloStrokes;
        private double m_BaseTime;
        private RenderTexture m_Target;
        private IVisualElementScheduledItem m_Timer;
        private Bounds m_Bounds;
        private int m_Slot, m_Motion, m_LastAction;
        private float m_Remaining, m_Blend;
        private double m_LastTime;
        private Vector2 m_EditorViewport;
        private bool m_Transition, m_Disposed, m_EditorScene;

        public MenuCharacterView(bool idleOnly = false)
        {
            m_IdleOnly = idleOnly;
            Element = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.ScaleToFit };
            Element.AddToClassList("terminal-character-art");
            m_Settings = Resources.Load<MenuCharacterSettings>("Moonkov/MenuCharacterSettings");
            if (m_Settings == null || m_Settings.VisualPrefab == null || m_Settings.Motions == null || m_Settings.Motions.Length == 0)
            {
                Debug.LogWarning("Moonkov menu character settings are missing.");
                return;
            }
            try
            {
                m_LastTime = Time.realtimeSinceStartupAsDouble;
                m_Timer = Element.schedule.Execute(Tick).Every(33);
            }
            catch { Dispose(); throw; }
        }

        private void CreateStage()
        {
            m_Slot = 0; m_Motion = 0; m_Blend = 0; m_BaseTime = 0; m_Transition = false;
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
            m_Stage.SetActive(true);
            animator.Rebind();
            m_Spring = m_Model.GetComponentInChildren<SecondaryBoneSpring>(true);
            m_HandAvoidance = m_Model.GetComponentInChildren<SkirtHandAvoidance>(true);
            m_HandAvoidance?.PreparePresentation(0, 0);
            m_Spring?.InitializePresentation();
            m_Graph = PlayableGraph.Create("Moonkov menu motions");
            m_Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            m_Mixer = AnimationMixerPlayable.Create(m_Graph, 2);
            m_BaseIdle = AnimationClipPlayable.Create(m_Graph, m_Settings.Motions[0]);
            m_BaseIdle.SetSpeed(0); m_BaseIdle.SetApplyFootIK(false); m_BaseIdle.SetApplyPlayableIK(true);
            m_Layers = AnimationLayerMixerPlayable.Create(m_Graph, 2);
            m_Graph.Connect(m_BaseIdle, 0, m_Layers, 0);
            m_Graph.Connect(m_Mixer, 0, m_Layers, 1);
            m_Layers.SetInputWeight(0, 1); m_Layers.SetInputWeight(1, 1);
            if (m_Settings.HaloAimMask != null) m_Layers.SetLayerMaskFromAvatarMask(1, m_Settings.HaloAimMask);
            AnimationPlayableOutput.Create(m_Graph, "Character", animator).SetSourcePlayable(m_Layers);
            SetClip(0, 0);
            m_Mixer.SetInputWeight(0, 1f);
            m_Graph.Play(); m_Graph.Evaluate(0);
            m_Remaining = IdleDuration();
            foreach (var t in m_Model.GetComponentsInChildren<Transform>(true))
                if (t.name == "HaloCrosshair") { m_Halo = t; break; }
            m_Head = animator.GetBoneTransform(HumanBodyBones.Head);
            m_Finger = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);
            if (m_Finger != null) while (m_Finger.childCount > 0) m_Finger = m_Finger.GetChild(0);
            if (m_Halo != null)
            {
                m_Halo.gameObject.SetActive(true);
                m_HaloStrokes = m_Halo.GetComponentsInChildren<LineRenderer>(true);
            }
            UpdateHalo();

            bool found = false;
            foreach (var renderer in m_Model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                renderer.updateWhenOffscreen = true;
                renderer.forceRenderingOff = false;
                if (!found) { m_Bounds = renderer.bounds; found = true; }
                else m_Bounds.Encapsulate(renderer.bounds);
            }
            if (!found) throw new InvalidOperationException("The menu visual prefab has no character renderer.");
            if (m_Head != null) m_Bounds.Encapsulate(m_Head.position + m_Model.transform.up * .4f);
            m_Camera = Child("Menu portrait camera").AddComponent<Camera>();
            m_Camera.enabled = false;
            m_Camera.cullingMask = 1 << ViewLayer;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = Color.clear;
            m_Camera.orthographic = false;
            m_Camera.fieldOfView = 27;
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
            // A short gesture always returns to neutral idle; idle picks a different gesture.
            int next = m_Motion == 0 && m_Settings.Motions.Length > 1 ? m_Random.Next(1, m_Settings.Motions.Length) : 0;
            if (next != 0 && m_Settings.Motions.Length > 2)
                while (next == m_LastAction) next = m_Random.Next(1, m_Settings.Motions.Length);
            if (next != 0)
            {
                m_LastAction = next;
                var mask = m_Settings.MotionMasks != null && next < m_Settings.MotionMasks.Length ? m_Settings.MotionMasks[next] : m_Settings.HaloAimMask;
                if (mask != null) m_Layers.SetLayerMaskFromAvatarMask(1, mask);
            }
            SetClip(1 - m_Slot, next);
            m_Motion = next; m_Transition = true; m_Blend = 0;
            m_Remaining = next == 0 ? IdleDuration() : next == 1 ? Mathf.Lerp(2f, 4f, (float)m_Random.NextDouble()) : m_Settings.Motions[next].length + m_Settings.CrossfadeSeconds;
            MotionChanges++;
        }

        private void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = Mathf.Clamp((float)(now - m_LastTime), 0, .1f); m_LastTime = now;
            if (m_Disposed) return;
            bool visible = Element.panel != null;
            for (VisualElement parent = Element; parent != null; parent = parent.parent)
                if (parent.resolvedStyle.display == DisplayStyle.None) { visible = false; break; }
            if (!visible) { ReleaseStage(); return; }
            if (m_Camera == null) CreateStage();
            if(!TerminalMotion.Animate(Element))
            {
                var size=Element.contentRect.size;
                if(m_Target==null || size!=m_EditorViewport){Render();m_EditorViewport=size;}
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
                m_Clips[i].SetTime(m_Indices[i] == 0 && clip.isLooping ? m_Times[i] % clip.length : Math.Min(m_Times[i], clip.length));
            }
            if (m_Transition)
            {
                m_Blend = Mathf.Min(1f, m_Blend + dt / m_Settings.CrossfadeSeconds);
                float weight = Mathf.SmoothStep(0, 1, m_Blend);
                m_Mixer.SetInputWeight(m_Slot, 1f - weight); m_Mixer.SetInputWeight(1 - m_Slot, weight);
                if (m_Blend >= 1) { m_Slot = 1 - m_Slot; m_Transition = false; }
            }
            float aimWeight = MotionWeight(1);
            m_HandAvoidance?.PreparePresentation(dt, aimWeight);
            m_Graph.Evaluate(0);
            m_Model.transform.localPosition = Vector3.zero;
            float turn = (MotionWeight(3) - MotionWeight(2)) * m_Settings.TurnDegrees;
            m_Model.transform.localRotation = Quaternion.Euler(0, m_Settings.Yaw + turn, 0);
            m_Spring?.SimulatePresentation(dt);
            UpdateHalo();
        }
        private float MotionWeight(int motion)
        {
            float weight = 0;
            for (int i = 0; i < 2; i++)
                if (m_Clips[i].IsValid() && m_Indices[i] == motion) weight += m_Mixer.GetInputWeight(i);
            return weight;
        }
        private void UpdateHalo()
        {
            HaloAimWeight = MotionWeight(1);
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
        public void Render()
        {
            if (m_Disposed || m_Camera == null) return;
            float width = Element.contentRect.width, height = Element.contentRect.height;
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
            // Fit both dimensions so the full silhouette survives narrow window layouts.
            float halfHeight = Mathf.Max(m_Bounds.extents.y * 1.08f, m_Bounds.extents.x / m_Camera.aspect * 1.12f);
            float distance = halfHeight / Mathf.Tan(m_Camera.fieldOfView * .5f * Mathf.Deg2Rad) + m_Bounds.extents.z;
            m_Camera.transform.position = m_Bounds.center + new Vector3(0, .035f, distance);
            m_Camera.transform.LookAt(m_Bounds.center);
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
            ReleaseStage();
        }
        private void ReleaseStage()
        {
            if (m_Graph.IsValid()) m_Graph.Destroy();
            ReleaseTarget();
            if (m_Stage != null) { m_Stage.SetActive(false); Destroy(m_Stage); }
            m_Stage = null; m_Camera = null; m_Model = null; m_Animator = null;
            m_Halo = null; m_Head = null; m_Finger = null; m_HaloStrokes = null;
            m_Spring = null; m_HandAvoidance = null;
            if (!m_Scene.IsValid()) return;
#if UNITY_EDITOR
            if (m_EditorScene) { EditorSceneManager.ClosePreviewScene(m_Scene); m_Scene = default; return; }
#endif
            SceneManager.UnloadSceneAsync(m_Scene);
            m_Scene = default;
        }
    }
}
