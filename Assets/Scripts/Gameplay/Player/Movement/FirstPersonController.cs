// 玩家共用的移动计算。把输入和 ControllerState 算成位移请求，再交给 Unity CharacterController 落地、撞墙和移动。
// 客户端预测与服务器都会用这套规则，远端角色用插值后的状态播放动画。
//#define DEBUG_RENDER_MOVEMENT
using System;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public partial class FirstPersonController : MonoBehaviour {
    public SoundDef PlayerHitSFX;

    private const float k_ResetMovementAdjustEpsilon = 1e-06f;

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

    // 地图预览需要显式启用这个入口。不开网络 World 也能复用移动控制器。
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

        // 缓存最近的跳跃和下落值，供读不到 ECS 数据的组件查询。
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
            // 根据角色的胶囊偏移确定探地球的起点。
            var controllerCentre = transform.rotation * ControllerOffset;
            float testRadius = m_Controller.radius;
            var testStart = new Vector3(currentPos.x + controllerCentre.x, currentPos.y + +controllerCentre.y,
                currentPos.z + controllerCentre.z);
            // 用球体向下扫跟角色胶囊同宽的区域。角色脚刚压到台阶边缘时，中心的一根射线可能漏判。
            int numHits = Physics.SphereCastNonAlloc(testStart, testRadius, Vector3.down, m_GroundCheckRaycastResults,
                GroundedOffset, consts.GroundLayers, QueryTriggerInteraction.Ignore);

            // 从碰撞结果挑出有效的地面位置。
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
                    // 这个碰撞体属于另一套角色世界，不能当当前角色的地面。
                    continue;
#if DEBUG_RENDER_MOVEMENT
                    Debug.DrawLine(raycastResult.point, raycastResult.point + raycastResult.normal, Color.red, k_DebugRenderingTimeout);
#endif
                // 最平的命中决定脚下坡度，最近的命中决定是什么表面。两者用途不同，例如脚步声使用材质。
                float dot = math.dot(raycastResult.normal, k_UpVector);
                if (dot > largestDot) //优先选择最朝上的法线，避免台阶拐角边被当成斜坡。
                {
                    largestDot = dot;
                    flattestHitIndex = i;
                }
                float distSq = raycastResult.point.sqrMagnitude > 0f
                    ? math.distancesq(raycastResult.point, currentPos)
                    : 0f; //球体一开始就在碰撞体里时，命中点可能是 (0,0,0)。
                if (distSq < closestDistSq) {
                    closestDistSq = distSq;
                    closestHitIndex = i;
                }
            }

            // 检查有没有可用的地面命中。
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
            // 直接应用该位置，无需再做物理移动。
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
        stateConsts.SpeedChangeRate = k_DefaultSpeedChange; //给较大的变化速度，让移动速度迅速回到零。
        stateConsts.LandingSpeedMult = 1f;
        stateConsts.AnimationMotionScale = 0.4f;

        // 传送后短暂停止移动。
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
                // 第三人称按镜头方向解释移动输入。瞄准时身体面朝镜头正在指的方向。
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
                // 第三人称原地绕看时，身体不需要跟着旋转。
                state.RotationVelocity = 0f;
            }
        }

        // 视角与身体的水平朝向独立。直接用正在转动的身体算输入，会让输入方向被旋转两次。
        // 将摇杆或按键方向换算成世界方向。自由观察例外，人物继续以身体朝向走，转头不会顺便改变走路方向。
        var rotQuat = state.FreeLooking ? state.CurrentRotation : quaternion.RotateY(math.radians(state.YawDegrees));
        var localMove = new float3(input.MoveInput.x, 0f, input.MoveInput.y);

        // 归一化后取得长度为 1 的纯方向向量。
        // 这一步让移动方向与速度的计算分离。
        // 输入只负责选方向，另外计算的速度决定走多快。归一化后斜按两个方向不会比直走快。
        var localDir = math.normalizesafe(localMove);

        // 用视角换算移动方向，与当前身体朝哪边无关。
        var worldDir = math.mul(rotQuat, localDir);

        // 方向姿势复用已同步的动画移动向量。
        // DollSinger 的外观再把世界空间移动换算回身体的本地轴。
        state.AnimatorMotion = worldDir * state.MovementSpeed;

        // 用 AccumulateMovement 算出的最终速度乘上这个方向。
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
            stateConsts.Speed * combinedMoveSpeedModifier; //瞄准移速不应用这组额外倍率。
        float inputMagnitude = math.length(input.MoveInput);

        // 忽略小于死区的摇杆幅度。
        // 输入幅度低于阈值按没操作处理。超过这个阈值使用当前走或跑速度，本段没有按摇杆幅度连续缩放。
        inputMagnitude = inputMagnitude >= 0.4f ? 1f : 0f;
        float applyTargetSpeed = modifiedTargetMoveSpeed * inputMagnitude;
        float blendAlpha = deltaTime * stateConsts.SpeedChangeRate;
        state.MovementSpeed = applyTargetSpeed;
        state.AnimatorTargetSpeedChangeRate = stateConsts.SpeedChangeRate;
        state.AnimatorTargetSpeed = stateConsts.Speed * inputMagnitude;
        state.AnimatorMotionSpeed = inputMagnitude > 0f ? state.MovementSpeed : 1f; //待机动画用正常一倍速度播放。
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
            // 把这一拍累计的位移送给角色控制器。
            var movementToApply = accumulatedMovement;

            // 角色出生等情况下，方法可能先于控制器启用被调用。
            // 碰撞马达停用或对象未激活时，不调用 Move，以免报错。
            if (m_Controller != null && m_Controller.enabled && gameObject.activeInHierarchy)
                // 控制器可用时正常应用位移。
                // CharacterController 在游戏场景里实际推一次胶囊。撞墙后的真实位置可能和刚才累加出的目标不一样。
                m_Controller.Move(movementToApply);
        }

        // 把旋转写入场景对象。
        transform.rotation = state.CurrentRotation;
        if (ShouldUpdateFallHeight(state.MovementType)) {
            // 用实际移动后的高度差累加落差，便于服务器的落地结算。撞到平台意味着这一段下落到此为止。
            float fallDist = state.CurrentPosition.y - transform.position.y;
            state.FallHeight += fallDist;
        }
        HandleFirstPersonFootstepSFX(state);

        // 保存移动后的实际位置。
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
        // 全身模型可以用已配置的动画事件来播脚步。
        if (m_Animator_1P == null || m_StandalonePreview) return;
        if (state.MovementType == MovementType.Standing && m_Controller != null && m_Controller.enabled &&
            gameObject.activeInHierarchy) {
            bool isClientOwned = m_PlayerGhost.Role == MultiplayerRole.ClientOwned;
            if (isClientOwned) {
                if (transform.position.x == state.CurrentPosition.x &&
                    transform.position.z == state.CurrentPosition.z) { footstepTriggerTimer = 0; } else {
                    if (footstepTriggerTimer == 0) {
                        footstepTriggerTimer =
                            Time.time; // 一次更新会调用多次，不能每次都代入整帧的 Time.delta。
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
                    //刚起跳或还在结束落地过渡时，继续施加重力。
                    AccumulateGravity(ref state, in consts, deltaTime);
                else
                    //稳定站立后仍保留很小向下速度，在起伏地面上走也贴着地。
                    // 稳定站立时仍留少量向下速度，让沿斜地走时脚贴住地面。
                    state.JumpFallSpeed = consts.StandingFallSpeed;
            }
                break;
            case MovementType.Falling:
            case MovementType.Jumping: {
                state.Jump = true;
                state.JumpTimeoutDelta = math.max(consts.JumpTimeout, state.JumpTimeoutDelta);

                // 累计超过下落动画的延迟才标为正在下落。
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
        // 清掉本次空中的下落计时。
        state.FallTimeoutDelta = 0f;

        // 清除给角色动画使用的跳跃和下落状态。
        state.Jump = false;
        state.Fall = false;
    }

    private static void AccumulateGravity(ref ControllerState state, in ControllerConsts consts, float deltaTime) {
        // 未到极限时，按这一小段时间施加重力改变速度。随后由速度乘时间算位移。
        float terminalVelocity = consts.TerminalVelocity;
        if (state.JumpFallSpeed > terminalVelocity)
            state.JumpFallSpeed += consts.Gravity * deltaTime;
        // 伞具展开后极限速度可能突然变化，这时平滑靠近新值。
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
        // GameResources 先加载 Ghost 预制体，再允许生成玩家。
        // 直接用缓存资产同步创建，避免异步重复或第一发等待。
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
            // H * -2 * G 的平方根是达到目标跳高需要的起跳速度。
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
        return movement switch {
            MovementType.Standing => Color.green,
            MovementType.Jumping => Color.cyan,
            MovementType.Falling => Color.blue,
            _ => Color.white
        };
    }
#endif

    [System.Diagnostics.Conditional("ENABLE_MOVEMENT_DIAGNOSTICS")]
    public static void MovementLog(string message) { Debug.Log($"[{Time.frameCount}] {message}"); }
}
