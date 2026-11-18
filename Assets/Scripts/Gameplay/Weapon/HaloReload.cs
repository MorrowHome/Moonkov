// 充能动作的共用时间轴。开始时写入目标和总时长，网络状态可以用同一计时显示进度和播放动作。
// 已有充能量由服务端提前预留，计时归零才交给 DollSingerWeapons 完成结算。
using UnityEngine;

namespace Unity.MP_FPS {
    // Battery energy is still reserved once, by the server, before Begin. Rounds
    // become usable together on completion; this timeline never grants free ammo.
    public static class HaloReload {
        // 传统武器用配置整段装填时间。光环的部分装填则会根据补进去的量缩短。
        public static float Duration(WeaponData weapon, uint weaponId, int currentAmmo, int targetAmmo) {
            if (!weapon || weapon.MagazineSize <= 0 || targetAmmo <= currentAmmo) return 0;
            if (!DollSingerWeapons.IsHalo(weaponId)) return Mathf.Max(.01f, weapon.ReloadTime);
            int added = Mathf.Clamp(targetAmmo - Mathf.Max(0, currentAmmo), 0, weapon.MagazineSize);
            // Opening/locking costs time even for one round. An empty reservoir
            // also needs priming. A complete empty reload remains ReloadTime.
            // 装一点也要花打开和锁好的时间，空仓还要预热。实际装填量只影响中间的那一段。
            float fraction = .25f + .63f * added / weapon.MagazineSize + (currentAmmo <= 0 ? .12f : 0);
            return Mathf.Max(.01f, weapon.ReloadTime * fraction);
        }

        public static void Begin(ref PredictedPlayerGhost state, WeaponData weapon, int targetAmmo) {
            // 记录目标读数和整段计时。动作和进度 UI 可以从状态读取时间，不必猜这次装填几发。
            state.ReloadTargetAmmo = targetAmmo;
            state.ReloadDuration = Duration(weapon, state.EquippedWeaponID, state.CurrentAmmo, targetAmmo) *
                                   RaidHealth.HandlingMultiplier(state);
            state.ReloadTimer = state.ReloadDuration;
            state.ControllerState.IsReloadingState = state.ReloadDuration > 0;
        }

        // The server reserves one battery transfer. Neither prediction nor an early
        // fire request can spend it before the shared reload timeline completes.
        public static void BeginEnergyCharge(ref PredictedPlayerGhost state, WeaponData weapon, int energy) {
            // 生命或装备状态可能在过程中变化，所以先记待入账的能量，完成时再根据角色是否仍有效决定。
            state.HaloRechargeEnergy = energy;
            float fraction = Mathf.Clamp01(energy / HaloEnergyRules.Maximum(state));
            state.ReloadDuration = Mathf.Max(.5f,
                (weapon ? weapon.ReloadTime : 2.4f) * (.25f + .75f * fraction) * RaidHealth.HandlingMultiplier(state));
            state.ReloadTimer = state.ReloadDuration;
            state.ControllerState.IsReloadingState = true;
            state.ReloadTargetAmmo =
                weapon
                    ? Mathf.Min(weapon.MagazineSize,
                        Mathf.FloorToInt((HaloEnergyRules.Energy(state) + energy) /
                                         Mathf.Max(1, weapon.EnergyPerRound)))
                    : 0;
            state.HaloRegenerating = false;
            HaloModuleFire.Reset(ref state);
        }

        public static void Tick(ref PredictedPlayerGhost state, WeaponData weapon, float deltaTime) {
            // 只让已经开始的动作走有效的步长。归零后通过统一完成函数关掉装填标志。
            if (!state.ControllerState.IsReloadingState || !float.IsFinite(deltaTime) || deltaTime <= 0) return;
            state.ReloadTimer = Mathf.Max(0, state.ReloadTimer - deltaTime);
            if (state.ReloadTimer <= 0) DollSingerWeapons.CompleteReload(ref state, weapon);
        }

        public static float Total(in PredictedPlayerGhost state, WeaponData weapon) {
            return state.ReloadDuration > 0
                ? state.ReloadDuration
                : Mathf.Max(.01f, (weapon ? weapon.ReloadTime : 1) * RaidHealth.HandlingMultiplier(state));
        }

        // 剩余秒数除以动作开始时定下的总时长，拿到从 0 到 1 的进度。
        public static float Progress(in PredictedPlayerGhost state, WeaponData weapon) {
            return state.ControllerState.IsReloadingState
                ? Mathf.Clamp01(1 - state.ReloadTimer / Total(state, weapon))
                : 0;
        }
    }
}