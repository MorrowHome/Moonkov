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

namespace Unity.MP_FPS
{
    public partial class PlayerGhost : GhostMonoBehaviour, IUpdateClient, IUpdateServer
    {
        [field: SerializeField] public AssetReferenceGameObject ProjectilePrefabAR { get; private set; }
        [SerializeField] private Vector3 m_CameraRotation;
        [SerializeField] private GameObject m_OwnerVisuals;
        [SerializeField] private GameObject m_OtherPlayerVisuals;
        [SerializeField] private SoundDef m_SpawnSFX;
        [field: SerializeField] public Transform CameraTarget { get; private set; }
        [field: SerializeField] public Transform ReticlePoint { get; private set; }
        [field: SerializeField] public Transform ShotOrigin { get; private set; }
        [field: FormerlySerializedAs("<VisualShotOrigin>k__BackingField")] [field: SerializeField] 
        public Transform VisualShotOrigin1P { get; private set; }
        [field: SerializeField] public Transform VisualShotOrigin3P { get; private set; }

        [Header("Manual Aiming Setup")]
        [SerializeField] private Animator m_Animator3P;
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

        // AudioListeners that were suppressed when this client took ownership of the ghost, and
        // the one this ghost added. Both are needed to put the scene back the way it was when the
        // ghost is destroyed - see RestoreAudioListeners.
        private AudioListener m_AddedListener;
        private readonly List<AudioListener> m_SuppressedListeners = new List<AudioListener>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_NextPredictionId = 1;
        }
        
        private static uint s_NextPredictionId = 1;

        public static uint GetNextPredictionID()
        {
            return s_NextPredictionId++;
        }

        public Camera GetPlayerCamera()
        {
            return m_PlayerCamera;
        }

        public Ray GetShotRay(in PlayerInput input, float range, out Vector3 aimPoint)
        {
            var direction = Quaternion.Euler(input.LookYawPitchDegrees.y, input.LookYawPitchDegrees.x, 0f) * Vector3.forward;
            Vector3 origin = CameraTarget.position;
            aimPoint = origin + direction * range;
            if (TryGetComponent<DollSingerNetworkPresentation>(out var dollSinger))
            {
                // A stable gameplay anchor also exists on a headless server, where the model is inactive.
                origin = ShotOrigin.position;
                Vector3 leanOffset = dollSinger.GetGameplayLeanOffset(input.Lean,
                    Quaternion.Euler(0f, input.LookYawPitchDegrees.x, 0f), input.ThirdPerson);
                origin += DollSingerMovement.ConstrainLeanOffset(origin, leanOffset, transform,
                    LayerMask.GetMask("Ground", "Default"));
                Vector3 requestedTarget = input.AimPoint;
                Vector3 offset = requestedTarget - origin;
                // Allow camera/muzzle parallax, but reject overflowing or rearward target intent.
                if (math.all(math.isfinite(input.AimPoint)) && math.lengthsq(input.AimPoint) > 0f &&
                    math.isfinite(offset.sqrMagnitude) && offset.sqrMagnitude > 0.0001f &&
                    Vector3.Dot(offset.normalized, direction) >= 0.1f)
                {
                    direction = offset.normalized;
                    aimPoint = origin + direction * Mathf.Min(offset.magnitude, range);
                }
                else
                    aimPoint = origin + direction * range;
            }
            return new Ray(origin, direction);
        }

        public void CreateHeadHitbox(int layer)
        {
            if(m_HeadHitbox!=null)return;
            var animator=m_Animator3P!=null ? m_Animator3P : GetComponentInChildren<Animator>(true);
            if(animator==null)return;
            if(animator.avatar!=null && animator.avatar.isHuman)
                m_HeadBone=animator.GetBoneTransform(HumanBodyBones.Head);
            else
                foreach(var bone in animator.GetComponentsInChildren<Transform>(true))
                    if(bone.name=="Head"){m_HeadBone=bone;break;}
            if(m_HeadBone==null)return;
            // Keep the hitbox outside the visual hierarchy: headless servers disable that model.
            var head=new GameObject("Head hitbox");head.layer=layer;head.transform.SetParent(transform,false);
            m_HeadHitbox=head.AddComponent<SphereCollider>();m_HeadHitbox.radius=.17f;
            head.AddComponent<PlayerHitRegion>().Part = BodyPart.Head;
            // Proxy movement controllers are disabled. They still need a queryable body
            // so the camera chooses the character surface instead of distant ground.
            var controller=GetComponent<UnityEngine.CharacterController>();
            if(controller!=null)
            {
                var body=new GameObject("Body hitbox");body.layer=layer;body.transform.SetParent(transform,false);
                var capsule=body.AddComponent<CapsuleCollider>();capsule.center=controller.center;
                m_BodyHitbox = capsule;
                capsule.height=controller.height;capsule.radius=controller.radius;capsule.direction=1;
                CapsuleCollider Arm(string name, BodyPart part)
                {
                    var arm = new GameObject(name); arm.layer = layer; arm.transform.SetParent(transform, false);
                    arm.AddComponent<PlayerHitRegion>().Part = part;
                    return arm.AddComponent<CapsuleCollider>();
                }
                m_LeftArmHitbox = Arm("Left arm hitbox", BodyPart.LeftArm);
                m_RightArmHitbox = Arm("Right arm hitbox", BodyPart.RightArm);
            }
            UpdateHeadHitbox();
        }

        private void UpdateHeadHitbox()
        {
            if(m_HeadHitbox==null)return;
            var capsule = Controller.CharacterController;
            if (m_BodyHitbox != null) { m_BodyHitbox.center = capsule.center; m_BodyHitbox.height = capsule.height; m_BodyHitbox.radius = capsule.radius; }
            if (Role == MultiplayerRole.Server)
                m_HeadHitbox.transform.localPosition = capsule.center + Vector3.up * (capsule.height * .5f - .14f);
            else if (m_HeadBone != null) m_HeadHitbox.transform.position=m_HeadBone.position+Vector3.up*.06f;
            void PlaceArm(CapsuleCollider arm, float side)
            {
                if (arm == null) return;
                arm.transform.localPosition = capsule.center + new Vector3(side * (capsule.radius + .06f), capsule.height * .08f, 0);
                arm.height = capsule.height * .36f; arm.radius = .09f;
            }
            PlaceArm(m_LeftArmHitbox, -1); PlaceArm(m_RightArmHitbox, 1);
        }

        public bool RaycastShot(Ray ray,float range,int mask,out RaycastHit nearest)
        {
            var physics=gameObject.scene.GetPhysicsScene();
            int count;
            while((count=physics.Raycast(ray.origin,ray.direction,s_ShotHits,range,mask,QueryTriggerInteraction.Ignore))==s_ShotHits.Length)
            {
                // Do not select an arbitrary target if an unusually dense scene fills the buffer.
                if(s_ShotHits.Length>=4096){nearest=default;return false;}
                System.Array.Resize(ref s_ShotHits,s_ShotHits.Length*2);
            }
            nearest=default;float distance=float.PositiveInfinity;bool found=false;
            for(int i=0;i<count;i++)
            {
                var hit=s_ShotHits[i];
                if(!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance>=distance)continue;
                nearest=hit;distance=hit.distance;found=true;
            }
            return found;
        }

        public CinemachineCamera GetPlayerCinemachineCamera()
        {
            return m_CinemachineCamera;
        }

        public CinemachinePositionComposer GetPositionComposer()
        {
            return m_PositionComposer;
        }

        public struct PlayerData : IComponentData
        {
            [GhostField] public FixedString128Bytes Name;
            public Entity ViewEntity;
            public Entity ControlledEntity;
        }

        public void Awake()
        {
            GetRequiredComponent(out m_Controller);
            m_ReticleVector = ReticlePoint.localPosition;
        }
        
        private void LateUpdate()
        {
            // This logic is for visual clients only
            if ((Role != MultiplayerRole.ClientProxy && Role != MultiplayerRole.ClientOwned) || m_Animator3P == null)
            {
                return;
            }

            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;
            
            // matches vertical angle value in ClientInputReaderSystem
            float normalizedPitch = controllerState.PitchDegrees / 85f;
            
            m_Animator3P.SetFloat(AimPitchHash, normalizedPitch);
        }

        public override void OnGhostLinked()
        {
            // Host server and client replicas share a PhysX scene. Hitboxes on the
            // other timeline must never push this movement controller during prediction.
            Controller.CharacterController.excludeLayers |= Role == MultiplayerRole.Server
                ? LayerMask.GetMask("ClientPlayer")
                : LayerMask.GetMask("ServerPlayer");
            CreateHeadHitbox(Role==MultiplayerRole.Server ? (int)LayerIndex.ServerPlayer : (int)LayerIndex.ClientPlayer);
            bool isClientOwned = (Role == MultiplayerRole.ClientOwned);
            m_OwnerVisuals.SetActive(isClientOwned);
            m_OtherPlayerVisuals.SetActive(!isClientOwned);

            if (Role != MultiplayerRole.Server)
            {
                _animatorCharacter = GetComponent<Animator>();
                // Our own deployment gets the layered confirmation cue (success gong + the
                // android acknowledging the order); other players arriving keep the spawn cue.
                var spawnSfx = isClientOwned ? MoonkovAudio.Library?.DeployConfirm : m_SpawnSFX;
                if (spawnSfx != null)
                {
                    GameManager.Instance.SoundSystem.CreateEmitter(spawnSfx, transform.position);
                }
            }

            if (isClientOwned)
            {
                var predictedPlayer = ReadGhostComponentData<PredictedPlayerGhost>();
                PlayerIndex = predictedPlayer.InputIndex;
                // create camera
                CreateClientCamera();

                // Move the AudioListener to the player model instead of the camera, suppressing
                // the others. This has to be undone in RestoreAudioListeners: the listener lived
                // on m_OwnerVisuals, so dying or extracting destroyed it while every other
                // listener stayed disabled, and Unity plays nothing at all without an enabled
                // AudioListener in the scene.
                m_AddedListener = m_OwnerVisuals.AddComponent<AudioListener>();
                m_SuppressedListeners.Clear();
                foreach (var a in Resources.FindObjectsOfTypeAll<AudioListener>())
                {
                    if (a == null || a == m_AddedListener || !a.enabled) continue;
                    if (!a.gameObject.scene.IsValid()) continue;   // skip prefab/asset contents
                    m_SuppressedListeners.Add(a);
                    a.enabled = false;
                }

                // Attach the listener to the player model rather than the camera
                GameManager.Instance.SoundSystem.SetListenerTransform(m_OwnerVisuals.transform);
            }
            else if (Role == MultiplayerRole.ClientProxy)
            {
                // no physics required
                Controller.CharacterController.enabled = false;
            }

            gameObject.layer = (Role == MultiplayerRole.Server)
                ? (int)LayerIndex.ServerPlayer
                : (int)LayerIndex.ClientPlayer;

            PlayerGhostManager.TryGetInstanceByRole(Role, out var playerManager);
            playerManager.Register(this);
        }

        public override void OnGhostPreDestroy()
        {
            RestoreAudioListeners();
            if (PlayerGhostManager.TryGetInstanceByRole(Role, out var playerManager))
            {
                playerManager.Unregister(this);
            }
        }

        /// <summary>
        /// Puts the AudioListeners back the way they were before this client took ownership.
        /// Without it, the listener that lived on the owner visuals dies with the ghost while
        /// every other listener stays disabled, which silences the death screen and the ship
        /// after extraction. The camera listener is also re-enabled explicitly, because Unity
        /// produces no sound at all when no enabled AudioListener exists.
        /// </summary>
        private void RestoreAudioListeners()
        {
            if (m_AddedListener == null && m_SuppressedListeners.Count == 0) return;

            foreach (var listener in m_SuppressedListeners)
            {
                if (listener != null) listener.enabled = true;
            }
            m_SuppressedListeners.Clear();
            m_AddedListener = null;

            var camera = MainCameraSingleton.Instance;
            var fallback = camera != null ? camera.GetComponent<AudioListener>() : null;
            if (fallback != null)
            {
                fallback.enabled = true;
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.SoundSystem.SetListenerTransform(fallback.transform);
                }
            }
        }

        void AttachPlayerViewCamera()
        {
            var playerCamera = GameObject.FindAnyObjectByType<Camera>();
            if (playerCamera != null)
            {
                playerCamera.transform.parent = transform.Find("ViewPoint");
                playerCamera.transform.localPosition = Vector3.zero;
                playerCamera.transform.localRotation = Quaternion.identity;
            }
        }

        private void CreateClientCamera()
        {
            //Disable current camera
            var existingCamera = FindAnyObjectByType<Camera>();
            if (existingCamera != null)
            {
                existingCamera.enabled = false;
            }

            if (TryGetComponent<DollSingerNetworkPresentation>(out var dollSinger))
            {
                m_PlayerCamera = dollSinger.ActivateOwnedView();
                Utils.SetCursorVisible(false);
                return;
            }

            // spawn the camera
            var mainCameraInstance = Instantiate(MainCameraPrefab, CameraTarget.transform);
            mainCameraInstance.transform.localPosition = Vector3.zero;
            mainCameraInstance.name = $"MainCamera_{PlayerIndex}";

            m_PlayerCamera = mainCameraInstance.GetComponent<Camera>();

            var audioListener = mainCameraInstance.GetComponent<AudioListener>();
            if (audioListener != null)
            {
                GameManager.Instance.SoundSystem.SetListenerTransform(audioListener.transform);
            }

            Utils.SetCursorVisible(false);
        }

        public void UpdateServer(float deltaTime)
        {
            UpdateHeadHitbox();
        }

        public void UpdateClient(float deltaTime)
        {
            UpdateHeadHitbox();
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;
            if (Role == MultiplayerRole.ClientOwned)
            {
                CameraTarget.transform.rotation = Quaternion.Euler(controllerState.PitchDegrees,
                    Camera.main.transform.rotation.eulerAngles.y,
                    Camera.main.transform.rotation.eulerAngles.z);
            }

            var rot = Quaternion.Euler(controllerState.PitchDegrees, 0.0f, 0.0f);
            ReticlePoint.localPosition = rot * m_ReticleVector;

            //TODO: The following is a temporary fix for animation root moves (Robot Jump for example)
            m_OtherPlayerVisuals.transform.localPosition = Vector3.zero;
            m_OtherPlayerVisuals.transform.localRotation = Quaternion.identity;
        }

        public bool SetPlayerPositionFromRPC(float3 rpcPosition, float positionErrorSq)
        {
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;

            float positionError = math.distancesq(controllerState.CurrentPosition, rpcPosition);

            //allow the current player position to be altered by the client but only within a certain tolerance
            //(this is to avoid sliding during some position locked animations caused by the player predicting ahead of the server)
            if (positionError <= (positionErrorSq))
            {
                controllerState.CurrentPosition = rpcPosition;
                predictedPlayerGhost.ControllerState = controllerState;
                WriteGhostComponentData(predictedPlayerGhost);

                return true;
            }

            return false;
        }
    }
}
