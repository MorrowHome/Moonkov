// 玩家共用的移动计算。把输入和 ControllerState 算成位移请求，再交给 Unity CharacterController 落地、撞墙和移动。
// 客户端预测与服务器都会用这套规则，远端角色用插值后的状态播放动画。
//#define DEBUG_RENDER_MOVEMENT
using System;
using Unity.MP_FPS.DollSinger;
using System.Runtime.CompilerServices;
using Unity.MP_FPS;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour {
    public SoundDef PlayerHitSFX;

    private static class AnimationParameters {
        //Speed, Strafe, Turn: float values
        //Verbs: triggers
        //Is-X: boolean values
        public static readonly int IsMoving = Animator.StringToHash("IsMoving"); //1P
        public static readonly int Speed = Animator.StringToHash("Speed"); //3P
        public static readonly int StrafeSpeed = Animator.StringToHash("SpeedStrafe"); //3P
        public static readonly int TurnSpeed = Animator.StringToHash("Turn"); //3P
        public static readonly int Shoot = Animator.StringToHash("Shoot");
        public static readonly int IsShooting = Animator.StringToHash("IsShooting");
        public static readonly int Reload = Animator.StringToHash("Reload");
        public static readonly int Jump = Animator.StringToHash("Jump");
        public static readonly int Fall = Animator.StringToHash("Fall");
        public static readonly int IsInAir = Animator.StringToHash("IsInAir");
        public static readonly int Land = Animator.StringToHash("Land");
        public static readonly int IsHit = Animator.StringToHash("IsHit");
        public static readonly int IsDead = Animator.StringToHash("IsDead"); //3P
    }

    private const float k_ResetMovementAdjustEpsilon = 1e-06f;

    public enum MovementType {
        Standing = 0,
        Jumping,
        Falling
    }

    // 这份可回滚状态记的是角色在某一拍的姿势、速度和动作。重算某一拍时先从旧状态出发，再重新读这一拍的输入。
    public struct ControllerState {
        public enum StateFlag {
            Jump = 1 << 0,
            Fall = 1 << 1,
            Land = 1 << 2,
            Shoot = 1 << 3,
            IsReloading = 1 << 4,
            IsHit = 1 << 5,
            JumpTrigger = 1 << 6,
            LandTrigger = 1 << 7,
            Aiming = 1 << 8,
            Sprinting = 1 << 9,
            HaloLightDisabled = 1 << 10,
            StanceLow = 1 << 11,
            StanceHigh = 1 << 12
        }

        //WARNING WARNING: Adding more members to this struct might break network serialisation speak to Claire/Andy B

        [GhostField(SendData = false)]
        public DollSingerStance Stance {
            get => (DollSingerStance)((StateFlags >> 11) & 3u);
            set => StateFlags = (StateFlags & ~(3u << 11)) | (((uint)value & 3u) << 11);
        }

        // booleans
        public uint StateFlags;
        public uint HaloLightingSettings;

        [GhostField(SendData = false)]
        public bool HaloLightEnabled {
            get => (StateFlags & (uint)StateFlag.HaloLightDisabled) == 0;
            set => SetFlag(StateFlag.HaloLightDisabled, !value);
        }

        [GhostField(SendData = false)]
        public bool Aiming {
            get => (StateFlags & (uint)StateFlag.Aiming) != 0;
            set => SetFlag(StateFlag.Aiming, value);
        }

        [GhostField(SendData = false)]
        public bool Sprinting {
            get => (StateFlags & (uint)StateFlag.Sprinting) != 0;
            set => SetFlag(StateFlag.Sprinting, value);
        }

        [GhostField(SendData = false)]
        public bool Jump { get => (StateFlags & (uint)StateFlag.Jump) != 0; set => SetFlag(StateFlag.Jump, value); }

        [GhostField(SendData = false)]
        public bool Fall { get => (StateFlags & (uint)StateFlag.Fall) != 0; set => SetFlag(StateFlag.Fall, value); }

        [GhostField(SendData = false)]
        public bool Land { get => (StateFlags & (uint)StateFlag.Land) != 0; set => SetFlag(StateFlag.Land, value); }

        [GhostField(SendData = false)]
        public bool Shoot { get => (StateFlags & (uint)StateFlag.Shoot) != 0; set => SetFlag(StateFlag.Shoot, value); }

        [GhostField(SendData = false)]
        public bool IsReloadingState {
            get => (StateFlags & (uint)StateFlag.IsReloading) != 0;
            set => SetFlag(StateFlag.IsReloading, value);
        }

        [GhostField(SendData = false)]
        public bool IsHit { get => (StateFlags & (uint)StateFlag.IsHit) != 0; set => SetFlag(StateFlag.IsHit, value); }

        [GhostField(SendData = false)]
        public bool JumpTriggered {
            get => (StateFlags & (uint)StateFlag.JumpTrigger) != 0;
            set => SetFlag(StateFlag.JumpTrigger, value);
        }

        [GhostField(SendData = false)]
        public bool LandTriggered {
            get => (StateFlags & (uint)StateFlag.LandTrigger) != 0;
            set => SetFlag(StateFlag.LandTrigger, value);
        }

        public quaternion CurrentRotation;
        public float3 CurrentPosition;
        public float3 GroundNormal;
        public float3 MovementRequest;
        public MovementType MovementType;
        public MovementType PreviousMovementType;
        public float TimeInState;
        public float YawDegrees;
        public float PitchDegrees;
        public float2 FreeLookOffset;
        public float Lean;
        public bool FirstPersonView;
        public bool FreeLooking;
        public float MovementSpeed;
        public float JumpFallSpeed;
        public float AnimatorTargetSpeed; // _animIDSpeed
        public float AnimatorTargetSpeedChangeRate;
        public float JumpTimeoutDelta;
        public float FallTimeoutDelta;
        public float FallHeight;
        public float RotationVelocity;
        public float3 AnimatorMotion;
        public float AnimatorMotionChangeRate;
        public float AnimatorSmoothedMotionX;
        public float AnimatorMotionSpeed; // _animIDMotionSpeed
        public float TeleportFreeze;

        // Nested fields are serialized by the generated PredictedPlayerGhost serializer.

        private void SetFlag(StateFlag flag, bool set) {
            if (set)
                StateFlags |= (uint)flag;
            else
                StateFlags &= ~(uint)flag;
        }

        public void Init(in float3 worldPosition, in quaternion worldRotation) {
            CurrentPosition = worldPosition;
            CurrentRotation = worldRotation;
            Quaternion rot = worldRotation;
            PitchDegrees = rot.eulerAngles.y;
        }
    }

    // 控制常量是角色的走跑速度、重力、碰撞层和动画参数。同样状态和输入要和同样常量配对，预测和服务器才容易一致。
    public struct ControllerConsts {
        public struct StateConsts {
            public float Speed;
            public float SpeedChangeRate;
            public float RotationSmoothTime;
            public float LandingSpeedMult;
            public float AnimationMotionScale;
        }

        public StateConsts Walk;
        public StateConsts Sprint;
        public float JumpHeight;
        public float Gravity;
        public float StandingFallSpeed;
        public float JumpTimeout;
        public float FallTimeout;
        public float LandingTimeout;
        public float LandingAnimTimeout;
        public float StateChangeSafetyTimeout;
        public float GroundedOffset;
        public LayerMask GroundLayers;
        public float TerminalVelocity;
    }

    [field: Header("Cinemachine")]
    [field: SerializeField]
    [field: Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
    public GameObject CinemachineCameraTarget { get; private set; }

    [SerializeField] [Tooltip("A small offset for the grounded spherecast. Should be a small positive value.")]
    private float GroundedOffset = 0.2f;

    public PhysicsMaterial GroundPhysicsMaterial { get; private set; }
    [field: SerializeField] public Vector3 ControllerOffset { get; private set; } = new(0f, 0f, 0f);
    [SerializeField] private Animator m_Animator_1P;
    [SerializeField] private Animator m_Animator_3P;
    [SerializeField] private bool m_EnableAnimationLogging = false;
    private DamageVisualsController m_DamageVisualsController;
    private uint _lastProcessedHitTick = 0;
    private uint _lastAnimatedShotTick = 0;
    private uint _lastAnimatedJumpTick = 0;
    private uint _lastAnimatedLandTick = 0;
    private uint _lastAnimatedReloadTick = 0;
    private CharacterController m_Controller;
    public CharacterController CharacterController => m_Controller;
    private float m_StandingHeight;
    private Vector3 m_StandingCenter;
    private PlayerGhost m_PlayerGhost;
    private PlayerGhost PlayerGhost => m_PlayerGhost;

    // Explicit opt-in for map previews: reuse the motor without creating a network world.
    [SerializeField] [HideInInspector] private bool m_StandalonePreview;

    // 无网络场景也可显式借用角色马达。正式角色则用已链接的 Ghost 识别自己在哪套物理世界。
    public void ConfigureStandalonePreview(FirstPersonController source) {
        m_StandalonePreview = true;
        GroundedOffset = source.GroundedOffset;
        ControllerOffset = source.ControllerOffset;
    }

    private const int k_NumPhysicsResults = 8;
    private readonly RaycastHit[] m_GroundCheckRaycastResults = new RaycastHit[k_NumPhysicsResults];
    private const float k_DefaultSpeedChange = 20f;
#if DEBUG_RENDER_MOVEMENT || DEBUG_RENDER_CLIMBING_MOVEMENT
    private const float k_DebugRenderingTimeout = 5f;
#endif
    private static readonly float3 k_UpVector = math.up();
    private static readonly float3 k_ForwardVector = math.forward();
    public float CachedJumpFallSpeed { get; private set; }
    public float CachedFallHeight { get; private set; }
#if UNITY_EDITOR || DEBUG
    private MovementType m_PrevMovementType;
#endif
    private float footstepTriggerTimer = 0;
    private float footstepStartTimer = 0;

    private void Awake() {
        TryGetComponent(out m_Controller);
        m_StandingHeight = m_Controller.height;
        m_StandingCenter = m_Controller.center;
        Debug.Assert(m_Controller, "[THIRDPERSONCONTROLLER] Player has no CharacterController component");
        TryGetComponent(out m_PlayerGhost);
        Debug.Assert(m_PlayerGhost || m_StandalonePreview,
            "[THIRDPERSONCONTROLLER] Player has no PlayerGhost component");
        TryGetComponent(out m_DamageVisualsController);
        Debug.Assert(m_DamageVisualsController || m_StandalonePreview,
            "[FIRSTPERSONCONTROLLER] Player has no DamageVisualsController component");
    }

    public void SetExcludeLayers(LayerMask excludeLayers) { m_Controller.excludeLayers = excludeLayers; }
    public float StanceHeightOffset => m_Controller.height - m_StandingHeight;

    public void ApplyStanceCapsule(DollSingerStance stance) {
        DollSingerStances.Apply(m_Controller, stance, m_StandingHeight, m_StandingCenter);
    }

    // 只给站在移动轿厢的人补一个高度差。跳起来后就跟随自己的速度，不能被电梯吸住。
    private float LiftCarryOffset(in ControllerState state) {
        if (state.MovementType != MovementType.Standing || !m_Controller || !m_Controller.enabled) return 0;
        var feet = (Vector3)state.CurrentPosition + (Quaternion)state.CurrentRotation * m_Controller.center -
                   Vector3.up * (m_Controller.height * .5f);
        return RaidLift.CarryHeight(feet);
    }

    public bool NeedsLiftCarry(in ControllerState state) { return LiftCarryOffset(state) != 0; }

    // 先前按输入累加的是位移请求。这里才通过碰撞实现实际移动，并更新网络状态要记录的位置。
    public void ApplyMovementUpdate(
        ref ControllerState state,
        in ControllerConsts consts,
        in float3 accumulatedMovement,
        float deltaTime) {
        // 碰撞前先确定胶囊高度和电梯随行偏移。角色下一步走不走得过去，得用新的身体尺寸来判断。
        DollSingerStances.Apply(m_Controller, state.Stance, m_StandingHeight, m_StandingCenter);
        state.CurrentPosition.y += LiftCarryOffset(state);
        ApplyMove(ref state, consts, accumulatedMovement, deltaTime);
        GroundedCheck(ref state, consts);

        // cache latest values for access outside of entity data
        CachedJumpFallSpeed = state.JumpFallSpeed;
        CachedFallHeight = state.FallHeight;
    }

    private static void SetMovementType(ref ControllerState state, MovementType type) {
        if (state.MovementType != type) {
#if ENABLE_MOVEMENT_DIAGNOSTICS
            Debug.Log($"SetMovementType to {(int)type} (from {(int)state.MovementType})");
#endif
            // 每次进入新的移动状态重新计时。开始下落时清零本次落差，后续只累计这一次掉下去的距离。
            bool wasUpdatingFallHeight = ShouldUpdateFallHeight(state.MovementType);
            state.PreviousMovementType = state.MovementType;
            state.MovementType = type;
            state.TimeInState = 0;
            if (!wasUpdatingFallHeight && ShouldUpdateFallHeight(type)) {
                state.FallHeight = 0f;
                state.FallTimeoutDelta = float.MaxValue;
                state.Fall = true;
            }
#if DEBUG_RENDER_MOVEMENT
            Debug.DrawLine(state.CurrentPosition - new float3(0.2f, 0f, 0f), 
                    state.CurrentPosition + new float3(0.2f, 0f, 0f), 
                    GetDebugColour(state.MovementType), k_DebugRenderingTimeout);
            Debug.DrawLine(state.CurrentPosition - new float3(0f, 0.2f, 0f), 
                    state.CurrentPosition + new float3(0f, 0.2f, 0f), 
                    GetDebugColour(state.PreviousMovementType), k_DebugRenderingTimeout);
#endif
        }
    }

    private static bool ShouldUpdateFallHeight(MovementType movementType) {
        return movementType == MovementType.Falling;
    }

    private struct GroundCollisionVariables {
        private RaycastHit m_ClosestHit;
        private RaycastHit m_FlattestHit;
        private readonly float m_FlattestHitDot;
        public Vector3 FlattestHitPoint => m_FlattestHit.point;
        public Vector3 FlattestHitNormal => m_FlattestHit.normal;
        public PhysicsMaterial ClosestHitSurfaceType => m_ClosestHit.collider.sharedMaterial;

        public GroundCollisionVariables(RaycastHit closestHit, RaycastHit flattestHit, float flattestHitDot) {
            m_ClosestHit = closestHit;
            m_FlattestHit = flattestHit;
            m_FlattestHitDot = flattestHitDot;
        }
    }

    private bool ShouldUpdateGround(MovementType movementType) { return movementType != MovementType.Jumping; }

    private static Vector3 GetGroundRaycastOrigin(in ControllerState state, in CharacterController controller) {
        var origin = state.CurrentPosition;
        var delta = origin - (float3)controller.bounds.center;
        delta.y = 0f;
        origin -= delta;
        return origin;
    }

    private bool UpdateGround(
        in ControllerState state,
        in ControllerConsts consts,
        out GroundCollisionVariables groundCollision) {
        if (ShouldUpdateGround(state.MovementType)) {
            var currentPos = GetGroundRaycastOrigin(state, m_Controller);
            // set sphere position, with offset
            var controllerCentre = transform.rotation * ControllerOffset;
            float testRadius = m_Controller.radius;
            var testStart = new Vector3(currentPos.x + controllerCentre.x, currentPos.y + +controllerCentre.y,
                currentPos.z + controllerCentre.z);
            // 用球体向下扫跟角色胶囊同宽的区域。角色脚刚压到台阶边缘时，中心的一根射线可能漏判。
            int numHits = Physics.SphereCastNonAlloc(testStart, testRadius, Vector3.down, m_GroundCheckRaycastResults,
                GroundedOffset, consts.GroundLayers, QueryTriggerInteraction.Ignore);

            // Choose the best hit
            float largestDot = float.MinValue;
            float closestDistSq = float.MaxValue;
            int flattestHitIndex = -1;
            int closestHitIndex = -1;
            for (int i = 0; i < numHits; ++i) {
                var raycastResult = m_GroundCheckRaycastResults[i];
                // 房主同时有服务器实体和客户端角色外观。找地面时拒绝另一套世界的角色碰撞，免得踩到同一个角色的另一份。
                if (!m_StandalonePreview && PlayerGhost &&
                    GhostGameObject.TryFindGhostGameObject(raycastResult.collider.gameObject, out var ghost) &&
                    !GhostGameObject.BroadClientServerRolesMatch(ghost.Role, PlayerGhost.Role))
                    // not a valid ground for this player (server hitting client object, or vice versa)
                    continue;
#if DEBUG_RENDER_MOVEMENT
                    Debug.DrawLine(raycastResult.point, raycastResult.point + raycastResult.normal, Color.red, k_DebugRenderingTimeout);
#endif
                // 最平的命中决定脚下坡度，最近的命中决定是什么表面。两者用途不同，例如脚步声使用材质。
                float dot = math.dot(raycastResult.normal, k_UpVector);
                if (dot > largestDot) //select the most upright normal to avoid issues with corner collisions looking like a slope
                {
                    largestDot = dot;
                    flattestHitIndex = i;
                }
                float distSq = raycastResult.point.sqrMagnitude > 0f
                    ? math.distancesq(raycastResult.point, currentPos)
                    : 0f; //point will be (0,0,0) if the spherecast starts inside the collider
                if (distSq < closestDistSq) {
                    closestDistSq = distSq;
                    closestHitIndex = i;
                }
            }

            // Did we discard all of the collisions?
            if (flattestHitIndex >= 0) {
                Debug.Assert(closestHitIndex >= 0,
                    "[THIRDPERSONCONTROLLER] flattest hit is valid but closest hit isn't, this shouldn't be possible!");
                var flattest = m_GroundCheckRaycastResults[flattestHitIndex];
                var closest = m_GroundCheckRaycastResults[closestHitIndex];
                groundCollision = new GroundCollisionVariables(closest, flattest, largestDot);
                return true;
            }
        }
        groundCollision = new GroundCollisionVariables();
        return false;
    }

    public void UpdateGround(ref ControllerState state, in ControllerConsts consts) {
        if (UpdateGround(state, consts, out var groundCollision)) {
            GroundPhysicsMaterial = groundCollision.ClosestHitSurfaceType;
            state.GroundNormal = groundCollision.FlattestHitNormal;
        } else {
            GroundPhysicsMaterial = null;
            state.GroundNormal = k_UpVector;
        }
    }

    public void GroundedCheck(ref ControllerState state, in ControllerConsts consts) {
        bool isGrounded = UpdateGround(state, consts, out var groundCollision);
        // 从下落到踩地这一次才标记为落地事件。站着却没地面表示走出边缘，需要进入 Falling。
        if (isGrounded && state.MovementType == MovementType.Falling) {
            state.JumpTimeoutDelta = consts.LandingTimeout;
            state.Land = true;
            state.LandTriggered = true;
            state.Jump = false;
            SetMovementType(ref state, MovementType.Standing);
            bool isClientOwned = !m_StandalonePreview && m_PlayerGhost &&
                                 m_PlayerGhost.Role == MultiplayerRole.ClientOwned;
            if (isClientOwned && m_Animator_1P != null)
                if (m_Animator_1P.TryGetComponent<Unity.MP_FPS.EventHandler>(out var eventHandler))
                    eventHandler.onFootDown = true;
        } else if (!isGrounded && state.MovementType == MovementType.Standing) {
            SetMovementType(ref state, MovementType.Falling);
        } else if (isGrounded && state.MovementType == MovementType.Standing) { state.Land = false; }
        if (isGrounded) {
            state.GroundNormal = groundCollision.FlattestHitNormal;
            GroundPhysicsMaterial = groundCollision.ClosestHitSurfaceType;
        } else {
            state.GroundNormal = k_UpVector;
            GroundPhysicsMaterial = null;
        }
    }

    // 其他玩家已经有插值后的位姿，直接把它送进显示对象。他们的移动不用本机重新碰撞。
    public void ApplyInterpolatedClientState(
        ref ControllerState state,
        in ControllerConsts consts,
        in LocalTransform localTransform,
        float deltaTime,
        bool applyAnimation = false) {
        // apply smoothed rotation
        state.CurrentPosition = localTransform.Position;
        state.CurrentRotation = localTransform.Rotation;
        transform.SetPositionAndRotation(state.CurrentPosition, state.CurrentRotation);
    }

    // 先消费只播一遍的事件，再更新每帧连续变化的动画参数。这样连续的移动姿态和开火、落地不会互相覆盖。
    public void ApplyAnimatorState(in ControllerState state, in ControllerConsts consts, float deltaTime) {
        if (m_PlayerGhost == null || m_PlayerGhost.GhostGameObject == null)
            return; //Return if the ghost has not been linked
        var predictedPlayerGhost = m_PlayerGhost.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>();
        bool isClientOwned = m_PlayerGhost.Role == MultiplayerRole.ClientOwned;
        var animator = isClientOwned ? m_Animator_1P : m_Animator_3P;
        if (animator == null) return;

        // Handle one-shot events first
        HandleAnimationEvents(animator, predictedPlayerGhost);

        // Then handle continuous state parameters
        if (isClientOwned)
            ApplyFirstPersonMovementAnimation(animator, state);
        else
            ApplyThirdPersonMovementAnimation(animator, state, predictedPlayerGhost.CurrentHealth);
    }

    private void HandleAnimationEvents(Animator animator, in PredictedPlayerGhost ghostState) {
        // Shooting
        if (ghostState.LastShotTick > _lastAnimatedShotTick) {
            if (!ghostState.ControllerState.IsReloadingState && !animator.GetBool(AnimationParameters.IsShooting)) {
                if (m_EnableAnimationLogging)
                    Debug.Log($"[ANIMATION] Firing SHOOT trigger at Tick: {ghostState.LastShotTick.ToString()}");
                animator.SetTrigger(AnimationParameters.Shoot);
            }
            _lastAnimatedShotTick = ghostState.LastShotTick;
        }

        // Reloading
        if (ghostState.LastReloadTick > _lastAnimatedReloadTick) {
            if (m_EnableAnimationLogging)
                Debug.Log($"[ANIMATION] Firing RELOAD trigger at Tick: {ghostState.LastReloadTick.ToString()}");
            animator.SetTrigger(AnimationParameters.Reload);
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghostState.EquippedWeaponID);
            if (weaponData != null && weaponData.WeaponReloadSfx != null) {
                var eventHandler = animator.GetComponent<Unity.MP_FPS.EventHandler>();
                eventHandler.reloadSFX = weaponData.WeaponReloadSfx;
            }
            _lastAnimatedReloadTick = ghostState.LastReloadTick;
        }

        // Jumping
        if (ghostState.LastJumpTick > _lastAnimatedJumpTick) {
            if (m_EnableAnimationLogging)
                Debug.Log($"[ANIMATION] Firing JUMP trigger at Tick: {ghostState.LastJumpTick.ToString()}");
            animator.SetTrigger(AnimationParameters.Jump);
            _lastAnimatedJumpTick = ghostState.LastJumpTick;
        }

        // Landing
        if (ghostState.LastLandTick > _lastAnimatedLandTick) {
            if (m_EnableAnimationLogging)
                Debug.Log($"[ANIMATION] Firing LAND trigger at Tick: {ghostState.LastLandTick.ToString()}");
            animator.SetTrigger(AnimationParameters.Land);
            _lastAnimatedLandTick = ghostState.LastLandTick;
        }

        // Hit Reaction
        if (ghostState.LastHitTick > _lastProcessedHitTick) {
            if (m_PlayerGhost.Role == MultiplayerRole.ClientOwned) {
                if (m_EnableAnimationLogging)
                    Debug.Log($"[ANIMATION] Firing 1P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");

                // Owning-camera feedback observes replicated tissue loss independently
                // of animation updates; shield/metabolic ticks must not replay gunshot blood.
            } else {
                if (m_EnableAnimationLogging)
                    Debug.Log($"[ANIMATION] Firing 3P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");

                // Handle the 3P hit animation
                animator.SetTrigger(AnimationParameters.IsHit);
            }
            if (PlayerHitSFX != null && ghostState.LastDamageAmount > 0 && !InjuryFeedbackState.Systemic(ghostState))
                GameManager.Instance.SoundSystem.CreateEmitter(PlayerHitSFX, transform.position);
            _lastProcessedHitTick = ghostState.LastHitTick;
        }
    }

    private void ApplyFirstPersonMovementAnimation(Animator animator, in ControllerState state) {
        float targetIsMovingValue = state.AnimatorTargetSpeed != 0.0f ? 1.0f : 0.0f;
        animator.SetFloat(AnimationParameters.IsMoving, targetIsMovingValue);
    }

    private void ApplyThirdPersonMovementAnimation(Animator animator, in ControllerState state, float currentHealth) {
        var relativeSpeed = math.mul(math.inverse(state.CurrentRotation), state.MovementRequest);
        animator.SetFloat(AnimationParameters.Speed, relativeSpeed.z); // Use raw value for blend trees
        animator.SetFloat(AnimationParameters.StrafeSpeed, relativeSpeed.x);
        float speedZ = math.abs(relativeSpeed.z) < 0.01f ? 0.0f : math.sign(relativeSpeed.z);
        float speedX = math.abs(relativeSpeed.x) < 0.01f ? 0.0f : math.sign(relativeSpeed.x);
        animator.SetFloat(AnimationParameters.Speed, speedZ);
        animator.SetFloat(AnimationParameters.StrafeSpeed, speedX);
        bool isDead = currentHealth <= 0;
        animator.SetBool(AnimationParameters.IsDead, isDead);
    }

    private void ApplyPosImmediate(in ControllerState state) {
        if (m_Controller.enabled) {
            m_Controller.enabled = false;
            transform.position = state.CurrentPosition;
            m_Controller.enabled = true;
#if UNITY_EDITOR || DEBUG
            float deltaSqrd = math.distancesq(state.CurrentPosition, transform.position);
            Debug.Assert(deltaSqrd <= k_ResetMovementAdjustEpsilon,
                $"ApplyPosImmediate has failed to move the controller to the correct location (target {state.CurrentPosition} actual {(float3)transform.position} delta {math.sqrt(deltaSqrd)} {state.CurrentPosition - (float3)transform.position} state {state.MovementType} prev {m_PrevMovementType})");
#endif
        } else {
            // set directly, no physics required
            transform.position = state.CurrentPosition;
        }
    }

    public void ApplyPosRotImmediate(in ControllerState state) {
        ApplyPosImmediate(state);
        transform.rotation = state.CurrentRotation;
    }

    private static void GetStateConsts(
        out ControllerConsts.StateConsts stateConsts,
        ref ControllerState state,
        in PlayerInput input,
        in ControllerConsts consts,
        float deltaTime) {
        stateConsts.Speed = 0f;
        stateConsts.RotationSmoothTime = 0f;
        stateConsts.SpeedChangeRate = k_DefaultSpeedChange; //just a large number to make it snap to the 0 speed
        stateConsts.LandingSpeedMult = 1f;
        stateConsts.AnimationMotionScale = 0.4f;

        // Freeze after teleport for a second
        // 传送刚发生时给一段暂停走动的时间，避免缓存中的输入把角色又移出落点。
        if (state.TeleportFreeze > 0f)
            state.TeleportFreeze -= deltaTime;
        else
            switch (state.MovementType) {
                case MovementType.Standing:
                case MovementType.Jumping:
                case MovementType.Falling:
                    stateConsts = state.Sprinting ? consts.Sprint : consts.Walk;
                    stateConsts.Speed *= DollSingerStances.SpeedScale(state.Stance);
                    break;
                default:
                    Debug.LogError(
                        $"[THIRDPERSONCONTROLLER] GetStateConsts : Unhandled state {state.MovementType.ToString()}");
                    break;
            }
    }

    private static float3 CalculateMovementFromInput(
        ref ControllerState state,
        in ControllerConsts consts,
        in ControllerConsts.StateConsts stateConsts,
        in PlayerInput input,
        bool updateRotation,
        float deltaTime,
        bool faceView) {
        if (updateRotation) {
            state.YawDegrees = input.LookYawPitchDegrees.x;
            state.PitchDegrees = input.LookYawPitchDegrees.y;
            if (!input.ThirdPerson) {
                if (!state.FreeLooking) state.CurrentRotation = quaternion.RotateY(math.radians(state.YawDegrees));
                state.RotationVelocity = 0f;
            // 第三人称能把镜头绕到身体侧面。攻击或瞄准时身体朝镜头，移动时可转向运动方向，闲置绕看不带动身体。
            } else if (faceView || input.Aim || state.MovementSpeed > 0f) {
                // Third person walks towards camera-relative input; aiming keeps the body facing the view.
                float targetYaw = state.YawDegrees;
                if (!input.Aim && !faceView)
                    targetYaw += math.degrees(math.atan2(input.MoveInput.x, input.MoveInput.y));
                var forward = math.forward(state.CurrentRotation);
                float bodyYaw = math.degrees(math.atan2(forward.x, forward.z));
                float yaw = stateConsts.RotationSmoothTime > 0f
                    ? Mathf.SmoothDampAngle(bodyYaw, targetYaw, ref state.RotationVelocity,
                        stateConsts.RotationSmoothTime, Mathf.Infinity, deltaTime)
                    : targetYaw;
                state.CurrentRotation = quaternion.RotateY(math.radians(yaw));
            } else {
                // Orbiting the camera at rest must not turn the third-person character.
                state.RotationVelocity = 0f;
            }
        }

        // View yaw and body yaw are independent: using the turning body would rotate the input twice.
        // 将摇杆或按键方向换算成世界方向。自由观察例外，人物继续以身体朝向走，转头不会顺便改变走路方向。
        var rotQuat = state.FreeLooking ? state.CurrentRotation : quaternion.RotateY(math.radians(state.YawDegrees));
        var localMove = new float3(input.MoveInput.x, 0f, input.MoveInput.y);

        // Normalize it to get a pure direction vector with a length of 1.
        // This is the crucial step.
        // 输入只负责选方向，另外计算的速度决定走多快。归一化后斜按两个方向不会比直走快。
        var localDir = math.normalizesafe(localMove);

        // Rotate movement by the view, regardless of which way the body currently faces.
        var worldDir = math.mul(rotQuat, localDir);

        // Reuse the existing replicated animation vector for directional character poses.
        // The DollSinger view converts this world-space motion into its body's local axes.
        state.AnimatorMotion = worldDir * state.MovementSpeed;

        // Multiply the pure direction by the final speed calculated in AccumulateMovement.
        return worldDir * state.MovementSpeed;
    }

    private static void AddMovementFromJumpFall(ref float3 moveDelta, in ControllerState state) {
        moveDelta.y += state.JumpFallSpeed;
    }

    public static void ProcessInputs(ref ControllerState state, in PlayerInput input, float deltaTime) {
        state.Aiming = input.Aim;
        state.HaloLightEnabled = input.HaloLightEnabled;
        state.HaloLightingSettings = HaloLighting.Sanitize(input.HaloLightingSettings);
        // 瞄准和低姿态不能冲刺。自由观察和探身的角度也在入口限制，双方使用同一个可预测范围。
        state.Sprinting = input.Sprint && !input.Aim && state.Stance == DollSingerStance.Standing;
        state.FirstPersonView = !input.ThirdPerson;
        state.FreeLooking = state.FirstPersonView && input.FreeLooking;
        state.FreeLookOffset = state.FirstPersonView
            ? math.clamp(input.FreeLookOffset, new float2(-70f, -45f), new float2(70f, 45f))
            : float2.zero;
        state.Lean = state.FirstPersonView && state.Stance != DollSingerStance.Prone
            ? math.clamp(input.Lean, -1f, 1f)
            : 0f;
    }

    // 先检查所请求的新姿态能否放进场景，再由输入、姿态、重力算出这一小段的速度与位移。
    public static void AccumulateMovement(
        ref ControllerState state,
        ref float3 accumulatedMovement,
        in PlayerInput input,
        in ControllerConsts consts,
        float deltaTime,
        FirstPersonController motor = null,
        bool faceView = false) {
        if (motor != null)
            state.Stance = DollSingerStances.Resolve(motor.m_Controller,
                (Vector3)(state.CurrentPosition + accumulatedMovement), state.Stance, input.Stance,
                state.MovementType == MovementType.Standing, motor.m_StandingHeight, motor.m_StandingCenter);
        ProcessInputs(ref state, input, deltaTime);
        state.TimeInState += deltaTime;
        AccumulateJumpAndGravity(ref state, input, consts, deltaTime);
        GetStateConsts(out var stateConsts, ref state, in input, in consts, deltaTime);
        bool updateRotation = true;
        float combinedMoveSpeedModifier = 1f;
        float modifiedTargetMoveSpeed =
            stateConsts.Speed * combinedMoveSpeedModifier; //don't apply modifiers to the aiming speed
        float inputMagnitude = math.length(input.MoveInput);

        // apply analog deadzone
        // 输入幅度低于阈值按没操作处理。超过这个阈值使用当前走或跑速度，本段没有按摇杆幅度连续缩放。
        inputMagnitude = inputMagnitude >= 0.4f ? 1f : 0f;
        float applyTargetSpeed = modifiedTargetMoveSpeed * inputMagnitude;
        float blendAlpha = deltaTime * stateConsts.SpeedChangeRate;
        state.MovementSpeed = applyTargetSpeed;
        state.AnimatorTargetSpeedChangeRate = stateConsts.SpeedChangeRate;
        state.AnimatorTargetSpeed = stateConsts.Speed * inputMagnitude;
        state.AnimatorMotionSpeed = inputMagnitude > 0f ? state.MovementSpeed : 1f; //play the idle at 1x
        state.AnimatorMotion = float3.zero;
        state.AnimatorMotionChangeRate = 0f;
        var moveDelta = float3.zero;
        updateRotation &= applyTargetSpeed > 0f;
        switch (state.MovementType) {
            case MovementType.Standing: {
                moveDelta = CalculateMovementFromInput(ref state, consts, stateConsts, input, true, deltaTime,
                    faceView);
                AddMovementFromJumpFall(ref moveDelta, state);
            }
                break;
            case MovementType.Jumping:
            case MovementType.Falling: {
                moveDelta = CalculateMovementFromInput(ref state, consts, stateConsts, input, true, deltaTime,
                    faceView);
                AddMovementFromJumpFall(ref moveDelta, state);
            }
                break;
            default: {
                Debug.LogError(
                    $"[THIRDPERSONCONTROLLER] AccumulateMovement : Unhandled state {state.MovementType.ToString()}");
            }
                break;
        }
        // 这里只累加想走的位移，可能还会合并同一拍的其他操作。之后 ApplyMove 才让碰撞体真正移动。
        accumulatedMovement += moveDelta * deltaTime;
        state.MovementRequest = accumulatedMovement;
#if DEBUG_RENDER_MOVEMENT
        Debug.DrawLine(state.CurrentPosition, state.CurrentPosition + accumulatedMovement, GetDebugColour(state.MovementType), k_DebugRenderingTimeout);
#endif
    }

    private void ApplyMove(
        ref ControllerState state,
        in ControllerConsts consts,
        in float3 accumulatedMovement,
        float deltaTime) {
        // 物件的姿势可能还停在预测前或传送前的位置。移动起点要先对齐这拍状态中的位置，才不会从旧位置再算一次。
        var posDelta = (Vector3)state.CurrentPosition - transform.position;
        if (posDelta.sqrMagnitude > 0f) {
            MovementLog($"{name} - Reset Teleport to {state.CurrentPosition} (from {(float3)transform.position})");
            ApplyPosImmediate(state);
        }
        bool allowMove = math.lengthsq(accumulatedMovement) > 0f;
        if (allowMove) {
            // apply movement
            var movementToApply = accumulatedMovement;

            // This can be called before the controller is enabled (e.g. on spawn)
            // prevents errors of moving when controller is disabled or GameObject inactive
            if (m_Controller != null && m_Controller.enabled && gameObject.activeInHierarchy)
                // just apply our move as normal
                // CharacterController 在游戏场景里实际推一次胶囊。撞墙后的真实位置可能和刚才累加出的目标不一样。
                m_Controller.Move(movementToApply);
        }

        // apply rotation
        transform.rotation = state.CurrentRotation;
        if (ShouldUpdateFallHeight(state.MovementType)) {
            // 用实际移动后的高度差累加落差，便于服务器的落地结算。撞到平台意味着这一段下落到此为止。
            float fallDist = state.CurrentPosition.y - transform.position.y;
            state.FallHeight += fallDist;
        }
        HandleFirstPersonFootstepSFX(state);

        // store position
        state.CurrentPosition = transform.position;
#if UNITY_EDITOR || DEBUG
        m_PrevMovementType = state.MovementType;
#endif
#if ENABLE_MOVEMENT_DIAGNOSTICS
        if (ServerPlayerMovementSystem.PlayerMovementActive)
        {
            MovementLog($"{name} - server tick {ServerPlayerMovementSystem.PlayerMovementTick} with position {state.CurrentPosition}");
        }
        else
        {
            MovementLog($"{name} - prediction tick {PlayerPredictionSystem.PlayerMovementTick} with position {state.CurrentPosition}");
        }
#endif
    }

#if ENABLE_MOVEMENT_DIAGNOSTICS
    public void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Debug.Log($"[{UnityEngine.Time.frameCount}] OnControllerColliderHit hit {hit.collider.name} ghost = {(GhostGameObject.TryFindGhostGameObject(hit.gameObject, out var ghost) ? ghost.name : "null")}");
    }
#endif

    private void HandleFirstPersonFootstepSFX(ControllerState state) {
        // Full-body presentations can use their own animation footstep events.
        if (m_Animator_1P == null || m_StandalonePreview) return;
        if (state.MovementType == MovementType.Standing && m_Controller != null && m_Controller.enabled &&
            gameObject.activeInHierarchy) {
            bool isClientOwned = m_PlayerGhost.Role == MultiplayerRole.ClientOwned;
            if (isClientOwned) {
                if (transform.position.x == state.CurrentPosition.x &&
                    transform.position.z == state.CurrentPosition.z) { footstepTriggerTimer = 0; } else {
                    if (footstepTriggerTimer == 0) {
                        footstepTriggerTimer =
                            Time.time; // This is called multiple times per update. So I can't use Time.delta
                        footstepStartTimer = footstepTriggerTimer;
                    }
                    footstepTriggerTimer = Time.time;
                    float t = footstepTriggerTimer - footstepStartTimer;
                    if (t >= 0.5f) {
                        footstepStartTimer += 0.5f;
                        if (m_Animator_1P.TryGetComponent<Unity.MP_FPS.EventHandler>(out var eventHandler))
                            eventHandler.onFootDown = true;
                    }
                }
            }
        }
    }

    private static void AccumulateJumpAndGravity(
        ref ControllerState state,
        in PlayerInput input,
        in ControllerConsts consts,
        float deltaTime) {
        switch (state.MovementType) {
            case MovementType.Standing: {
                ClearFallingState(ref state);
                if (AccumulateJump(ref state, in input, in consts, deltaTime) ||
                    state.TimeInState < consts.LandingTimeout)
                    //we've just started jumping or we're in the process of landing so apply gravity
                    AccumulateGravity(ref state, in consts, deltaTime);
                else
                    //fully in standing state reset gravity to a minimal fall speed to keep player aligned on ground (necessary for uneven terrain)
                    // 稳定站立时仍留少量向下速度，让沿斜地走时脚贴住地面。
                    state.JumpFallSpeed = consts.StandingFallSpeed;
            }
                break;
            case MovementType.Falling:
            case MovementType.Jumping: {
                state.Jump = true;
                state.JumpTimeoutDelta = math.max(consts.JumpTimeout, state.JumpTimeoutDelta);

                // fall timeout
                if (state.FallTimeoutDelta < consts.FallTimeout)
                    state.FallTimeoutDelta += deltaTime;
                else
                    state.Fall = true;
                AccumulateGravity(ref state, in consts, deltaTime);
                // 竖直速度过零时由向上跳切到落下，落地检查随后才重新接受地面碰撞。
                var newMovementState = state.JumpFallSpeed >= 0f ? MovementType.Jumping : MovementType.Falling;
                SetMovementType(ref state, newMovementState);
            }
                break;
            default: {
                Debug.LogError(
                    $"[THIRDPERSONCONTROLLER] AccumulateJumpAndGravity : Unhandled state {state.MovementType.ToString()}");
            }
                break;
        }
    }

    private static void ClearFallingState(ref ControllerState state) {
        // reset the fall timeout timer
        state.FallTimeoutDelta = 0f;

        // update animator if using character
        state.Jump = false;
        state.Fall = false;
    }

    private static void AccumulateGravity(ref ControllerState state, in ControllerConsts consts, float deltaTime) {
        // apply gravity over time if we haven't reached terminal (multiply by delta time twice to linearly speed up over time)
        float terminalVelocity = consts.TerminalVelocity;
        if (state.JumpFallSpeed > terminalVelocity)
            state.JumpFallSpeed += consts.Gravity * deltaTime;
        // Handle the terminal velocity becoming much smaller due to umbrella opening
        else if (state.JumpFallSpeed <= terminalVelocity)
            state.JumpFallSpeed = Mathf.Lerp(state.JumpFallSpeed, terminalVelocity, deltaTime);
    }

    // 自己按下开火时先生成本地弹体外观。服务器的同一发出现后，按 tick、武器和弹丸序号接管这个对象。
    public void SpawnPredictedProjectile(
        uint spawnTick,
        uint weaponId,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        int pelletIndex = 0,
        ushort modules = 0) {
        // 回滚后一拍可能被客户端重算。同拍同武器同弹丸已创建时直接复用，别让同一秒内出现重复弹体。
        foreach (var shot in Projectile.PredictedProjectiles)
            if (shot.SpawnTick == spawnTick && shot.WeaponID == weaponId && shot.PelletIndex == pelletIndex)
                return;
        var player = GetComponent<PlayerGhost>();
        var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
        // GameResources has already loaded ghost prefabs before it permits player spawning.
        // Instantiate that cached asset synchronously: no async duplicate or first-shot load delay.
        var world = player.GhostGameObject.World;
        var prefab = world.GetExistingSystemManaged<GhostEntityPrefabSystem>()
            ?.GetGameObjectPrefab(weapon.ProjectileGhostPrefab.GhostGuid)?.Asset as GameObject;
        if (prefab == null) {
            Debug.LogWarning("Predicted projectile prefab is not loaded in this client world.");
            return;
        }
        var instance = Instantiate(prefab, spawnPosition, spawnRotation,
            GhostBridgeBootstrap.Instance.ClientGameObjectHierarchy.transform);
        instance.GetComponent<Projectile>().InitializePrediction(weaponId, spawnPosition, spawnRotation, transform,
            shotTick: spawnTick, pelletIndex: pelletIndex, modules: modules);
        Projectile.PredictedProjectiles.Add(new Projectile.PredictedProjectileInfo {
            Instance = instance, SpawnTick = spawnTick, WeaponID = weaponId, PelletIndex = pelletIndex,
            CreatedAt = Time.time
        });
    }

    private static bool AccumulateJump(
        ref ControllerState state,
        in PlayerInput input,
        in ControllerConsts consts,
        float deltaTime) {
        bool jumped = false;
        if (input.Jump && state.Stance == DollSingerStance.Standing && state.JumpTimeoutDelta <= 0f) {
            // the square root of H * -2 * G = how much velocity needed to reach desired height
            // 起跳速度由配置的跳高与重力反推。先记一个跳起事件，再让后续步长逐渐施加重力。
            state.JumpFallSpeed = math.sqrt(consts.JumpHeight * -2f * consts.Gravity); // 6
            SetMovementType(ref state, MovementType.Jumping);
            state.Jump = true;
            state.JumpTriggered = true;
            jumped = true;
#if DEBUG_RENDER_MOVEMENT
            Debug.DrawLine(state.CurrentPosition - new float3(0.2f, 0f, 0f), state.CurrentPosition + new float3(0.2f, 0f, 0f), Color.black, k_DebugRenderingTimeout);
            Debug.DrawLine(state.CurrentPosition - new float3(0f, 0f, 0.2f), state.CurrentPosition + new float3(0f, 0f, 0.2f), Color.black, k_DebugRenderingTimeout);
#endif
        }
        if (state.JumpTimeoutDelta > 0f) state.JumpTimeoutDelta -= deltaTime;
        return jumped;
    }

#if DEBUG_RENDER_MOVEMENT
    private static Color GetDebugColour(MovementType movement)
    {
        var colour = Color.white;

        switch (movement)
        {
            case MovementType.Standing:
                colour = Color.green;
                break;
            case MovementType.Jumping:
                colour = Color.cyan;
                break;
            case MovementType.Falling:
                colour = Color.blue;
                break;
            case MovementType.Sliding:
                colour = Color.yellow;
                break;
            case MovementType.Stationary:
                colour = Color.red;
                break;
            case MovementType.Swimming:
                colour = Color.magenta;
                break;
        }

        return colour;
    }
#endif

    // taken from DOTSSample MathHelper
    // Collection of converted classic Unity (Mathf, Vector3 etc.) + some homegrown math functions using Unity.Mathematics
    // These are made/converted for production and unlike a proper library they are lacking any tests, so use at your own peril!
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothDampAngle(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime) {
        target = current + DeltaAngle(current, target);
        float result = SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        result = Repeat360(result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothDamp(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime) {
        // Based on Game Programming Gems 4 Chapter 1.10
        smoothTime = math.max(0.0001F, smoothTime);
        float omega = 2F / smoothTime;
        float x = omega * deltaTime;
        float exp = 1F / (1F + x + 0.48F * x * x + 0.235F * x * x * x);
        float change = current - target;
        float originalTo = target;

        // Clamp maximum speed
        float maxChange = maxSpeed * smoothTime;
        change = math.clamp(change, -maxChange, maxChange);
        target = current - change;
        float temp = (currentVelocity + omega * change) * deltaTime;
        currentVelocity = (currentVelocity - omega * temp) * exp;
        float result = target + (change + temp) * exp;

        // Prevent overshooting
        if (originalTo - current > 0.0F == result > originalTo) {
            result = originalTo;
            currentVelocity = (result - originalTo) / deltaTime;
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DeltaAngle(float current, float target) {
        float delta = Repeat360(target - current);
        if (delta > 180.0F) delta -= 360.0F;
        float result = delta;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Repeat360(float t) {
        const float repeat_length = 360f;
        const float inverse_length = 1 / repeat_length;
        return math.clamp(t - math.floor(t * inverse_length) * repeat_length, 0.0f, repeat_length);
    }

    [System.Diagnostics.Conditional("ENABLE_MOVEMENT_DIAGNOSTICS")]
    public static void MovementLog(string message) { Debug.Log($"[{Time.frameCount}] {message}"); }
}