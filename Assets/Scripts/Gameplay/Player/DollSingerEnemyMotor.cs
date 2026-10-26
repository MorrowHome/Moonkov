using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        private PlayerInput Think(DollSingerEnemyBrain brain, PlayerGhost ghost, LocalTransform pose,
            ref PredictedPlayerGhost health, MoonRaidMap map, double now, float dt)
        {
            Vector3 position = pose.Position;
            Vector3 eye = ghost.ShotOrigin.position;
            brain.SelfStatus = null;
            if (m_TacticalWorld != null && m_TacticalWorld.Statuses.TryGetValue(ghost.GhostGameObject.LinkedEntity, out var status))
                brain.SelfStatus = status;
            brain.BleedOnlyDamage = brain.SelfStatus != null && brain.SelfStatus.Bleed > now && brain.SelfStatus.Fire <= now &&
                (brain.SelfStatus.Poison <= now || brain.SelfStatus.Antidote > now);
            UpdateCombatPressure(brain, health, now, dt);
            if (now >= brain.NextSense)
            {
                Sense(brain, ghost.transform, eye, map.EnemySightRange, now);
                brain.NextSense = now + .1;
            }
            if (now >= brain.NextDecision)
            {
                Decide(brain, position, health, map, now);
                brain.NextDecision = now + m_Tuning.DecisionInterval;
            }
            AdvanceCover(brain, position, now);
            AdvanceHealing(brain, ref health, position, now);
            AdvanceObjective(brain, ref health, position, eye, map, now, dt);
            // A timed search is stationary; re-pathing to the actor's own position
            // can fail and cancel an otherwise reachable corpse or drawer.
            if (brain.Action != PmcAction.Scavenge || brain.LootUntil <= 0)
                UpdatePath(brain, position, brain.TacticalGoal, now);
            Vector3 move = FollowPath(brain, position);
            bool searching = (brain.Action == PmcAction.Search || brain.Action == PmcAction.Investigate) && !brain.Visible;
            bool checkingCorner = searching && brain.Corner < brain.CornerCount - 1 &&
                HorizontalDistance(position, brain.Corners[brain.Corner]) < 2.5f;
            if (searching) move *= checkingCorner ? .45f : .7f;
            if (brain.IsPeeking) move *= .65f;
            if (brain.Action == PmcAction.Scavenge && brain.LootUntil > 0 || brain.ExtractionProgress > 0 ||
                brain.HealUntil > 0 || brain.Interacting || searching && now < brain.SearchPauseUntil ||
                HorizontalDistance(position, brain.TacticalGoal) <= TacticalArrivalDistance &&
                    Mathf.Abs(position.y - brain.TacticalGoal.y) < .6f) move = Vector3.zero;

            brain.MovementRequested = move.sqrMagnitude > .01f;
            bool hasContact = brain.Target != Unity.Entities.Entity.Null;
            Vector3 aim = brain.LastSeen + brain.AimOffset;
            float distance = hasContact ? Vector3.Distance(eye, aim) : 50;
            var weapon = WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID,health.EquippedModules);
            if (brain.Visible && weapon != null && weapon.Type == WeaponType.Projectile)
            {
                float flight = Mathf.Min(.6f, distance / Mathf.Max(1, weapon.ProjectileSpeed));
                aim += Vector3.ClampMagnitude(brain.ObservedVelocity * flight, 3);
                aim.y += .5f * weapon.ProjectileGravity * flight * flight;
            }
            Vector3 facing = hasContact ? aim - eye : move;
            bool looting = brain.Action == PmcAction.Scavenge && brain.LootUntil > 0;
            if (looting) facing = brain.LootLookPoint - eye;
            if (!hasContact && brain.IsClashBot && move.sqrMagnitude < .01f)
                facing = brain.MissionLookPoint - eye;
            if (searching && brain.SearchFocusValid)
            {
                Vector3 lookPoint = brain.SearchLookPoint;
                if (now >= brain.SearchPauseUntil && brain.Corner < brain.CornerCount)
                {
                    // Check the space beyond an upcoming turn before walking around it.
                    int corner = checkingCorner ? brain.Corner + 1 : brain.Corner;
                    lookPoint = brain.Corners[corner] + Vector3.up * 1.15f;
                }
                facing = lookPoint - eye;
            }
            if (facing.sqrMagnitude > .001f)
            {
                float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                float pitch = hasContact || looting ? -Mathf.Atan2(facing.y, new Vector2(facing.x, facing.z).magnitude) * Mathf.Rad2Deg : 0;
                brain.Yaw = Mathf.MoveTowardsAngle(brain.Yaw, yaw, m_Tuning.TurnSpeed * dt);
                brain.Pitch = Mathf.MoveTowardsAngle(brain.Pitch, pitch, m_Tuning.TurnSpeed * .7f * dt);
            }
            if (now >= brain.NextAimError)
            {
                float settled = Mathf.Clamp01((float)(now - brain.VisibleSince) / 2);
                float error = Mathf.Lerp(m_Tuning.FirstShotError, m_Tuning.SettledAimError, settled);
                error += brain.Suppression * 4 + move.magnitude * 1.5f;
                brain.AimErrorX = brain.Random.NextFloat(-error, error);
                brain.AimErrorY = brain.Random.NextFloat(-error * .6f, error * .6f);
                brain.NextAimError = now + .25;
            }
            var look = new float2(brain.Yaw + (brain.Visible ? brain.AimErrorX : 0), brain.Pitch + (brain.Visible ? brain.AimErrorY : 0));
            Vector3 direction = Quaternion.Euler(look.y, look.x, 0) * Vector3.forward;
            Vector3 localMove = Quaternion.Euler(0, -look.x, 0) * move;
            var input = new PlayerInput { MoveInput = new float2(localMove.x, localMove.z), LookYawPitchDegrees = look,
                // AimPoint must follow the imperfect, rate-limited look ray; exact target coordinates would bypass it.
                AimPoint = eye + direction * Mathf.Max(1, distance) };
            input.SetFlag(PlayerInput.InputFlag.ThirdPerson, true);
            bool fighting = CanReturnFire(brain) && health.CurrentAmmo > 0 && !health.ControllerState.IsReloadingState;
            input.SetFlag(PlayerInput.InputFlag.Aim, fighting && brain.Visible || checkingCorner);
            input.SetFlag(PlayerInput.InputFlag.Sprint, brain.HealUntil <= 0 && !(fighting && brain.Visible) && move.sqrMagnitude > .1f &&
                (brain.Action == PmcAction.Retreat || brain.Action == PmcAction.EvadeGrenade || brain.Action == PmcAction.Flank && !brain.Visible ||
                    (brain.Action == PmcAction.Extract || brain.IsClashBot && brain.Leaving) && !hasContact || brain.Action == PmcAction.Cover &&
                    (brain.CoverPhase == PmcCoverPhase.Approaching || brain.CoverPhase == PmcCoverPhase.Returning)));
            int preferred = !brain.IsClashBot && brain.Visible && CanReturnFire(brain) && brain.LootUntil <= 0 && now >= brain.NextTacticalSwitch
                ? PmcWeaponTactics.PreferredSlot(health, distance) : 0;
            uint preferredWeapon = preferred == 1 ? health.PrimaryWeaponID : preferred == 2 ? health.SecondaryWeaponID : health.PistolWeaponID;
            bool switching = preferred != 0 && WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(preferredWeapon) != null;
            if (switching)
            {
                input.SetFlag(preferred == 1 ? PlayerInput.InputFlag.EquipHalo : preferred == 2 ?
                    PlayerInput.InputFlag.EquipRevolver : PlayerInput.InputFlag.EquipPistol, true);
                brain.NextTacticalSwitch = now + 3;
            }
            if (!switching && brain.LootUntil <= 0 && brain.HealUntil <= 0 && !brain.Interacting &&
                ShouldReload(brain, health, position, now) && !health.ControllerState.IsReloadingState)
            {
                bool blocked=weapon != null && brain.ReloadFailure.Matches(brain.Inventory.Version,health.EquippedWeaponID,weapon.EnergyPerRound);
                bool cellReady = weapon != null && !blocked && PmcReloadInventory.Prepare(brain.Inventory, weapon.EnergyPerRound);
                if (cellReady) {brain.ReloadFailure.Clear();input.SetFlag(PlayerInput.InputFlag.Reload, true);}
                else
                {
                    if(weapon != null && !blocked)
                    {brain.ReloadFailure.Record(brain.Inventory.Version,health.EquippedWeaponID,weapon.EnergyPerRound);brain.NextDecision=0;}
                    if(health.CurrentAmmo==0)
                    {
                        if (health.EquippedWeaponSlot != 3 && health.PistolAmmo > 0) input.SetFlag(PlayerInput.InputFlag.EquipPistol, true);
                        else if (health.EquippedWeaponSlot != 1 && health.PrimaryAmmo > 0) input.SetFlag(PlayerInput.InputFlag.EquipHalo, true);
                        else if (health.EquippedWeaponSlot != 2 && health.SecondaryAmmo > 0) input.SetFlag(PlayerInput.InputFlag.EquipRevolver, true);
                    }
                }
            }
            bool firingSolution = !switching && fighting && brain.Visible && health.CurrentAmmo > 0 && !health.ControllerState.IsReloadingState &&
                now - brain.VisibleSince >= m_Tuning.ReactionSeconds && distance < map.EnemySightRange &&
                Vector3.Angle(direction, facing) < 7 && ClearSight(ghost.transform, eye, brain.LastSeen + brain.AimOffset, brain.Target) &&
                (!brain.IsClashBot || ClashShotLaneClear(brain, eye, direction, map.EnemySightRange));
            // Do not spend a short peek's entire burst while still reacting or
            // turning toward the contact. The burst starts when the shot is possible.
            if (now >= brain.NextBurst && firingSolution)
            {
                brain.BurstUntil = now + brain.Random.NextFloat(.25f, .6f);
                brain.NextBurst = brain.BurstUntil + brain.Random.NextFloat(.5f, 1.1f);
            }
            input.SetFlag(PlayerInput.InputFlag.Shoot, firingSolution && now < brain.BurstUntil);
            return input;
        }

        private static bool CanReturnFire(DollSingerEnemyBrain brain) =>
            brain.HealUntil <= 0 && !brain.Interacting &&
            (brain.Action == PmcAction.Engage || brain.Action == PmcAction.HoldAngle ||
             brain.Action == PmcAction.Cover && (!brain.HasCover || brain.IsPeeking || brain.CoverPhase == PmcCoverPhase.Returning) ||
             brain.Action == PmcAction.Retreat || brain.Action == PmcAction.Flank);

        private static void UpdateCombatPressure(DollSingerEnemyBrain brain, PredictedPlayerGhost health, double now, float dt) =>
            PmcCombatPressure.Update(brain, health, now, dt);
    }

    public static class PmcCombatPressure
    {
        public static void Update(DollSingerEnemyBrain brain, in PredictedPlayerGhost health, double now, float dt)
        {
            float lost = Mathf.Max(0, brain.LastHealth - health.CurrentHealth);
            bool shieldHit = health.ShieldHitSequence != brain.LastShieldHitSequence;
            bool impact = health.LastImpactTick != brain.LastImpactTick;
            // The impact event is authoritative even when healing in the same
            // update makes the net health change zero or positive.
            if (impact || shieldHit || lost > 0 && !brain.BleedOnlyDamage)
            {
                // Pressure scales with actual injury. Grazes and periodic tissue loss
                // must not count like repeated half-strength panic impulses.
                float pressure = Mathf.Clamp01(lost / RaidHealth.Maximum(BodyPart.Chest)) * .6f;
                if (shieldHit) pressure += .08f + Mathf.Clamp01(health.ShieldHitStrength) * .12f;
                brain.Suppression = Mathf.Clamp01(brain.Suppression + pressure);
                brain.LastDamage = now;
                brain.NextDecision = 0;
            }
            brain.LastHealth = health.CurrentHealth;
            brain.LastShieldHitSequence = health.ShieldHitSequence;
            brain.LastImpactTick = health.LastImpactTick;
            brain.Suppression = Mathf.Max(0, brain.Suppression - Mathf.Max(0, dt) * .32f);
        }
    }
}
