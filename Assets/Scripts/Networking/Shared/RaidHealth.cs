using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Anatomical left/right, independent of the camera. Auto is an intent: the
    // server chooses the most injured part when using the existing quick-use key.
    public enum BodyPart : byte { Head, Chest, Abdomen, LeftArm, RightArm, LeftLeg, RightLeg, Auto = 255 }

    public static class RaidHealth
    {
        public const int PartCount = 7;
        public const float OxygenCapacity = 100f;
        public const float SafeFallHeight = 4.5f;
        public const float MoonGravity = 1.62f;
        public const float FallDamagePerMeter = 12f;
        public const float MaximumFallDamage = 600f;
        public static float Maximum(BodyPart part)
        {
            switch (part)
            {
                case BodyPart.Head: return 35f;
                case BodyPart.Chest: return 85f;
                case BodyPart.Abdomen: return 70f;
                case BodyPart.LeftArm: case BodyPart.RightArm: return 60f;
                case BodyPart.LeftLeg: case BodyPart.RightLeg: return 65f;
                default: return 0f;
            }
        }
        public static float Maximum(in PredictedPlayerGhost state, BodyPart part) => Maximum(part) *
            (state.ClashEnabled ? Mathf.Clamp01(state.ClashMaxHealthFraction) : 1);
        public static float Get(in PredictedPlayerGhost state, BodyPart part)
        {
            switch (part)
            {
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
        public static void Set(ref PredictedPlayerGhost state, BodyPart part, float value)
        {
            value = Mathf.Clamp(value, 0, Maximum(state, part));
            switch (part)
            {
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
        public static void Initialize(ref PredictedPlayerGhost state)
        {
            state.PendingBulletWound = 0; state.PendingBulletWoundSource = default;
            state.BodyHealthInitialized = true;
            state.HeadHealth = 35; state.ChestHealth = 85; state.AbdomenHealth = 70;
            state.LeftArmHealth = state.RightArmHealth = 60;
            state.LeftLegHealth = state.RightLegHealth = 65;
            if(!state.ProtectionInitialized)RaidOxygen.Initialize(ref state);
            RaidNutrition.Initialize(ref state);
            Synchronize(ref state);
        }
        private static void Synchronize(ref PredictedPlayerGhost state)
        {
            if (state.ClashEnabled)
            {
                state.HeadHealth=Mathf.Min(state.HeadHealth,Maximum(state,BodyPart.Head));
                state.ChestHealth=Mathf.Min(state.ChestHealth,Maximum(state,BodyPart.Chest));
                state.AbdomenHealth=Mathf.Min(state.AbdomenHealth,Maximum(state,BodyPart.Abdomen));
                state.LeftArmHealth=Mathf.Min(state.LeftArmHealth,Maximum(state,BodyPart.LeftArm));
                state.RightArmHealth=Mathf.Min(state.RightArmHealth,Maximum(state,BodyPart.RightArm));
                state.LeftLegHealth=Mathf.Min(state.LeftLegHealth,Maximum(state,BodyPart.LeftLeg));
                state.RightLegHealth=Mathf.Min(state.RightLegHealth,Maximum(state,BodyPart.RightLeg));
            }
            state.MaxHealth = 440f * (state.ClashEnabled ? Mathf.Clamp01(state.ClashMaxHealthFraction) : 1);
            state.CurrentHealth = state.ClashEnabled && state.ClashLife != ClashLife.Alive || state.HeadHealth <= 0 || state.ChestHealth <= 0 ? 0f :
                state.HeadHealth + state.ChestHealth + state.AbdomenHealth + state.LeftArmHealth +
                state.RightArmHealth + state.LeftLegHealth + state.RightLegHealth;
        }
        public static float Damage(ref PredictedPlayerGhost state, BodyPart part, float amount, uint tick, bool physiological=false)
        {
            if (state.CurrentHealth <= 0 || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount) || Maximum(part) <= 0) return 0;
            if(state.AlienFoodEffectSeconds>0 && state.AlienFoodEffect==AlienFoodEffect.GelArmor)amount*=.75f;
            float before = state.CurrentHealth;
            if (!state.BodyHealthInitialized) state.CurrentHealth = Mathf.Max(0, before - amount);
            else
            {
                float local = Mathf.Min(Get(state, part), amount);
                Set(ref state, part, Get(state, part) - local);
                float overflow = amount - local;
                // A blacked-out limb/abdomen still receives damage. Overflow is
                // distributed over remaining tissue, conserving incoming damage.
                if (overflow > 0 && state.CurrentHealth > 0)
                {
                    float remaining = state.CurrentHealth;
                    for (int i = 0; i < PartCount; i++)
                    {
                        var other = (BodyPart)i;
                        if (other != part) Set(ref state, other, Get(state, other) * Mathf.Max(0, 1f - overflow / remaining));
                    }
                }
            }
            state.LastHitPart = part; state.LastDamageAmount = amount;
            state.LastHitTick = tick; state.ControllerState.IsHit = true;
            if(!physiological && state.CurrentHealth<before)state.NonPhysiologicalDamageSequence++;
            return before - state.CurrentHealth;
        }

        // The controller measures physical descent after the apex, not its artificial
        // standing fall velocity. Gravity scales impact energy for the same distance.
        public static float FallDamage(float height, float gravity)
        {
            if (!float.IsFinite(height) || !float.IsFinite(gravity) || height <= SafeFallHeight || gravity >= 0)
                return 0;
            double damage = ((double)height - SafeFallHeight) * FallDamagePerMeter * (-(double)gravity / MoonGravity);
            return (float)Math.Min(MaximumFallDamage, damage);
        }

        // Called only by the server immediately after the controller changes to landed.
        // The pending flag prevents replaying an event until input processing clears it.
        public static float ApplyLandingDamage(ref PredictedPlayerGhost state, bool wasLanding, float gravity, uint tick)
        {
            if (wasLanding || !state.ControllerState.LandTriggered || !state.BodyHealthInitialized ||
                state.ClashEnabled || state.CurrentHealth <= 0) return 0;
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
        public static float CombatDamage(ref PredictedPlayerGhost state,BodyPart part,float amount,uint tick,
            RaidDamageKind kind,Unity.Mathematics.float3 impactPoint=default)
        {
            if(!EnergyShieldRules.Alive(state) || !float.IsFinite(amount) || amount<=0 ||
                part!=BodyPart.Auto && Maximum(part)<=0)return 0;
            float remaining=EnergyShieldRules.Absorb(ref state,amount,tick,kind,impactPoint);
            float before=state.CurrentHealth;
            if(part==BodyPart.Auto)
            {
                if(!state.BodyHealthInitialized)Damage(ref state,BodyPart.Chest,remaining,tick);
                else for(int i=0;i<PartCount;i++)
                {var target=(BodyPart)i;float share=remaining*Get(state,target)/Mathf.Max(.001f,before);Damage(ref state,target,share,tick);}
                state.LastDamageAmount=before-state.CurrentHealth;state.LastHitPart=BodyPart.Auto;
            }
            else Damage(ref state,part,remaining,tick);
            if (state.CurrentHealth < before &&
                (kind == RaidDamageKind.Bullet || kind == RaidDamageKind.Explosion || kind == RaidDamageKind.Melee))
                state.LastImpactTick = tick;
            return before-state.CurrentHealth;
        }

        // A shield-only hit is real, but a team-protected or otherwise rejected
        // impact must not produce a hit confirmation or mark.
        public static byte ImpactFlags(in PredictedPlayerGhost before, in PredictedPlayerGhost after)
        {
            if (!(before.CurrentHealth > 0) ||
                (!(after.CurrentHealth < before.CurrentHealth) && after.ShieldHitSequence == before.ShieldHitSequence)) return 0;
            return (byte)(after.CurrentHealth <= 0 ? 3 : 1);
        }
        public static BodyPart MostInjured(in PredictedPlayerGhost state)
        {
            var selected = BodyPart.Auto; float lowest = 1f;
            for (int i = 0; i < PartCount; i++)
            {
                var part = (BodyPart)i; float fraction = Get(state, part) / Maximum(part);
                if (fraction < lowest) { lowest = fraction; selected = part; }
            }
            return selected;
        }
        public static bool CanHeal(in PredictedPlayerGhost state, BodyPart part) => state.BodyHealthInitialized &&
            state.CurrentHealth > 0 && Maximum(state,part) > 0 && Get(state, part) < Maximum(state,part);
        public static Inventory.InventoryError UseMedical(ref PredictedPlayerGhost state, Inventory.InventoryGraph inventory,
            string itemId, int expectedVersion, BodyPart part)
        {
            if(state.TacticalPoisonSeconds>0)return Inventory.InventoryError.Inaccessible;
            if (part == BodyPart.Auto) part = MostInjured(state);
            if (!CanHeal(state, part)) return Inventory.InventoryError.Invalid;
            var error = inventory.UseMedical(itemId, expectedVersion, Get(state, part), Maximum(state,part), out var healed, allowZeroHealth: true);
            if (error == Inventory.InventoryError.None) Set(ref state, part, healed);
            return error;
        }

        public static void TickOxygen(ref PredictedPlayerGhost state, bool breathable, float dt,
            float outdoorSeconds, float recoveryPerSecond, float graceSeconds, float damagePerSecond, uint tick)
        {
            // Compatibility entry point for existing airlock previews and callers.
            // This overload retains their constant-rate consumption contract.
            var atmosphere = new AtmosphereSample(null, breathable ? 101.325f : 0, breathable ? .21f : 0, breathable);
            var settings = new OxygenParameters(outdoorSeconds, recoveryPerSecond, graceSeconds, damagePerSecond, 1, 1);
            RaidOxygen.Tick(ref state, atmosphere, settings, dt, tick);
        }
        public static FirstPersonController.ControllerConsts Movement(in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants)
            => TacticalItemEffects.Movement(state, RaidNutrition.Movement(state, RaidEncumbrance.Movement(state, constants)));
        public static float HandlingMultiplier(in PredictedPlayerGhost state) => RaidNutrition.HandlingMultiplier(state) * (state.TacticalCoughSeconds>0 ? 1.5f : 1);
        public static bool HandsBusy(in PredictedPlayerGhost state) => state.ControllerState.IsReloadingState || state.WeaponEquipRemaining > 0;
        // Self-treatment can continue through physiological damage, but not a new
        // external injury or a hit fully absorbed by the shield.
        public static bool SelfTreatmentDamageUnchanged(in PredictedPlayerGhost state,uint damageSequence,uint shieldSequence) =>
            state.NonPhysiologicalDamageSequence==damageSequence && state.ShieldHitSequence==shieldSequence;
        // A large remaining limb total must not hide a nearly fatal head/chest
        // injury from the compact HUD or the existing PMC retreat decision.
        public static float DangerFraction(in PredictedPlayerGhost state)
        {
            float total = state.CurrentHealth / Mathf.Max(1, state.MaxHealth);
            return state.BodyHealthInitialized ? Mathf.Min(total, state.HeadHealth / 35f, state.ChestHealth / 85f) : total;
        }
        public static PlayerInput RestrictInput(in PredictedPlayerGhost state, PlayerInput input)
        {
            if(state.ClashEnabled && !state.ClashCanAct)
            {
                // Suppress actions without resetting the player's continuous light
                // settings in the authoritative snapshot seen by other clients.
                var blocked=new PlayerInput { LookYawPitchDegrees=input.LookYawPitchDegrees, AimPoint=input.AimPoint,
                    HaloLightingSettings=Unity.MP_FPS.DollSinger.HaloLighting.Sanitize(input.HaloLightingSettings) };
                blocked.SetFlag(PlayerInput.InputFlag.HaloLightDisabled,!input.HaloLightEnabled);
                blocked.SetFlag(PlayerInput.InputFlag.ThirdPerson,input.ThirdPerson);
                blocked.SetStance(state.ClashLife==ClashLife.Downed ? Unity.MP_FPS.DollSinger.DollSingerStance.Prone : input.Stance);return blocked;
            }
            if(state.ClashEnabled && state.ClashDarkSight)
            {input.SetFlag(PlayerInput.InputFlag.Shoot,false);input.SetFlag(PlayerInput.InputFlag.Aim,false);}
            if (!RaidNutrition.CanSprint(state))
                input.SetFlag(PlayerInput.InputFlag.Sprint, false);
            return input;
        }

        // The server uses its query hit position, body yaw and controller capsule,
        // never a client-reported body part. Headless servers need no animated bones.
        public static BodyPart ResolveHit(PlayerGhost target, Collider collider, Vector3 point)
        {
            if (collider.TryGetComponent<PlayerHitRegion>(out var region)) return region.Part;
            if (target == null || target.Controller == null) return BodyPart.Chest;
            var capsule = target.Controller.CharacterController;
            var local = target.transform.InverseTransformPoint(point) - capsule.center;
            return ResolveCapsuleHit(local, capsule.height, capsule.radius);
        }
        public static BodyPart ResolveCapsuleHit(Vector3 localFromCenter, float height, float radius)
        {
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
