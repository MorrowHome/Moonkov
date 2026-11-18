// PlayerInput 表达玩家想做什么。网络命令加上输入 tick 和插值 tick，客户端预测与服务端移动系统都能按时间读取。
using System;
using Unity.MP_FPS.DollSinger;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public struct PlayerInput {
    // 一条指令里有很多开关。每种操作占一个位，可以一起发送站姿、开火、举盾等状态。
    [Flags]
    public enum InputFlag {
        Jump = 1 << 0,
        Shoot = 1 << 1,
        Sprint = 1 << 2,
        Reload = 1 << 3,
        Aim = 1 << 4,
        ThirdPerson = 1 << 5,
        EquipHalo = 1 << 6,
        EquipRevolver = 1 << 7,
        EquipPistol = 1 << 8,
        HaloLightDisabled = 1 << 9,
        Crouch = 1 << 10,
        Prone = 1 << 11,
        Tiptoe = 1 << 12,
        FireHeld = 1 << 13,
        FireSuppressed = 1 << 14,
        HaloDefense = 1 << 15,
        HaloTransfer = 1 << 16,
        HaloGuard = 1 << 17,
        HaloAnchor = 1 << 18,
        HaloMode = 1 << 19,
        HaloScan = 1 << 20
    }

    public float2 MoveInput;
    public float2 LookYawPitchDegrees;

    // Head-only angles relative to the normal view; locomotion and weapons keep their base facing.
    // 自由观察只给头或相机一个相对角度。身体正常转向仍在 LookYawPitchDegrees，后续模拟靠它判断移动方向。
    public float2 FreeLookOffset;
    public float Lean;
    public bool FreeLooking;

    // Camera-centre target intent. The server still owns the shot origin, raycast and damage.
    // 这是按准星得到的预期目标点。服务器还会从它认可的起点重算射线，命中结算由服务端给出。
    public float3 AimPoint;

    // Ghost identity includes its spawn tick so a recycled id cannot change the recipient.
    public int HaloTransferGhostId;
    public uint HaloTransferSpawnTick;
    public uint HaloLightingSettings;
    public uint InputFlags;

    public DollSingerStance Stance =>
        (InputFlags & (uint)InputFlag.Prone) != 0 ? DollSingerStance.Prone :
        (InputFlags & (uint)InputFlag.Crouch) != 0 ? DollSingerStance.Crouching :
        (InputFlags & (uint)InputFlag.Tiptoe) != 0 ? DollSingerStance.Tiptoe : DollSingerStance.Standing;

    public void SetStance(DollSingerStance stance) {
        SetFlag(InputFlag.Crouch, stance == DollSingerStance.Crouching);
        SetFlag(InputFlag.Prone, stance == DollSingerStance.Prone);
        SetFlag(InputFlag.Tiptoe, stance == DollSingerStance.Tiptoe);
    }

    public bool Jump => (InputFlags & (uint)InputFlag.Jump) != 0;
    public bool Shoot => (InputFlags & (uint)InputFlag.Shoot) != 0;
    public bool FireHeld => (InputFlags & (uint)InputFlag.FireHeld) != 0;
    public bool FireSuppressed => (InputFlags & (uint)InputFlag.FireSuppressed) != 0;
    public bool HaloDefense => (InputFlags & (uint)InputFlag.HaloDefense) != 0;
    public bool HaloTransfer => (InputFlags & (uint)InputFlag.HaloTransfer) != 0;
    public bool HaloGuard => HaloTransfer && (InputFlags & (uint)InputFlag.HaloGuard) != 0;
    public bool HaloAnchor => (InputFlags & (uint)InputFlag.HaloAnchor) != 0;
    public bool HaloMode => (InputFlags & (uint)InputFlag.HaloMode) != 0;
    public bool HaloScan => (InputFlags & (uint)InputFlag.HaloScan) != 0;
    public bool Reload => (InputFlags & (uint)InputFlag.Reload) != 0;
    public bool Sprint => (InputFlags & (uint)InputFlag.Sprint) != 0;
    public bool Aim => (InputFlags & (uint)InputFlag.Aim) != 0;
    public bool ThirdPerson => (InputFlags & (uint)InputFlag.ThirdPerson) != 0;
    public bool EquipHalo => (InputFlags & (uint)InputFlag.EquipHalo) != 0;
    public bool EquipRevolver => (InputFlags & (uint)InputFlag.EquipRevolver) != 0;
    public bool EquipPistol => (InputFlags & (uint)InputFlag.EquipPistol) != 0;
    public bool HaloLightEnabled => (InputFlags & (uint)InputFlag.HaloLightDisabled) == 0;

    public void SetFlag(InputFlag flag, bool set) {
        if (set)
            InputFlags |= (uint)flag;
        else
            InputFlags &= ~(uint)flag;
    }

    // Apply the same numeric contract to client prediction and server simulation.
    // Command payloads are player intent, never trusted movement or shot state.
    // 压住移速输入、视角和其他数据的范围，处理 NaN 等异常值，再按自由视角和光环动作的互斥规则去掉冲突的操作。
    public PlayerInput Sanitized() {
        var result = this;
        const uint k_KnownFlags = (uint)(InputFlag.Jump | InputFlag.Shoot | InputFlag.Sprint | InputFlag.Reload |
                                         InputFlag.Aim | InputFlag.ThirdPerson | InputFlag.EquipHalo |
                                         InputFlag.EquipRevolver | InputFlag.EquipPistol | InputFlag.HaloLightDisabled |
                                         InputFlag.Crouch | InputFlag.Prone | InputFlag.Tiptoe | InputFlag.FireHeld |
                                         InputFlag.FireSuppressed | InputFlag.HaloDefense | InputFlag.HaloTransfer |
                                         InputFlag.HaloGuard | InputFlag.HaloAnchor | InputFlag.HaloMode |
                                         InputFlag.HaloScan);
        result.InputFlags &= k_KnownFlags;
        result.HaloLightingSettings = HaloLighting.Sanitize(HaloLightingSettings);
        result.MoveInput = math.all(math.isfinite(MoveInput)) ? math.clamp(MoveInput, -1f, 1f) : float2.zero;
        // 先修正每根轴的范围，再把向量长度限制在一以内。这样同时按横向和前进不会让斜走比直走更快。
        float moveLengthSq = math.lengthsq(result.MoveInput);
        if (moveLengthSq > 1f) result.MoveInput *= math.rsqrt(moveLengthSq);
        // 有效视角会变成有界的角度。无效视角则一起关掉本次移动和开火，避免把异常坐标带进模拟。
        if (math.all(math.isfinite(LookYawPitchDegrees))) {
            result.LookYawPitchDegrees.x = math.fmod(LookYawPitchDegrees.x, 360f);
            result.LookYawPitchDegrees.y = math.clamp(LookYawPitchDegrees.y, -85f, 85f);
        } else {
            result.LookYawPitchDegrees = float2.zero;
            result.MoveInput = float2.zero;
            result.SetFlag(InputFlag.Shoot, false);
            result.SetFlag(InputFlag.FireSuppressed, true);
        }
        result.FreeLookOffset = math.all(math.isfinite(FreeLookOffset))
            ? math.clamp(FreeLookOffset, new float2(-70f, -45f), new float2(70f, 45f))
            : float2.zero;
        result.Lean = math.isfinite(Lean) ? math.clamp(Lean, -1f, 1f) : 0f;
        result.AimPoint = math.all(math.isfinite(AimPoint)) && math.isfinite(math.lengthsq(AimPoint))
            ? AimPoint
            : float3.zero;
        if (result.FireSuppressed) {
            result.SetFlag(InputFlag.Shoot, false);
            result.SetFlag(InputFlag.FireHeld, false);
            result.SetFlag(InputFlag.HaloTransfer, false);
        }
        result.ApplyTransferIntent();
        return result;
    }

    // 普通操作与部署、支援、自由观察有互斥关系。先清冲突的动作，再根据是否真的转移来决定保不保留目标 ID。
    private void ApplyTransferIntent() {
        if (FreeLooking || FireSuppressed) {
            SetFlag(InputFlag.HaloAnchor, false);
            SetFlag(InputFlag.HaloMode, false);
            SetFlag(InputFlag.HaloScan, false);
        }
        if (HaloAnchor || HaloMode)
            InputFlags &= ~(uint)(InputFlag.Shoot | InputFlag.FireHeld | InputFlag.Aim | InputFlag.Reload |
                                  InputFlag.HaloDefense | InputFlag.HaloTransfer | InputFlag.EquipHalo |
                                  InputFlag.EquipRevolver | InputFlag.EquipPistol);
        if (FreeLooking || FireSuppressed) SetFlag(InputFlag.HaloTransfer, false);
        if (!HaloTransfer) {
            SetFlag(InputFlag.HaloGuard, false);
            HaloTransferGhostId = 0;
            HaloTransferSpawnTick = 0;
            return;
        }
        InputFlags &= ~(uint)(InputFlag.Shoot | InputFlag.FireHeld | InputFlag.Aim | InputFlag.Reload |
                              InputFlag.HaloDefense | InputFlag.EquipHalo | InputFlag.EquipRevolver |
                              InputFlag.EquipPistol);
    }

    // 跨帧合并时，允许更新连续状态就用当前的移动和视角。短按触发的跳跃、开火和换装会累积下来。
    public void UpdateFrom(in PlayerInput input, bool updateContinuousState = true) {
        bool hadBufferedShot = Shoot;
        var bufferedAimPoint = AimPoint;
        var bufferedLook = LookYawPitchDegrees;
        float bufferedLean = Lean;
        bool bufferedThirdPerson = ThirdPerson;
        if (updateContinuousState) {
            HaloLightingSettings = input.HaloLightingSettings;
            MoveInput = input.MoveInput;
            LookYawPitchDegrees = input.LookYawPitchDegrees;
            FreeLookOffset = input.FreeLookOffset;
            Lean = input.Lean;
            FreeLooking = input.FreeLooking;
            AimPoint = input.AimPoint;
            HaloTransferGhostId = input.HaloTransferGhostId;
            HaloTransferSpawnTick = input.HaloTransferSpawnTick;
            // 短按类操作在多个渲染帧内只亮一瞬。把旧命令的这些位并进来，下一拍才不会漏掉一次操作。
            const uint events = (uint)(InputFlag.Jump | InputFlag.Shoot | InputFlag.Reload | InputFlag.EquipHalo |
                                       InputFlag.EquipRevolver | InputFlag.EquipPistol);
            InputFlags = (InputFlags & events) | input.InputFlags;
        }
        // 短按开火合并到后面的帧时，应带上按下当时那次瞄准，避免之后移开鼠标让这枪换了方向。
        // Preserve the target belonging to a buffered single-shot press.
        if (input.Shoot && !HaloTransfer) {
            AimPoint = input.AimPoint;
            LookYawPitchDegrees = input.LookYawPitchDegrees;
            Lean = input.Lean;
            SetFlag(InputFlag.ThirdPerson, input.ThirdPerson);
        } else if (hadBufferedShot && !HaloTransfer) {
            AimPoint = bufferedAimPoint;
            LookYawPitchDegrees = bufferedLook;
            Lean = bufferedLean;
            SetFlag(InputFlag.ThirdPerson, bufferedThirdPerson);
        }
        InputFlags |= input.InputFlags & (uint)(InputFlag.Jump | InputFlag.Shoot | InputFlag.Reload |
                                                InputFlag.EquipHalo | InputFlag.EquipRevolver | InputFlag.EquipPistol);
        if (FireSuppressed) {
            SetFlag(InputFlag.Shoot, false);
            SetFlag(InputFlag.FireHeld, false);
        }
        ApplyTransferIntent();
    }
}

public struct PlayerInputComponent : IComponentData {
    public PlayerInput Input;
}

public struct PlayerClientCommandInputLookup : IComponentData {
    public Entity ClientCommandInputEntity;
}

// 这两种组件先装当前采样。网络实际发送的是带命令 tick 的 ClientCommandInput，供另一端按拍重放。
public struct ClientInput : IComponentData {
    public PlayerInput PlayerInput;
    public void SetInput(int playerIndex, in PlayerInput playerInput) { PlayerInput = playerInput.Sanitized(); }
}

public struct ClientMovementInput : IComponentData {
    public PlayerInput PlayerInput;
    public void SetInput(int playerIndex, in PlayerInput playerInput) { PlayerInput = playerInput.Sanitized(); }

    public void UpdateFrom(in ClientMovementInput clientInput, bool updateContinuousState = true) {
        PlayerInput.UpdateFrom(clientInput.PlayerInput, updateContinuousState);
    }
}

public struct ClientCommandInput : ICommandData {
    public NetworkTick Tick { get; set; }
    // 记录采样时客户端渲染远端对象的时间。插值让远端画面的时间落后于服务器模拟，以便在已有快照之间平滑移动。
    public NetworkTick ClientInterpolationTick;
    public PlayerInput PlayerInput;

    public void SetPlayerMovementInput(int playerIndex, in PlayerInput playerInput) {
        PlayerInput = playerInput.Sanitized();
    }

    public void UpdatePlayerInput(int playerIndex, in PlayerInput playerInput) { PlayerInput.UpdateFrom(playerInput); }

    public bool TryGetPlayerMovementInput(int playerIndex, out PlayerInput playerInput) {
        playerInput = PlayerInput.Sanitized();
        return true;
    }

    public void UpdateFrom(in ClientMovementInput clientInput, bool updateContinuousState = true) {
        PlayerInput.UpdateFrom(clientInput.PlayerInput, updateContinuousState);
    }

    public void SetFrom(in ClientMovementInput clientInput) { PlayerInput = clientInput.PlayerInput.Sanitized(); }
}