// 远征的饥渴、吃喝效果和随之变化的移动处理。用真实移动和时间计算消耗，归零后产生身体伤害，并把轻重状态提供给控制器。
using System;
using Unity.Mathematics;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // 服务器记录实际水平位移，与控制器请求的动画速度分别保存。
    // 位置不连续的跳变不当作体力运动。
    public struct RaidSurvivalMotion {
        private float3 m_Position;
        private bool m_Known;

        // 记录真实位置差而非只看输入。帧时长异常或突然传送时不把那段距离记为常规走动。
        public bool Observe(float3 position, float dt) {
            if (!math.all(math.isfinite(position)) || !float.IsFinite(dt) || dt <= 0) {
                m_Known = false;
                return false;
            }
            var travel = position - m_Position;
            bool known = m_Known;
            m_Position = position;
            m_Known = true;
            float distanceSq = travel.x * travel.x + travel.z * travel.z;
            float minimum = Mathf.Max(.005f, dt * .15f), maximum = Mathf.Max(2, dt * 12);
            return known && distanceSq > minimum * minimum && distanceSq <= maximum * maximum;
        }
    }

    [Flags]
    public enum SurvivalEffects : ushort {
        None = 0,
        Hungry = 1,
        Starving = 2,
        Thirsty = 4,
        Dehydrated = 8,
        FullStomach = 16,
        LowOxygen = 32,
        Hypoxia = 64,
        ArmDisabled = 128,
        LegDisabled = 256,
        AbdomenDisabled = 512,
        AlienSurge = 1024
    }

    public static class RaidNutrition {
        public const float Capacity = 100, LowThreshold = 25, FullThreshold = 85;
        public const float FoodUseSeconds = 3, DrinkUseSeconds = 2;
        public const float SupplyUseMaxSpeed = 1.5f;

        public static void Initialize(ref PredictedPlayerGhost state) {
            state.Energy = state.Hydration = Capacity;
            state.Satiety = 25;
            state.StarvationSeconds = state.DehydrationSeconds = 0;
            state.AlienSurgeSeconds = 0;
            state.MucusBlindSeconds = 0;
            state.AlienFoodEffect = AlienFoodEffect.None;
            state.AlienFoodEffectSeconds = 0;
        }

        public static SurvivalEffects Effects(in PredictedPlayerGhost state) {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0) return SurvivalEffects.None;
            // 界面的饥渴、缺氧和肢体受伤是同一个组合状态，可以同时亮几项。资源值在各自规则里推进。
            var effects = SurvivalEffects.None;
            if (state.Energy <= 0)
                effects |= SurvivalEffects.Starving;
            else if (state.Energy <= LowThreshold) effects |= SurvivalEffects.Hungry;
            if (state.Hydration <= 0)
                effects |= SurvivalEffects.Dehydrated;
            else if (state.Hydration <= LowThreshold) effects |= SurvivalEffects.Thirsty;
            if (state.Satiety >= FullThreshold) effects |= SurvivalEffects.FullStomach;
            if (RaidOxygen.NeedsSuitAir(state)) {
                if (state.Oxygen <= 0)
                    effects |= SurvivalEffects.Hypoxia;
                else if (state.Oxygen <= 10) effects |= SurvivalEffects.LowOxygen;
            }
            if (state.LeftArmHealth <= 0 || state.RightArmHealth <= 0) effects |= SurvivalEffects.ArmDisabled;
            if (state.LeftLegHealth <= 0 || state.RightLegHealth <= 0) effects |= SurvivalEffects.LegDisabled;
            if (state.AbdomenHealth <= 0) effects |= SurvivalEffects.AbdomenDisabled;
            if (state.AlienSurgeSeconds > 0) effects |= SurvivalEffects.AlienSurge;
            return effects;
        }

        // 进食检查饱腹空间以及是否确实有可恢复的资源或可延长的效果，饮水按水分需求判。氧气罐走补氧分支。
        public static bool CanConsume(in PredictedPlayerGhost state, ConsumableDefinition item) {
            return state.BodyHealthInitialized && state.CurrentHealth > 0 && item != null && item.Oxygen <= 0 &&
                   (item.Food
                       ? (state.Energy < Capacity || (item.Health > 0 && HasInjuredLivingTissue(state)) ||
                          item.SurgeSeconds > state.AlienSurgeSeconds ||
                          (item.EffectSeconds > 0 && (state.AlienFoodEffect != item.Effect ||
                                                      state.AlienFoodEffectSeconds < item.EffectSeconds))) &&
                         state.Satiety + item.Satiety <= Capacity
                       : state.Hydration < Capacity);
        }

        public static InventoryError Consume(
            ref PredictedPlayerGhost state,
            InventoryGraph graph,
            string id,
            int version) {
            if (graph == null) return InventoryError.Invalid;
            if (version != graph.Version) return InventoryError.Stale;
            var item = graph.Find(id);
            if (item == null) return InventoryError.Missing;
            if (!graph.ConsumableAccessible(item)) return InventoryError.Inaccessible;
            var definition = ConsumableCatalog.Get(item.Code);
            if (!CanConsume(state, definition)) return InventoryError.Invalid;
            // 改资源或物品堆前先全部校验。版本和请求号
            // 挡重复提交，恢复量从道具定义取。
            // 先查版本、可访问性和是否用得上，再扣实际物品。更新的饥渴效果都从固定道具定义取，不听客户端自报数值。
            var error = graph.ConsumeOne(id, version);
            if (error != InventoryError.None) return error;
            state.Energy = Mathf.Clamp(state.Energy + definition.Energy, 0, Capacity);
            state.Hydration = Mathf.Clamp(state.Hydration + definition.Hydration, 0, Capacity);
            state.Satiety = Mathf.Clamp(state.Satiety + definition.Satiety, 0, Capacity);
            if (definition.SurgeSeconds > 0) state.AlienSurgeSeconds = definition.SurgeSeconds;
            if (definition.EffectSeconds > 0) {
                state.AlienFoodEffect = definition.Effect;
                state.AlienFoodEffectSeconds = definition.EffectSeconds;
            }
            RestoreLivingTissue(ref state, definition.Health);
            if (state.Energy > 0) state.StarvationSeconds = 0;
            if (state.Hydration > 0) state.DehydrationSeconds = 0;
            return InventoryError.None;
        }

        // 食物回复只作用在还没归零的部位。已经损坏的部位要走专用医疗流程。
        private static void RestoreLivingTissue(ref PredictedPlayerGhost state, float healing) {
            // 食物只回复尚有生命的部位，不能修复已毁坏的部位。
            for (int i = 0; i < RaidHealth.PartCount && healing > 0; i++) {
                var part = (BodyPart)i;
                float current = RaidHealth.Get(state, part);
                if (current <= 0) continue;
                float restore = Mathf.Min(healing, Mathf.Max(0, RaidHealth.Maximum(state, part) - current));
                if (restore <= 0) continue;
                RaidHealth.Set(ref state, part, current + restore);
                healing -= restore;
            }
        }

        public static void Tick(
            ref PredictedPlayerGhost state,
            float dt,
            float energySeconds,
            float hydrationSeconds,
            float digestionSeconds,
            float graceSeconds,
            float starvationDamage,
            float dehydrationDamage,
            uint tick,
            bool? movingOverride = null) {
            if (dt <= 0 || !float.IsFinite(dt)) return;
            state.MucusBlindSeconds = state.CurrentHealth > 0 ? Mathf.Max(0, state.MucusBlindSeconds - dt) : 0;
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0) return;
            state.AlienSurgeSeconds = Mathf.Max(0, state.AlienSurgeSeconds - dt);
            // 异形食物的增益在帧中途到期时，只把有效的那段计算进去。回复与负面副作用都使用这一段。
            float activeTime = Mathf.Min(dt, Mathf.Max(0, state.AlienFoodEffectSeconds));
            float activeFraction = activeTime / dt;
            if (activeTime > 0) {
                if (state.AlienFoodEffect == AlienFoodEffect.Regeneration)
                    RestoreLivingTissue(ref state, activeTime);
                else if (state.AlienFoodEffect == AlienFoodEffect.VenomPower)
                    DamageSystemic(ref state, activeTime * .6f, tick);
            }
            if (state.CurrentHealth <= 0) return;
            bool moving = movingOverride ?? state.ControllerState.MovementSpeed > .1f;
            bool sprinting = moving && state.ControllerState.Sprinting;
            // 腹部损伤和实际跑动都会加大消耗。基础时长从地图设定拿，倍率由角色当前状态决定。
            float abdomen = state.AbdomenHealth <= 0 ? 2 : 1;
            float energyRate = Capacity / Mathf.Max(1, energySeconds) * abdomen *
                               (sprinting ? 1.75f : moving ? 1.1f : 1);
            float waterRate = Capacity / Mathf.Max(1, hydrationSeconds) * abdomen *
                              (sprinting ? 1.4f : moving ? 1.1f : 1);
            if (state.AlienFoodEffect == AlienFoodEffect.Regeneration) energyRate *= 1 + .5f * activeFraction;
            if (state.AlienFoodEffect == AlienFoodEffect.AdrenalRush) waterRate *= 1 + .5f * activeFraction;
            if (state.AlienFoodEffect == AlienFoodEffect.NeuralOverdrive) waterRate *= 1 + .75f * activeFraction;
            state.AlienFoodEffectSeconds = Mathf.Max(0, state.AlienFoodEffectSeconds - dt);
            if (state.AlienFoodEffectSeconds <= 0) state.AlienFoodEffect = AlienFoodEffect.None;
            // 分别计算能量与水分在这帧超出宽限的耗尽时间。最后将两种损害一起送给身体，不在到零的那一刻扣整帧。
            float hungryTime = Drain(ref state.Energy, ref state.StarvationSeconds, energyRate, dt, graceSeconds);
            float thirstyTime = Drain(ref state.Hydration, ref state.DehydrationSeconds, waterRate, dt, graceSeconds);
            state.Satiety = Mathf.Max(0, state.Satiety - Capacity / Mathf.Max(1, digestionSeconds) * dt);
            float damage = hungryTime * Mathf.Max(0, starvationDamage) + thirstyTime * Mathf.Max(0, dehydrationDamage);
            if (damage > 0) DamageSystemic(ref state, damage, tick);
        }

        private static bool HasInjuredLivingTissue(in PredictedPlayerGhost state) {
            for (int i = 0; i < RaidHealth.PartCount; i++) {
                var part = (BodyPart)i;
                float health = RaidHealth.Get(state, part);
                if (health > 0 && health < RaidHealth.Maximum(state, part)) return true;
            }
            return false;
        }

        private static float Drain(ref float resource, ref float depletedSeconds, float rate, float dt, float grace) {
            float exhausted = Mathf.Max(0, dt - resource / rate);
            resource = Mathf.Max(0, resource - rate * dt);
            if (exhausted <= 0) {
                depletedSeconds = 0;
                return 0;
            }
            grace = Mathf.Max(0, grace);
            float before = Mathf.Max(0, depletedSeconds - grace);
            depletedSeconds += exhausted;
            return Mathf.Max(0, depletedSeconds - grace) - before;
        }

        // 代谢伤害按剩余部位血量比例扣除，腹部归零的角色也适用，
        // 死亡仍沿用既有战局结算。
        // 系统性损伤按剩余部位血量比例分摊，同时留下 HUD 受击状态。这个路径属于生存消耗，没有护盾挡伤。
        private static void DamageSystemic(ref PredictedPlayerGhost state, float amount, uint tick) {
            float scale = Mathf.Max(0, 1 - amount / state.CurrentHealth);
            for (int i = 0; i < RaidHealth.PartCount; i++) {
                var part = (BodyPart)i;
                RaidHealth.Set(ref state, part, RaidHealth.Get(state, part) * scale);
            }
            state.LastDamageAmount = amount;
            state.LastHitTick = tick;
            state.LastHitPart = BodyPart.Auto;
            state.ControllerState.IsHit = true;
        }

        public static bool CanSprint(in PredictedPlayerGhost state) {
            return !state.BodyHealthInitialized || (state.Energy > 0 && state.Hydration > 0 &&
                                                    (state.Oxygen > 0 || !RaidOxygen.NeedsSuitAir(state)));
        }

        // 把营养、缺氧或肢体伤对移动的影响写进本帧控制参数，移动和减少资源是两条独立计算。
        public static FirstPersonController.ControllerConsts Movement(
            in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants) {
            if (!state.BodyHealthInitialized) return constants;
            if (state.Energy <= LowThreshold) {
                constants.Walk.Speed *= state.Energy <= 0 ? .75f : .9f;
                constants.Sprint.Speed *= .8f;
                constants.JumpHeight *= .8f;
            }
            if (state.Hydration <= LowThreshold) {
                constants.Walk.Speed *= state.Hydration <= 0 ? .75f : .9f;
                constants.Sprint.Speed *= .85f;
                constants.JumpHeight *= .8f;
            }
            if (state.Satiety >= FullThreshold) constants.Sprint.Speed *= .85f;
            if (state.Oxygen <= 10 && RaidOxygen.NeedsSuitAir(state)) {
                constants.Walk.Speed *= state.Oxygen <= 0 ? .65f : .85f;
                constants.Sprint.Speed *= .8f;
                constants.JumpHeight *= .7f;
            }
            if (state.LeftLegHealth <= 0 || state.RightLegHealth <= 0) {
                constants.Walk.Speed *= .65f;
                constants.Sprint.Speed *= .65f;
                constants.JumpHeight *= .5f;
            }
            if (!CanSprint(state)) constants.Sprint.Speed = constants.Walk.Speed;
            if (state.AlienFoodEffectSeconds > 0) {
                float speed = state.AlienFoodEffect == AlienFoodEffect.AdrenalRush ? 1.15f :
                    state.AlienFoodEffect == AlienFoodEffect.ContractilePower ? .85f :
                    state.AlienFoodEffect == AlienFoodEffect.GelArmor ? .8f :
                    state.AlienFoodEffect == AlienFoodEffect.OxygenReserve ? .9f : 1;
                constants.Walk.Speed *= speed;
                constants.Sprint.Speed *= speed;
            }
            return constants;
        }

        public static float HandlingMultiplier(in PredictedPlayerGhost state) {
            if (!state.BodyHealthInitialized) return 1;
            float multiplier = state.LeftArmHealth <= 0 || state.RightArmHealth <= 0 ? 1.4f : 1;
            if (state.Hydration <= LowThreshold) multiplier *= state.Hydration <= 0 ? 1.25f : 1.1f;
            if (state.Oxygen <= 10 && RaidOxygen.NeedsSuitAir(state)) multiplier *= 1.1f;
            if (((HaloModule)state.EquippedModules & HaloModule.BiologicalStability) != 0)
                multiplier = 1 + (multiplier - 1) * .85f;
            return multiplier;
        }
    }
}