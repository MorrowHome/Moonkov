using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        private PlayerInput Think(DollSingerEnemyBrain brain, PlayerGhost ghost, LocalTransform pose,
            PredictedPlayerGhost health, MoonRaidMap map, double now, float dt)
        {
            Vector3 position = pose.Position;
            Vector3 eye = ghost.ShotOrigin.position;
            if (health.CurrentHealth < brain.LastHealth)
            {
                brain.Suppression = Mathf.Clamp01(brain.Suppression + .5f);
                brain.LastDamage = now;
                brain.NextDecision = 0;
            }
            brain.LastHealth = health.CurrentHealth;
            brain.Suppression = Mathf.Max(0, brain.Suppression - dt * .12f);
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
            AdvanceObjective(brain, position, eye, map, now, dt);
            UpdatePath(brain, position, brain.TacticalGoal, now);
            Vector3 move = FollowPath(brain, position);
            if (brain.Action == PmcAction.Scavenge && brain.LootUntil > 0 || brain.ExtractionProgress > 0 ||
                HorizontalDistance(position, brain.TacticalGoal) < .65f) move = Vector3.zero;

            bool hasContact = brain.Target != Unity.Entities.Entity.Null;
            Vector3 aim = brain.LastSeen + Vector3.up * 1.15f;
            float distance = hasContact ? Vector3.Distance(eye, aim) : 50;
            var weapon = WeaponManager.Instance?.WeaponRegistry.GetWeaponData(health.EquippedWeaponID);
            if (brain.Visible && weapon != null && weapon.Type == WeaponType.Projectile)
            {
                float flight = Mathf.Min(.6f, distance / Mathf.Max(1, weapon.ProjectileSpeed));
                aim += Vector3.ClampMagnitude(brain.ObservedVelocity * flight, 3);
                aim.y += .5f * weapon.ProjectileGravity * flight * flight;
            }
            Vector3 facing = hasContact ? aim - eye : move;
            if (brain.Action == PmcAction.Search && !brain.Visible && now - brain.LastSeenTime > 1)
                facing = move.sqrMagnitude > .01f ? move : Quaternion.Euler(0, brain.Yaw + 35 * dt, 0) * Vector3.forward;
            if (facing.sqrMagnitude < .001f && brain.Action == PmcAction.Search)
                facing = Quaternion.Euler(0, brain.Yaw + 25 * dt, 0) * Vector3.forward;
            if (facing.sqrMagnitude > .001f)
            {
                float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                float pitch = hasContact ? -Mathf.Atan2(facing.y, new Vector2(facing.x, facing.z).magnitude) * Mathf.Rad2Deg : 0;
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
            bool fighting = brain.Action == PmcAction.Engage || brain.Action == PmcAction.Cover && brain.IsPeeking;
            input.SetFlag(PlayerInput.InputFlag.Aim, fighting && brain.Visible);
            input.SetFlag(PlayerInput.InputFlag.Sprint, move.sqrMagnitude > .1f &&
                (brain.Action == PmcAction.Retreat || brain.Action == PmcAction.Flank || brain.Action == PmcAction.Extract && !hasContact));
            if (health.CurrentAmmo == 0 && !health.ControllerState.IsReloadingState)
            {
                bool cellReady = PrepareReloadCell(brain.Inventory);
                if (cellReady) input.SetFlag(PlayerInput.InputFlag.Reload, true);
                else if (health.EquippedWeaponID == 2 && health.StoredRevolverAmmo > 0) input.SetFlag(PlayerInput.InputFlag.EquipRevolver, true);
                else if (health.EquippedWeaponID == 3 && health.StoredHaloAmmo > 0) input.SetFlag(PlayerInput.InputFlag.EquipHalo, true);
            }
            if (now >= brain.NextBurst && fighting && brain.Visible)
            {
                brain.BurstUntil = now + brain.Random.NextFloat(.25f, .6f);
                brain.NextBurst = brain.BurstUntil + brain.Random.NextFloat(.5f, 1.1f);
            }
            bool shoot = fighting && brain.Visible && health.CurrentAmmo > 0 && !health.ControllerState.IsReloadingState &&
                now - brain.VisibleSince >= m_Tuning.ReactionSeconds && now < brain.BurstUntil && distance < map.EnemySightRange &&
                Vector3.Angle(direction, facing) < 7 && ClearSight(ghost.transform, eye, brain.LastSeen + Vector3.up * 1.15f, brain.Target);
            input.SetFlag(PlayerInput.InputFlag.Shoot, shoot);
            return input;
        }
    }
}
