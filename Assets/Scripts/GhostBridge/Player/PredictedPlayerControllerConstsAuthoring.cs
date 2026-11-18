// 在角色预制件上填写移动和动画的参数，例如步行和冲刺速度、加速、平滑时间。烘焙成 ECS 数据后，服务端和本机预测共用这些参数。
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public class PredictedPlayerControllerConstsAuthoring : MonoBehaviour {
    [field: Header("Player Movement Speeds")]
    [field: SerializeField]
    [field: Tooltip("Walk speed of the character in m/s")]
    public float WalkSpeed { get; private set; } = 2.35f;

    [field: SerializeField]
    [field: Tooltip("Sprint speed of the character in m/s")]
    public float SprintSpeed { get; private set; } = 4.7f;

    [field: Header("Player Rotation Smoothing Times")]
    [field: SerializeField]
    [field: Tooltip("How fast the character turns to face movement direction while walking")]
    public float WalkRotationSmoothTime { get; private set; } = 0.2f;

    [field: SerializeField] public float WalkAnimationMotionScale { get; private set; } = 0.4f;

    [field: SerializeField]
    [field: Tooltip("How fast the character turns to face movement direction while sprinting")]
    public float SprintRotationSmoothTime { get; private set; } = 0f;

    [field: Header("Player Speed Change Rates")]
    [field: SerializeField]
    [field: Tooltip("Acceleration and deceleration while walking")]
    public float WalkSpeedChangeRate { get; private set; } = 10.0f;

    [field: SerializeField]
    [field: Tooltip("Acceleration and deceleration while sprinting")]
    public float SprintSpeedChangeRate { get; private set; } = 10f;

    [field: SerializeField]
    [field: Tooltip("Multiplier to player target speed during the landing animation timeout when sprinting")]
    public float SprintLandingSpeedMultiplier { get; private set; } = 0.6f;

    [field: SerializeField] public float SprintAnimationMotionScale { get; private set; } = 0.1f;

    [field: Space(10)]
    [field: Header("Player Jumping and Gravity")]
    [field: SerializeField]
    [field: Tooltip("The height the player can jump")]
    public float JumpHeight { get; private set; } = 1.2f;

    [field: SerializeField]
    [field: Tooltip("Lunar surface acceleration in metres per second squared.")]
    public float Gravity { get; private set; } = -1.62f;

    [field: SerializeField] public float TerminalVelocity { get; private set; } = -53.0f;

    [field: SerializeField]
    [field:
        Tooltip(
            "The fall speed applied to the player when in the standing state, to ensure player aligns with the ground on uneven terrain")]
    public float StandingFallSpeed { get; private set; } = -1.25f;

    [field: Space(10)]
    [field: Header("Player State Timeouts")]
    [field: SerializeField]
    [field: Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
    public float JumpTimeout { get; private set; } = 0.50f;

    [field: SerializeField]
    [field: Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
    public float FallTimeout { get; private set; } = 0.15f;

    [field: SerializeField]
    [field:
        Tooltip(
            "Buffer time to continue applying gravity while in standing state, ensures player fully settles on the ground")]
    public float LandingTimeout { get; private set; } = 0.15f;

    [field: SerializeField]
    [field: Tooltip("Length of land animation for applying the landing speed multiplier and blocking turns")]
    public float LandingAnimTimeout { get; private set; } = 0.4f;

    [field: SerializeField]
    [field: Tooltip("Time required to pass between movement state changes before batching is re-enabled")]
    public float StateChangeSafetyTimeout { get; private set; } = 0.3f;

    [field: Space(10)]
    [field: Header("Player Grounded")]
    [field: SerializeField]
    [field: Tooltip("Additional offset for the grounded spherecast")]
    public float GroundedOffset { get; private set; } = -0.14f;

    [field: SerializeField]
    [field: Tooltip("What layers the character uses as ground")]
    public LayerMask GroundLayers { get; private set; }

    // 把 Inspector 的各组速度、加速、转向和跳跃值汇总成一个结构。服务器移动和本地预测都可直接借用同一结构。
    /// <summary>给实体烘焙与离线玩法预览共用的移动参数。</summary>
    public FirstPersonController.ControllerConsts BuildControllerConsts() {
        return new FirstPersonController.ControllerConsts {
            JumpHeight = JumpHeight,
            Gravity = Gravity,
            StandingFallSpeed = StandingFallSpeed,
            JumpTimeout =
                math.max(JumpTimeout, math.EPSILON), //给跳跃超时设一个最短延时，至少等一帧。
            FallTimeout = FallTimeout,
            LandingTimeout = LandingTimeout,
            LandingAnimTimeout = LandingAnimTimeout,
            StateChangeSafetyTimeout = StateChangeSafetyTimeout,
            GroundedOffset = GroundedOffset,
            GroundLayers = GroundLayers,
            TerminalVelocity = TerminalVelocity,
            // 地面有步行和冲刺参数。控制器从当前状态选一组，这会同时影响移动速度和动画跟随方式。
            Walk = new FirstPersonController.ControllerConsts.StateConsts {
                Speed = WalkSpeed,
                RotationSmoothTime = WalkRotationSmoothTime,
                SpeedChangeRate = WalkSpeedChangeRate,
                LandingSpeedMult = 1f,
                AnimationMotionScale = WalkAnimationMotionScale
            },
            Sprint = new FirstPersonController.ControllerConsts.StateConsts {
                Speed = SprintSpeed,
                RotationSmoothTime = SprintRotationSmoothTime,
                SpeedChangeRate = SprintSpeedChangeRate,
                LandingSpeedMult = SprintLandingSpeedMultiplier,
                AnimationMotionScale = SprintAnimationMotionScale
            }
        };
    }
}

public class PredictedPlayerControllerConstsBaker : Baker<PredictedPlayerControllerConstsAuthoring> {
    public override void Bake(PredictedPlayerControllerConstsAuthoring authoring) {
        var entity = GetEntity(TransformUsageFlags.None);
        AddComponent(entity, new PredictedPlayerControllerConsts {
            // 烘焙时把参数装到实体上。角色运行时有一个无需回头查找场景组件的数值副本。
            ControllerConsts = authoring.BuildControllerConsts()
        });
    }
}