using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using UnityEngine;

public partial class FirstPersonController {
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

        // 字段布局由预测快照使用。改动时一起检查生成的序列化代码。
        [GhostField(SendData = false)]
        public DollSingerStance Stance {
            get => (DollSingerStance)((StateFlags >> 11) & 3u);
            set => StateFlags = (StateFlags & ~(3u << 11)) | (((uint)value & 3u) << 11);
        }

        // 这些是需要用标志位表示的布尔值。
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

        // 内部字段由生成的 PredictedPlayerGhost 序列化代码负责。

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
}
