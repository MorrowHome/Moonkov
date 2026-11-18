// 这份资源像声音索引表。菜单、交互和角色表现通过字段拿到对应 SoundDef，资源替换或调参可以集中完成。
using UnityEngine;

namespace Unity.MP_FPS {
    [CreateAssetMenu(menuName = "Moonkov/Audio Library")]
    public sealed class MoonkovAudioLibrary : ScriptableObject {
        public SoundDef Click, Hover, Confirm, Error, Notification;
        public SoundDef AirlockButtonPress;
        // 界面音与道具音共用配置入口。每个名字指向一个 SoundDef 资源，改录音可以只换引用。
        public SoundDef InventoryOpen, InventoryClose, ContainerClose;

        public SoundDef InventoryCloth,
            InventoryMetal,
            InventoryPlastic,
            InventoryFood,
            InventoryEquip,
            InventoryDrop,
            BatteryInsert;

        public SoundDef WeaponDraw, WeaponDrawPistol, WeaponStow, WeaponAimIn, WeaponAimOut, MeleeDraw;
        public SoundDef GrenadeDraw, GrenadePin, GrenadeThrow, OxygenConnect, OxygenDisconnect, RoomSwitch;
        public SoundDef TerminalStart, TerminalComplete;
        public SoundDef ShieldImpact, ShieldBreak, ShieldRestore;
        public SoundDef ShieldRaise, ShieldLower;
        public SoundDef TacticalDeploy, TacticalAlarm, TacticalExplosion, TacticalGas, TacticalCough;
        public SoundDef TacticalTrapArm, TacticalWireRelease, TacticalSupplyDeploy, TacticalFlash, TacticalFireStart;
        public SoundDef TacticalFireLoop, TacticalSmokeLoop, TacticalChokeLoop, TacticalPoisonLoop;
        public SoundDef MeleeSwing, MeleeEnergySwing, MeleeImpact, MeleeBlocked;
        public SoundDef MeleeEffort;
        public SoundDef Consume, PlayerPain, PlayerGroan;
        public SoundDef EatBite, DrinkSip, BottleOpen, PacketOpen, InjectorCap, InjectorPress;
        public SoundDef BottleClose, MedicalHandling, OxygenCanisterFlow;
        public SoundDef MedicalOpen, MedicalGel;
        public SoundDef Equipment, Container, LootSearchWorld, Jump, Land, Hit, Death;
        public SoundDef RegolithFootsteps, MetalFootsteps, Cloth;
        public SoundDef HardFootsteps;
        public SoundDef AlienContact, AlienWarning, AlienStrike, AlienHurt, AlienDeath, AlienIdle;
        public SoundDef SuitWallBrush, SuitWallMetal, Crawl, StanceChange, GearMotion;
        public SoundDef AlienWallScrape, AlienWallMetal, AlienIdleMotion;
        public SoundDef AlienSwing, AlienSpit, AlienImpact, AlienWetMove, AlienHeavyStep, AlienGrip, AlienBossVoice;
        public SoundDef Aim, Light, DryFire, ReloadReady, Medical, Pickup, OxygenRefill;
        public SoundDef SuitBreath, Heartbeat, SuitWarning, ExtractStart, ExtractCancel, ExtractTick;
        public SoundDef SuitBreathEffort;
        public SoundDef JumpEffort;
        public SoundDef LiftMotor, LiftGate, LiftStart, LiftStop;
        public SoundDef InteriorVent, SuitVent;
        public SoundDef OxygenSupply;
        public SoundDef ShipInterior;
        // 这一组配合登录画面的各阶段，和进战局后的角色声或地图环境声单独配置。
        public SoundDef AccessCarrier, AccessTerminal, AccessCut, AccessReveal, AccessClose, AccessRoom, AccessGlitch;

        // 多层的出战确认音，由成功锣声和仿生人语音组成。
        public SoundDef DeployConfirm;
    }
}