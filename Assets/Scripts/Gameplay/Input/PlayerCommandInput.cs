using Unity.MP_FPS.DollSinger;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public struct PlayerInput
{
    public enum InputFlag
    {
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
        FireSuppressed = 1 << 14
    }

    public float2 MoveInput;
    public float2 LookYawPitchDegrees;
    // Head-only angles relative to the normal view; locomotion and weapons keep their base facing.
    public float2 FreeLookOffset;
    public float Lean;
    public bool FreeLooking;
    // Camera-centre target intent. The server still owns the shot origin, raycast and damage.
    public float3 AimPoint;

    public uint HaloLightingSettings;
    public uint InputFlags;

    public DollSingerStance Stance => (InputFlags & (uint)InputFlag.Prone) != 0 ? DollSingerStance.Prone :
        (InputFlags & (uint)InputFlag.Crouch) != 0 ? DollSingerStance.Crouching :
        (InputFlags & (uint)InputFlag.Tiptoe) != 0 ? DollSingerStance.Tiptoe : DollSingerStance.Standing;

    public void SetStance(DollSingerStance stance)
    {
        SetFlag(InputFlag.Crouch, stance == DollSingerStance.Crouching);
        SetFlag(InputFlag.Prone, stance == DollSingerStance.Prone);
        SetFlag(InputFlag.Tiptoe, stance == DollSingerStance.Tiptoe);
    }

    public bool Jump => (InputFlags & (uint)InputFlag.Jump) != 0;
    public bool Shoot => (InputFlags & (uint)InputFlag.Shoot) != 0;
    public bool FireHeld => (InputFlags & (uint)InputFlag.FireHeld) != 0;
    public bool FireSuppressed => (InputFlags & (uint)InputFlag.FireSuppressed) != 0;
    public bool Reload => (InputFlags & (uint)InputFlag.Reload) != 0;
    public bool Sprint => (InputFlags & (uint)InputFlag.Sprint) != 0;
    public bool Aim => (InputFlags & (uint)InputFlag.Aim) != 0;
    public bool ThirdPerson => (InputFlags & (uint)InputFlag.ThirdPerson) != 0;
    public bool EquipHalo => (InputFlags & (uint)InputFlag.EquipHalo) != 0;
    public bool EquipRevolver => (InputFlags & (uint)InputFlag.EquipRevolver) != 0;
    public bool EquipPistol => (InputFlags & (uint)InputFlag.EquipPistol) != 0;
    public bool HaloLightEnabled => (InputFlags & (uint)InputFlag.HaloLightDisabled) == 0;

    public void SetFlag(InputFlag flag, bool set)
    {
        if (set)
        {
            InputFlags |= (uint)flag;
        }
        else
        {
            InputFlags &= ~(uint)flag;
        }
    }

    // Apply the same numeric contract to client prediction and server simulation.
    // Command payloads are player intent, never trusted movement or shot state.
    public PlayerInput Sanitized()
    {
        var result = this;
        const uint knownFlags = (uint)(InputFlag.Jump | InputFlag.Shoot | InputFlag.Sprint | InputFlag.Reload |
            InputFlag.Aim | InputFlag.ThirdPerson | InputFlag.EquipHalo | InputFlag.EquipRevolver |
            InputFlag.EquipPistol | InputFlag.HaloLightDisabled | InputFlag.Crouch | InputFlag.Prone | InputFlag.Tiptoe |
            InputFlag.FireHeld | InputFlag.FireSuppressed);
        result.InputFlags &= knownFlags;
        result.HaloLightingSettings = HaloLighting.Sanitize(HaloLightingSettings);
        result.MoveInput = math.all(math.isfinite(MoveInput)) ? math.clamp(MoveInput, -1f, 1f) : float2.zero;
        float moveLengthSq = math.lengthsq(result.MoveInput);
        if (moveLengthSq > 1f) result.MoveInput *= math.rsqrt(moveLengthSq);
        if (math.all(math.isfinite(LookYawPitchDegrees)))
        {
            result.LookYawPitchDegrees.x = math.fmod(LookYawPitchDegrees.x, 360f);
            result.LookYawPitchDegrees.y = math.clamp(LookYawPitchDegrees.y, -85f, 85f);
        }
        else
        {
            result.LookYawPitchDegrees = float2.zero;
            result.MoveInput = float2.zero;
            result.SetFlag(InputFlag.Shoot, false);
            result.SetFlag(InputFlag.FireSuppressed, true);
        }
        result.FreeLookOffset = math.all(math.isfinite(FreeLookOffset))
            ? math.clamp(FreeLookOffset, new float2(-70f, -45f), new float2(70f, 45f)) : float2.zero;
        result.Lean = math.isfinite(Lean) ? math.clamp(Lean, -1f, 1f) : 0f;
        result.AimPoint = math.all(math.isfinite(AimPoint)) && math.isfinite(math.lengthsq(AimPoint))
            ? AimPoint : float3.zero;
        if (result.FireSuppressed)
        {
            result.SetFlag(InputFlag.Shoot, false);
            result.SetFlag(InputFlag.FireHeld, false);
        }
        return result;
    }

    public void UpdateFrom(in PlayerInput input, bool updateContinuousState = true)
    {
        bool hadBufferedShot = Shoot;
        float3 bufferedAimPoint = AimPoint;
        float2 bufferedLook = LookYawPitchDegrees;
        float bufferedLean = Lean;
        bool bufferedThirdPerson = ThirdPerson;
        if (updateContinuousState)
        {
            HaloLightingSettings = input.HaloLightingSettings;
            MoveInput = input.MoveInput;
            LookYawPitchDegrees = input.LookYawPitchDegrees;
            FreeLookOffset = input.FreeLookOffset;
            Lean = input.Lean;
            FreeLooking = input.FreeLooking;
            AimPoint = input.AimPoint;
            const uint events = (uint)(InputFlag.Jump | InputFlag.Shoot | InputFlag.Reload | InputFlag.EquipHalo | InputFlag.EquipRevolver | InputFlag.EquipPistol);
            InputFlags = (InputFlags & events) | input.InputFlags;
        }
        // Preserve the target belonging to a buffered single-shot press.
        if (input.Shoot)
        {
            AimPoint = input.AimPoint;
            LookYawPitchDegrees = input.LookYawPitchDegrees;
            Lean = input.Lean;
            SetFlag(InputFlag.ThirdPerson, input.ThirdPerson);
        }
        else if (hadBufferedShot)
        {
            AimPoint = bufferedAimPoint;
            LookYawPitchDegrees = bufferedLook;
            Lean = bufferedLean;
            SetFlag(InputFlag.ThirdPerson, bufferedThirdPerson);
        }
        InputFlags |= input.InputFlags & (uint)(InputFlag.Jump | InputFlag.Shoot | InputFlag.Reload | InputFlag.EquipHalo | InputFlag.EquipRevolver | InputFlag.EquipPistol);
        if (FireSuppressed)
        {
            SetFlag(InputFlag.Shoot, false);
            SetFlag(InputFlag.FireHeld, false);
        }
    }
}

public struct PlayerInputComponent : IComponentData
{
    public PlayerInput Input;
}

public struct PlayerClientCommandInputLookup : IComponentData
{
    public Entity ClientCommandInputEntity;
}

public struct ClientInput : IComponentData
{
    public PlayerInput PlayerInput;

    public void SetInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput.Sanitized();
    }
}

public struct ClientMovementInput : IComponentData
{
    public PlayerInput PlayerInput;

    public void SetInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput.Sanitized();
    }

    public void UpdateFrom(in ClientMovementInput clientInput, bool updateContinuousState = true)
    {
        PlayerInput.UpdateFrom(clientInput.PlayerInput, updateContinuousState);
    }
}

public struct ClientCommandInput : ICommandData
{
    public NetworkTick Tick { get; set; }

    public NetworkTick ClientInterpolationTick;

    public PlayerInput PlayerInput;

    public void SetPlayerMovementInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput.Sanitized();
    }

    public void UpdatePlayerInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput.UpdateFrom(playerInput);
    }

    public bool TryGetPlayerMovementInput(int playerIndex, out PlayerInput playerInput)
    {
        playerInput = PlayerInput.Sanitized();
        return true;
    }

    public void UpdateFrom(in ClientMovementInput clientInput, bool updateContinuousState = true)
    {
        PlayerInput.UpdateFrom(clientInput.PlayerInput, updateContinuousState);
    }

    public void SetFrom(in ClientMovementInput clientInput)
    {
        PlayerInput = clientInput.PlayerInput.Sanitized();
    }
}
