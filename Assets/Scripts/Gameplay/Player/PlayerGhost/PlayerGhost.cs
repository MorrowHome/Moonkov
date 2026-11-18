// 把角色的 ECS Ghost 接到场景中的模型、控制器和碰撞体。自己控制的客户端还会挂镜头和声音监听器。
// GetShotRay 从角色的游戏坐标确定射线起点，也能在没有模型画面的服务端使用。
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Serialization;
using Unity.MP_FPS.DollSinger;
using static FirstPersonController;

namespace Unity.MP_FPS {
    public partial class PlayerGhost : GhostMonoBehaviour, IUpdateClient, IUpdateServer {
        [field: SerializeField] public AssetReferenceGameObject ProjectilePrefabAR { get; private set; }
        [SerializeField] private Vector3 m_CameraRotation;
        [SerializeField] private GameObject m_OwnerVisuals;
        [SerializeField] private GameObject m_OtherPlayerVisuals;
        [SerializeField] private SoundDef m_SpawnSFX;
        [field: SerializeField] public Transform CameraTarget { get; private set; }
        [field: SerializeField] public Transform ReticlePoint { get; private set; }
        [field: SerializeField] public Transform ShotOrigin { get; private set; }

        [field: FormerlySerializedAs("<VisualShotOrigin>k__BackingField")]
        [field: SerializeField]
        public Transform VisualShotOrigin1P { get; private set; }

        [field: SerializeField] public Transform VisualShotOrigin3P { get; private set; }

        [Header("Manual Aiming Setup")] [SerializeField]
        private Animator m_Animator3P;

        private static readonly int AimPitchHash = Animator.StringToHash("AimPitch");
        public int PlayerIndex { get; private set; }
        public int InputUserId { get; set; } = -1;
        public PlayerInput ServerMovementInput { get; set; }
        public ControllerConsts ControllerConsts { get; private set; }

        #region Cached Player Components

        private FirstPersonController m_Controller;
        public FirstPersonController Controller => m_Controller;

        #endregion

        [field: SerializeField] public GameObject MainCameraPrefab { get; private set; }
        private Camera m_PlayerCamera;
        private Animator _animatorCharacter;
        private Vector3 m_ReticleVector;
        private SphereCollider m_HeadHitbox;
        private Transform m_HeadBone;
        private CapsuleCollider m_BodyHitbox, m_LeftArmHitbox, m_RightArmHitbox;
        private static RaycastHit[] s_ShotHits = new RaycastHit[64];
        private CinemachineTargetGroup m_TargetGroup;
        private CinemachinePositionComposer m_PositionComposer;
        private CinemachineCamera m_CinemachineCamera;

        // 记录这个客户端接管 Ghost 时临时关闭的 AudioListener，
        // 也记下它自己新增的那个。Ghost 被销毁时要靠这两份记录还原场景，
        // 具体见 RestoreAudioListeners。
        private AudioListener m_AddedListener;
        private readonly List<AudioListener> m_SuppressedListeners = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() { s_NextPredictionId = 1; }

        private static uint s_NextPredictionId = 1;
        public static uint GetNextPredictionID() { return s_NextPredictionId++; }
        // 统一提供当前有效的角色镜头。拥有者和普通网络观察者并不一定有相同的显示对象。
        public Camera GetPlayerCamera() { return m_PlayerCamera; }

        // 从角色身上的稳定锚点发出射线，再根据镜头提供的瞄准点检查开火方向。
        // 服务器或离线对象也需要稳定的开火锚点。视角提供瞄准意图，枪口射线再按本角色位置、头高和探身重新算。
        public Ray GetShotRay(in PlayerInput input, float range, out Vector3 aimPoint) {
            var direction = Quaternion.Euler(input.LookYawPitchDegrees.y, input.LookYawPitchDegrees.x, 0f) *
                            Vector3.forward;
            var origin = CameraTarget.position;
            aimPoint = origin + direction * range;
            if (TryGetComponent<DollSingerNetworkPresentation>(out var dollSinger)) {
                // 在模型关闭的无画面服务器上仍有可用的固定玩法锚点。
                origin = ShotOrigin.position + Vector3.up * Controller.StanceHeightOffset;
                var leanOffset = dollSinger.GetGameplayLeanOffset(input.Lean,
                    Quaternion.Euler(0f, input.LookYawPitchDegrees.x, 0f), input.ThirdPerson);
                origin += DollSingerMovement.ConstrainLeanOffset(origin, leanOffset, transform,
                    LayerMask.GetMask("Ground", "Default"));
                Vector3 requestedTarget = input.AimPoint;
                var offset = requestedTarget - origin;
                // 允许摄像机和枪口之间的视差。拒绝超限或朝后转的目标方向。
                if (math.all(math.isfinite(input.AimPoint)) && math.lengthsq(input.AimPoint) > 0f &&
                    math.isfinite(offset.sqrMagnitude) && offset.sqrMagnitude > 0.0001f &&
                    Vector3.Dot(offset.normalized, direction) >= 0.1f) {
                    direction = offset.normalized;
                    aimPoint = origin + direction * Mathf.Min(offset.magnitude, range);
                } else { aimPoint = origin + direction * range; }
            }
            return new Ray(origin, direction);
        }

        // 服务器即使把角色藏起来也要判定头部命中，所以用独立的碰撞体跟随角色姿势。
        // 真实头部占位按角色所在世界的层创建。代理不能靠主胶囊移动，但仍需要可供客户端准星查询的表面。
        public void CreateHeadHitbox(int layer) {
            if (m_HeadHitbox != null) return;
            var animator = m_Animator3P != null ? m_Animator3P : GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            if (animator.avatar != null && animator.avatar.isHuman)
                m_HeadBone = animator.GetBoneTransform(HumanBodyBones.Head);
            else
                foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "Head") {
                        m_HeadBone = bone;
                        break;
                    }
            if (m_HeadBone == null) return;
            // 碰撞体放在可见模型层级之外。无画面的服务器会关闭模型。
            var head = new GameObject("Head hitbox");
            head.layer = layer;
            head.transform.SetParent(transform, false);
            m_HeadHitbox = head.AddComponent<SphereCollider>();
            m_HeadHitbox.radius = .17f;
            head.AddComponent<PlayerHitRegion>().Part = BodyPart.Head;
            // 远端代理的移动控制器处于禁用状态。仍需能被物理查询到的身体，
            // 准星碰到人时取角色表面，避免射到背后很远的地板。
            var controller = GetComponent<UnityEngine.CharacterController>();
            if (controller != null) {
                var body = new GameObject("Body hitbox");
                body.layer = layer;
                body.transform.SetParent(transform, false);
                var capsule = body.AddComponent<CapsuleCollider>();
                capsule.center = controller.center;
                m_BodyHitbox = capsule;
                capsule.height = controller.height;
                capsule.radius = controller.radius;
                capsule.direction = 1;

                CapsuleCollider Arm(string name, BodyPart part) {
                    var arm = new GameObject(name);
                    arm.layer = layer;
                    arm.transform.SetParent(transform, false);
                    arm.AddComponent<PlayerHitRegion>().Part = part;
                    return arm.AddComponent<CapsuleCollider>();
                }

                m_LeftArmHitbox = Arm("Left arm hitbox", BodyPart.LeftArm);
                m_RightArmHitbox = Arm("Right arm hitbox", BodyPart.RightArm);
            }
            UpdateHeadHitbox();
        }

        // 同步身体胶囊尺寸与头手位置。专用服务器没有动画更新时走稳定的锚点，客户端能跟可见骨骼。
        private void UpdateHeadHitbox() {
            if (m_HeadHitbox == null) return;
            var capsule = Controller.CharacterController;
            if (m_BodyHitbox != null) {
                m_BodyHitbox.center = capsule.center;
                m_BodyHitbox.height = capsule.height;
                m_BodyHitbox.radius = capsule.radius;
            }
            if (Role == MultiplayerRole.Server)
                m_HeadHitbox.transform.localPosition = capsule.center + Vector3.up * (capsule.height * .5f - .14f);
            else if (m_HeadBone != null) m_HeadHitbox.transform.position = m_HeadBone.position + Vector3.up * .06f;

            void PlaceArm(CapsuleCollider arm, float side) {
                if (arm == null) return;
                arm.transform.localPosition =
                    capsule.center + new Vector3(side * (capsule.radius + .06f), capsule.height * .08f, 0);
                arm.height = capsule.height * .36f;
                arm.radius = .09f;
            }

            PlaceArm(m_LeftArmHitbox, -1);
            PlaceArm(m_RightArmHitbox, 1);
        }

        // 同一物理场景中查询射线上所有碰撞，过滤发射者自身并找最近一个。候选装满时宁可跳过任意选择。
        public bool RaycastShot(Ray ray, float range, int mask, out RaycastHit nearest) {
            var physics = gameObject.scene.GetPhysicsScene();
            int count;
            while ((count = physics.Raycast(ray.origin, ray.direction, s_ShotHits, range, mask,
                       QueryTriggerInteraction.Ignore)) == s_ShotHits.Length) {
                // 密集场景装满碰撞缓冲时保守放弃，别从可能不完整的结果里随便挑目标。
                if (s_ShotHits.Length >= 4096) {
                    nearest = default;
                    return false;
                }
                System.Array.Resize(ref s_ShotHits, s_ShotHits.Length * 2);
            }
            nearest = default;
            float distance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++) {
                var hit = s_ShotHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
                nearest = hit;
                distance = hit.distance;
                found = true;
            }
            return found;
        }

        public CinemachineCamera GetPlayerCinemachineCamera() { return m_CinemachineCamera; }
        public CinemachinePositionComposer GetPositionComposer() { return m_PositionComposer; }

        public struct PlayerData : IComponentData {
            [GhostField] public FixedString128Bytes Name;
            public Entity ViewEntity;
            public Entity ControlledEntity;
        }

        public void Awake() {
            GetRequiredComponent(out m_Controller);
            m_ReticleVector = ReticlePoint.localPosition;
        }

        private void LateUpdate() {
            // 这段逻辑只给有画面的客户端运行。
            if ((Role != MultiplayerRole.ClientProxy && Role != MultiplayerRole.ClientOwned) || m_Animator3P == null)
                return;
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;

            // 与 ClientInputReaderSystem 的竖直角度一致。
            float normalizedPitch = controllerState.PitchDegrees / 85f;
            m_Animator3P.SetFloat(AimPitchHash, normalizedPitch);
        }

        // Host 有服务器和客户端两份角色。按角色归属设置能查到哪一层，并在各自管理器登记。
        public override void OnGhostLinked() {
            // 房主的服务端与客户端副本共用 PhysX 场景。
            // 预测移动时要避免被属于另一套时间线的碰撞体推出去。
            Controller.CharacterController.excludeLayers |= Role == MultiplayerRole.Server
                ? LayerMask.GetMask("ClientPlayer")
                : LayerMask.GetMask("ServerPlayer");
            CreateHeadHitbox(Role == MultiplayerRole.Server
                ? (int)LayerIndex.ServerPlayer
                : (int)LayerIndex.ClientPlayer);
            bool isClientOwned = Role == MultiplayerRole.ClientOwned;
            m_OwnerVisuals.SetActive(isClientOwned);
            m_OtherPlayerVisuals.SetActive(!isClientOwned);
            if (Role != MultiplayerRole.Server) {
                _animatorCharacter = GetComponent<Animator>();
                // 自己出战播放两层确认音，包括成功锣声，
                // 以及人形角色确认命令的声音。别人进场仍用普通出生提示。
                var spawnSfx = isClientOwned ? MoonkovAudio.Library?.DeployConfirm : m_SpawnSFX;
                if (spawnSfx != null) GameManager.Instance.SoundSystem.CreateEmitter(spawnSfx, transform.position);
            }
            // 自己有可操作镜头，另建跟随头部的 AudioListener 并临时关掉场景里启用的其他监听器。远端只更新角色外观。
            if (isClientOwned) {
                var predictedPlayer = ReadGhostComponentData<PredictedPlayerGhost>();
                PlayerIndex = predictedPlayer.InputIndex;
                // 创建玩家摄像机。
                CreateClientCamera();

                // 单独的子物体跟随画面里的转头和探身，不靠模型的动画姿势。
                // 不接管或复制预制体的监听器。这次拥有权结束时销毁子物体。
                var listening = new GameObject("Owned audio listener").transform;
                listening.SetParent(m_PlayerCamera ? m_PlayerCamera.transform : m_OwnerVisuals.transform, false);
                m_AddedListener = listening.gameObject.AddComponent<AudioListener>();
                m_SuppressedListeners.Clear();
                foreach (var a in Resources.FindObjectsOfTypeAll<AudioListener>()) {
                    if (a == null || a == m_AddedListener || !a.enabled) continue;
                    if (!a.gameObject.scene.IsValid()) continue; // 跳过不属于已加载场景的预制体和资源对象。
                    m_SuppressedListeners.Add(a);
                    a.enabled = false;
                }
                GameManager.Instance.SoundSystem.SetListenerTransform(listening);
            // 远端角色用快照位姿，不启动人物碰撞马达。其可查询的代理部位由链接后的物件提供。
            } else if (Role == MultiplayerRole.ClientProxy) {
                // 远端角色直接使用同步位姿，无需物理驱动。
                Controller.CharacterController.enabled = false;
            }
            gameObject.layer = Role == MultiplayerRole.Server
                ? (int)LayerIndex.ServerPlayer
                : (int)LayerIndex.ClientPlayer;
            if (Role != MultiplayerRole.Server) Moon.MoonSilverPrintSubject.Protect(gameObject);
            PlayerGhostManager.TryGetInstanceByRole(Role, out var playerManager);
            playerManager.Register(this);
        }

        public override void OnGhostPreDestroy() {
            RestoreAudioListeners();
            if (PlayerGhostManager.TryGetInstanceByRole(Role, out var playerManager)) playerManager.Unregister(this);
        }

        // 离开自己角色时恢复原监听器并重新设置当前音频监听位置，回主界面也能听到声音。
        /// <summary>
        /// 把 AudioListener 的状态还原到当前客户端角色接管之前。
        /// 否则自己的监听器会随 Ghost 销毁，
        /// 而其它监听器仍关闭，导致死亡画面和撤离后的飞船没有声音。
        /// 明确重新开启相机上的监听器。Unity 需要至少一个启用的 AudioListener，
        /// 才能继续正常收听声音。
        /// </summary>
        private void RestoreAudioListeners() {
            if (m_AddedListener == null && m_SuppressedListeners.Count == 0) return;
            foreach (var listener in m_SuppressedListeners)
                if (listener != null)
                    listener.enabled = true;
            m_SuppressedListeners.Clear();
            if (m_AddedListener) {
                m_AddedListener.enabled = false;
                Destroy(m_AddedListener.gameObject);
            }
            m_AddedListener = null;
            var camera = MainCameraSingleton.Instance;
            var fallback = camera != null ? camera.GetComponent<AudioListener>() : null;
            if (fallback != null) {
                fallback.enabled = true;
                if (GameManager.Instance != null)
                    GameManager.Instance.SoundSystem.SetListenerTransform(fallback.transform);
            }
        }

        private void AttachPlayerViewCamera() {
            var playerCamera = FindAnyObjectByType<Camera>();
            if (playerCamera != null) {
                playerCamera.transform.parent = transform.Find("ViewPoint");
                playerCamera.transform.localPosition = Vector3.zero;
                playerCamera.transform.localRotation = Quaternion.identity;
            }
        }

        // 使用角色专门的视角组件时直接接过去。否则才初始化通用摄像机预制体。
        private void CreateClientCamera() {
            //先停用当前摄像机。
            var existingCamera = FindAnyObjectByType<Camera>();
            if (existingCamera != null) existingCamera.enabled = false;
            if (TryGetComponent<DollSingerNetworkPresentation>(out var dollSinger)) {
                m_PlayerCamera = dollSinger.ActivateOwnedView();
                Utils.SetCursorVisible(false);
                return;
            }

            // 生成本次要使用的摄像机。
            var mainCameraInstance = Instantiate(MainCameraPrefab, CameraTarget.transform);
            mainCameraInstance.transform.localPosition = Vector3.zero;
            mainCameraInstance.name = $"MainCamera_{PlayerIndex}";
            m_PlayerCamera = mainCameraInstance.GetComponent<Camera>();
            var audioListener = mainCameraInstance.GetComponent<AudioListener>();
            if (audioListener != null) GameManager.Instance.SoundSystem.SetListenerTransform(audioListener.transform);
            Utils.SetCursorVisible(false);
        }

        public void UpdateServer(float deltaTime) { UpdateHeadHitbox(); }

        public void UpdateClient(float deltaTime) {
            UpdateHeadHitbox();
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;
            if (Role == MultiplayerRole.ClientOwned)
                CameraTarget.transform.rotation = Quaternion.Euler(controllerState.PitchDegrees,
                    Camera.main.transform.rotation.eulerAngles.y, Camera.main.transform.rotation.eulerAngles.z);
            var rot = Quaternion.Euler(controllerState.PitchDegrees, 0.0f, 0.0f);
            ReticlePoint.localPosition = rot * m_ReticleVector;

            //TODO：这里是动画根位移的临时处理，例如 Robot Jump。
            m_OtherPlayerVisuals.transform.localPosition = Vector3.zero;
            m_OtherPlayerVisuals.transform.localRotation = Quaternion.identity;
        }

        // 服务端的动画定位信息只在允许的误差范围内校正。它跟常规预测的移动不是同一个入口。
        public bool SetPlayerPositionFromRPC(float3 rpcPosition, float positionErrorSq) {
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;
            float positionError = math.distancesq(controllerState.CurrentPosition, rpcPosition);

            //只允许客户端在指定的位置误差范围内应用这次校正。
            //用于减少被动画锁定位置时的滑步。这类差异来自客户端先于服务端预测。
            if (positionError <= positionErrorSq) {
                controllerState.CurrentPosition = rpcPosition;
                predictedPlayerGhost.ControllerState = controllerState;
                WriteGhostComponentData(predictedPlayerGhost);
                return true;
            }
            return false;
        }
    }
}