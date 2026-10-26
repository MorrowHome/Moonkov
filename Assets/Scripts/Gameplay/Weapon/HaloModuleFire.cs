using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Prediction and server share timing, the one-shot input buffer and burst lifecycle.
    public static class HaloModuleFire
    {
        public const float InputBufferSeconds = .12f;

        public static void Reset(ref PredictedPlayerGhost state)
        {
            state.BurstRemaining = 0;
            state.BurstSequence = 0;
            state.BurstTriggerHeld = false;
            state.FireBufferRemaining = 0;
        }

        public static void AdvanceTimers(ref PredictedPlayerGhost state, float deltaTime)
        {
            state.WeaponCooldown += deltaTime;
            state.WeaponEquipRemaining = Mathf.Max(0, state.WeaponEquipRemaining - deltaTime);
            state.FireBufferRemaining = Mathf.Max(0, state.FireBufferRemaining - deltaTime);
            state.PrimaryFireRecovery = Mathf.Max(0, state.PrimaryFireRecovery - deltaTime);
            state.SecondaryFireRecovery = Mathf.Max(0, state.SecondaryFireRecovery - deltaTime);
            state.PistolFireRecovery = Mathf.Max(0, state.PistolFireRecovery - deltaTime);
        }

        private static bool IsPulse(in PredictedPlayerGhost state, WeaponData weapon) =>
            (weapon != null && weapon.BurstCount > 1) || state.EquippedWeaponID == DollSingerWeapons.Halo &&
            ((HaloModule)state.EquippedModules & HaloModule.PulseController) != 0;

        private static float Interval(in PredictedPlayerGhost state, WeaponData weapon, bool betweenBursts) =>
            Mathf.Max(.001f, weapon.BurstCount > 1 ? (betweenBursts ? weapon.BurstCooldown : weapon.BurstInterval) : betweenBursts ? Mathf.Max(.42f, weapon.CooldownInMs) : weapon.CooldownInMs) *
            RaidHealth.HandlingMultiplier(state);

        public static void BeginEquip(ref PredictedPlayerGhost state, WeaponData weapon)
        {
            Reset(ref state);
            state.ReloadQueued=false;
            if (weapon == null) { state.WeaponEquipRemaining = 0; state.WeaponCooldown = 0; return; }
            float recovery = state.EquippedWeaponSlot == 1 ? state.PrimaryFireRecovery :
                state.EquippedWeaponSlot == 2 ? state.SecondaryFireRecovery : state.PistolFireRecovery;
            state.WeaponEquipRemaining = DollSingerWeapons.IsHalo(state.EquippedWeaponID)
                ? Mathf.Max(0, weapon.EquipTime) * RaidHealth.HandlingMultiplier(state) : 0;
            state.WeaponCooldown = DollSingerWeapons.IsHalo(state.EquippedWeaponID)
                ? Interval(state, weapon, IsPulse(state, weapon)) - recovery : 0;
        }

        // R pressed during a draw waits for that draw, without spending a cell
        // early. A later weapon switch discards the old weapon's queued intent.
        public static bool ReadyToReload(ref PredictedPlayerGhost state,bool requested,int magazineSize)
        {
            if(state.CurrentHealth<=0 || state.ControllerState.IsReloadingState || magazineSize<=state.CurrentAmmo)
            {state.ReloadQueued=false;return false;}
            if(state.WeaponEquipRemaining>0)
            {
                if(requested)state.ReloadQueued=true;
                return false;
            }
            bool queued=state.ReloadQueued;state.ReloadQueued=false;
            return requested || queued;
        }

        public static bool TryFire(ref PredictedPlayerGhost state, WeaponData weapon, bool trigger, bool aiming, bool switched)
        {
            bool pulse = IsPulse(state, weapon);
            bool pressed = trigger && !state.BurstTriggerHeld;
            state.BurstTriggerHeld = trigger;
            bool blocked = weapon == null || switched || state.CurrentHealth <= 0 ||
                (DollSingerWeapons.IsHalo(state.EquippedWeaponID) && !aiming);
            if (blocked) { state.BurstRemaining = 0; state.FireBufferRemaining = 0; return false; }

            // Semi-auto input is already a press event; pulse rifles receive a held trigger.
            // A single pending press expires quickly and follows the current aim when released.
            if (((!pulse && !weapon.Automatic && trigger) || (pulse && pressed)) &&
                (!state.ControllerState.IsReloadingState || state.ReloadTimer <= InputBufferSeconds))
                state.FireBufferRemaining = InputBufferSeconds;

            if (state.ControllerState.IsReloadingState)
            {
                state.BurstRemaining = 0;
                if (state.ReloadTimer > InputBufferSeconds) state.FireBufferRemaining = 0;
                return false;
            }
            if (state.CurrentAmmo <= 0) { state.BurstRemaining = 0; state.FireBufferRemaining = 0; return false; }
            if (state.WeaponEquipRemaining > 0) return false;
            float interval = Interval(state, weapon, pulse && state.BurstRemaining == 0);
            if (state.WeaponCooldown + .00001f < interval) return false;
            if (!pulse)
            {
                state.BurstRemaining = 0;
                state.BurstSequence = 0;
                bool fire = weapon.Automatic ? trigger : state.FireBufferRemaining > 0;
                if (fire) state.FireBufferRemaining = 0;
                return fire;
            }
            if (state.BurstRemaining == 0)
            {
                if (state.FireBufferRemaining <= 0) return false;
                state.FireBufferRemaining = 0;
                state.BurstRemaining = weapon.BurstCount > 1 ? weapon.BurstCount : 3;
            }
            state.BurstSequence = (weapon.BurstCount > 1 ? weapon.BurstCount : 3) - state.BurstRemaining;
            state.BurstRemaining--;
            return true;
        }

        // Retain at most one simulation step of overshoot for continuous fire. Idle time,
        // a buffered semi-auto press, or a hitch never buys several accelerated shots.
        public static void CommitShot(ref PredictedPlayerGhost state, WeaponData weapon, float deltaTime)
        {
            bool pulse = IsPulse(state, weapon);
            float firedInterval = Interval(state, weapon, pulse && state.BurstSequence == 0);
            float remainder = state.WeaponCooldown - firedInterval;
            bool preserveRemainder = (weapon.Automatic || pulse) && deltaTime <= firedInterval &&
                remainder >= 0 && remainder <= deltaTime + .00001f;
            state.WeaponCooldown = preserveRemainder ? Mathf.Min(remainder, deltaTime) : 0;
            float recovery = Interval(state, weapon, pulse && state.BurstRemaining == 0) - state.WeaponCooldown;
            if (state.EquippedWeaponSlot == 1) state.PrimaryFireRecovery = recovery;
            else if (state.EquippedWeaponSlot == 2) state.SecondaryFireRecovery = recovery;
            else if (state.EquippedWeaponSlot == 3) state.PistolFireRecovery = recovery;
        }
    }
}
