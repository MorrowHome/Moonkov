using System;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    [Flags]
    public enum SurvivalEffects : ushort
    {
        None = 0, Hungry = 1, Starving = 2, Thirsty = 4, Dehydrated = 8,
        FullStomach = 16, LowOxygen = 32, Hypoxia = 64,
        ArmDisabled = 128, LegDisabled = 256, AbdomenDisabled = 512, AlienSurge = 1024
    }

    public static class RaidNutrition
    {
        public const float Capacity = 100, LowThreshold = 25, FullThreshold = 85;
        public const float FoodUseSeconds = 3, DrinkUseSeconds = 2;
        public static void Initialize(ref PredictedPlayerGhost state)
        {
            state.Energy = state.Hydration = Capacity; state.Satiety = 25;
            state.StarvationSeconds = state.DehydrationSeconds = 0;
            state.AlienSurgeSeconds = 0;
            state.MucusBlindSeconds = 0;
            state.AlienFoodEffect=AlienFoodEffect.None;state.AlienFoodEffectSeconds=0;
        }
        public static SurvivalEffects Effects(in PredictedPlayerGhost state)
        {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0) return SurvivalEffects.None;
            var effects = SurvivalEffects.None;
            if (state.Energy <= 0) effects |= SurvivalEffects.Starving;
            else if (state.Energy <= LowThreshold) effects |= SurvivalEffects.Hungry;
            if (state.Hydration <= 0) effects |= SurvivalEffects.Dehydrated;
            else if (state.Hydration <= LowThreshold) effects |= SurvivalEffects.Thirsty;
            if (state.Satiety >= FullThreshold) effects |= SurvivalEffects.FullStomach;
            if (RaidOxygen.NeedsSuitAir(state))
            {
                if (state.Oxygen <= 0) effects |= SurvivalEffects.Hypoxia;
                else if (state.Oxygen <= 10) effects |= SurvivalEffects.LowOxygen;
            }
            if (state.LeftArmHealth <= 0 || state.RightArmHealth <= 0) effects |= SurvivalEffects.ArmDisabled;
            if (state.LeftLegHealth <= 0 || state.RightLegHealth <= 0) effects |= SurvivalEffects.LegDisabled;
            if (state.AbdomenHealth <= 0) effects |= SurvivalEffects.AbdomenDisabled;
            if (state.AlienSurgeSeconds > 0) effects |= SurvivalEffects.AlienSurge;
            return effects;
        }
        public static bool CanConsume(in PredictedPlayerGhost state, ConsumableDefinition item) =>
            state.BodyHealthInitialized && state.CurrentHealth > 0 && item != null && item.Oxygen <= 0 &&
            (item.Food ? (state.Energy < Capacity || item.Health > 0 && HasInjuredLivingTissue(state) || item.SurgeSeconds > state.AlienSurgeSeconds ||
                item.EffectSeconds>0 && (state.AlienFoodEffect!=item.Effect || state.AlienFoodEffectSeconds<item.EffectSeconds)) &&
                state.Satiety + item.Satiety <= Capacity : state.Hydration < Capacity);
        public static InventoryError Consume(ref PredictedPlayerGhost state, InventoryGraph graph, string id, int version)
        {
            if (graph == null) return InventoryError.Invalid;
            if (version != graph.Version) return InventoryError.Stale;
            var item = graph.Find(id);
            if (item == null) return InventoryError.Missing;
            if (!graph.ConsumableAccessible(item)) return InventoryError.Inaccessible;
            var definition = ConsumableCatalog.Get(item.Code);
            if (!CanConsume(state, definition)) return InventoryError.Invalid;
            // Validate everything before changing either resource or stack. Version
            // and request ID guard replays; no client-specified recovery values.
            var error = graph.ConsumeOne(id, version);
            if (error != InventoryError.None) return error;
            state.Energy = Mathf.Clamp(state.Energy + definition.Energy, 0, Capacity);
            state.Hydration = Mathf.Clamp(state.Hydration + definition.Hydration, 0, Capacity);
            state.Satiety = Mathf.Clamp(state.Satiety + definition.Satiety, 0, Capacity);
            if (definition.SurgeSeconds > 0) state.AlienSurgeSeconds = definition.SurgeSeconds;
            if(definition.EffectSeconds>0) {state.AlienFoodEffect=definition.Effect;state.AlienFoodEffectSeconds=definition.EffectSeconds;}
            RestoreLivingTissue(ref state,definition.Health);
            if (state.Energy > 0) state.StarvationSeconds = 0;
            if (state.Hydration > 0) state.DehydrationSeconds = 0;
            return InventoryError.None;
        }
        private static void RestoreLivingTissue(ref PredictedPlayerGhost state,float healing)
        {
            // Food restores living tissue only; it cannot revive a destroyed body part.
            for (int i = 0; i < RaidHealth.PartCount && healing > 0; i++)
            {
                var part = (BodyPart)i;
                float current = RaidHealth.Get(state, part);
                if (current <= 0) continue;
                float restore = Mathf.Min(healing, RaidHealth.Maximum(part) - current);
                RaidHealth.Set(ref state, part, current + restore); healing -= restore;
            }
        }
        public static void Tick(ref PredictedPlayerGhost state, float dt, float energySeconds, float hydrationSeconds,
            float digestionSeconds, float graceSeconds, float starvationDamage, float dehydrationDamage, uint tick)
        {
            if (dt <= 0 || !float.IsFinite(dt)) return;
            state.MucusBlindSeconds = state.CurrentHealth > 0 ? Mathf.Max(0,state.MucusBlindSeconds-dt) : 0;
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0) return;
            state.AlienSurgeSeconds = Mathf.Max(0, state.AlienSurgeSeconds - dt);
            float activeTime=Mathf.Min(dt,Mathf.Max(0,state.AlienFoodEffectSeconds));
            float activeFraction=activeTime/dt;
            if(activeTime>0)
            {
                if(state.AlienFoodEffect==AlienFoodEffect.Regeneration)RestoreLivingTissue(ref state,activeTime);
                else if(state.AlienFoodEffect==AlienFoodEffect.VenomPower)DamageSystemic(ref state,activeTime*.6f,tick);
            }
            if(state.CurrentHealth<=0)return;
            bool moving = state.ControllerState.MovementSpeed > .1f;
            bool sprinting = moving && state.ControllerState.Sprinting;
            float abdomen = state.AbdomenHealth <= 0 ? 2 : 1;
            float energyRate = Capacity / Mathf.Max(1, energySeconds) * abdomen * (sprinting ? 1.75f : moving ? 1.1f : 1);
            float waterRate = Capacity / Mathf.Max(1, hydrationSeconds) * abdomen * (sprinting ? 1.4f : moving ? 1.1f : 1);
            if(state.AlienFoodEffect==AlienFoodEffect.Regeneration)energyRate*=1+.5f*activeFraction;
            if(state.AlienFoodEffect==AlienFoodEffect.AdrenalRush)waterRate*=1+.5f*activeFraction;
            if(state.AlienFoodEffect==AlienFoodEffect.NeuralOverdrive)waterRate*=1+.75f*activeFraction;
            state.AlienFoodEffectSeconds=Mathf.Max(0,state.AlienFoodEffectSeconds-dt);
            if(state.AlienFoodEffectSeconds<=0)state.AlienFoodEffect=AlienFoodEffect.None;
            float hungryTime = Drain(ref state.Energy, ref state.StarvationSeconds, energyRate, dt, graceSeconds);
            float thirstyTime = Drain(ref state.Hydration, ref state.DehydrationSeconds, waterRate, dt, graceSeconds);
            state.Satiety = Mathf.Max(0, state.Satiety - Capacity / Mathf.Max(1, digestionSeconds) * dt);
            float damage = hungryTime * Mathf.Max(0, starvationDamage) + thirstyTime * Mathf.Max(0, dehydrationDamage);
            if (damage > 0) DamageSystemic(ref state, damage, tick);
        }
        private static bool HasInjuredLivingTissue(in PredictedPlayerGhost state)
        {
            for(int i=0;i<RaidHealth.PartCount;i++)
            { float health=RaidHealth.Get(state,(BodyPart)i); if(health>0 && health<RaidHealth.Maximum((BodyPart)i))return true; }
            return false;
        }
        private static float Drain(ref float resource, ref float depletedSeconds, float rate, float dt, float grace)
        {
            float exhausted = Mathf.Max(0, dt - resource / rate);
            resource = Mathf.Max(0, resource - rate * dt);
            if (exhausted <= 0) { depletedSeconds = 0; return 0; }
            grace = Mathf.Max(0, grace);
            float before = Mathf.Max(0, depletedSeconds - grace);
            depletedSeconds += exhausted;
            return Mathf.Max(0, depletedSeconds - grace) - before;
        }
        // Metabolic damage affects remaining tissue proportionally, including
        // players with a black abdomen, and uses the existing death/settlement path.
        private static void DamageSystemic(ref PredictedPlayerGhost state, float amount, uint tick)
        {
            float scale = Mathf.Max(0, 1 - amount / state.CurrentHealth);
            for (int i = 0; i < RaidHealth.PartCount; i++)
            { var part = (BodyPart)i; RaidHealth.Set(ref state, part, RaidHealth.Get(state, part) * scale); }
            state.LastDamageAmount = amount; state.LastHitTick = tick;
            state.LastHitPart = BodyPart.Auto; state.ControllerState.IsHit = true;
        }
        public static bool CanSprint(in PredictedPlayerGhost state) => !state.BodyHealthInitialized ||
            (state.Energy > 0 && state.Hydration > 0 && (state.Oxygen > 0 || !RaidOxygen.NeedsSuitAir(state)) &&
             state.LeftLegHealth > 0 && state.RightLegHealth > 0);
        public static FirstPersonController.ControllerConsts Movement(in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants)
        {
            if (!state.BodyHealthInitialized) return constants;
            if (state.Energy <= LowThreshold)
            { constants.Walk.Speed *= state.Energy <= 0 ? .75f : .9f; constants.Sprint.Speed *= .8f; constants.JumpHeight *= .8f; }
            if (state.Hydration <= LowThreshold)
            { constants.Walk.Speed *= state.Hydration <= 0 ? .75f : .9f; constants.Sprint.Speed *= .85f; constants.JumpHeight *= .8f; }
            if (state.Satiety >= FullThreshold) constants.Sprint.Speed *= .85f;
            if (state.Oxygen <= 10 && RaidOxygen.NeedsSuitAir(state))
            { constants.Walk.Speed *= state.Oxygen <= 0 ? .65f : .85f; constants.Sprint.Speed *= .8f; constants.JumpHeight *= .7f; }
            if (state.LeftLegHealth <= 0 || state.RightLegHealth <= 0)
            { constants.Walk.Speed *= .65f; constants.JumpHeight *= .5f; }
            if (!CanSprint(state)) constants.Sprint.Speed = constants.Walk.Speed;
            if(state.AlienFoodEffectSeconds>0)
            {
                float speed=state.AlienFoodEffect==AlienFoodEffect.AdrenalRush ? 1.15f : state.AlienFoodEffect==AlienFoodEffect.ContractilePower ? .85f :
                    state.AlienFoodEffect==AlienFoodEffect.GelArmor ? .8f : state.AlienFoodEffect==AlienFoodEffect.OxygenReserve ? .9f : 1;
                constants.Walk.Speed*=speed;constants.Sprint.Speed*=speed;
            }
            return constants;
        }
        public static float HandlingMultiplier(in PredictedPlayerGhost state)
        {
            if (!state.BodyHealthInitialized) return 1;
            float multiplier = state.LeftArmHealth <= 0 || state.RightArmHealth <= 0 ? 1.4f : 1;
            if (state.Hydration <= LowThreshold) multiplier *= state.Hydration <= 0 ? 1.25f : 1.1f;
            if (state.Oxygen <= 10 && RaidOxygen.NeedsSuitAir(state)) multiplier *= 1.1f;
            if(((HaloModule)state.EquippedModules&HaloModule.BiologicalStability)!=0)multiplier=1+(multiplier-1)*.85f;
            return multiplier;
        }
    }
}
