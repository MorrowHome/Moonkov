using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Prediction and server execute the same burst lifecycle. One press commits
    // three individually simulated shots; aim loss, reload, switching or death cancel it.
    public static class HaloModuleFire
    {
        public static void Reset(ref PredictedPlayerGhost state)
        { state.BurstRemaining = 0; state.BurstSequence = 0; state.BurstTriggerHeld = false; }

        public static bool TryFire(ref PredictedPlayerGhost state, WeaponData weapon, bool trigger, bool aiming, bool switched)
        {
            bool pulse = state.EquippedWeaponID == DollSingerWeapons.Halo &&
                ((HaloModule)state.EquippedModules & HaloModule.PulseController) != 0;
            bool pressed = trigger && !state.BurstTriggerHeld;
            state.BurstTriggerHeld = trigger;
            bool blocked = weapon == null || switched || state.CurrentHealth <= 0 || state.ControllerState.IsReloadingState ||
                state.CurrentAmmo <= 0 || (DollSingerWeapons.IsHalo(state.EquippedWeaponID) && !aiming);
            if (blocked) { state.BurstRemaining = 0; return false; }
            float handling = RaidHealth.HandlingMultiplier(state);
            float interval = pulse && state.BurstRemaining == 0 ? Mathf.Max(.42f, weapon.CooldownInMs) : weapon.CooldownInMs;
            if (state.WeaponCooldown < interval * handling) return false;
            if (!pulse)
            { state.BurstRemaining = 0; state.BurstSequence = 0; return trigger; }
            if (state.BurstRemaining == 0)
            {
                if (!pressed) return false;
                state.BurstRemaining = 3;
            }
            state.BurstSequence = 3 - state.BurstRemaining;
            state.BurstRemaining--;
            return true;
        }
    }
}
