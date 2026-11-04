using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Unity.MP_FPS
{
    public enum HaloTransferPhase : byte { Idle, Extending, Sending, Returning }

    // One-way, server-owned transfer between the actors' existing energy pools.
    public static class HaloTransferRules
    {
        public const float Range = 6, CostPerSecond = 20, Efficiency = .75f;
        public const float ExtendSeconds = .25f, ReturnSeconds = .2f;
        private const float Epsilon = .001f;

        public static bool Occupied(in PredictedPlayerGhost state) => state.HaloTransferPhase != HaloTransferPhase.Idle;
        public static bool CanSend(in PredictedPlayerGhost state) => state.HaloEquipped && state.HaloEnergyInitialized &&
            DollSingerWeapons.IsHalo(state.EquippedWeaponID) && EnergyShieldRules.Alive(state) &&
            (!state.ClashEnabled || state.ClashCanAct) && !state.ClashDarkSight && !state.ConsumingSupply &&
            state.HandAction == 0 && state.MeleePhase == 0 && !state.ControllerState.IsReloadingState &&
            state.WeaponEquipRemaining <= 0 && !state.HaloDefenseHeld && HaloEnergyRules.Energy(state) > Epsilon;
        public static bool CanReceive(in PredictedPlayerGhost state) => state.HaloEquipped && state.HaloEnergyInitialized &&
            EnergyShieldRules.Alive(state) && (!state.ClashEnabled || state.ClashCanAct) &&
            HaloEnergyRules.Maximum(state) - HaloEnergyRules.Energy(state) > Epsilon;

        public static bool Teammates(in PredictedPlayerGhost sender, in PredictedPlayerGhost receiver,
            bool senderHuman, bool receiverHuman)
        {
            if (sender.ClashEnabled || receiver.ClashEnabled)
                return sender.ClashEnabled && receiver.ClashEnabled && sender.ClashTeam >= 0 && sender.ClashTeam == receiver.ClashTeam;
            if (sender.OnlineHuman || receiver.OnlineHuman)
                return sender.OnlineHuman && receiver.OnlineHuman && sender.OnlineTeam >= 0 && sender.OnlineTeam == receiver.OnlineTeam;
            return senderHuman && receiverHuman;
        }

        public static float3 Anchor(in PredictedPlayerGhost state) => state.ControllerState.CurrentPosition + new float3(0, 1.25f, 0);
        public static bool InRange(float3 from, float3 to) => math.all(math.isfinite(from)) && math.all(math.isfinite(to)) &&
            math.distancesq(from, to) <= Range * Range;

        public static void Begin(ref PredictedPlayerGhost sender, Entity target, float3 point)
        {
            sender.HaloTransferPhase = HaloTransferPhase.Extending;
            sender.HaloTransferRemaining = ExtendSeconds;
            sender.HaloTransferTarget = target;
            sender.HaloTransferPoint = point;
            sender.HaloTransferWeapon = sender.EquippedWeaponID;
            HaloEnergyRules.Release(ref sender);
            HaloModuleFire.Reset(ref sender);
            sender.WeaponRaiseProgress = sender.HipFireHoldRemaining = 0;
            sender.HaloRecoveryDelay = HaloEnergyRules.RecoveryDelay;
            sender.HaloRegenerating = false;
        }

        public static void Recall(ref PredictedPlayerGhost sender)
        {
            if (!Occupied(sender) || sender.HaloTransferPhase == HaloTransferPhase.Returning) return;
            sender.HaloTransferPhase = HaloTransferPhase.Returning;
            sender.HaloTransferRemaining = ReturnSeconds;
            sender.HaloTransferTarget = Entity.Null;
        }

        public static void Clear(ref PredictedPlayerGhost sender)
        {
            sender.HaloTransferPhase = HaloTransferPhase.Idle;
            sender.HaloTransferRemaining = 0;
            sender.HaloTransferTarget = Entity.Null;
        }

        public static void TickReturn(ref PredictedPlayerGhost sender, float dt)
        {
            if (sender.HaloTransferPhase != HaloTransferPhase.Returning || !float.IsFinite(dt) || dt <= 0) return;
            sender.HaloTransferRemaining = Mathf.Max(0, sender.HaloTransferRemaining - dt);
            if (sender.HaloTransferRemaining <= 0) Clear(ref sender);
        }

        // Caller validates ownership, team, target identity, distance and line of sight each tick.
        public static float Transfer(ref PredictedPlayerGhost sender, ref PredictedPlayerGhost receiver, float dt)
        {
            if (!float.IsFinite(dt) || dt <= 0 || !CanSend(sender) || !CanReceive(receiver)) return 0;
            if (sender.HaloTransferPhase == HaloTransferPhase.Extending)
            {
                float travel = Mathf.Min(dt, sender.HaloTransferRemaining);
                sender.HaloTransferRemaining -= travel;
                dt -= travel;
                if (sender.HaloTransferRemaining <= 0) sender.HaloTransferPhase = HaloTransferPhase.Sending;
            }
            if (sender.HaloTransferPhase != HaloTransferPhase.Sending) return 0;
            float received = Mathf.Min(CostPerSecond * Efficiency * dt,
                HaloEnergyRules.Maximum(receiver) - HaloEnergyRules.Energy(receiver), HaloEnergyRules.Energy(sender) * Efficiency);
            float spent = HaloEnergyRules.Spend(ref sender, received / Efficiency);
            received = spent * Efficiency;
            receiver.HaloEnergy = Mathf.Min(HaloEnergyRules.Maximum(receiver), HaloEnergyRules.Energy(receiver) + received);
            if (!CanSend(sender) || !CanReceive(receiver)) Recall(ref sender);
            return received;
        }
    }
}
