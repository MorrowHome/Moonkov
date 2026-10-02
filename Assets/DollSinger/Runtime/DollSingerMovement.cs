using System;
using UnityEngine;
using UnityEngine.InputSystem;
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

        public AudioClip m_LandingAudioClip;
        public AudioClip[] m_FootstepAudioClips;
        [Range(0, 1)] public float m_FootstepAudioVolume = 0.5f;

        [Space(10)] [Tooltip("The height the player can jump")]
        public float m_JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float m_Gravity = -15.0f;

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
        [Tooltip("How far the camera slides sideways at full lean, in metres. Lets you peek past cover.")]
        [Range(0f, 0.6f)] public float m_LeanShift = 0.22f;
        [Tooltip("Upper body roll as a fraction of the camera roll.")]
        [Range(0f, 1f)] public float m_BodyLeanRatio = 0.65f;
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
        private float m_LeanLockedSide = 0f;
        private Transform m_ChestBone;
        private bool m_ChestBoneSearched;

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
        private float m_HeadLookWeight;

        // Out
        public float CurrentSpeed => m_Controller == null ? 0f :
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
        /// <summary>Sideways camera slide along the character's right axis, in metres. Negative = to the left.</summary>
        public float LeanShift => m_LeanState * m_LeanShift;
        /// <summary>Upper-body roll about the character's forward axis. Same sign convention as <see cref="LeanRoll"/>.</summary>
        public float BodyLeanRoll => -m_LeanState * m_LeanAngle * m_BodyLeanRatio;
        /// <summary>True while a lean is latched with Alt+Q / Alt+E.</summary>
        public bool IsLeanLocked => m_LeanLockedSide != 0f;
        public bool IsFirstPersonView {
            get => m_IsFirstPersonView;
            set {
                m_IsFirstPersonView = value;
                m_BodyTurningToLook = false;
                m_RotationVelocity = 0f;
                if (value) return;
                m_FreeLookYaw = 0f;
                m_FreeLookPitch = 0f;
                m_FreeLookHeld = false;
            }
        }
        public DollSingerInput input;
        public DollSingerView view;

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
                }
            }
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
            IsLocallyAiming = input && input.AimHeld;
            ReadInput();
            m_HeadLookWeight = Mathf.MoveTowards(m_HeadLookWeight,
                IsFirstPersonView ? 1f : 0f, Time.deltaTime * 6f);
            JumpedThisFrame = false;
            JumpAndGravity();
            Move();
            GroundedCheck();
        }

        private void LateUpdate() {
            m_LeanState = Mathf.MoveTowards(m_LeanState, m_LeanTarget, Time.deltaTime * m_LeanSpeed);
            CameraRotation();
            ApplyBodyLean();
        }

        // There is no lean animation on this rig, so the upper-body roll is applied on top
        // of the animated pose. LateUpdate runs after the Animator has written the bones.
        private void ApplyBodyLean() {
            if (!m_HasAnimator || !m_Animator.isHuman) return;
            if (!m_ChestBoneSearched) {
                m_ChestBone = m_Animator.GetBoneTransform(HumanBodyBones.UpperChest)
                              ?? m_Animator.GetBoneTransform(HumanBodyBones.Chest);
                m_ChestBoneSearched = true;
            }
            if (!m_ChestBone) return;
            float roll = BodyLeanRoll;
            if (Mathf.Abs(roll) < 0.01f) return;
            // Roll around the character's forward axis, not the bone's local one — MMD
            // bone axes are arbitrary and would tilt the wrong way.
            m_ChestBone.rotation = Quaternion.AngleAxis(roll, transform.forward) * m_ChestBone.rotation;
        }

        private void OnAnimatorIK(int layerIndex) {
            if (layerIndex != 0 || !m_HasAnimator || !m_Animator.isHuman) return;
            // clampWeight 0 = no restriction. In first person the head has to track the
            // camera exactly, otherwise the hair that frames the view drifts off screen.
            m_Animator.SetLookAtWeight(m_HeadLookWeight, 0f, 1f, 0f, 0f);
            if (m_HeadLookWeight <= 0.001f) return;
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
            if (!canRead) { m_LeanTarget = 0f; return; }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool altHeld = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
            if (canRead && altHeld && keyboard.qKey.wasPressedThisFrame)
                m_LeanLockedSide = m_LeanLockedSide < 0f ? 0f : -1f;
            else if (canRead && altHeld && keyboard.eKey.wasPressedThisFrame)
                m_LeanLockedSide = m_LeanLockedSide > 0f ? 0f : 1f;

            float held = 0f;
            if (canRead && !altHeld) {
                if (keyboard.qKey.isPressed) held = -1f;
                else if (keyboard.eKey.isPressed) held = 1f;
            }
            // A held key wins over the latch, so tapping Q/E still works while locked and
            // returns to the latched angle on release.
            m_LeanTarget = held != 0f ? held : m_LeanLockedSide;
        }

        private void ReadInput() {
            bool canRead = input && input.CanReadPlayerInput;
            m_MoveInput = canRead ? input.Move : Vector2.zero;
            m_JumpInput = canRead && input.JumpPressed;
            m_SprintInput = canRead && input.SprintHeld;
            m_FreeLookHeld = canRead && IsFirstPersonView && !IsLocallyAiming &&
                             Keyboard.current != null && Keyboard.current.leftAltKey.isPressed;
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
                if (m_FreeLookHeld) {
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
            if (m_HasMoveParameter) m_Animator.SetBool("Move", CurrentSpeed > 0.05f);
            if (m_HasMotionSpeedParameter) {
                // As a Float this multiplies the locomotion state's playback speed;
                // leaving it at 0 freezes the run cycle mid-pose. Keep natural rate.
                if (m_MotionSpeedIsFloat) m_Animator.SetFloat(k_AnimIDMotionSpeed, 1f);
                else m_Animator.SetInteger(k_AnimIDMotionSpeed, inputMagnitude > 0.01f ? 1 : 0);
            }
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
