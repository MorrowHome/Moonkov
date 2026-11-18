// 把同步的护盾、项圈、面罩和穿戴状态套到角色骨骼上，处理受击闪烁和第一人称可见性。最终绘制由 EnergyShieldRendering 负责。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    /// <summary>读取客户端装备并绘制外观。生命、库存、氧气和命中区域由游戏系统维护。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(180)]
    public sealed partial class EnergyShieldPresentation : MonoBehaviour {
        private struct CameraState {
            public Camera Camera;
            public bool Hidden;
        }

        private readonly Stack<CameraState> m_Cameras = new(4);
        private readonly List<Renderer> m_Hardware = new(12);
        private MaterialPropertyBlock m_ShieldProperties;
        private Transform m_Head, m_Neck, m_Hips, m_Chest, m_LeftFoot, m_RightFoot;
        private Transform m_Shell, m_Collar, m_Device, m_Root;
        private MeshRenderer m_ShellRenderer;
        private HaloDefenseSphere m_HaloDefense;
        private DollSinger.DollSingerHaloAim m_DefenseHalo;
        private DollSinger.DollSingerHaloAim.HaloWeapon m_DefenseWeapon;
        private bool m_Defending;
        private Material m_FieldMaterial;

        private bool m_Ready,
            m_GeometryRetained,
            m_HasSequence,
            m_Owned,
            m_FirstPerson,
            m_HeadEquipped,
            m_ShieldEquipped,
            m_Regenerating,
            m_Hidden;

        private bool m_StandardCollar;
        private bool m_OmitHardware;
        private Color? m_PreviewHaloColor;
        public void SetPreviewHaloColor(Color color) { m_PreviewHaloColor = color; }
        public bool PresentationAudioEnabled { get; set; } = true;

        public void ConfigureHardwareBudget(bool omitHardware) {
            if (m_OmitHardware == omitHardware) return;
            if (m_Ready) DisposePresentation();
            m_OmitHardware = omitHardware;
        }

        private Camera m_OwnedCamera;
        private uint m_HitSequence, m_BreakSequence, m_RestoreSequence;
        private float m_HitAt = -100, m_BreakAt = -100, m_RestoreAt = -100, m_Strength, m_LastSound = -100;
        private Vector3 m_Impact;
        private SoundSystem.SoundInfo m_Sound;
        internal Material FieldMaterial => m_FieldMaterial;

        internal bool HasSurfaces =>
            m_Ready && isActiveAndEnabled &&
            ((m_ShellRenderer && m_ShellRenderer.enabled && !m_ShellRenderer.forceRenderingOff) ||
             (m_VisorRenderer && m_VisorRenderer.enabled && !m_VisorRenderer.forceRenderingOff) ||
             (m_ApertureRenderer && m_ApertureRenderer.enabled && m_ApertureRenderer.gameObject.activeInHierarchy &&
              !m_ApertureRenderer.forceRenderingOff));

        public void Present(in PredictedPlayerGhost snapshot, bool owned, bool firstPerson, Camera ownedCamera) {
            if (!EnsureReady()) return;
            m_Owned = owned;
            m_FirstPerson = owned && firstPerson;
            m_OwnedCamera = ownedCamera;
            bool head = snapshot.HeadDomeEquipped;
            // 戴上头盔这一刻单独记时间。之后渲染用这个时间平滑带出面罩，保持戴着的时候不用重新开动画。
            if (head && !m_HeadEquipped) m_VisorEquippedAt = Time.unscaledTime;
            m_HeadEquipped = head;
            m_ShieldEquipped = snapshot.HaloEquipped;
            bool wasDefending = m_Defending;
            m_Defending = snapshot.HaloDefending && EnergyShieldRules.Alive(snapshot);
            m_DefenseWeapon =
                (DollSinger.DollSingerHaloAim.HaloWeapon)(HaloAppearanceCatalog.CurrentWeapon(snapshot) - 1);
            m_StandardCollar = snapshot.HeadDomeStandard;
            m_BackpackStyle = (byte)Mathf.Clamp(snapshot.SubspaceBackpack, 0, 4);
            m_BadgeStyle = (byte)Mathf.Clamp(snapshot.SubspaceBadge, 0, 3);
            m_Charge = HaloEnergyRules.Energy(snapshot) / HaloEnergyRules.Maximum(snapshot);
            m_Energy = m_Charge;
            m_Regenerating = snapshot.ShieldRegenerating && snapshot.CurrentHealth > 0 &&
                             (!snapshot.ClashEnabled || snapshot.ClashLife == ClashLife.Alive);
            // 首份角色快照只记录装备和事件序号。随后有新序号才播放受击、碎盾或恢复，避免玩家刚进图就看到之前的特效。
            if (!m_HasSequence) {
                m_HitSequence = snapshot.ShieldHitSequence;
                m_BreakSequence = snapshot.ShieldBreakSequence;
                m_RestoreSequence = snapshot.ShieldRestoreSequence;
                m_HasSequence = true;
            } else {
                // 各类盾变化用自己的序号，客户端只消费更晚的反馈。同一快照同时有碎盾和受击时，声音按更新处理里的次序取一条。
                bool hit = NewSequence(snapshot.ShieldHitSequence, ref m_HitSequence),
                    broken = NewSequence(snapshot.ShieldBreakSequence, ref m_BreakSequence),
                    restored = NewSequence(snapshot.ShieldRestoreSequence, ref m_RestoreSequence);
                float now = Time.unscaledTime;
                if (hit) {
                    m_HitAt = now;
                    m_Impact = (Vector3)snapshot.ShieldHitPoint;
                    m_Strength = EnergyShieldRules.Resource(snapshot.ShieldHitStrength * 100) / 100;
                }
                if (broken) m_BreakAt = now;
                if (restored) m_RestoreAt = now;
                if (broken)
                    PlayCue(1);
                else if (hit)
                    PlayCue(0);
                else if (restored)
                    PlayCue(2);
                else if (wasDefending != m_Defending && EnergyShieldRules.Alive(snapshot)) PlayCue(m_Defending ? 3 : 4);
            }
        }

        // 可供库存人物预览接入。直接读真实物品数值，不另维护一份配装。
        public void PresentInventory(InventoryGraph graph) {
            // 仓库预览直接读实际装备栏，填成表现层能理解的几项角色字段。局内角色的同一套外观类就能画菜单里的预览。
            var head = graph?.Equipped("Helmet");
            var snapshot = new PredictedPlayerGhost {
                CurrentHealth = 1, HeadDomeEquipped = EnergyShieldEquipment.IsHeadDome(head?.Code),
                HeadDomeStandard = head?.Code == EnergyShieldEquipment.StandardHeadDomeCode
            };
            snapshot.SubspaceBackpack = EnergyShieldEquipment.BackpackStyle(graph?.Equipped("Backpack")?.Code);
            snapshot.SubspaceBadge = EnergyShieldEquipment.BadgeStyle(graph?.Equipped("ChestRig")?.Code);
            Present(snapshot, false, false, null);
        }

        private static bool NewSequence(uint current, ref uint previous) {
            if (current == 0) {
                previous = 0;
                return false;
            }
            if (unchecked((int)(current - previous)) <= 0) return false;
            previous = current;
            return true;
        }

        private bool EnsureReady() {
            // 沿渲染表现路径初始化 Unity 的材质属性块，
            // 编辑器重新加载或新建组件后，原来未序列化的字段也能补齐。
            m_ShieldProperties ??= new MaterialPropertyBlock();
            if (m_Ready) {
                EnergyShieldRendering.Register(this);
                return true;
            }
            // 先找到人形 Animator 和头部骨骼，再创建材质和运行时装备物体。基准骨骼缺失时等资源就绪后再试。
            var animator = GetComponentInChildren<Animator>();
            if (!animator || !animator.isHuman) return false;
            var shader = Resources.Load<Shader>("Moonkov/EnergyShield");
            if (!shader || !shader.isSupported) return false;
            m_Head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (!m_Head) return false;
            m_Neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            m_Hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            m_Chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            m_LeftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            m_RightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (!m_OmitHardware) FitCollar(animator);
            EnergyShieldGeometry.Retain();
            m_GeometryRetained = true;
            m_FieldMaterial = new Material(shader)
                { name = "Energy protection / private surface", hideFlags = HideFlags.HideAndDontSave };
            m_Root = new GameObject("Energy protection / original runtime geometry") { hideFlags = HideFlags.DontSave }
                .transform;
            m_Root.SetParent(transform, false);
            m_DefenseHalo = GetComponentInChildren<DollSinger.DollSingerHaloAim>(true);
            var defense = new GameObject("Halo / spherical defense") { layer = gameObject.layer };
            defense.transform.SetParent(m_Root, false);
            m_HaloDefense = defense.AddComponent<HaloDefenseSphere>();
            // 简化的远端角色仍显示护盾受击、破裂和恢复提示。
            // 纯装饰的项圈、面罩支架和背包配件按需启用。
            if (m_OmitHardware) {
                m_Ready = true;
                EnergyShieldRendering.Register(this);
                return true;
            }
            m_Collar = new GameObject("Neko life-support collar").transform;
            m_Collar.SetParent(m_Root, false);
            m_Device = new GameObject("Independent shield generator").transform;
            m_Device.SetParent(m_Root, false);
            var metal = Resources.Load<Material>("Moonkov/TacticalMetal");
            var indicator = Resources.Load<Material>("Moonkov/TacticalIndicator");
            if (!metal || !indicator) {
                DisposePresentation();
                return false;
            }
            BuildCollar(metal, indicator);
            BuildVisor(metal, indicator);
            FitEquipment();
            BuildEquipment(metal, indicator);
            FitBadge(animator);
            Moon.MoonSilverPrintSubject.Protect(m_Collar.gameObject);
            Moon.MoonSilverPrintSubject.Protect(m_Device.gameObject);
            Moon.MoonSilverPrintSubject.Protect(m_Subspace.gameObject);
            m_Ready = true;
            EnergyShieldRendering.Register(this);
            return true;
        }

        private MeshRenderer Surface(string name, out Transform node) {
            node = new GameObject(name) { layer = gameObject.layer }.transform;
            node.SetParent(m_Root, false);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = EnergyShieldGeometry.Sphere;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_FieldMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        private MeshRenderer Hardware(
            Transform parent,
            Mesh mesh,
            Vector3 position,
            Vector3 scale,
            Material material,
            Color color) {
            var node = new GameObject("Original protection hardware") { layer = gameObject.layer };
            node.transform.SetParent(parent, false);
            node.transform.localPosition = position;
            node.transform.localScale = scale;
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
            m_Hardware.Add(renderer);
            return renderer;
        }

        private static void WorldPose(Transform node, Vector3 position, Quaternion rotation, Vector3 size) {
            node.SetPositionAndRotation(position, rotation);
            var scale = node.parent ? node.parent.lossyScale : Vector3.one;
            node.localScale = new Vector3(size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                size.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)), size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }

        private void LateUpdate() {
            if (!m_Ready || !m_Head) return;
            if (!m_OmitHardware) {
                UpdateCollarPose();
                m_Collar.gameObject.SetActive(m_HeadEquipped);
                m_CollarNekoDetails.gameObject.SetActive(!m_StandardCollar);
                m_CollarStandardDetails.gameObject.SetActive(m_StandardCollar);
                UpdateVisor();
                UpdateEquipment();
                UpdateBadge();
            }
            var feet = m_LeftFoot && m_RightFoot
                ? (m_LeftFoot.position + m_RightFoot.position) * .5f
                : transform.position;
            float radius = Mathf.Max(.85f, Vector3.Distance(feet, m_Head.position) * .78f);
            var center = (feet + m_Head.position) * .5f;
            var color = m_PreviewHaloColor ??
                        (m_DefenseHalo ? m_DefenseHalo.ColorForWeapon(m_DefenseWeapon) : new Color(1, .18f, .42f));
            // 碰撞点可能在角色内部或球外。拿中心到撞击的方向投到护盾球面，这样闪烁出现在罩子上。
            var impactDirection = m_Impact - center;
            var surfaceImpact = center +
                                (impactDirection.sqrMagnitude > .001f
                                    ? impactDirection.normalized
                                    : transform.forward) * radius;
            m_HaloDefense.Present(m_Defending, m_Head.position + transform.up * .35f, center, radius, color, m_Energy,
                Time.unscaledTime - m_HitAt, surfaceImpact, m_FirstPerson,
                Application.isPlaying ? Time.deltaTime : 1f / 60, (uint)m_DefenseWeapon + 1);
        }

        internal bool OwnerVisor(Camera camera) {
            return m_Ready && m_HeadEquipped && m_Owned && m_FirstPerson && camera == m_OwnedCamera;
        }

        internal void SimulatePresentation() { LateUpdate(); }

        internal float OwnerPulse(Camera camera) {
            if (!m_Ready || !m_Owned || !m_FirstPerson || camera != m_OwnedCamera || !m_ShieldEquipped) return 0;
            float hit = Mathf.Clamp01(1 - (Time.unscaledTime - m_HitAt) / .32f) * m_Strength;
            float broken = Mathf.Clamp01(1 - (Time.unscaledTime - m_BreakAt) / .5f);
            return Mathf.Max(hit * .12f, broken * .17f);
        }

        internal void RestoreCameraVisibility() {
            m_Cameras.Clear();
            SetHidden(false);
        }

        // 同一件装备在第一人称和其他相机里有不同的可见状态。每台相机渲染前单独设置，渲染后恢复，避免影响下一台相机。
        internal void BeginCamera(Camera camera) {
            m_Cameras.Push(new CameraState { Camera = camera, Hidden = m_Hidden });
            SetHidden(m_Owned && m_FirstPerson && camera == m_OwnedCamera);
        }

        internal void EndCamera(Camera camera) {
            if (m_Cameras.Count == 0) return;
            var state = m_Cameras.Pop();
            SetHidden(state.Hidden);
        }

        private void SetHidden(bool hidden) {
            m_Hidden = hidden;
            if (m_ShellRenderer) m_ShellRenderer.forceRenderingOff = hidden;
            if (m_VisorRenderer) m_VisorRenderer.forceRenderingOff = hidden;
            if (m_ApertureRenderer) m_ApertureRenderer.forceRenderingOff = hidden;
            foreach (var renderer in m_Hardware)
                if (renderer)
                    renderer.forceRenderingOff = hidden;
        }

        internal bool InView(Camera camera, Plane[] planes) {
            // 渲染前先检查是否有可见部件，再用相机层和包围盒筛选。相机看不到时就不用提交折射绘制。
            if (!HasSurfaces) return false;
            return Visible(m_ShellRenderer) || Visible(m_VisorRenderer) || Visible(m_ApertureRenderer);

            bool Visible(Renderer renderer) {
                return renderer && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                       !renderer.forceRenderingOff && (camera.cullingMask & (1 << renderer.gameObject.layer)) != 0 &&
                       GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
            }
        }

        private void PlayCue(int cue) {
            if (!PresentationAudioEnabled || !Application.isPlaying ||
                (cue == 0 && Time.unscaledTime - m_LastSound < .18f))
                return;
            var library = MoonkovAudio.Library;
            if (!library) return;
            if (cue <= 2) m_LastSound = Time.unscaledTime;
            GameManager.Instance?.SoundSystem?.Stop(m_Sound, .025f);
            m_Sound = MoonkovAudio.PlayWeaponLayer(
                cue == 1 ? library.ShieldBreak :
                cue == 2 ? library.ShieldRestore :
                cue == 3 ? library.ShieldRaise :
                cue == 4 ? library.ShieldLower : library.ShieldImpact, cue == 0 ? m_Impact : transform.position, 1,
                m_Owned ? .05f : 1);
        }

        public void ResetPresentation() {
            m_HeadEquipped = m_ShieldEquipped = m_Defending = m_Regenerating = m_HasSequence = false;
            if (m_HaloDefense) m_HaloDefense.Clear();
            m_BackpackStyle = 0;
            m_BadgeStyle = 0;
            if (m_Badge) m_Badge.gameObject.SetActive(false);
            if (m_Subspace) m_Subspace.gameObject.SetActive(false);
            m_HitAt = m_BreakAt = m_RestoreAt = -100;
            GameManager.Instance?.SoundSystem?.Stop(m_Sound, .03f);
            m_Sound = null;
            if (m_ShellRenderer) m_ShellRenderer.enabled = false;
            if (m_VisorRenderer) m_VisorRenderer.enabled = false;
            if (m_VisorRails != null)
                foreach (var rail in m_VisorRails)
                    if (rail)
                        rail.enabled = false;
            if (m_EnergyEars != null)
                foreach (var ear in m_EnergyEars)
                    if (ear)
                        ear.enabled = false;
            if (m_Collar) m_Collar.gameObject.SetActive(false);
            if (m_Device) m_Device.gameObject.SetActive(false);
        }

        public void DisposePresentation() {
            // 清理时先解除渲染订阅并恢复物体可见性，再释放本物体的材质、节点和共享网格引用。
            EnergyShieldRendering.Unregister(this);
            SetHidden(false);
            m_Cameras.Clear();
            GameManager.Instance?.SoundSystem?.Stop(m_Sound, .03f);
            m_Sound = null;
            DestroyOwned(m_FieldMaterial);
            if (m_Root) DestroyOwned(m_Root.gameObject);
            m_HaloDefense = null;
            m_DefenseHalo = null;
            m_Hardware.Clear();
            m_Root = m_Shell = m_Collar = m_Device = null;
            m_ShellRenderer = null;
            m_FieldMaterial = null;
            m_Sound = null;
            m_Visor = null;
            m_VisorRenderer = null;
            m_VisorRails = null;
            m_EnergyEars = m_EmitterLenses = null;
            m_SealHalo = null;
            m_SealHaloStroke = null;
            m_CollarNekoDetails = m_CollarStandardDetails = null;
            m_Subspace = null;
            m_ApertureRenderer = null;
            m_PackRings = null;
            m_PackLights = null;
            m_Badge = m_BadgeAnchor = null;
            m_BadgeModel = null;
            m_BadgeRenderers = null;
            m_BadgeBuiltStyle = 0;
            if (m_GeometryRetained) {
                EnergyShieldGeometry.Release();
                m_GeometryRetained = false;
            }
            m_Ready = false;
            ResetPresentation();
        }

        private void OnDisable() { DisposePresentation(); }
        private void OnDestroy() { DisposePresentation(); }

        private static void DestroyOwned(Object obj) {
            if (!obj) return;
            if (Application.isPlaying)
                Destroy(obj);
            else
                DestroyImmediate(obj);
        }
    }
}