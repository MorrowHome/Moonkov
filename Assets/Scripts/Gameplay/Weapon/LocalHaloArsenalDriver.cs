// 独立运行角色时的光环武器驱动。按手势为每种武器保留一份简化状态，让本地角色也能试射、连发和装填。
// 用 HaloModuleFire 共用射速规则，网络战局则由角色预测和服务器驱动。
using UnityEngine;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    /// <summary>独立运行的演示角色也使用正式玩法的弹仓、连发和能量规则。</summary>
    public sealed class LocalHaloArsenalDriver : MonoBehaviour, ILocalArsenalDriver {
        public WeaponRegistry registry;
        private readonly PredictedPlayerGhost[] states = new PredictedPlayerGhost[HaloArsenal.Entries.Length];
        private InventoryGraph battery;
        private int active = -1;
        private uint shotTick, reloadTick;
        public bool Reloading => active >= 0 && states[active].ControllerState.IsReloadingState;

        private void Initialize() {
            // 独立场景首次用到武器才建局部电池库存和各武器的试用弹仓。
            if (battery != null) return;
            battery = InventoryGraph.Create(starter: false);
            battery.AddSupply("cells", 4, "pockets");
            for (int i = 0; i < states.Length; i++)
                states[i] = new PredictedPlayerGhost {
                    CurrentHealth = 100, EquippedWeaponID = (uint)(i + 6),
                    CurrentAmmo = registry.GetWeaponData((uint)(i + 6)).MagazineSize, WeaponCooldown = 10,
                    EquippedWeaponSlot = 1
                };
        }

        public int Ammo(DollSingerHaloAim.HaloWeapon weapon) {
            Initialize();
            int i = (int)weapon - 5;
            return i >= 0 && i < states.Length ? states[i].CurrentAmmo : 0;
        }

        public void CancelBurst() {
            if (active >= 0) HaloModuleFire.Reset(ref states[active]);
            active = -1;
        }

        public bool Step(
            DollSingerHaloAim halo,
            DollSingerHaloAim.HaloWeapon gesture,
            bool held,
            bool pressed,
            bool reload,
            bool aiming,
            float deltaTime) {
            Initialize();
            int index = (int)gesture - 5;
            var weapon = registry.GetWeaponData((uint)(index + 6));
            ref var state = ref states[index];
            // 换到新手势时，旧武器若正在排队连发就清掉，新武器开始自己的取出时间。
            if (active != index) {
                if (active >= 0) HaloModuleFire.Reset(ref states[active]);
                active = index;
                HaloModuleFire.BeginEquip(ref state, weapon);
            }
            // 所有武器的恢复时间都随时间走，当前手里那把才实际执行装填或射击。
            for (int i = 0; i < states.Length; i++) HaloModuleFire.AdvanceTimers(ref states[i], deltaTime);
            if (state.ControllerState.IsReloadingState) {
                state.ReloadTimer = Mathf.Max(0, state.ReloadTimer - deltaTime);
                if (state.ReloadTimer == 0) DollSingerWeapons.CompleteReload(ref state, weapon);
            }
            // 从这个场景的临时库存取电池量，先开始动作，计时结束后才增加可用弹药。
            if (!state.ControllerState.IsReloadingState && (reload || state.CurrentAmmo == 0) &&
                battery.TryRecharge(state.CurrentAmmo, weapon.MagazineSize, weapon.EnergyPerRound, out int target)) {
                HaloReload.Begin(ref state, weapon, target);
                reloadTick++;
                HaloModuleFire.Reset(ref state);
            }
            bool fired = HaloModuleFire.TryFire(ref state, weapon,
                weapon.Automatic || weapon.BurstCount > 1 ? held : pressed, aiming, false);
            if (fired) {
                state.CurrentAmmo--;
                shotTick++;
                HaloModuleFire.CommitShot(ref state, weapon, deltaTime);
            }
            // 将枪械状态写入同一个角色外观接口。展演中也能复用正式对局的装填进度和射击动画。
            halo.SetNetworkWeapon(gesture, state.CurrentAmmo, state.ControllerState.IsReloadingState,
                HaloReload.Progress(state, weapon), shotTick, reloadTick, state.ReloadTargetAmmo, 0,
                weapon.MagazineSize, state.BurstSequence);
            return fired;
        }
    }
}