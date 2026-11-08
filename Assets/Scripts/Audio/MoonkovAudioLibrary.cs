using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(menuName = "Moonkov/Audio Library")]
    public sealed class MoonkovAudioLibrary : ScriptableObject
    {
        public SoundDef Click, Hover, Confirm, Error, Notification;
        public SoundDef AirlockButtonPress;
        public SoundDef InventoryOpen, InventoryClose, ContainerClose;
        public SoundDef InventoryCloth, InventoryMetal, InventoryPlastic, InventoryFood, InventoryEquip, InventoryDrop, BatteryInsert;
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
        public SoundDef AccessCarrier, AccessTerminal, AccessCut, AccessReveal, AccessClose, AccessRoom, AccessGlitch;
        // Layered deployment confirmation (success gong + android voice).
        public SoundDef DeployConfirm;
    }
}
