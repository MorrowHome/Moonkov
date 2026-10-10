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
            value = Mathf.Clamp(value, 0, Maximum(part));
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
            state.BodyHealthInitialized = true;
            state.HeadHealth = 35; state.ChestHealth = 85; state.AbdomenHealth = 70;
            state.LeftArmHealth = state.RightArmHealth = 60;
            state.LeftLegHealth = state.RightLegHealth = 65;
            state.Oxygen = OxygenCapacity; state.BreathableAir = false; state.HypoxiaSeconds = 0;
            RaidNutrition.Initialize(ref state);
            Synchronize(ref state);
        }
        private static void Synchronize(ref PredictedPlayerGhost state)
        {
            state.MaxHealth = 440f;
            state.CurrentHealth = state.HeadHealth <= 0 || state.ChestHealth <= 0 ? 0f :
                state.HeadHealth + state.ChestHealth + state.AbdomenHealth + state.LeftArmHealth +
                state.RightArmHealth + state.LeftLegHealth + state.RightLegHealth;
        }
        public static float Damage(ref PredictedPlayerGhost state, BodyPart part, float amount, uint tick)
        {
            if (state.CurrentHealth <= 0 || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount) || Maximum(part) <= 0) return 0;
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
            return before - state.CurrentHealth;
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
            state.CurrentHealth > 0 && Maximum(part) > 0 && Get(state, part) < Maximum(part);
        public static Inventory.InventoryError UseMedical(ref PredictedPlayerGhost state, Inventory.InventoryGraph inventory,
            string itemId, int expectedVersion, BodyPart part)
        {
            if (part == BodyPart.Auto) part = MostInjured(state);
            if (!CanHeal(state, part)) return Inventory.InventoryError.Invalid;
            var error = inventory.UseMedical(itemId, expectedVersion, Get(state, part), Maximum(part), out var healed, allowZeroHealth: true);
            if (error == Inventory.InventoryError.None) Set(ref state, part, healed);
            return error;
        }

        public static void TickOxygen(ref PredictedPlayerGhost state, bool breathable, float dt,
            float outdoorSeconds, float recoveryPerSecond, float graceSeconds, float damagePerSecond, uint tick)
        {
            if (!state.BodyHealthInitialized || state.CurrentHealth <= 0 || dt <= 0) return;
            state.BreathableAir = breathable;
            if (breathable)
            {
                state.Oxygen = Mathf.Min(OxygenCapacity, state.Oxygen + Mathf.Max(0, recoveryPerSecond) * dt);
                state.HypoxiaSeconds = 0; return;
            }
            float rate = OxygenCapacity / Mathf.Max(1, outdoorSeconds);
            float exhaustedTime = Mathf.Max(0, dt - state.Oxygen / rate);
            state.Oxygen = Mathf.Max(0, state.Oxygen - dt * rate);
            if (exhaustedTime <= 0) { state.HypoxiaSeconds = 0; return; }
            float prior = Mathf.Max(0, state.HypoxiaSeconds - graceSeconds);
            state.HypoxiaSeconds += exhaustedTime;
            float exposure = Mathf.Max(0, state.HypoxiaSeconds - graceSeconds) - prior;
            if (exposure > 0) Damage(ref state, BodyPart.Chest, exposure * Mathf.Max(0, damagePerSecond), tick);
        }
        public static FirstPersonController.ControllerConsts Movement(in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants)
            => RaidNutrition.Movement(state, constants);
        public static float HandlingMultiplier(in PredictedPlayerGhost state) => RaidNutrition.HandlingMultiplier(state);
        // A large remaining limb total must not hide a nearly fatal head/chest
        // injury from the compact HUD or the existing PMC retreat decision.
        public static float DangerFraction(in PredictedPlayerGhost state)
        {
            float total = state.CurrentHealth / Mathf.Max(1, state.MaxHealth);
            return state.BodyHealthInitialized ? Mathf.Min(total, state.HeadHealth / 35f, state.ChestHealth / 85f) : total;
        }
        public static PlayerInput RestrictInput(in PredictedPlayerGhost state, PlayerInput input)
        {
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
