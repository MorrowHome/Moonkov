using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

using Unity.MP_FPS;

using static FirstPersonController;

public struct PredictedPlayerGhostState : IPredictedState
{
    public uint Tick { get; set; }

    public ControllerState PredictedControllerState;
    public float3 PredictedAccumulatedMovement;
}

public struct PredictedPlayerControllerConsts : IComponentData
{
    public ControllerConsts ControllerConsts;
}

public struct PredictedClientInput : IComponentData
{
    [GhostField]
    public bool SeenNewSnapshot;

    [GhostField]
    public uint LastProcessedServerTick;

    public int BeginInputIndex;
    public int InputCount;
}

public struct PredictedPlayerGhost : IComponentData
{
    public float2 LocalLookYawPitchDegrees;
    public float3 AccumulatedMovement;
    public float3 AppliedError;
    public float ErrorTimeout;
    public float RotationError;
    public float RotationErrorTimeout;
    public bool RequestApplyMovement;

    public float DisabledPredictionLerpFactor;

    public uint ClientPredictionEnabledTick;

    [GhostField] public bool ServerDisabledPrediction;

    [GhostField] public int InputIndex;

    [GhostField] public ControllerState ControllerState;
    [GhostField] public float3 AimPoint;
    [GhostField] public float CurrentHealth;
    [GhostField] public float MaxHealth;
    // Match-server authority. CurrentHealth/MaxHealth are derived compatibility
    // values for death, AI, animation and the compact HUD; never heal them directly.
    [GhostField] public bool BodyHealthInitialized;
    [GhostField] public float HeadHealth;
    [GhostField] public float ChestHealth;
    [GhostField] public float AbdomenHealth;
    [GhostField] public float LeftArmHealth;
    [GhostField] public float RightArmHealth;
    [GhostField] public float LeftLegHealth;
    [GhostField] public float RightLegHealth;
    [GhostField] public float Oxygen;
    [GhostField] public bool BreathableAir;
    [GhostField] public BodyPart LastHitPart;
    public float HypoxiaSeconds;
    
    [GhostField] public uint EquippedWeaponID;
    [GhostField] public float WeaponCooldown;   // Timer to control rate of fire
    
    [GhostField] public int CurrentAmmo;
    // Only the inactive weapon uses its stored ammo; CurrentAmmo owns the equipped weapon.
    [GhostField] public int StoredHaloAmmo;
    [GhostField] public int StoredRevolverAmmo;
    [GhostField] public bool InventoryWeapons;
    [GhostField] public int EquippedWeaponSlot;
    [GhostField] public Unity.Collections.FixedString64Bytes EquippedWeaponItem;
    [GhostField] public uint PrimaryWeaponID;
    [GhostField] public uint SecondaryWeaponID;
    [GhostField] public uint PistolWeaponID;
    [GhostField] public int PrimaryAmmo;
    [GhostField] public int SecondaryAmmo;
    [GhostField] public int PistolAmmo;
    [GhostField] public float LastDamageAmount;
    [GhostField] public uint LastHitTick;
    [GhostField] public uint LastShotTick;
    [GhostField] public uint LastJumpTick;
    [GhostField] public uint LastLandTick;
    [GhostField] public uint LastReloadTick;
    [GhostField] public uint LastGrenadeShotTick;
    [GhostField] public float ReloadTimer;
    [GhostField] public int ReloadTargetAmmo;
}
