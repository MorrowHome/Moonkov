using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(menuName = "Moonkov/Audio Library")]
    public sealed class MoonkovAudioLibrary : ScriptableObject
    {
        public SoundDef Click, Hover, Confirm, Error, Notification;
        public SoundDef ShieldImpact, ShieldBreak, ShieldRestore;
        public SoundDef TacticalDeploy, TacticalAlarm, TacticalExplosion, TacticalGas, TacticalCough;
        public SoundDef MeleeSwing, MeleeEnergySwing, MeleeImpact, MeleeBlocked;
        public SoundDef Consume, PlayerPain, PlayerGroan;
        public SoundDef EatBite, DrinkSip, BottleOpen, PacketOpen, InjectorCap, InjectorPress;
        public SoundDef Equipment, Container, LootSearchWorld, Jump, Land, Hit, Death;
        public SoundDef RegolithFootsteps, MetalFootsteps, Cloth;
        public SoundDef HardFootsteps;
        public SoundDef AlienContact, AlienWarning, AlienStrike, AlienHurt, AlienDeath, AlienIdle;
        public SoundDef SuitWallBrush, SuitWallMetal, Crawl, StanceChange, GearMotion;
        public SoundDef AlienWallScrape, AlienWallMetal, AlienIdleMotion;
        public SoundDef AlienSwing, AlienSpit, AlienImpact, AlienWetMove, AlienHeavyStep, AlienGrip, AlienBossVoice;
        public SoundDef Aim, Light, DryFire, ReloadReady, Medical, Pickup, OxygenRefill;
        public SoundDef SuitBreath, Heartbeat, SuitWarning, ExtractStart, ExtractCancel, ExtractTick;
        public SoundDef OxygenSupply;
        public SoundDef ShipInterior;
        // Layered deployment confirmation (success gong + android voice).
        public SoundDef DeployConfirm;
    }
}
