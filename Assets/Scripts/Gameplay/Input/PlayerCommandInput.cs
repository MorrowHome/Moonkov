using NUnit.Framework.Constraints;
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
        Aim = 1 << 4
    }

    public float2 MoveInput;
    public float2 LookYawPitchDegrees;
    // Camera-centre target intent. The server still owns the shot origin, raycast and damage.
    public float3 AimPoint;

    public uint InputFlags; // 4 (16)

    public bool Jump => (InputFlags & (uint)InputFlag.Jump) != 0;
    public bool Shoot => (InputFlags & (uint)InputFlag.Shoot) != 0;
    public bool Reload => (InputFlags & (uint)InputFlag.Reload) != 0;
    public bool Sprint => (InputFlags & (uint)InputFlag.Sprint) != 0;
    public bool Aim => (InputFlags & (uint)InputFlag.Aim) != 0;

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

    public void UpdateFrom(in PlayerInput input)
    {
        // Preserve the target belonging to a buffered single-shot press.
        if (input.Shoot)
        {
            AimPoint = input.AimPoint;
            LookYawPitchDegrees = input.LookYawPitchDegrees;
        }
        InputFlags |= input.InputFlags;
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
        PlayerInput = playerInput;
    }
}

public struct ClientMovementInput : IComponentData
{
    public PlayerInput PlayerInput;

    public void SetInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput;
    }

    public void UpdateFrom(in ClientMovementInput clientInput)
    {
        PlayerInput.UpdateFrom(clientInput.PlayerInput);
    }
}

public struct ClientCommandInput : ICommandData
{
    public NetworkTick Tick { get; set; }

    public NetworkTick ClientInterpolationTick;

    public PlayerInput PlayerInput;

    public void SetPlayerMovementInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput;
    }

    public void UpdatePlayerInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput.UpdateFrom(playerInput);
    }

    public bool TryGetPlayerMovementInput(int playerIndex, out PlayerInput playerInput)
    {
        playerInput = PlayerInput;
        return true;
    }

    public void UpdateFrom(in ClientMovementInput clientInput)
    {
        PlayerInput.UpdateFrom(clientInput.PlayerInput);
    }

    public void SetFrom(in ClientMovementInput clientInput)
    {
        PlayerInput = clientInput.PlayerInput;
    }
}
