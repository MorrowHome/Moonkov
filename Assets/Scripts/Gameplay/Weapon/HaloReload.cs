using UnityEngine;

namespace Unity.MP_FPS
{
    // Battery energy is still reserved once, by the server, before Begin. Rounds
    // become usable together on completion; this timeline never grants free ammo.
    public static class HaloReload
    {
        public static float Duration(WeaponData weapon, uint weaponId, int currentAmmo, int targetAmmo)
        {
            if (!weapon || weapon.MagazineSize <= 0 || targetAmmo <= currentAmmo) return 0;
            if (!DollSingerWeapons.IsHalo(weaponId)) return Mathf.Max(.01f, weapon.ReloadTime);
            int added = Mathf.Clamp(targetAmmo - Mathf.Max(0, currentAmmo), 0, weapon.MagazineSize);
            // Opening/locking costs time even for one round. An empty reservoir
            // also needs priming. A complete empty reload remains ReloadTime.
            float fraction = .25f + .63f * added / weapon.MagazineSize + (currentAmmo <= 0 ? .12f : 0);
            return Mathf.Max(.01f, weapon.ReloadTime * fraction);
        }

        public static void Begin(ref PredictedPlayerGhost state, WeaponData weapon, int targetAmmo)
        {
            state.ReloadTargetAmmo = targetAmmo;
            state.ReloadDuration = Duration(weapon, state.EquippedWeaponID, state.CurrentAmmo, targetAmmo) *
                RaidHealth.HandlingMultiplier(state);
            state.ReloadTimer = state.ReloadDuration;
            state.ControllerState.IsReloadingState = state.ReloadDuration > 0;
        }

        // The server reserves one battery transfer. Neither prediction nor an early
        // fire request can spend it before the shared reload timeline completes.
        public static void BeginEnergyCharge(ref PredictedPlayerGhost state, WeaponData weapon, int energy)
        {
            state.HaloRechargeEnergy = energy;
            float fraction = Mathf.Clamp01(energy / HaloEnergyRules.Maximum(state));
            state.ReloadDuration = Mathf.Max(.5f, (weapon ? weapon.ReloadTime : 2.4f) *
                (.25f + .75f * fraction) * RaidHealth.HandlingMultiplier(state));
            state.ReloadTimer = state.ReloadDuration;
            state.ControllerState.IsReloadingState = true;
            state.ReloadTargetAmmo = weapon ? Mathf.Min(weapon.MagazineSize,
                Mathf.FloorToInt((HaloEnergyRules.Energy(state) + energy) / Mathf.Max(1, weapon.EnergyPerRound))) : 0;
            state.HaloRegenerating = false;
            HaloModuleFire.Reset(ref state);
        }

        public static void Tick(ref PredictedPlayerGhost state, WeaponData weapon, float deltaTime)
        {
            if (!state.ControllerState.IsReloadingState || !float.IsFinite(deltaTime) || deltaTime <= 0) return;
            state.ReloadTimer = Mathf.Max(0, state.ReloadTimer - deltaTime);
            if (state.ReloadTimer <= 0) DollSingerWeapons.CompleteReload(ref state, weapon);
        }

        public static float Total(in PredictedPlayerGhost state, WeaponData weapon) =>
            state.ReloadDuration > 0 ? state.ReloadDuration : Mathf.Max(.01f,
                (weapon ? weapon.ReloadTime : 1) * RaidHealth.HandlingMultiplier(state));

        public static float Progress(in PredictedPlayerGhost state, WeaponData weapon) =>
            state.ControllerState.IsReloadingState ? Mathf.Clamp01(1 - state.ReloadTimer / Total(state, weapon)) : 0;
    }
}
