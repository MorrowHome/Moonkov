// 分部位血量、受伤、治疗和身体动作限制的共用规则。外部击中通过 CombatDamage 先算护盾，缺氧或饥渴等内部伤害直接走身体扣血。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    // Anatomical left/right, independent of the camera. Auto is an intent: the
    // server chooses the most injured part when using the existing quick-use key.
    public enum BodyPart : byte {
        Head,
        Chest,
        Abdomen,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
        Auto = 255
    }

    public static class RaidHealth {
        public const int PartCount = 7;
        public const float OxygenCapacity = 100f;
        public const float SafeFallHeight = 4.5f;
        public const float MoonGravity = 1.62f;
        public const float FallDamagePerMeter = 12f;
        public const float MaximumFallDamage = 600f;
        public const float InjuredSprintDamagePerLegPerSecond = 2f;

        public static float Maximum(BodyPart part) {
            switch (part) {
                case BodyPart.Head: return 35f;
                case BodyPart.Chest: return 85f;
                case BodyPart.Abdomen: return 70f;
                case BodyPart.LeftArm:
                case BodyPart.RightArm:
                    return 60f;
                case BodyPart.LeftLeg:
                case BodyPart.RightLeg:
                    return 65f;
                default: return 0f;
            }
        }

        public static float Maximum(in PredictedPlayerGhost state, BodyPart part) {
            return Maximum(part) * (state.ClashEnabled ? Mathf.Clamp01(state.ClashMaxHealthFraction) : 1);
        }

        public static float Get(in PredictedPlayerGhost state, BodyPart part) {
            switch (part) {
                case BodyPart.Head: return state.HeadHealth;
                case BodyPart.Chest: return state.ChestHealth;
                case BodyPart.Abdomen: return state.AbdomenHealth;
                case BodyPart.LeftArm: return state.LeftArmHealth;
                case BodyPart.RightArm: return state.RightArmHealth;
                case BodyPart.LeftLeg: return state.LeftLegHealth;
                case BodyPart.RightLeg: return state.RightLegHealth;
                default: return 0f;
            }
        }

        // 按部位上限夹住实际生命值，再重算总量。任何调用者更新一个部位后，都能读到一致的存活状态。
        public static void Set(ref PredictedPlayerGhost state, BodyPart part, float value) {
            value = Mathf.Clamp(value, 0, Maximum(state, part));
            switch (part) {
                case BodyPart.Head: state.HeadHealth = value; break;
                case BodyPart.Chest: state.ChestHealth = value; break;
                case BodyPart.Abdomen: state.AbdomenHealth = value; break;
                case BodyPart.LeftArm: state.LeftArmHealth = value; break;
                case BodyPart.RightArm: state.RightArmHealth = value; break;
                case BodyPart.LeftLeg: state.LeftLegHealth = value; break;
                case BodyPart.RightLeg: state.RightLegHealth = value; break;
            }
            Synchronize(ref state);
        }

        // 出生初始化玩家七个部位及对应的生存相关字段。不同模式的血量倍率在 Maximum 中一起应用。
        public static void Initialize(ref PredictedPlayerGhost state) {
            state.PendingBulletWound = 0;
            state.PendingBulletWoundSource = default;
            state.ReloadQueued = false;
            state.BodyHealthInitialized = true;
            state.HeadHealth = 35;
            state.ChestHealth = 85;
            state.AbdomenHealth = 70;
            state.LeftArmHealth = state.RightArmHealth = 60;
            state.LeftLegHealth = state.RightLegHealth = 65;
            if (!state.ProtectionInitialized) RaidOxygen.Initialize(ref state);
            RaidNutrition.Initialize(ref state);
            state.TacticalSprintStamina = TacticalSprintRules.Capacity;
            state.TacticalSprintExhausted = false;
            Synchronize(ref state);
        }

        // 角色可否行动与各部位余量分开。头胸是生死门槛，其他部位损坏主要影响动作。
        private static void Synchronize(ref PredictedPlayerGhost state) {
            if (state.ClashEnabled) {
                state.HeadHealth = Mathf.Min(state.HeadHealth, Maximum(state, BodyPart.Head));
                state.ChestHealth = Mathf.Min(state.ChestHealth, Maximum(state, BodyPart.Chest));
                state.AbdomenHealth = Mathf.Min(state.AbdomenHealth, Maximum(state, BodyPart.Abdomen));
                state.LeftArmHealth = Mathf.Min(state.LeftArmHealth, Maximum(state, BodyPart.LeftArm));
                state.RightArmHealth = Mathf.Min(state.RightArmHealth, Maximum(state, BodyPart.RightArm));
                state.LeftLegHealth = Mathf.Min(state.LeftLegHealth, Maximum(state, BodyPart.LeftLeg));
                state.RightLegHealth = Mathf.Min(state.RightLegHealth, Maximum(state, BodyPart.RightLeg));
            }
            state.MaxHealth = 440f * (state.ClashEnabled ? Mathf.Clamp01(state.ClashMaxHealthFraction) : 1);
            state.CurrentHealth =
                (state.ClashEnabled && state.ClashLife != ClashLife.Alive) || state.HeadHealth <= 0 ||
                state.ChestHealth <= 0
                    ? 0f
                    : TissueHealth(state);
        }

        internal static float TissueHealth(in PredictedPlayerGhost state) {
            return state.HeadHealth + state.ChestHealth + state.AbdomenHealth + state.LeftArmHealth +
                   state.RightArmHealth + state.LeftLegHealth + state.RightLegHealth;
        }

        // 先扣命中部位。当一个普通部位剩余血量吃不下本次伤害时，溢出的部分继续摊给其他部位。头或胸归零会把角色总血量标为零。
        public static float Damage(
            ref PredictedPlayerGhost state,
            BodyPart part,
            float amount,
            uint tick,
            bool physiological = false) {
            if (state.CurrentHealth <= 0 || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount) ||
                Maximum(part) <= 0)
                return 0;
            float before = state.CurrentHealth;
            if (!state.BodyHealthInitialized) { state.CurrentHealth = Mathf.Max(0, before - amount); } else {
                // 先把命中部位可承受的部分扣完，再将剩余伤害按比例给其他仍有血的部位。
                float local = Mathf.Min(Get(state, part), amount);
                Set(ref state, part, Get(state, part) - local);
                float overflow = amount - local;
                // A blacked-out limb/abdomen still receives damage. Overflow is
                // distributed over remaining tissue, conserving incoming damage.
                if (overflow > 0 && state.CurrentHealth > 0) {
                    float remaining = state.CurrentHealth;
                    for (int i = 0; i < PartCount; i++) {
                        var other = (BodyPart)i;
                        if (other != part)
                            Set(ref state, other, Get(state, other) * Mathf.Max(0, 1f - overflow / remaining));
                    }
                }
            }
            // 记录伤害部位、显示量和 tick 给 HUD、表现与动作打断用。生理扣血和被敌人击中用不同的细分序号。
            state.LastHitPart = part;
            state.LastDamageAmount = amount;
            state.LastHitTick = tick;
            state.ControllerState.IsHit = true;
            if (!physiological && state.CurrentHealth < before) state.NonPhysiologicalDamageSequence++;
            return before - state.CurrentHealth;
        }

        // Server-only, after collision resolution. Holding sprint at rest, jumping,
        // teleports and prediction replays must not charge an injured runner.
        // 受伤冲刺扣血依赖服务端碰撞处理后的实际位移。原地按住冲刺、空中移动或跳过来的位置都不按有效冲刺计。
        public static float ApplyInjuredSprintDamage(
            ref PredictedPlayerGhost state,
            Vector3 travel,
            bool wasGrounded,
            float dt,
            uint tick) {
            if (!state.BodyHealthInitialized || state.ClashEnabled || state.CurrentHealth <= 0 ||
                !state.ControllerState.Sprinting || !wasGrounded ||
                state.ControllerState.MovementType != FirstPersonController.MovementType.Standing ||
                !float.IsFinite(dt) || dt <= 0 || dt > .25f)
                return 0;
            travel.y = 0;
            float distance = travel.magnitude;
            float speed = state.ControllerState.MovementSpeed;
            if (!float.IsFinite(distance) || !float.IsFinite(speed) || speed <= .1f || distance <= dt * .15f ||
                distance > Mathf.Max(.5f, speed * dt * 1.5f))
                return 0;
            float before = state.CurrentHealth;
            float damage = InjuredSprintDamagePerLegPerSecond * dt;
            if (state.LeftLegHealth <= 0) Damage(ref state, BodyPart.LeftLeg, damage, tick, true);
            if (state.RightLegHealth <= 0) Damage(ref state, BodyPart.RightLeg, damage, tick, true);
            return before - state.CurrentHealth;
        }

        // The controller measures physical descent after the apex, not its artificial
        // standing fall velocity. Gravity scales impact energy for the same distance.
        public static float FallDamage(float height, float gravity) {
            if (!float.IsFinite(height) || !float.IsFinite(gravity) || height <= SafeFallHeight || gravity >= 0)
                return 0;
            double damage = ((double)height - SafeFallHeight) * FallDamagePerMeter * (-(double)gravity / MoonGravity);
            return (float)Math.Min(MaximumFallDamage, damage);
        }

        // Called only by the server immediately after the controller changes to landed.
        // The pending flag prevents replaying an event until input processing clears it.
        // 只有首次进入落地状态才按高度对两腿分摊伤害。重复调用不能因同一个落地事件连扣。
        public static float ApplyLandingDamage(
            ref PredictedPlayerGhost state,
            bool wasLanding,
            float gravity,
            uint tick) {
            if (wasLanding || !state.ControllerState.LandTriggered || !state.BodyHealthInitialized ||
                state.ClashEnabled || state.CurrentHealth <= 0)
                return 0;
            float damage = FallDamage(state.ControllerState.FallHeight, gravity);
            if (damage <= 0) return 0;
            float before = state.CurrentHealth;
            Damage(ref state, BodyPart.LeftLeg, damage * .5f, tick);
            Damage(ref state, BodyPart.RightLeg, damage * .5f, tick);
            float applied = before - state.CurrentHealth;
            state.LastDamageAmount = applied;
            return applied;
        }

        // Combat calls once per physical impact, before anatomical distribution.
        // Hypoxia, nutrition and other physiological damage keep using Damage/Set directly.
        // 如果附近有能保护该角色的外放光环，先用它吸收一部分，剩下的再走角色自己的护盾和部位伤害。
        public static float CombatDamage(
            Entities.EntityManager manager,
            Entities.Entity victim,
            ref PredictedPlayerGhost state,
            BodyPart part,
            float amount,
            uint tick,
            RaidDamageKind kind,
            Mathematics.float3 impactPoint = default) {
            if (!EnergyShieldRules.Alive(state) || !float.IsFinite(amount) || amount <= 0 ||
                (part != BodyPart.Auto && Maximum(part) <= 0))
                return 0;
            var movement = manager.World.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            if (movement != null)
                amount = movement.AbsorbHaloSupport(victim, ref state, amount, tick, kind, impactPoint);
            return CombatDamage(ref state, part, amount, tick, kind, impactPoint);
        }

        public static float CombatDamage(
            ref PredictedPlayerGhost state,
            BodyPart part,
            float amount,
            uint tick,
            RaidDamageKind kind,
            Mathematics.float3 impactPoint = default) {
            if (!EnergyShieldRules.Alive(state) || !float.IsFinite(amount) || amount <= 0 ||
                (part != BodyPart.Auto && Maximum(part) <= 0))
                return 0;
            // 一次攻击先完整过护盾，再看是否按比例分摊到各部位。不能先分成七份再扣七次光环能量。
            float remaining = EnergyShieldRules.Absorb(ref state, amount, tick, kind, impactPoint);
            // Gel armor modifies combat damage reaching tissue. It does not
            // reduce oxygen deprivation or the physical impact of a fall.
            if (state.AlienFoodEffectSeconds > 0 && state.AlienFoodEffect == AlienFoodEffect.GelArmor)
                remaining *= .75f;
            float before = state.CurrentHealth;
            if (part == BodyPart.Auto) {
                if (!state.BodyHealthInitialized)
                    Damage(ref state, BodyPart.Chest, remaining, tick);
                else
                    for (int i = 0; i < PartCount; i++) {
                        var target = (BodyPart)i;
                        float share = remaining * Get(state, target) / Mathf.Max(.001f, before);
                        Damage(ref state, target, share, tick);
                    }
                state.LastDamageAmount = before - state.CurrentHealth;
                state.LastHitPart = BodyPart.Auto;
            } else { Damage(ref state, part, remaining, tick); }
            if (state.CurrentHealth < before && (kind == RaidDamageKind.Bullet || kind == RaidDamageKind.Explosion ||
                                                 kind == RaidDamageKind.Melee))
                state.LastImpactTick = tick;
            return before - state.CurrentHealth;
        }

        // A shield-only hit is real, but a team-protected or otherwise rejected
        // impact must not produce a hit confirmation or mark.
        // 根据攻击前后身体或盾的实际变化给命中反馈。友军被保护没有变化就不该显示命中确认。
        public static byte ImpactFlags(in PredictedPlayerGhost before, in PredictedPlayerGhost after) {
            if (!(before.CurrentHealth > 0) || (!(after.CurrentHealth < before.CurrentHealth) &&
                                                after.ShieldHitSequence == before.ShieldHitSequence))
                return 0;
            return (byte)(after.CurrentHealth <= 0 ? 3 : 1);
        }

        public static BodyPart MostInjured(in PredictedPlayerGhost state) {
            var selected = BodyPart.Auto;
            float lowest = 1f;
            for (int i = 0; i < PartCount; i++) {
                var part = (BodyPart)i;
                float fraction = Get(state, part) / Maximum(part);
                if (fraction < lowest) {
                    lowest = fraction;
                    selected = part;
                }
            }
            return selected;
        }

        public static bool CanHeal(in PredictedPlayerGhost state, BodyPart part) {
            return state.BodyHealthInitialized && state.CurrentHealth > 0 && Maximum(state, part) > 0 &&
                   Get(state, part) < Maximum(state, part);
        }

        // One total budget across the body. Never reconstruct a destroyed part or a lost Clash health cap.
        // 被动回复只加还活着且缺血的部位。不复活归零的身体部位。
        public static float Regenerate(ref PredictedPlayerGhost state, float amount) {
            if (!float.IsFinite(amount) || amount <= 0 || !state.BodyHealthInitialized || state.CurrentHealth <= 0 ||
                state.TacticalPoisonSeconds > 0 || (state.ClashEnabled && state.ClashLife != ClashLife.Alive))
                return 0;
            float before = state.CurrentHealth;
            for (int n = 0; n < PartCount && amount > 0; n++) {
                var selected = BodyPart.Auto;
                float fraction = 1;
                for (int p = 0; p < PartCount; p++) {
                    var part = (BodyPart)p;
                    float value = Get(state, part), maximum = Maximum(state, part);
                    if (value > 0 && maximum > value && value / maximum < fraction) {
                        selected = part;
                        fraction = value / maximum;
                    }
                }
                if (selected == BodyPart.Auto) break;
                float current = Get(state, selected), healed = Mathf.Min(amount, Maximum(state, selected) - current);
                Set(ref state, selected, current + healed);
                amount -= healed;
            }
            return state.CurrentHealth - before;
        }

        // 在背包、版本和目标部位核对完成后才扣医疗物品。止血和恢复值由具体物品规则决定。
        public static Inventory.InventoryError UseMedical(
            ref PredictedPlayerGhost state,
            Inventory.InventoryGraph inventory,
            string itemId,
            int expectedVersion,
            BodyPart part) {
            if (state.TacticalPoisonSeconds > 0) return Inventory.InventoryError.Inaccessible;
            bool bandage = state.TacticalBleeding &&
                           Inventory.ConsumableCatalog.StopsBleeding(inventory?.Find(itemId)?.Code);
            if (part == BodyPart.Auto) part = MostInjured(state);
            if (bandage && part == BodyPart.Auto) part = BodyPart.Chest;
            if (!CanHeal(state, part) && !(bandage && state.BodyHealthInitialized && state.CurrentHealth > 0 &&
                                           Maximum(state, part) > 0))
                return Inventory.InventoryError.Invalid;
            var error = inventory.UseMedical(itemId, expectedVersion, Get(state, part), Maximum(state, part),
                out float healed, true, bandage);
            if (error == Inventory.InventoryError.None) {
                Set(ref state, part, healed);
                if (bandage) state.TacticalBleeding = false;
            }
            return error;
        }

        public static void TickOxygen(
            ref PredictedPlayerGhost state,
            bool breathable,
            float dt,
            float outdoorSeconds,
            float recoveryPerSecond,
            float graceSeconds,
            float damagePerSecond,
            uint tick) {
            // Compatibility entry point for existing airlock previews and callers.
            // This overload retains their constant-rate consumption contract.
            var atmosphere = new AtmosphereSample(null, breathable ? 101.325f : 0, breathable ? .21f : 0, breathable);
            var settings = new OxygenParameters(outdoorSeconds, recoveryPerSecond, graceSeconds, damagePerSecond, 1, 1);
            RaidOxygen.Tick(ref state, atmosphere, settings, dt, tick);
        }

        public static FirstPersonController.ControllerConsts Movement(
            in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants) {
            constants = TacticalItemEffects.Movement(state,
                RaidNutrition.Movement(state, RaidEncumbrance.Movement(state, constants)));
            if (state.ConsumingSupply || state.TimedActionActive || state.ControllerState.IsReloadingState) {
                constants.Walk.Speed = Mathf.Min(constants.Walk.Speed, RaidNutrition.SupplyUseMaxSpeed);
                constants.Sprint.Speed = Mathf.Min(constants.Sprint.Speed, RaidNutrition.SupplyUseMaxSpeed);
            }
            return constants;
        }

        public static float HandlingMultiplier(in PredictedPlayerGhost state) {
            return RaidNutrition.HandlingMultiplier(state) * (state.TacticalCoughSeconds > 0 ? 1.5f : 1);
        }

        // 多个互斥动作共享这套手上是否空闲的判断。某些入口允许远端外放光环，所以可按用途带选项。
        public static bool HandsBusy(in PredictedPlayerGhost state, bool allowAnchoredHalo = false) {
            return GrenadeHandling.Occupied(state) || state.HaloDefenseHeld ||
                   (HaloTransferRules.Occupied(state) && !(allowAnchoredHalo && HaloSupportRules.Anchored(state) &&
                                                           state.HaloTransferPhase == HaloTransferPhase.Sending)) ||
                   state.ControllerState.IsReloadingState || state.WeaponEquipRemaining > 0;
        }

        public static bool SearchHandsBusy(in PredictedPlayerGhost state) {
            return GrenadeHandling.Occupied(state) || state.ControllerState.IsReloadingState ||
                   state.WeaponEquipRemaining > 0 ||
                   HaloTransferRules.Occupied(state) && !HaloSupportRules.Anchored(state);
        }

        // Self-treatment can continue through physiological damage, but not a new
        // external injury or a hit fully absorbed by the shield.
        public static bool SelfTreatmentDamageUnchanged(
            in PredictedPlayerGhost state,
            uint damageSequence,
            uint shieldSequence) {
            return state.NonPhysiologicalDamageSequence == damageSequence && state.ShieldHitSequence == shieldSequence;
        }

        // A large remaining limb total must not hide a nearly fatal head/chest
        // injury from the compact HUD or the existing PMC retreat decision.
        public static float DangerFraction(in PredictedPlayerGhost state) {
            float total = state.CurrentHealth / Mathf.Max(1, state.MaxHealth);
            return state.BodyHealthInitialized
                ? Mathf.Min(total, state.HeadHealth / 35f, state.ChestHealth / 85f)
                : total;
        }

        // 控制器更新前就限制按键组合，避免角色状态不允许的时候仍执行冲刺、装填或射击。
        public static PlayerInput RestrictInput(in PredictedPlayerGhost state, PlayerInput input) {
            if (GrenadeHandling.Occupied(state)) {
                input.SetFlag(PlayerInput.InputFlag.Shoot, false);
                input.SetFlag(PlayerInput.InputFlag.FireHeld, false);
                input.SetFlag(PlayerInput.InputFlag.Aim, false);
                input.SetFlag(PlayerInput.InputFlag.Reload, false);
                input.SetFlag(PlayerInput.InputFlag.EquipHalo, false);
                input.SetFlag(PlayerInput.InputFlag.EquipRevolver, false);
                input.SetFlag(PlayerInput.InputFlag.EquipPistol, false);
                input.SetFlag(PlayerInput.InputFlag.HaloDefense, false);
                input.SetFlag(PlayerInput.InputFlag.HaloTransfer, false);
                input.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
            }
            if (state.ClashEnabled && !state.ClashCanAct) {
                // Suppress actions without resetting the player's continuous light
                // settings in the authoritative snapshot seen by other clients.
                var blocked = new PlayerInput {
                    LookYawPitchDegrees = input.LookYawPitchDegrees, AimPoint = input.AimPoint,
                    HaloLightingSettings = DollSinger.HaloLighting.Sanitize(input.HaloLightingSettings)
                };
                blocked.SetFlag(PlayerInput.InputFlag.HaloLightDisabled, !input.HaloLightEnabled);
                blocked.SetFlag(PlayerInput.InputFlag.ThirdPerson, input.ThirdPerson);
                blocked.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
                blocked.SetStance(
                    state.ClashLife == ClashLife.Downed ? DollSinger.DollSingerStance.Prone : input.Stance);
                return blocked;
            }
            if (state.ClashEnabled && state.ClashDarkSight) {
                input.SetFlag(PlayerInput.InputFlag.Shoot, false);
                input.SetFlag(PlayerInput.InputFlag.Aim, false);
                input.SetFlag(PlayerInput.InputFlag.FireHeld, false);
                input.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
            }
            if (state.ConsumingSupply || state.TimedActionActive || state.ControllerState.IsReloadingState ||
                !RaidNutrition.CanSprint(state))
                input.SetFlag(PlayerInput.InputFlag.Sprint, false);
            return input;
        }

        // The server uses its query hit position, body yaw and controller capsule,
        // never a client-reported body part. Headless servers need no animated bones.
        // 优先用实际命中的玩家部位碰撞信息，缺少时根据命中点在胶囊中的位置回退到身体区域。
        public static BodyPart ResolveHit(PlayerGhost target, Collider collider, Vector3 point) {
            if (collider.TryGetComponent<PlayerHitRegion>(out var region)) return region.Part;
            if (target == null || target.Controller == null) return BodyPart.Chest;
            var capsule = target.Controller.CharacterController;
            var local = target.transform.InverseTransformPoint(point) - capsule.center;
            return ResolveCapsuleHit(local, capsule.height, capsule.radius);
        }

        public static BodyPart ResolveCapsuleHit(Vector3 localFromCenter, float height, float radius) {
            height = Mathf.Max(.3f, height);
            var local = localFromCenter;
            float fraction = (local.y + height * .5f) / height;
            if (fraction > .86f) return BodyPart.Head;
            if (fraction < .44f) return local.x < 0 ? BodyPart.LeftLeg : BodyPart.RightLeg;
            if (Mathf.Abs(local.x) > radius * .65f && fraction > .5f)
                return local.x < 0 ? BodyPart.LeftArm : BodyPart.RightArm;
            return fraction > .62f ? BodyPart.Chest : BodyPart.Abdomen;
        }
    }
}