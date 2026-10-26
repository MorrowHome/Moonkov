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
    [GhostField] public bool OnlineHuman;
    [GhostField] public int OnlineTeam;
    [GhostField] public bool ClashEnabled, ClashCanAct, ClashDarkSight;
    [GhostField] public Unity.MP_FPS.ClashLife ClashLife;
    [GhostField] public int ClashTeam;
    [GhostField] public float ClashMaxHealthFraction;
    [GhostField] public float TacticalWireSeconds, TacticalCoughSeconds, TacticalPoisonSeconds;
    [GhostField] public float TacticalSprintStamina;
    [GhostField] public bool TacticalSprintExhausted;
    [GhostField(Quantization = 100)] public float CarriedWeight;
    [GhostField] public bool ShieldEquipped, HeadDomeEquipped, ShieldRegenerating;
    [GhostField] public byte SubspaceBackpack; // Equipped inventory: 0 none, 1 light, 2 expedition, 3 cargo, 4 scout.
    [GhostField] public bool HeadDomeStandard;
    [GhostField(Quantization = 100)] public float ShieldCharge, ShieldEnergy, ShieldRecoveryDelay;
    [GhostField] public uint ShieldHitSequence, ShieldBreakSequence, ShieldRestoreSequence;
    [GhostField] public float3 ShieldHitPoint;
    [GhostField(Quantization = 100)] public float ShieldHitStrength;
    // Server bindings are item identities, not a second inventory or client authority.
    public bool ProtectionInitialized;
    public Unity.Collections.FixedString64Bytes ShieldItem, HeadDomeItem;
    [GhostField] public byte MeleeWeapon, MeleeAttackWeapon, MeleeAttack, MeleePhase, MeleeCombo, MeleeFeedback;
    [GhostField] public uint MeleeAttackSequence, MeleeCancelSequence, MeleeHitSequence, MeleeRequestId, MeleeStartTick;
    [GhostField] public float MeleeElapsed, MeleeRecoveryRemaining, MeleeStamina;
    [GhostField] public int MeleeEpoch;
    [GhostField] public float3 MeleeHitPoint;
    [GhostField] public bool MeleeHitBlocked;
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
    [GhostField] public float HypoxiaSeconds;
    [GhostField] public float OxygenGraceRemaining;
    [GhostField] public float OxygenDrainPerSecond;
    [GhostField] public float OxygenSupplyPerSecond;
    [GhostField] public float AmbientPressureKPa;
    [GhostField] public float AmbientOxygenPartialPressureKPa;
    [GhostField] public float Energy;
    [GhostField] public float Hydration;
    [GhostField] public float Satiety;
    // Server exposure timers; snapshots carry the resulting resources/effects.
    public float StarvationSeconds, DehydrationSeconds;
    // Server-only reload intent retained until the current draw completes.
    public bool ReloadQueued;
    // Server action guard: ongoing hunger, thirst and hypoxia must not prevent
    // a player from searching for and using lifesaving supplies.
    public uint NonPhysiologicalDamageSequence;
    // Server-only, accumulated between tactical updates; never client-authored.
    public float PendingBulletWound;
    public ClashDamageSource PendingBulletWoundSource;
    
    [GhostField] public uint EquippedWeaponID;
    [GhostField] public float WeaponCooldown;   // Elapsed firing time, including fractional tick remainder.
    [GhostField] public float WeaponEquipRemaining, FireBufferRemaining;
    // Slot recovery survives switching so drawing a weapon cannot bypass its firing interval.
    [GhostField] public float PrimaryFireRecovery, SecondaryFireRecovery, PistolFireRecovery;
    
    [GhostField] public ushort EquippedModules, PrimaryModules, SecondaryModules, PistolModules;
    [GhostField] public float AlienSurgeSeconds;
    [GhostField(Quantization = 100)] public float MucusBlindSeconds;
    [GhostField] public AlienFoodEffect AlienFoodEffect;
    [GhostField] public float AlienFoodEffectSeconds;
    [GhostField] public int BurstRemaining, BurstSequence;
    [GhostField] public bool BurstTriggerHeld;
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
    // Tissue impact presentation; physiological/status ticks must not retrigger impact audio.
    [GhostField] public uint LastImpactTick;
    [GhostField] public uint LastShotTick;
    [GhostField] public uint LastJumpTick;
    [GhostField] public uint LastLandTick;
    [GhostField] public uint LastReloadTick;
    // Server-authored; one event when a container is opened or searched.
    [GhostField] public uint LootSearchSequence;
    [GhostField] public uint LastGrenadeShotTick;
    [GhostField] public float ReloadTimer;
    [GhostField] public int ReloadTargetAmmo;
}
