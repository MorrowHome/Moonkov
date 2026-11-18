// 光环离开自己、交给队友、放到地面和召回的共用状态。决定角色是否符合操作条件，也按玩法判断两名角色是否队友。
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS {
    public enum HaloTransferPhase : byte {
        Idle,
        Extending,
        Sending,
        Returning
    }

    // 服务器在两个角色现有的能量池之间做单向转送。
    public static class HaloTransferRules {
        public const float Range = 6, CostPerSecond = 20, Efficiency = .75f;
        public const float ExtendSeconds = .25f, ReturnSeconds = .2f;
        private const float Epsilon = .001f;

        public static bool Occupied(in PredictedPlayerGhost state) {
            return state.HaloTransferPhase != HaloTransferPhase.Idle;
        }

        // 发送者要活、持有光环并且不忙其他互斥手部动作。真正转送时每 tick 仍由调用者查身份和位置。
        public static bool CanSend(in PredictedPlayerGhost state) {
            return state.HaloEquipped && state.HaloEnergyInitialized &&
                   DollSingerWeapons.IsHalo(state.EquippedWeaponID) && EnergyShieldRules.Alive(state) &&
                   (!state.ClashEnabled || state.ClashCanAct) && !state.ClashDarkSight && !state.ConsumingSupply &&
                   !GrenadeHandling.Occupied(state) &&
                   (state.HandAction == 0 || HaloSupportRules.Anchored(state) &&
                       state.HandAction == (byte)Unity.MP_FPS.DollSinger.HandAction.Search) && state.MeleePhase == 0 &&
                   !state.ControllerState.IsReloadingState && state.WeaponEquipRemaining <= 0 &&
                   !state.HaloDefenseHeld && HaloEnergyRules.Energy(state) > Epsilon;
        }

        // 接收者也要有可用的光环能量池，填满或失去行动资格后不再继续接收。
        public static bool CanReceive(in PredictedPlayerGhost state) {
            return state.HaloEquipped && state.HaloEnergyInitialized && EnergyShieldRules.Alive(state) &&
                   (!state.ClashEnabled || state.ClashCanAct) &&
                   HaloEnergyRules.Maximum(state) - HaloEnergyRules.Energy(state) > Epsilon;
        }

        public static bool Teammates(
            in PredictedPlayerGhost sender,
            in PredictedPlayerGhost receiver,
            bool senderHuman,
            bool receiverHuman) {
            // 队友关系按玩法判。极速赏金用场内队伍，在线远征用后端队伍，两端普通真人合作则走本机友方规则。
            if (sender.ClashEnabled || receiver.ClashEnabled)
                return sender.ClashEnabled && receiver.ClashEnabled && sender.ClashTeam >= 0 &&
                       sender.ClashTeam == receiver.ClashTeam;
            if (sender.OnlineHuman || receiver.OnlineHuman)
                return sender.OnlineHuman && receiver.OnlineHuman && sender.OnlineTeam >= 0 &&
                       sender.OnlineTeam == receiver.OnlineTeam;
            return senderHuman && receiverHuman;
        }

        public static float3 Anchor(in PredictedPlayerGhost state) {
            return state.ControllerState.CurrentPosition + new float3(0, 1.25f, 0);
        }

        public static bool InRange(float3 from, float3 to) {
            return math.all(math.isfinite(from)) && math.all(math.isfinite(to)) &&
                   math.distancesq(from, to) <= Range * Range;
        }

        public static void Begin(
            ref PredictedPlayerGhost sender,
            Entity target,
            float3 point,
            HaloDeploymentMode mode = HaloDeploymentMode.Transfer) {
            sender.HaloDeploymentMode = mode;
            sender.HaloTransferPhase = HaloTransferPhase.Extending;
            sender.HaloTransferRemaining = ExtendSeconds;
            sender.HaloTransferTarget = target;
            sender.HaloTransferPoint = point;
            // 发起转送时保存操作归属和武器，同时收掉手持盾与射击准备。中途换武器不应让另一件光环接手。
            sender.HaloTransferWeapon = sender.EquippedWeaponID;
            HaloEnergyRules.Release(ref sender);
            HaloModuleFire.Reset(ref sender);
            sender.WeaponRaiseProgress = sender.HipFireHoldRemaining = 0;
            sender.HaloRecoveryDelay = HaloEnergyRules.RecoveryDelay;
            sender.HaloRegenerating = false;
        }

        // 召回将目标和哨戒攻击清空，改为一段返回读条。已经在回收时重复按不会重置时间。
        public static void Recall(ref PredictedPlayerGhost sender) {
            if (!Occupied(sender) || sender.HaloTransferPhase == HaloTransferPhase.Returning) return;
            sender.HaloTransferPhase = HaloTransferPhase.Returning;
            sender.HaloTransferRemaining = ReturnSeconds;
            sender.HaloTransferTarget = Entity.Null;
            sender.HaloSentryTarget = Entity.Null;
        }

        public static void Clear(ref PredictedPlayerGhost sender) {
            sender.HaloDeploymentMode = HaloDeploymentMode.Transfer;
            sender.HaloTransferPhase = HaloTransferPhase.Idle;
            sender.HaloTransferRemaining = 0;
            sender.HaloTransferTarget = Entity.Null;
        }

        public static void TickReturn(ref PredictedPlayerGhost sender, float dt) {
            if (sender.HaloTransferPhase != HaloTransferPhase.Returning || !float.IsFinite(dt) || dt <= 0) return;
            sender.HaloTransferRemaining = Mathf.Max(0, sender.HaloTransferRemaining - dt);
            // 到手才恢复空闲。此时可以重新发出、举盾或做其他被占用的动作。
            if (sender.HaloTransferRemaining <= 0) Clear(ref sender);
        }

        // 每个 tick 都由调用者检查归属、队伍、目标、距离和视线。
        public static float Transfer(ref PredictedPlayerGhost sender, ref PredictedPlayerGhost receiver, float dt) {
            if (!float.IsFinite(dt) || dt <= 0 || !CanSend(sender) || !CanReceive(receiver)) return 0;
            if (sender.HaloTransferPhase == HaloTransferPhase.Extending) {
                // 伸出过程会占用这帧的一部分时间，从剩余时间开始转送，避免光环还没到位就给队友加电。
                float travel = Mathf.Min(dt, sender.HaloTransferRemaining);
                sender.HaloTransferRemaining -= travel;
                dt -= travel;
                if (sender.HaloTransferRemaining <= 0) sender.HaloTransferPhase = HaloTransferPhase.Sending;
            }
            if (sender.HaloTransferPhase != HaloTransferPhase.Sending) return 0;
            // 转多少同时受速率、对方空容量和自己现有能量限制。先真实扣发送方，再按效率向接收方计入。
            float received = Mathf.Min(CostPerSecond * Efficiency * dt,
                HaloEnergyRules.Maximum(receiver) - HaloEnergyRules.Energy(receiver),
                HaloEnergyRules.Energy(sender) * Efficiency);
            float spent = HaloEnergyRules.Spend(ref sender, received / Efficiency);
            received = spent * Efficiency;
            receiver.HaloEnergy =
                Mathf.Min(HaloEnergyRules.Maximum(receiver), HaloEnergyRules.Energy(receiver) + received);
            if (!CanSend(sender) || !CanReceive(receiver)) Recall(ref sender);
            return received;
        }
    }
}