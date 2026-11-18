// 玩家在 ECS 中的网络状态表。服务端移动、战斗和生存逻辑写入数据，预测系统和 HUD、动作、声音等表现从这里读取。
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.MP_FPS;
using static FirstPersonController;

public struct PredictedPlayerGhostState : IPredictedState {
    public uint Tick { get; set; }
    public ControllerState PredictedControllerState;
    public float3 PredictedAccumulatedMovement;
}

public struct PredictedPlayerControllerConsts : IComponentData {
    public ControllerConsts ControllerConsts;
}

public struct PredictedClientInput : IComponentData {
    [GhostField] public bool SeenNewSnapshot;
    // 这是输入 Ghost 的处理进度。命令缓冲保存按拍的原始操作，角色自身的姿态与血量在另一个组件里。
    [GhostField] public uint LastProcessedServerTick;
    public int BeginInputIndex;
    public int InputCount;
}

// GhostField 标记 NetCode 需要同步的字段。开头的位移累积和本地视角留在本端，角色的血量、装备和控制状态会随快照传输。
public struct PredictedPlayerGhost : IComponentData {
    public float2 LocalLookYawPitchDegrees;
    public float3 AccumulatedMovement;
    public float3 AppliedError;
    public float ErrorTimeout;
    public float RotationError;
    public float RotationErrorTimeout;
    public bool RequestApplyMovement;
    public float DisabledPredictionLerpFactor;
    public uint ClientPredictionEnabledTick;
    // 服务器可以同步预测开关。客户端的本地控制和插值权衡需要两边都能看到这个状态。
    [GhostField] public bool ServerDisabledPrediction;
    [GhostField] public int InputIndex;
    [GhostField] public ControllerState ControllerState;
    [GhostField] public float3 AimPoint;
    [GhostField] public float CurrentHealth;
    [GhostField] public float MaxHealth;
    // 网络编号和玩法里的队伍是两个概念。这组变量表达在线玩家、队伍、随从编组和当前赏金玩法条件。
    [GhostField] public bool OnlineHuman;
    [GhostField] public int OnlineTeam;
    [GhostField] public int CompanionSquadId;
    [GhostField] public bool ClashEnabled, ClashCanAct, ClashDarkSight;
    [GhostField] public ClashLife ClashLife;
    [GhostField] public int ClashTeam;
    [GhostField] public float ClashMaxHealthFraction;
    [GhostField] public float TacticalWireSeconds, TacticalCoughSeconds, TacticalPoisonSeconds;
    [GhostField] public bool TacticalBleeding;
    [GhostField] public float TacticalSprintStamina;
    [GhostField] public bool TacticalSprintExhausted;
    // 持续变化的数据可以先按精度取整再同步。例如 Quantization 为 100 时，预期保留约两位小数。
    [GhostField(Quantization = 100)] public float CarriedWeight;
    [GhostField] public bool ConsumingSupply;
    [GhostField] public bool TimedActionActive;

    // 供客户端画出手部动作，真实使用和消耗由库存及战术系统决定。
    // 动作字段记录正在做哪件事、操作的是哪个物品以及还要多久。序号变化可让动画和声音认出这是新的一轮动作。
    [GhostField] public byte HandAction;
    [GhostField] public uint HandActionSequence;
    [GhostField] public Unity.Collections.FixedString64Bytes HandActionItem;
    [GhostField(Quantization = 100)] public float HandActionRemaining, HandActionDuration;
    [GhostField(Quantization = 1000)] public float3 HandActionTarget;
    [GhostField] public bool HeadDomeEquipped;

    // 开火、主动防御和充电会共同改变光环能量。能量、上限和当前防御状态一起同步，客户端才能画对能量条和外观。
    [GhostField] public bool HaloEquipped,
        HaloEnergyInitialized,
        HaloDefending,
        HaloDefenseHeld,
        HaloDefenseBroken,
        HaloRegenerating;

    [GhostField(Quantization = 100)] public float HaloEnergy, HaloCapacity, HaloRecoveryDelay;
    [GhostField(Quantization = 100)] public float HaloRechargeEnergy;
    [GhostField(Quantization = 100)] public float HaloLightGlare;
    [GhostField(Quantization = 100)] public float3 HaloGlareSource;
    [GhostField] public uint HaloRechargeSequence;
    // 转移或部署是一个有阶段的动作。客户端收到目标、剩余时间和位置后，可以把光环表现跟上服务器进度。
    [GhostField] public HaloTransferPhase HaloTransferPhase;
    [GhostField] public HaloDeploymentMode HaloDeploymentMode;
    [GhostField] public Entity HaloTransferTarget;
    [GhostField(Quantization = 100)] public float HaloTransferRemaining;
    [GhostField(Quantization = 1000)] public float3 HaloTransferPoint;

    // 按住一次只会开始一次动作。选另一名接收者前先松开。
    public bool HaloTransferLatched, HaloGuardLatched;
    public bool HaloAnchorLatched, HaloModeLatched;
    public float HaloSentrySearchRemaining;
    public Entity HaloSentryTarget;
    public bool HaloScanLatched;
    public uint HaloScanSequence;
    public float HaloScanCooldown;
    public uint HaloTransferWeapon;

    // 旧版表现和测试通过这些别名读取同一份资源。
    public bool ShieldEquipped { get => HaloEquipped; set => HaloEquipped = value; }
    public bool ShieldRegenerating { get => HaloRegenerating; set => HaloRegenerating = value; }
    public float ShieldCharge { get => HaloEnergy; set => HaloEnergy = value; }
    public float ShieldEnergy { get => HaloEnergy; set => HaloEnergy = value; }
    public float ShieldRecoveryDelay { get => HaloRecoveryDelay; set => HaloRecoveryDelay = value; }
    [GhostField] public byte SubspaceBackpack; // 当前背包。0 未装备，1 轻型，2 远征，3 货运，4 侦察。
    [GhostField] public byte SubspaceBadge; // 当前胸挂。0 未装备，1 侦察，2 标准，3 野战。
    [GhostField] public bool HeadDomeStandard;
    [GhostField] public uint ShieldHitSequence, ShieldBreakSequence, ShieldRestoreSequence;
    [GhostField] public float3 ShieldHitPoint;
    [GhostField(Quantization = 100)] public float ShieldHitStrength;

    // 这些服务端字段记录真实库存中的物品 ID，库存仍由服务器管理。
    public bool ProtectionInitialized;
    public Unity.Collections.FixedString64Bytes ShieldItem, HeadDomeItem;
    // 玩家手里武器和本次近战实际使用的武器分开记。攻击阶段、次数和反馈字段给预测以外的动画及声音消费。
    [GhostField] public byte MeleeWeapon, MeleeAttackWeapon, MeleeAttack, MeleePhase, MeleeCombo, MeleeFeedback;
    [GhostField] public uint MeleeAttackSequence, MeleeCancelSequence, MeleeHitSequence, MeleeRequestId, MeleeStartTick;
    [GhostField] public float MeleeElapsed, MeleeRecoveryRemaining, MeleeStamina;
    [GhostField] public int MeleeEpoch;
    [GhostField] public float3 MeleeHitPoint;
    [GhostField] public bool MeleeHitBlocked;

    // 生命值由战局服务器结算。CurrentHealth 和 MaxHealth 是从部位状态推导的兼容值，
    // 用于死亡、AI、动画和紧凑 HUD。治疗请写入对应部位。
    // 部位生命分开保存，护具和治疗可以对应到头、胸、腹和四肢。BodyHealthInitialized 说明已经有本局的有效部位数据。
    [GhostField] public bool BodyHealthInitialized;
    [GhostField] public float HeadHealth;
    [GhostField] public float ChestHealth;
    [GhostField] public float AbdomenHealth;
    [GhostField] public float LeftArmHealth;
    [GhostField] public float RightArmHealth;
    [GhostField] public float LeftLegHealth;
    [GhostField] public float RightLegHealth;
    // 氧气存量之外还同步所在环境、供气与消耗速度。HUD 可以解释氧气的变化，规则本身在 RaidOxygen。
    [GhostField] public float Oxygen;
    [GhostField] public bool BreathableAir;
    [GhostField] public BodyPart LastHitPart;
    [GhostField] public float HypoxiaSeconds;
    [GhostField] public float OxygenGraceRemaining;
    [GhostField] public float OxygenDrainPerSecond;
    [GhostField] public float OxygenSupplyPerSecond;
    [GhostField] public float AmbientPressureKPa;
    [GhostField] public float AmbientOxygenPartialPressureKPa;
    // 能量、饮水和饱腹表示几种不同的生存状态，迟迟缺少补给时由生存规则施加影响。
    [GhostField] public float Energy;
    [GhostField] public float Hydration;
    [GhostField] public float Satiety;

    // 服务端保存暴露计时，再把结果与效果同步到客户端。
    public float StarvationSeconds, DehydrationSeconds;

    // 换武器动作还没结束时，在服务端暂存这次装填意图。
    public bool ReloadQueued;

    // 服务端判定能否搜索和使用救命补给时，
    // 应让饥渴、缺氧中的玩家仍有机会拿到补给。
    public uint NonPhysiologicalDamageSequence;

    // 这项数据由服务器在两次战术更新之间累加。
    public float PendingBulletWound;
    public ClashDamageSource PendingBulletWoundSource;
    // 武器切换和冷却要一起同步，保证刚切换、正在举枪和可以开火时各端显示一致。
    [GhostField] public uint EquippedWeaponID;
    [GhostField] public float WeaponCooldown; // 开火已过时间，包含不满一拍的部分。
    [GhostField] public float WeaponEquipRemaining, FireBufferRemaining;
    [GhostField] public float WeaponRaiseProgress, HipFireHoldRemaining;
    [GhostField] public bool HaloPendingShot;

    // 切武器后仍保留原槽的恢复时间，防止切回来马上越过开火间隔。
    [GhostField] public float PrimaryFireRecovery, SecondaryFireRecovery, PistolFireRecovery;
    [GhostField] public ushort EquippedModules, PrimaryModules, SecondaryModules, PistolModules;
    [GhostField] public float AlienSurgeSeconds;
    [GhostField(Quantization = 100)] public float MucusBlindSeconds;
    [GhostField] public AlienFoodEffect AlienFoodEffect;
    [GhostField] public float AlienFoodEffectSeconds;
    [GhostField] public int BurstRemaining, BurstSequence;
    [GhostField] public bool BurstTriggerHeld;
    [GhostField] public int CurrentAmmo;

    // 在手武器用 CurrentAmmo，其余槽各记自己的剩余弹量。
    [GhostField] public int StoredHaloAmmo;
    [GhostField] public int StoredRevolverAmmo;
    [GhostField] public bool InventoryWeapons;
    [GhostField] public int EquippedWeaponSlot;
    [GhostField] public Unity.Collections.FixedString64Bytes EquippedWeaponItem;
    // 背包能有主武器、副武器和手枪。各槽分别存武器及弹量，临时切换装备不会把在手武器的数值串到另一格。
    [GhostField] public uint PrimaryWeaponID;
    [GhostField] public uint SecondaryWeaponID;
    [GhostField] public uint PistolWeaponID;
    [GhostField] public int PrimaryAmmo;
    [GhostField] public int SecondaryAmmo;
    [GhostField] public int PistolAmmo;
    [GhostField] public float LastDamageAmount;
    [GhostField] public uint LastHitTick;

    // 受伤触碰的声画反馈靠这个标记，生理或状态周期伤害用各自提示。
    // 事件用 tick 或序号标记。画面和音效靠它判断何时收到新事件，连续收到同一份快照也只播放一次。
    [GhostField] public uint LastImpactTick;
    [GhostField] public uint LastShotTick;

    // 部署光环的自主射击由服务器独立推进，玩家的手仍可以继续搜索。
    public uint LastSentryShotTick;
    [GhostField] public uint LastJumpTick;
    [GhostField] public uint LastLandTick;
    [GhostField] public uint LastReloadTick;

    // 服务端同步打开容器和搜索持续阶段的提示。
    // 记录搜刮或使用物品的次数，用来把当前动作动画与上一轮分开。
    [GhostField] public uint LootSearchSequence;
    [GhostField] public bool LootSearching;

    // 服务端在定时使用物品或开始自救时记一次事件。
    [GhostField] public uint ConsumableUseSequence;
    [GhostField] public uint LastGrenadeShotTick;
    [GhostField] public byte GrenadePhase;
    [GhostField] public uint GrenadeHoldId;
    [GhostField] public Unity.Collections.FixedString64Bytes GrenadeItem;
    // 手雷的动作阶段和引信是两条计时，前者给拉环、握持和投掷动画，后者决定引爆时机。
    [GhostField(Quantization = 1000)] public float GrenadePhaseRemaining, GrenadeFuseRemaining;
    [GhostField] public bool GrenadeUnderhand;
    [GhostField] public float ReloadTimer;
    [GhostField] public float ReloadDuration;
    [GhostField] public int ReloadTargetAmmo;
}