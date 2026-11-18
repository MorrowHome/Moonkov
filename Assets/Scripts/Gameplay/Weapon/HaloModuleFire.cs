// 客户端预测和服务端共用的开火节奏。用角色状态跟踪取武器、抬手、换弹、冷却和连发剩余几发。
// TryFire 判断这一拍可不可以开火，真正扣能量、生成投射物和确认伤害由调用方负责。
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS {
    // 客户端预测和服务器共用计时、单发输入缓存与连射阶段。
    public static class HaloModuleFire {
        public const float InputBufferSeconds = .12f;

        // 外观层也会读取待出手状态。有缓存的点按时，瞄准已经松开也可能还有一发在等。
        public static bool HasPendingFire(in PredictedPlayerGhost state) {
            return state.HaloPendingShot || state.BurstRemaining > 0 || state.FireBufferRemaining > 0;
        }

        // 只清理待发和连发序列等瞬间状态。和换枪的冷却、已付能量分开。
        public static void Reset(ref PredictedPlayerGhost state) {
            state.BurstRemaining = 0;
            state.BurstSequence = 0;
            state.BurstTriggerHeld = false;
            state.FireBufferRemaining = 0;
            state.HaloPendingShot = false;
        }

        // 计时由模拟步长推进，包括闲置武器槽的开火恢复。角色切走一把枪再切回时，不应重置它的射击节奏。
        public static void AdvanceTimers(ref PredictedPlayerGhost state, float deltaTime) {
            state.WeaponCooldown += deltaTime;
            state.WeaponEquipRemaining = Mathf.Max(0, state.WeaponEquipRemaining - deltaTime);
            state.FireBufferRemaining = Mathf.Max(0, state.FireBufferRemaining - deltaTime);
            state.PrimaryFireRecovery = Mathf.Max(0, state.PrimaryFireRecovery - deltaTime);
            state.SecondaryFireRecovery = Mathf.Max(0, state.SecondaryFireRecovery - deltaTime);
            state.PistolFireRecovery = Mathf.Max(0, state.PistolFireRecovery - deltaTime);
        }

        private static bool IsPulse(in PredictedPlayerGhost state, WeaponData weapon) {
            return (weapon != null && weapon.BurstCount > 1) || state.EquippedWeaponID == DollSingerWeapons.Halo &&
                ((HaloModule)state.EquippedModules & HaloModule.PulseController) != 0;
        }

        // 连发的一组里面和两组之间用不同的时间。伤势造成的处理倍率还会延长这些时间。
        private static float Interval(in PredictedPlayerGhost state, WeaponData weapon, bool betweenBursts) {
            return Mathf.Max(.001f,
                       weapon.BurstCount > 1 ? (betweenBursts ? weapon.BurstCooldown : weapon.BurstInterval) :
                       betweenBursts ? Mathf.Max(.42f, weapon.CooldownInMs) : weapon.CooldownInMs) *
                   RaidHealth.HandlingMultiplier(state);
        }

        // 换武器先重建抬手和装备等待状态，并从对应槽位取还没走完的射击恢复时间。
        public static void BeginEquip(ref PredictedPlayerGhost state, WeaponData weapon) {
            Reset(ref state);
            state.WeaponRaiseProgress = state.HipFireHoldRemaining = 0;
            state.ReloadQueued = false;
            if (weapon == null) {
                state.WeaponEquipRemaining = 0;
                state.WeaponCooldown = 0;
                return;
            }
            float recovery = state.EquippedWeaponSlot == 1 ? state.PrimaryFireRecovery :
                state.EquippedWeaponSlot == 2 ? state.SecondaryFireRecovery : state.PistolFireRecovery;
            state.WeaponEquipRemaining = DollSingerWeapons.IsHalo(state.EquippedWeaponID)
                ? Mathf.Max(0, weapon.EquipTime) * RaidHealth.HandlingMultiplier(state)
                : 0;
            state.WeaponCooldown = DollSingerWeapons.IsHalo(state.EquippedWeaponID)
                ? Interval(state, weapon, IsPulse(state, weapon)) - recovery
                : 0;
        }

        // 取枪时按 R 会记下装填意图，等取枪结束再消耗电池。
        // 之后若换武器，就不用这个旧请求了。
        // 如果还在取武器，能记录一次装填请求，等武器就绪后再消费；占手、已满弹等情况则直接清掉请求。
        public static bool ReadyToReload(ref PredictedPlayerGhost state, bool requested, int magazineSize) {
            if (GrenadeHandling.Occupied(state) || state.CurrentHealth <= 0 || state.ControllerState.IsReloadingState ||
                magazineSize <= state.CurrentAmmo) {
                state.ReloadQueued = false;
                return false;
            }
            if (state.WeaponEquipRemaining > 0) {
                if (requested) state.ReloadQueued = true;
                return false;
            }
            bool queued = state.ReloadQueued;
            state.ReloadQueued = false;
            return requested || queued;
        }

        // 瞄准键控制镜片。腰射与开镜射击都要抬起手和光环。
        // 抬手期间单独记一发点按，与短冷却时的输入缓存分开。
        public static bool TryFire(
            ref PredictedPlayerGhost state,
            WeaponData weapon,
            in PlayerInput input,
            bool switched,
            float deltaTime) {
            HaloEnergyRules.RefreshAmmo(ref state, weapon);
            bool halo = DollSingerWeapons.IsHalo(state.EquippedWeaponID);
            // 正在做手雷、防御、转移光环或自由观察等事时，先取消待发，等手空出来再接受新动作。
            bool blocked = GrenadeHandling.Occupied(state) || input.HaloDefense || input.HaloTransfer ||
                           state.HaloDefenseHeld || HaloTransferRules.Occupied(state) || input.FireSuppressed ||
                           input.FreeLooking || switched || weapon == null || state.CurrentHealth <= 0;
            if (blocked) {
                Reset(ref state);
                state.WeaponRaiseProgress = state.HipFireHoldRemaining = 0;
                return false;
            }
            if (!halo) return TryFire(ref state, weapon, input.Shoot, input.Aim, switched);
            float step = Mathf.Max(0, deltaTime);
            bool wasReady = state.WeaponRaiseProgress >= 1f - .00001f;
            // 光环放下时可能提前返回，还没走到 TryFire 里的扳机边沿检测。
            if (!input.Shoot) state.BurstTriggerHeld = false;
            // 开始瞄准会消耗掉腰射姿势的保持时间。
            // 即使刚刚瞄准开火，松开右键也不会继续摆腰射姿势。
            // 瞄准和腰射可共用抬手姿势，但保持方式不同。腰射短暂保持，按住瞄准则由瞄准键维持。
            if (input.Aim)
                state.HipFireHoldRemaining = 0;
            else if (input.FireHeld || input.Shoot || (state.HipFireHoldRemaining > 0 && HasPendingFire(state)))
                state.HipFireHoldRemaining = DollSingerHaloAim.HipFireHoldSeconds;
            else
                state.HipFireHoldRemaining = Mathf.Max(0, state.HipFireHoldRemaining - step);
            // 抬手还没完成时，暂记一次点按。换弹仅在快结束时也允许缓存，防止玩家按早一点就完全丢键。
            bool reloadEnding = state.ControllerState.IsReloadingState && state.ReloadTimer <= InputBufferSeconds;
            if (!wasReady && input.Shoot && (!state.ControllerState.IsReloadingState || reloadEnding) &&
                (state.CurrentAmmo > 0 || reloadEnding))
                state.HaloPendingShot = true;
            bool raised = input.Aim || state.HipFireHoldRemaining > 0 || HasPendingFire(state);
            state.WeaponRaiseProgress = Mathf.MoveTowards(state.WeaponRaiseProgress, raised ? 1 : 0,
                step / ((raised ? DollSingerHaloAim.RaiseSeconds : DollSingerHaloAim.LowerSeconds) *
                        RaidHealth.HandlingMultiplier(state)));
            if (state.ControllerState.IsReloadingState && !reloadEnding) state.HaloPendingShot = false;
            if (state.WeaponRaiseProgress < 1f - .00001f || state.WeaponEquipRemaining > 0) return false;
            // 抬手达到可开火位置后，把这一拍的按键和之前暂存的一发汇在一起，再进入射速判断。
            bool trigger = input.Shoot || state.HaloPendingShot;
            state.HaloPendingShot = false;
            bool fired = TryFire(ref state, weapon, trigger, true, false);
            if (fired && !input.Aim && state.HipFireHoldRemaining > 0)
                state.HipFireHoldRemaining = DollSingerHaloAim.HipFireHoldSeconds;
            return fired;
        }

        public static bool TryFire(
            ref PredictedPlayerGhost state,
            WeaponData weapon,
            bool trigger,
            bool aiming,
            bool switched) {
            HaloEnergyRules.RefreshAmmo(ref state, weapon);
            bool pulse = IsPulse(state, weapon);
            // 连发需要一次新的按下才会建一组。一直按着不会在每拍都重新开始一组。
            bool pressed = trigger && !state.BurstTriggerHeld;
            state.BurstTriggerHeld = trigger;
            bool blocked = GrenadeHandling.Occupied(state) || weapon == null || switched || state.CurrentHealth <= 0 ||
                           state.HaloDefenseHeld || HaloTransferRules.Occupied(state) ||
                           (DollSingerWeapons.IsHalo(state.EquippedWeaponID) && !aiming);
            if (blocked) {
                state.BurstRemaining = 0;
                state.FireBufferRemaining = 0;
                return false;
            }

            // 半自动收到的是一次按下事件。脉冲连发武器收到的是扳机持续状态。
            // 缓存的点按很快过期。实际开火时使用那一刻的瞄准方向。
            if (((!pulse && !weapon.Automatic && trigger) || (pulse && pressed)) &&
                (!state.ControllerState.IsReloadingState || state.ReloadTimer <= InputBufferSeconds))
                state.FireBufferRemaining = InputBufferSeconds;
            // 换弹期间不能在本拍打出子弹。只有计时快结束时才保留很短的点按缓存。
            if (state.ControllerState.IsReloadingState) {
                state.BurstRemaining = 0;
                if (state.ReloadTimer > InputBufferSeconds) state.FireBufferRemaining = 0;
                return false;
            }
            if (state.CurrentAmmo <= 0) {
                state.BurstRemaining = 0;
                state.FireBufferRemaining = 0;
                return false;
            }
            if (state.WeaponEquipRemaining > 0) return false;
            float interval = Interval(state, weapon, pulse && state.BurstRemaining == 0);
            if (state.WeaponCooldown + .00001f < interval) return false;
            // 全自动看当前是否仍按住，半自动看是否有一发缓存，连发另走剩余发数判断。
            if (!pulse) {
                state.BurstRemaining = 0;
                state.BurstSequence = 0;
                bool fire = weapon.Automatic ? trigger : state.FireBufferRemaining > 0;
                if (fire) state.FireBufferRemaining = 0;
                return fire;
            }
            // 连发只在缓存了新按键时开始。后面的几发由这一组的剩余发数继续驱动。
            if (state.BurstRemaining == 0) {
                if (state.FireBufferRemaining <= 0) return false;
                state.FireBufferRemaining = 0;
                state.BurstRemaining = weapon.BurstCount > 1 ? weapon.BurstCount : 3;
            }
            state.BurstSequence = (weapon.BurstCount > 1 ? weapon.BurstCount : 3) - state.BurstRemaining;
            state.BurstRemaining--;
            return true;
        }

        // 连续射击最多保留一拍多算的冷却时间。
        // 等待、半自动缓存或卡顿不会换来多发超速射击。
        public static void CommitShot(ref PredictedPlayerGhost state, WeaponData weapon, float deltaTime) {
            bool pulse = IsPulse(state, weapon);
            float firedInterval = Interval(state, weapon, pulse && state.BurstSequence == 0);
            float remainder = state.WeaponCooldown - firedInterval;
            // 连续开火允许保留不到一拍的时间余量，以免模拟步长改变射速。长时间卡顿不会攒出多发。
            bool preserveRemainder = (weapon.Automatic || pulse) && deltaTime <= firedInterval && remainder >= 0 &&
                                     remainder <= deltaTime + .00001f;
            state.WeaponCooldown = preserveRemainder ? Mathf.Min(remainder, deltaTime) : 0;
            // 为目前的装备槽记录本发后还要等多久，换到别的枪时原枪仍保留自己的恢复时间。
            float recovery = Interval(state, weapon, pulse && state.BurstRemaining == 0) - state.WeaponCooldown;
            if (state.EquippedWeaponSlot == 1)
                state.PrimaryFireRecovery = recovery;
            else if (state.EquippedWeaponSlot == 2)
                state.SecondaryFireRecovery = recovery;
            else if (state.EquippedWeaponSlot == 3) state.PistolFireRecovery = recovery;
        }
    }
}