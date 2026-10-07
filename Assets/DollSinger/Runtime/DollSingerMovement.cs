using System;
using UnityEngine;
using Random = UnityEngine.Random;

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

namespace Unity.MP_FPS.DollSinger {
    [RequireComponent(typeof(CharacterController))]
    public class DollSingerMovement : MonoBehaviour {
        [Header("Player")] [Tooltip("Move speed of the character in m/s")]
        public float m_MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float m_SprintSpeed = 5.335f;

        [Tooltip("How fast the character turns to face movement direction")] [Range(0.0f, 0.3f)]
        public float m_RotationSmoothTime = 0.12f;

        [Tooltip("How quickly the movement animation follows actual speed")]
        public float m_SpeedChangeRate = 10.0f;

        [Header("Turn in place")]
        [Tooltip("Ignore small rotation corrections below this angular speed, in degrees/second.")]
        [Min(1f)] public float m_TurnMinAngularSpeed = 12f;
        [Tooltip("Use walking animations instead when horizontal speed exceeds this value.")]
        [Min(0f)] public float m_TurnStationarySpeed = 0.15f;

        public AudioClip m_LandingAudioClip;
        public AudioClip[] m_FootstepAudioClips;
        [Range(0, 1)] public float m_FootstepAudioVolume = 0.5f;

        [Space(10)] [Tooltip("The height the player can jump")]
        public float m_JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float m_Gravity = -1.62f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float m_JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float m_FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool m_Grounded = true;

        [Tooltip("Useful for rough ground")] public float m_GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float m_GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask m_GroundLayers;

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject m_CinemachineCameraTarget;

        [Tooltip("How far in degrees can you move the camera up")]
        public float m_TopClamp = 70.0f;

        [Tooltip("How far in degrees can you move the camera down")]
        public float m_BottomClamp = -30.0f;

        [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
        public float m_CameraAngleOverride = 0.0f;

        [Tooltip("For locking the camera position on all axis")]
        public bool m_LockCameraPosition = false;

        [Header("First-person free look")]
        [Range(15f, 100f)] public float m_FreeLookYawLimit = 70f;
        [Range(10f, 80f)] public float m_FreeLookPitchLimit = 45f;
        [Min(30f)] public float m_FreeLookReturnSpeed = 240f;

        [Header("First-person head turn")]
        [Range(5f, 80f)] public float m_HeadTurnBeforeBody = 35f;
        [Range(0.05f, 0.5f)] public float m_FirstPersonBodyTurnTime = 0.18f;

        [Header("Lean")]
        [Tooltip("Camera roll in degrees at full lean.")]
        [Range(5f, 35f)] public float m_LeanAngle = 15f;
        [Tooltip("Total waist/chest bend at full lean. Eye displacement comes from the skeleton's arc.")]
        [Range(5f, 45f)] public float m_BodyLeanAngle = 42f;
        // Legacy serialized settings; fixed translation is no longer applied to any bone.
        [HideInInspector] public float m_LeanShift = 0.22f;
        [HideInInspector] public float m_BodyLeanRatio = 0.65f;
        [Tooltip("How fast the lean eases in and out, in units per second.")]
        [Min(0.5f)] public float m_LeanSpeed = 6f;
        [Tooltip("Movement speed multiplier at full lean.")]
        [Range(0.3f, 1f)] public float m_LeanMoveSpeedScale = 0.72f;

        // cinemachine
        private float m_CinemachineTargetYaw;
        private float m_CinemachineTargetPitch;
        private float m_FreeLookYaw;
        private float m_FreeLookPitch;
        private bool m_IsFirstPersonView;
        private bool m_FreeLookHeld;
        private bool m_BodyTurningToLook;
        private float m_LeanState = 0f;
        private float m_LeanTarget = 0f;
        private Transform m_ChestBone;
        private Transform m_SpineBone;
        private Transform m_HeadBone;
        private bool m_ChestBoneSearched;
        private Quaternion m_NetworkHeadRotation;
        private Vector3 m_SpineToChest;
        private Vector3 m_ChestToHead;
        private Vector3 m_AppliedLeanOffset;
        private static readonly RaycastHit[] s_LeanHits = new RaycastHit[32];

        // player
        private float m_Speed;
        private float m_AnimationBlend;
        private float m_TargetRotation = 0.0f;
        private float m_RotationVelocity;
        private float m_VerticalVelocity;
        private const float k_TerminalVelocity = 53.0f;

        // timeout deltatime
        private float m_JumpTimeoutDelta;
        private float m_FallTimeoutDelta;

        // animation IDs
        private static readonly int k_AnimIDSpeed = Animator.StringToHash("Speed");
        private static readonly int k_AnimIDGrounded = Animator.StringToHash("Grounded");
        private static readonly int k_AnimIDJump = Animator.StringToHash("Jump");
        private static readonly int k_AnimIDFreeFall = Animator.StringToHash("FreeFall");
        private static readonly int k_AnimIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        private static readonly int k_AnimIDMoveX = Animator.StringToHash("MoveX");
        private static readonly int k_AnimIDMoveZ = Animator.StringToHash("MoveZ");
        private Animator m_Animator;
        private CharacterController m_Controller;
        private GameObject m_MainCamera;
        private const float k_Threshold = 0.01f;
        private bool m_HasAnimator;
        private Vector2 m_MoveInput;
        private Vector2 m_LookInput;
        private bool m_JumpInput;
        private bool m_SprintInput;
        private bool m_HasSpeedParameter;
        private bool m_HasMoveParameter;
        private bool m_HasGroundedParameter;
        private bool m_HasFreeFallParameter;
        private bool m_HasMotionSpeedParameter;
        private bool m_MotionSpeedIsFloat;
        private bool m_HasJumpParameter;
        private bool m_HasMoveXParameter, m_HasMoveZParameter;
        private Vector2 m_LastMoveDirection = Vector2.up;
        private static readonly int k_AnimIDTurnDirection = Animator.StringToHash("TurnDirection");
        private static readonly int k_AnimIDTurnPlayback = Animator.StringToHash("TurnPlayback");
        private static readonly int k_AnimIDTurnState = Animator.StringToHash("Turn In Place.Turn Steps");
        private int m_TurnLayer = -1;
        private bool m_HasTurnParameters;
        private bool m_HasBodyYawSample;
        private float m_PreviousBodyYaw, m_TurnWeight, m_TurnHoldTime, m_TurnDirection;
        private float m_HeadLookWeight;

        // Out
        private bool m_NetworkDriven;
        private float m_NetworkSpeed;
        public float CurrentSpeed => m_NetworkDriven ? m_NetworkSpeed : m_Controller == null ? 0f :
            new Vector3(m_Controller.velocity.x, 0f, m_Controller.velocity.z).magnitude;
        public float VerticalVelocity => m_VerticalVelocity;
        public bool IsGrounded => m_Grounded;
        public bool IsSprinting => m_SprintInput;
        public bool JumpedThisFrame { get; private set; }
        // The local halo prototype uses this to face the camera while strafing.
        public bool IsLocallyAiming { get; set; }
        /// <summary>
        /// Camera roll to apply about the view axis, in degrees. Positive tips the top of the
        /// view toward the character's left, i.e. a Q lean. The sign is inverted from
        /// <see cref="m_LeanState"/> because Unity's positive Z rotation tips "up" toward -X.
        /// </summary>
        public float LeanRoll => -m_LeanState * m_LeanAngle;
        /// <summary>Actual sideways travel produced by waist/chest rotation.</summary>
        public float LeanShift => Vector3.Dot(LeanOffset, transform.right);
        public float LeanAmount => IsFirstPersonView ? m_LeanState : 0f;
        public Vector3 LeanOffset => IsFirstPersonView ? m_AppliedLeanOffset : Vector3.zero;

        public Vector3 GetLeanOffset(float amount, Quaternion bodyRotation)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || !EnsureLeanBones()) return Vector3.zero;
            amount = Mathf.Clamp(amount, -1f, 1f);
            float roll = -amount * Mathf.Clamp(m_BodyLeanAngle, 5f, 45f);
            float waistWeight = m_ChestBone ? 0.75f : 1f;
            Quaternion waist = Quaternion.AngleAxis(roll * waistWeight, Vector3.forward);
            Quaternion total = Quaternion.AngleAxis(roll, Vector3.forward);
            Vector3 offset = waist * m_SpineToChest + total * m_ChestToHead
                - m_SpineToChest - m_ChestToHead;
            return bodyRotation * offset;
        }

        private bool EnsureLeanBones()
        {
            if (m_ChestBoneSearched) return m_SpineBone;
            var animator = m_Animator ? m_Animator : GetComponent<Animator>();
            if (!animator || !animator.isHuman) return false;
            m_SpineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
            m_ChestBone = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            m_HeadBone = animator.GetBoneTransform(HumanBodyBones.Head);
            m_ChestBoneSearched = true;
            CaptureLeanGeometry();
            return m_SpineBone;
        }

        private void CaptureLeanGeometry()
        {
            if (!m_SpineBone) return;
            Transform chest = m_ChestBone ? m_ChestBone : m_SpineBone;
            Transform head = m_HeadBone ? m_HeadBone : chest;
            Quaternion inverseBody = Quaternion.Inverse(transform.rotation);
            m_SpineToChest = inverseBody * (chest.position - m_SpineBone.position);
            m_ChestToHead = inverseBody * (head.position - chest.position);
        }

        public static Vector3 ConstrainLeanOffset(Vector3 origin, Vector3 offset, Transform owner, int layers)
        {
            float distance = offset.magnitude;
            if (distance < 0.0001f) return Vector3.zero;
            int count = Physics.SphereCastNonAlloc(origin, 0.045f, offset / distance, s_LeanHits,
                distance, layers, QueryTriggerInteraction.Ignore);
            // Be conservative if a crowded query overflows the fixed buffer.
            if (count == s_LeanHits.Length) return Vector3.zero;
            float allowed = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = s_LeanHits[i];
                if (!hit.collider || (owner && hit.collider.transform.IsChildOf(owner))) continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.01f));
            }
            return offset * (allowed / distance);
        }
        /// <summary>Upper-body roll about the character's forward axis. Same sign convention as <see cref="LeanRoll"/>.</summary>
        public float BodyLeanRoll => -m_LeanState * Mathf.Clamp(m_BodyLeanAngle, 5f, 45f);
        /// <summary>True while a lean is latched with Alt+Q / Alt+E.</summary>
        public bool IsLeanLocked => input && input.IsLeanLocked;
        public bool IsFreeLooking => m_FreeLookHeld ||
            Mathf.Abs(m_FreeLookYaw) > 0.1f || Mathf.Abs(m_FreeLookPitch) > 0.1f;
        public bool IsFirstPersonView {
            get => m_IsFirstPersonView;
            set {
                m_IsFirstPersonView = value;
                if (input) input.SetLeanEnabled(value);
                m_BodyTurningToLook = false;
                m_RotationVelocity = 0f;
                if (value) return;
                m_LeanState = m_LeanTarget = 0f;
                m_AppliedLeanOffset = Vector3.zero;
                m_FreeLookYaw = 0f;
                m_FreeLookPitch = 0f;
                m_FreeLookHeld = false;
            }
        }
        public DollSingerInput input;
        public DollSingerView view;

        public void SetNetworkLeanAmount(float lean)
        {
            m_LeanState = m_LeanTarget = Mathf.Clamp(lean, -1f, 1f);
        }

        // Network prediction owns the root CharacterController. Keep this component
        // enabled for Animator IK and LateUpdate, but skip local movement simulation.
        public void SetNetworkViewPresentation(bool firstPersonView, Quaternion headRotation, float lean)
        {
            m_NetworkDriven = true;
            m_IsFirstPersonView = firstPersonView;
            if (input) input.SetLeanEnabled(firstPersonView);
            m_NetworkHeadRotation = headRotation;
            SetNetworkLeanAmount(firstPersonView ? lean : 0f);
            m_HeadLookWeight = Mathf.MoveTowards(m_HeadLookWeight,
                firstPersonView ? 1f : 0f, Time.deltaTime * 6f);
        }

        public void ApplyNetworkPresentation(float speed, float verticalVelocity, bool grounded,
            bool sprinting, bool jumped, float deltaTime, Vector3 worldMovement = default,
            float? bodyYaw = null)
        {
            m_NetworkDriven = true;
            m_NetworkSpeed = speed;
            m_VerticalVelocity = verticalVelocity;
            m_Grounded = grounded;
            m_SprintInput = sprinting;
            JumpedThisFrame = jumped;
            if (!m_HasAnimator) return;
            // Start promptly, but blend deceleration so a zero-speed snapshot does not snap the feet to idle.
            m_AnimationBlend = speed >= m_AnimationBlend ? speed : Mathf.Lerp(m_AnimationBlend, speed,
                1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) * Mathf.Max(0f, m_SpeedChangeRate)));
            if (m_AnimationBlend < 0.01f) m_AnimationBlend = 0f;
            if (m_HasSpeedParameter) m_Animator.SetFloat(k_AnimIDSpeed, m_AnimationBlend);
            ApplyDirectionalAnimation(worldMovement, deltaTime);
            ApplyTurnAnimation(bodyYaw ?? transform.eulerAngles.y, m_AnimationBlend, grounded && !jumped, deltaTime);
            if (m_HasMoveParameter) m_Animator.SetBool("Move", m_AnimationBlend > 0.05f);
            if (m_HasGroundedParameter) m_Animator.SetBool(k_AnimIDGrounded, grounded);
            if (m_HasFreeFallParameter) m_Animator.SetBool(k_AnimIDFreeFall, !grounded && verticalVelocity < 0f);
            if (m_HasJumpParameter && jumped) m_Animator.SetTrigger(k_AnimIDJump);
            if (m_HasMotionSpeedParameter)
            {
                if (m_MotionSpeedIsFloat) m_Animator.SetFloat(k_AnimIDMotionSpeed, 1f);
                else m_Animator.SetInteger(k_AnimIDMotionSpeed, m_AnimationBlend > 0.01f ? 1 : 0);
            }
        }

        private void Awake() {
            m_Controller = GetComponent<CharacterController>();
            m_HasAnimator = TryGetComponent(out m_Animator);
            if (m_HasAnimator) {
                foreach (var parameter in m_Animator.parameters) {
                    if (parameter.nameHash == k_AnimIDSpeed && parameter.type == AnimatorControllerParameterType.Float)
                        m_HasSpeedParameter = true;
                    if (parameter.name == "Move" && parameter.type == AnimatorControllerParameterType.Bool)
                        m_HasMoveParameter = true;
                    if (parameter.nameHash == k_AnimIDGrounded && parameter.type == AnimatorControllerParameterType.Bool)
                        m_HasGroundedParameter = true;
                    if (parameter.nameHash == k_AnimIDFreeFall && parameter.type == AnimatorControllerParameterType.Bool)
                        m_HasFreeFallParameter = true;
                    if (parameter.nameHash == k_AnimIDMotionSpeed &&
                        (parameter.type == AnimatorControllerParameterType.Float ||
                         parameter.type == AnimatorControllerParameterType.Int)) {
                        m_HasMotionSpeedParameter = true;
                        m_MotionSpeedIsFloat = parameter.type == AnimatorControllerParameterType.Float;
                    }
                    if (parameter.nameHash == k_AnimIDJump && parameter.type == AnimatorControllerParameterType.Trigger)
                        m_HasJumpParameter = true;
                    if (parameter.nameHash == k_AnimIDMoveX && parameter.type == AnimatorControllerParameterType.Float)
                        m_HasMoveXParameter = true;
                    if (parameter.nameHash == k_AnimIDMoveZ && parameter.type == AnimatorControllerParameterType.Float)
                        m_HasMoveZParameter = true;
                }
                m_TurnLayer = m_Animator.GetLayerIndex("Turn In Place");
                bool hasDirection = false, hasPlayback = false;
                foreach (var parameter in m_Animator.parameters) {
                    if (parameter.type != AnimatorControllerParameterType.Float) continue;
                    hasDirection |= parameter.nameHash == k_AnimIDTurnDirection;
                    hasPlayback |= parameter.nameHash == k_AnimIDTurnPlayback;
                }
                m_HasTurnParameters = hasDirection && hasPlayback;
            }
        }

        private void OnEnable() => ResetTurnAnimation();
        private void OnDisable()
        {
            ResetTurnAnimation();
            m_LeanState = m_LeanTarget = 0f;
            m_AppliedLeanOffset = Vector3.zero;
            m_FreeLookYaw = m_FreeLookPitch = m_HeadLookWeight = 0f;
            m_FreeLookHeld = false;
        }

        private void ResetTurnAnimation()
        {
            m_HasBodyYawSample = false;
            m_TurnWeight = m_TurnHoldTime = m_TurnDirection = 0f;
            if (m_Animator && m_TurnLayer >= 0) m_Animator.SetLayerWeight(m_TurnLayer, 0f);
        }

        private void Start() {
            // reset our timeouts on start
            m_JumpTimeoutDelta = m_JumpTimeout;
            m_FallTimeoutDelta = m_FallTimeout;
            if (m_MainCamera == null && Camera.main != null) m_MainCamera = Camera.main.gameObject;
            if (m_CinemachineCameraTarget != null)
                m_CinemachineTargetYaw = m_CinemachineCameraTarget.transform.eulerAngles.y;
        }

        private void Update() {
            if (m_NetworkDriven) return;
            IsLocallyAiming = input && input.AimHeld;
            ReadInput();
            CameraRotation();
            m_LeanState = IsFirstPersonView
                ? Mathf.MoveTowards(m_LeanState, m_LeanTarget, Time.deltaTime * m_LeanSpeed) : 0f;
            if (view && IsFirstPersonView) m_LeanState = view.LimitLeanAmount(m_LeanState);
            if (IsFreeLooking) IsLocallyAiming = false;
            m_HeadLookWeight = Mathf.MoveTowards(m_HeadLookWeight,
                IsFirstPersonView ? 1f : 0f, Time.deltaTime * 6f);
            JumpedThisFrame = false;
            JumpAndGravity();
            Move();
            GroundedCheck();
            if (m_HasAnimator)
                ApplyTurnAnimation(transform.eulerAngles.y, CurrentSpeed, m_Grounded && !JumpedThisFrame, Time.deltaTime);
        }

        private void LateUpdate() {
            m_AppliedLeanOffset = Vector3.zero;
            if (!IsFirstPersonView) { m_LeanState = m_LeanTarget = 0f; return; }
            ApplyBodyLean();
        }

        // There is no lean animation on this rig, so the upper-body roll is applied on top
        // of the animated pose. LateUpdate runs after the Animator has written the bones.
        private void ApplyBodyLean() {
            if (!IsFirstPersonView || !m_HasAnimator || !m_Animator.isHuman) return;
            if (!EnsureLeanBones()) return;
            CaptureLeanGeometry();
            float roll = BodyLeanRoll;
            if (Mathf.Abs(LeanAmount) < 0.0001f) return;
            Transform leanReference = m_HeadBone ? m_HeadBone : m_ChestBone ? m_ChestBone : m_SpineBone;
            Vector3 headBeforePosition = leanReference.position;
            Quaternion headBeforeLean = m_HeadBone ? m_HeadBone.rotation : Quaternion.identity;
            // Roll around the character's forward axis, not the bone's local one — MMD
            // bone axes are arbitrary and would tilt the wrong way.
            // Keep every bone's local position intact. The head moves along the arc of
            // the waist/chest joints; it is never translated to force a fixed peek distance.
            float waistWeight = m_ChestBone ? 0.75f : 1f;
            m_SpineBone.rotation = Quaternion.AngleAxis(roll * waistWeight, transform.forward) * m_SpineBone.rotation;
            if (m_ChestBone)
                m_ChestBone.rotation = Quaternion.AngleAxis(roll * 0.25f, transform.forward) * m_ChestBone.rotation;
            if (m_IsFirstPersonView && m_HeadBone)
            {
                // Keep yaw/pitch in world space after the torso bend; only roll follows lean.
                Quaternion look = m_NetworkDriven ? m_NetworkHeadRotation :
                    view ? view.transform.rotation : m_HeadBone.rotation;
                m_HeadBone.rotation = Quaternion.AngleAxis(LeanRoll, look * Vector3.forward) * headBeforeLean;
            }
            m_AppliedLeanOffset = leanReference.position - headBeforePosition;
        }

        private void OnAnimatorIK(int layerIndex) {
            if (layerIndex != 0 || !m_HasAnimator || !m_Animator.isHuman) return;
            // clampWeight 0 = no restriction. In first person the head has to track the
            // camera exactly, otherwise the hair that frames the view drifts off screen.
            m_Animator.SetLookAtWeight(m_HeadLookWeight, 0f, 1f, 0f, 0f);
            if (m_HeadLookWeight <= 0.001f) return;
            if (m_NetworkDriven)
            {
                Transform head = m_Animator.GetBoneTransform(HumanBodyBones.Head);
                if (head) m_Animator.SetLookAtPosition(head.position + m_NetworkHeadRotation * Vector3.forward * 12f);
                return;
            }
            var rig = view;
            if (rig != null) {
                m_Animator.SetLookAtPosition(rig.LookPoint);
                return;
            }
            if (m_MainCamera == null && Camera.main != null)
                m_MainCamera = Camera.main.gameObject;
            if (m_MainCamera != null)
                m_Animator.SetLookAtPosition(m_MainCamera.transform.position +
                    m_MainCamera.transform.forward * 10f);
        }

        public void BindCamera(DollSingerView camera) {
            if (camera == null) return;
            view = camera;
            m_CinemachineCameraTarget = camera.gameObject;
            m_MainCamera = camera.camera != null ? camera.camera.gameObject : camera.gameObject;
            m_CinemachineTargetYaw = camera.transform.eulerAngles.y;
            m_FreeLookYaw = m_FreeLookPitch = 0f;
        }

        // Tarkov scheme: hold Q/E to lean and spring back on release, Alt+Q/Alt+E to latch
        // the lean. Pressing the same Alt combo again unlatches and returns to upright;
        // the opposite Alt combo switches sides.
        private void ReadLeanInput(bool canRead) {
            m_LeanTarget = canRead && IsFirstPersonView ? input.LeanTarget : 0f;
        }

        private void ReadInput() {
            bool canRead = input && input.CanReadPlayerInput;
            m_MoveInput = canRead ? input.Move : Vector2.zero;
            m_JumpInput = canRead && input.JumpPressed;
            m_SprintInput = canRead && input.SprintHeld;
            m_FreeLookHeld = canRead && IsFirstPersonView && input.AltHeld;
            ReadLeanInput(canRead);
            m_LookInput = Vector2.zero;
            if (!canRead) return;
            m_LookInput = input.Look;
        }

        private void GroundedCheck() {
            // The result of Move excludes our own CharacterController collider.
            m_Grounded = m_Controller.isGrounded && m_VerticalVelocity <= 0f;

            // update animator if using character
            if (m_HasGroundedParameter) m_Animator.SetBool(k_AnimIDGrounded, m_Grounded);
        }

        private void CameraRotation() {
            // if there is an input and camera position is not fixed
            if (m_CinemachineCameraTarget == null) return;
            if (m_LookInput.sqrMagnitude >= k_Threshold && !m_LockCameraPosition) {
                if (m_FreeLookHeld) {
                    m_FreeLookYaw = Mathf.Clamp(m_FreeLookYaw + m_LookInput.x,
                        -m_FreeLookYawLimit, m_FreeLookYawLimit);
                    m_FreeLookPitch = Mathf.Clamp(m_FreeLookPitch - m_LookInput.y,
                        -m_FreeLookPitchLimit, m_FreeLookPitchLimit);
                } else {
                    m_CinemachineTargetYaw += m_LookInput.x;
                    m_CinemachineTargetPitch -= m_LookInput.y;
                }
            }

            if (!m_FreeLookHeld) {
                float returnStep = m_FreeLookReturnSpeed * Time.deltaTime;
                m_FreeLookYaw = Mathf.MoveTowards(m_FreeLookYaw, 0f, returnStep);
                m_FreeLookPitch = Mathf.MoveTowards(m_FreeLookPitch, 0f, returnStep);
            }

            // clamp our rotations so our values are limited 360 degrees
            m_CinemachineTargetYaw = ClampAngle(m_CinemachineTargetYaw, float.MinValue, float.MaxValue);
            m_CinemachineTargetPitch = ClampAngle(m_CinemachineTargetPitch, m_BottomClamp, m_TopClamp);

            // Cinemachine will follow this target
            m_CinemachineCameraTarget.transform.rotation =
                Quaternion.Euler(Mathf.Clamp(m_CinemachineTargetPitch + m_FreeLookPitch +
                        m_CameraAngleOverride, -80f, 80f),
                    m_CinemachineTargetYaw + m_FreeLookYaw, 0.0f);
        }

        private void Move() {
            // set target speed based on move speed, sprint speed and if sprint is pressed
            float targetSpeed = m_SprintInput ? m_SprintSpeed : m_MoveSpeed;

            // Apply movement immediately; smooth only the animation blend below.
            if (m_MoveInput == Vector2.zero) targetSpeed = 0.0f;
            float inputMagnitude = Mathf.Clamp01(m_MoveInput.magnitude);
            // Leaning costs speed, as in Tarkov. Slower is always safe against the server cap.
            float leanScale = Mathf.Lerp(1f, m_LeanMoveSpeedScale, Mathf.Abs(m_LeanState));
            m_Speed = targetSpeed * inputMagnitude * leanScale;
            // normalise input direction
            var inputDirection = new Vector3(m_MoveInput.x, 0.0f, m_MoveInput.y).normalized;

            // note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
            // if there is a move input rotate player when the player is moving
            if (IsFirstPersonView && m_CinemachineCameraTarget != null) {
                float yawToLook = Mathf.DeltaAngle(transform.eulerAngles.y, m_CinemachineTargetYaw);
                if (IsFreeLooking) {
                    m_BodyTurningToLook = false;
                    m_RotationVelocity = 0f;
                }
                else if (IsLocallyAiming || Mathf.Abs(yawToLook) > m_HeadTurnBeforeBody)
                    m_BodyTurningToLook = true;

                if (m_BodyTurningToLook) {
                    float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y,
                        m_CinemachineTargetYaw, ref m_RotationVelocity, m_FirstPersonBodyTurnTime);
                    transform.rotation = Quaternion.Euler(0f, rotation, 0f);
                    if (Mathf.Abs(Mathf.DeltaAngle(rotation, m_CinemachineTargetYaw)) < 1.5f)
                        m_BodyTurningToLook = false;
                }
                m_TargetRotation = transform.eulerAngles.y;
            } else if (IsLocallyAiming && m_CinemachineCameraTarget != null) {
                m_TargetRotation = m_CinemachineTargetYaw;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, m_TargetRotation,
                    ref m_RotationVelocity, m_RotationSmoothTime);
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            } else if (m_MoveInput != Vector2.zero) {
                m_TargetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg +
                                   (m_CinemachineCameraTarget != null ? m_CinemachineTargetYaw :
                                       m_MainCamera != null ? m_MainCamera.transform.eulerAngles.y : 0f);
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, m_TargetRotation,
                    ref m_RotationVelocity, m_RotationSmoothTime);

                // rotate to face input direction relative to camera position
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }
            var targetDirection = IsFirstPersonView
                ? transform.rotation * inputDirection
                : IsLocallyAiming && m_CinemachineCameraTarget != null
                    ? Quaternion.Euler(0f, m_CinemachineTargetYaw, 0f) * inputDirection
                    : Quaternion.Euler(0f, m_TargetRotation, 0f) * Vector3.forward;

            // move the player
            m_Controller.Move(targetDirection.normalized * (m_Speed * Time.deltaTime) +
                              new Vector3(0.0f, m_VerticalVelocity, 0.0f) * Time.deltaTime);

            // update animator if using character
            if (!m_HasAnimator) return;
            m_AnimationBlend = Mathf.Lerp(m_AnimationBlend, CurrentSpeed, Time.deltaTime * m_SpeedChangeRate);
            if (m_AnimationBlend < 0.01f) m_AnimationBlend = 0f;
            if (m_HasSpeedParameter) m_Animator.SetFloat(k_AnimIDSpeed, m_AnimationBlend);
            ApplyDirectionalAnimation(m_Controller.velocity, Time.deltaTime);
            if (m_HasMoveParameter) m_Animator.SetBool("Move", CurrentSpeed > 0.05f);
            if (m_HasMotionSpeedParameter) {
                // As a Float this multiplies the locomotion state's playback speed;
                // leaving it at 0 freezes the run cycle mid-pose. Keep natural rate.
                if (m_MotionSpeedIsFloat) m_Animator.SetFloat(k_AnimIDMotionSpeed, 1f);
                else m_Animator.SetInteger(k_AnimIDMotionSpeed, inputMagnitude > 0.01f ? 1 : 0);
            }
        }

        private void ApplyDirectionalAnimation(Vector3 worldMovement, float deltaTime)
        {
            if (!m_HasMoveXParameter || !m_HasMoveZParameter) return;
            Vector3 local = transform.InverseTransformDirection(worldMovement);
            var horizontal = new Vector2(local.x, local.z);
            if (horizontal.sqrMagnitude > 0.0001f) m_LastMoveDirection = horizontal.normalized;
            if (m_NetworkDriven)
            {
                m_Animator.SetFloat(k_AnimIDMoveX, m_LastMoveDirection.x * m_AnimationBlend);
                m_Animator.SetFloat(k_AnimIDMoveZ, m_LastMoveDirection.y * m_AnimationBlend);
            }
            else
            {
                m_Animator.SetFloat(k_AnimIDMoveX, m_LastMoveDirection.x * m_AnimationBlend, 0.06f, deltaTime);
                m_Animator.SetFloat(k_AnimIDMoveZ, m_LastMoveDirection.y * m_AnimationBlend, 0.06f, deltaTime);
            }
        }

        private void ApplyTurnAnimation(float bodyYaw, float speed, bool grounded, float deltaTime)
        {
            if (m_TurnLayer < 0 || !m_HasTurnParameters) return;
            float angle = m_HasBodyYawSample ? Mathf.DeltaAngle(m_PreviousBodyYaw, bodyYaw) : 0f;
            bool hadSample = m_HasBodyYawSample;
            m_PreviousBodyYaw = bodyYaw;
            m_HasBodyYawSample = true;

            // Only animate real stationary body rotation. Camera orbit, walking, airborne
            // motion and respawn/teleport discontinuities must not start a turn step.
            if (!hadSample || deltaTime <= 0f || deltaTime > 0.25f || Mathf.Abs(angle) > 45f ||
                !grounded || speed > m_TurnStationarySpeed)
            {
                m_TurnWeight = m_TurnHoldTime = 0f;
                m_Animator.SetLayerWeight(m_TurnLayer, 0f);
                return;
            }
            float angularSpeed = angle / deltaTime;
            if (Mathf.Abs(angularSpeed) >= m_TurnMinAngularSpeed)
            {
                float direction = Mathf.Sign(angularSpeed);
                if (m_TurnWeight <= 0.001f || direction != m_TurnDirection)
                    m_Animator.Play(k_AnimIDTurnState, m_TurnLayer, 0f);
                m_TurnDirection = direction;
                m_TurnHoldTime = 0.12f;
                m_Animator.SetFloat(k_AnimIDTurnDirection, direction);
                m_Animator.SetFloat(k_AnimIDTurnPlayback, Mathf.Clamp(Mathf.Abs(angularSpeed) / 100f, 0.65f, 2f));
            }
            else m_TurnHoldTime = Mathf.Max(0f, m_TurnHoldTime - deltaTime);
            float targetWeight = m_TurnHoldTime > 0f ? 1f : 0f;
            m_TurnWeight = Mathf.MoveTowards(m_TurnWeight, targetWeight,
                deltaTime / (targetWeight > m_TurnWeight ? 0.08f : 0.15f));
            m_Animator.SetLayerWeight(m_TurnLayer, m_TurnWeight);
        }

        private void JumpAndGravity() {
            if (m_Grounded) {
                // reset the fall timeout timer
                m_FallTimeoutDelta = m_FallTimeout;

                // update animator if using character
                if (m_HasFreeFallParameter) m_Animator.SetBool(k_AnimIDFreeFall, false);

                // stop our velocity dropping infinitely when grounded
                if (m_VerticalVelocity < 0.0f) { m_VerticalVelocity = -2f; }

                // Jump
                if (m_JumpInput && m_JumpTimeoutDelta <= 0.0f) {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    m_VerticalVelocity = Mathf.Sqrt(m_JumpHeight * -2f * m_Gravity);

                    // update animator if using character
                    JumpedThisFrame = true;
                    if (m_HasJumpParameter) m_Animator.SetTrigger(k_AnimIDJump);
                }

                // jump timeout
                if (m_JumpTimeoutDelta >= 0.0f) { m_JumpTimeoutDelta -= Time.deltaTime; }
            } else {
                // reset the jump timeout timer
                m_JumpTimeoutDelta = m_JumpTimeout;

                // fall timeout
                if (m_FallTimeoutDelta >= 0.0f) { m_FallTimeoutDelta -= Time.deltaTime; } else {
                    // update animator if using character
                    if (m_HasFreeFallParameter) m_Animator.SetBool(k_AnimIDFreeFall, true);
                }

                // if we are not grounded, do not jump
                m_JumpInput = false;
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (m_VerticalVelocity > -k_TerminalVelocity) { m_VerticalVelocity += m_Gravity * Time.deltaTime; }
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax) {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected() {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);
            Gizmos.color = m_Grounded ? transparentGreen : transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(
                new Vector3(transform.position.x, transform.position.y - m_GroundedOffset, transform.position.z),
                m_GroundedRadius);
        }

        private void OnFootstep(AnimationEvent animationEvent) {
            if (!(animationEvent.animatorClipInfo.weight > 0.5f)) return;
            if (m_FootstepAudioClips == null || m_FootstepAudioClips.Length == 0) return;
            int index = Random.Range(0, m_FootstepAudioClips.Length);
            AudioSource.PlayClipAtPoint(m_FootstepAudioClips[index], transform.TransformPoint(m_Controller.center),
                m_FootstepAudioVolume);
        }

        private void OnLand(AnimationEvent animationEvent) {
            if (animationEvent.animatorClipInfo.weight > 0.5f && m_LandingAudioClip != null) {
                AudioSource.PlayClipAtPoint(m_LandingAudioClip, transform.TransformPoint(m_Controller.center),
                    m_FootstepAudioVolume);
            }
        }
    }
}
