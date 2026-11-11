using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        internal ClassicBot SpawnClassicBot(LocalTransform pose, int team, int slot, int id, int round, double now)
        {
            if (!SystemAPI.TryGetSingleton<PlayerEntityPrefabs>(out var prefabs) || WeaponManager.Instance?.WeaponRegistry == null) return null;
            if (m_SpawnRandom.state == 0) m_SpawnRandom = new Unity.Mathematics.Random(1789);
            if (!m_Characters.TryChoose(prefabs, ref m_SpawnRandom, out var prefab, out var character)) return null;
            var request = new ClashBotSpawnRequest { ParticipantId = id, TeamId = team, Slot = slot, Epoch = round, OwnerNetworkId = id,
                Name = "Bot " + (-id).ToString("00"), Position = pose.Position };
            var actor = SpawnActor(MoonRaidMap.Active, prefab, pose.Position, now, character, request);
            EntityManager.SetComponentData(actor, pose);
            var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(actor);
            brain.Inventory = ClassicMatch.StartingKit();
            if (round > 1 && round != ClassicMatch.Half + 1)
            {
                string code = slot % 2 == 0 ? "rifle" : "cicada";
                ArenaArmory.Issue(brain.Inventory, code, brain.Inventory.Version);
            }
            var ghost = EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
            ghost.ClashLife = ClashLife.Alive;
            RaidHealth.Initialize(ref ghost);
            ClassicDust2Map.InitializeAir(ref ghost);
            ghost.ClashLife = ClashLife.Waiting;
            EntityManager.SetComponentData(actor, ghost);
            Vector3 facing = math.forward(pose.Rotation);
            return new ClassicBot(actor, EntityManager.GetComponentData<DollSingerEnemy>(actor).InputEntity, id, team, slot, pose.Position,
                Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg);
        }
    }

    internal sealed class ClassicBot
    {
        public readonly Entity Actor, Input;
        public readonly int Id, Team, Slot;
        public Vector3 LastPosition;
        private const float SightRange = 62;
        private static readonly int s_Obstacles = LayerMask.GetMask("Default", "Ground");
        private static readonly float[] s_CoverAngles = { -80, 80, -125, 125, 180 };
        private readonly NavMeshPath m_Path = new();
        private readonly Vector3[] m_Corners = new Vector3[96];
        private int m_Corner, m_CornerCount, m_TargetSeat = -1, m_Stalls;
        private double m_NextPath, m_NextSense, m_ReactAt, m_BurstUntil, m_NextBurst, m_NextReload;
        private double m_LastSeenAt = -10, m_LastHitAt = -10, m_LastNoiseAt = -10;
        private double m_CoverUntil, m_NextCover, m_CheckProgressAt, m_BackOffUntil;
        private Vector3 m_LastSeen, m_NoisePoint, m_CoverPoint, m_ProgressPosition, m_PathGoal, m_Approach;
        private Vector3 m_TargetVelocity;
        private float m_Yaw, m_Pitch;
        private uint m_LastHit;
        private Entity m_Target;
        private bool m_PassedApproach;

        public ClassicBot(Entity actor, Entity input, int id, int team, int slot, Vector3 position, float yaw)
        {
            Actor = actor;
            Input = input;
            Id = id;
            Team = team;
            Slot = slot;
            LastPosition = m_ProgressPosition = position;
            m_Yaw = yaw;
        }

        // Returns whether this bot is committing to the server's current interaction.
        public bool Tick(EntityManager manager, ClassicBotTactics tactics, ClassicDust2Map map, ClassicMatch rules,
            in PredictedPlayerGhost health, TacticalServerWorld tacticalWorld, ClassicUse opportunity, double now, float dt)
        {
            if (!health.ClashCanAct || !manager.HasComponent<PlayerInputComponent>(Actor)) return false;
            var physics = map.gameObject.scene.GetPhysicsScene();
            Vector3 eye = Eye(manager);
            bool flashed = tacticalWorld.Statuses.TryGetValue(Actor, out var status) && status.FlashUntil > now && status.FlashStrength > .25f;
            if (now >= m_NextSense && !flashed)
            {
                Sense(tactics, map, physics, tacticalWorld, eye, now);
                m_NextSense = now + .115 + (-Id % 4) * .013;
            }
            if (health.LastHitTick != m_LastHit)
            {
                m_LastHit = health.LastHitTick;
                m_LastHitAt = now;
            }

            bool visible = !flashed && m_TargetSeat >= 0 && tactics.Roster[m_TargetSeat].Alive &&
                tactics.Roster[m_TargetSeat].Actor == m_Target && !Obscured(physics, tacticalWorld, eye, m_LastSeen);
            var weapon = WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID, health.EquippedModules);
            float distance = Vector3.Distance(eye, m_LastSeen);
            bool recentlyHit = now - m_LastHitAt < .7;
            bool critical = opportunity == ClassicUse.Defuse ? rules.Deadline - now < 12 : rules.Deadline - now < 5.5;
            bool use = tactics.HasBombAssignment(Id, opportunity) && (opportunity == ClassicUse.PickUp || critical ||
                !recentlyHit && (!visible || rules.HoldActor == Id && distance > 15));
            bool wasReloading = health.ControllerState.IsReloadingState;
            bool switchToPistol = visible && health.CurrentAmmo == 0 && health.EquippedWeaponSlot != 3 && health.PistolAmmo > 0;
            bool returnToPrimary = !visible && now - m_LastSeenAt > 2 && health.EquippedWeaponSlot == 3 && health.PrimaryAmmo > 0;
            bool takeCover = !use && TakeCover(physics, now, recentlyHit || visible && (wasReloading || health.CurrentAmmo == 0 && !switchToPistol));
            var order = tactics.Order(this, rules, map);
            bool shootLane = visible && ShotLaneClear(tactics, eye, m_LastSeen);
            Vector3 aim = AimPoint(weapon, distance);
            float fireRange = weapon ? FireRange(weapon, health.EquippedWeaponSlot) : 34;
            Vector3 move = use ? Vector3.zero : Movement(order, visible, shootLane, takeCover, distance, fireRange, now);
            if (!use) move = SpaceFromAllies(tactics, move);
            bool corner = m_Corner < m_CornerCount - 1 && (m_Corners[m_Corner] - LastPosition).sqrMagnitude < 2.2f * 2.2f;
            Vector3 look = LookDirection(order, move, aim, visible, takeCover, corner, eye, now);
            UpdateLook(look, visible, dt);

            float sway = visible ? Mathf.Lerp(1.6f, .55f, Mathf.Clamp01((float)(now - m_ReactAt))) : 0;
            float yaw = m_Yaw + sway * Mathf.Sin((float)now * 8 + Id * 1.3f);
            float pitch = m_Pitch + sway * .45f * Mathf.Cos((float)now * 6 + Id);
            Vector3 forward = Quaternion.Euler(pitch, yaw, 0) * Vector3.forward;
            Vector3 localMove = Quaternion.Euler(0, -yaw, 0) * move;
            var input = new PlayerInput
            {
                LookYawPitchDegrees = new float2(yaw, pitch),
                MoveInput = new float2(localMove.x, localMove.z),
                AimPoint = eye + forward * (visible ? Mathf.Max(1, distance) : 40)
            };
            input.SetFlag(PlayerInput.InputFlag.ThirdPerson, true);
            bool moving = move.sqrMagnitude > .01f;
            input.SetFlag(PlayerInput.InputFlag.Aim, !use && !takeCover && (visible || (moving ? corner : order.Guard)));
            input.SetFlag(PlayerInput.InputFlag.Sprint, !visible && !corner && !takeCover && !flashed && now - m_LastSeenAt > 2 &&
                !health.TacticalSprintExhausted && localMove.z > .85f);
            input.SetFlag(PlayerInput.InputFlag.Crouch, !use && takeCover && !moving);
            bool switched = !use && (switchToPistol || returnToPrimary);
            input.SetFlag(PlayerInput.InputFlag.EquipPistol, switched && switchToPistol);
            input.SetFlag(PlayerInput.InputFlag.EquipHalo, switched && returnToPrimary);
            bool reload = !use && !switched && !wasReloading && weapon && now >= m_NextReload &&
                (health.CurrentAmmo == 0 || (!visible || takeCover) && health.CurrentAmmo < weapon.MagazineSize * .35f);
            if (reload) m_NextReload = now + Mathf.Max(.7f, weapon.ReloadTime);
            input.SetFlag(PlayerInput.InputFlag.Reload, reload);

            // Burst timing starts once a shot is possible, with a pause to move
            // or reacquire between bursts. Losing the lane also cancels pending fire.
            bool clear = !use && !takeCover && shootLane && !switched && !reload && !wasReloading;
            bool aimed = clear && weapon && health.CurrentAmmo > 0 && health.WeaponEquipRemaining <= 0 &&
                now >= m_ReactAt && distance <= fireRange &&
                Vector3.Dot(forward, (aim - eye).normalized) >= .997f &&
                !WallBetween(physics, eye, eye + forward * distance) && ShotLaneClear(tactics, eye, eye + forward * distance);
            if (aimed && now >= m_NextBurst)
            {
                double raise = DollSingerWeapons.IsHalo(health.EquippedWeaponID) ?
                    (1 - Mathf.Clamp01(health.WeaponRaiseProgress)) * DollSinger.DollSingerHaloAim.RaiseSeconds * RaidHealth.HandlingMultiplier(health) : 0;
                m_BurstUntil = now + raise + (distance < 16 ? .46 : .24) + (Slot % 3) * .035;
                m_NextBurst = m_BurstUntil + .36 + (Slot % 3) * .065;
            }
            bool fire = aimed && now < m_BurstUntil;
            input.SetFlag(PlayerInput.InputFlag.Shoot, fire);
            input.SetFlag(PlayerInput.InputFlag.FireHeld, fire);
            input.SetFlag(PlayerInput.InputFlag.FireSuppressed, !clear && (m_TargetSeat >= 0 || use || takeCover));
            manager.SetComponentData(Actor, new PlayerInputComponent { Input = input });
            return use;
        }

        private Vector3 Eye(EntityManager manager)
        {
            if (manager.HasComponent<GhostGameObjectLink>(Actor))
            {
                var link = manager.GetComponentObject<GhostGameObjectLink>(Actor);
                var player = link.LinkedInstance ? link.LinkedInstance.GetComponent<PlayerGhost>() : null;
                if (player && player.ShotOrigin) return player.ShotOrigin.position;
            }
            return LastPosition + Vector3.up * 1.5f;
        }

        private void Sense(ClassicBotTactics tactics, ClassicDust2Map map, PhysicsScene physics, TacticalServerWorld tactical, Vector3 eye, double now)
        {
            int best = -1;
            float nearest = float.PositiveInfinity;
            float loudest = float.PositiveInfinity;
            double heardAt = m_LastNoiseAt;
            Vector3 target = Vector3.zero;
            Vector3 facing = Quaternion.Euler(m_Pitch, m_Yaw, 0) * Vector3.forward;
            for (int i = 0; i < tactics.Roster.Length; i++)
            {
                ref var entry = ref tactics.Roster[i];
                if (!entry.Alive || entry.Team == Team) continue;
                Vector3 aim = entry.Position + Vector3.up * 1.19f;
                Vector3 to = aim - eye;
                float squared = to.sqrMagnitude;
                if (squared <= SightRange * SightRange && Vector3.Dot(facing, to.normalized) > .28f &&
                    !Obscured(physics, tactical, eye, aim))
                {
                    tactics.Report(Team, i, entry.Position, map, now);
                    float score = squared * (entry.Actor == m_Target ? .7f : 1);
                    if (score < nearest) { nearest = score; best = i; target = aim; }
                }
                if (entry.ShotAt <= m_LastNoiseAt || now - entry.ShotAt > .65 || entry.Shot == 0) continue;
                Vector3 sound = entry.ShotPosition + Vector3.up;
                float noiseDistance = (sound - eye).sqrMagnitude;
                if (noiseDistance >= loudest || !TacticalAlarmHearing.Audible(physics, sound, eye, 38)) continue;
                loudest = noiseDistance;
                m_NoisePoint = sound + new Vector3(Mathf.Sin(entry.Shot + Id) * 2.1f, .1f, Mathf.Cos(entry.Shot + Id) * 2.1f);
                heardAt = entry.ShotAt;
            }
            m_LastNoiseAt = heardAt;

            m_TargetSeat = best;
            if (best < 0) { m_Target = Entity.Null; return; }
            Entity enemy = tactics.Roster[best].Actor;
            if (enemy != m_Target)
            {
                m_ReactAt = now + .19 + (Slot % 4) * .037;
                m_BurstUntil = m_NextBurst = m_ReactAt;
                m_TargetVelocity = Vector3.zero;
            }
            else if (now > m_LastSeenAt)
                m_TargetVelocity = Vector3.ClampMagnitude((target - m_LastSeen) / (float)(now - m_LastSeenAt), 6.5f);
            m_Target = enemy;
            m_LastSeen = target;
            m_LastSeenAt = now;
        }

        private bool ShotLaneClear(ClassicBotTactics tactics, Vector3 eye, Vector3 target)
        {
            Vector3 direction = target - eye;
            float range = direction.magnitude;
            if (range < .1f) return false;
            direction /= range;
            foreach (var ally in tactics.Roster)
            {
                if (!ally.Alive || ally.Team != Team || ally.Id == Id) continue;
                var body = ally.Position + Vector3.up * 1.05f - eye;
                float along = Vector3.Dot(body, direction);
                if (along > 0 && along < range && (body - direction * along).sqrMagnitude < .65f * .65f) return false;
            }
            return true;
        }

        private Vector3 AimPoint(WeaponData weapon, float distance)
        {
            if (!weapon || weapon.Type != WeaponType.Projectile) return m_LastSeen;
            float flight = Mathf.Min(.52f, distance / Mathf.Max(1, weapon.ProjectileSpeed));
            return m_LastSeen + m_TargetVelocity * flight + Vector3.up * (.5f * weapon.ProjectileGravity * flight * flight);
        }

        private static float FireRange(WeaponData weapon, int slot) => weapon.Type == WeaponType.Hitscan ?
            Mathf.Min(slot == 3 ? 44 : SightRange, weapon.HitscanRange) : slot == 3 ? 36 : 51;

        private Vector3 Movement(ClassicBotOrder order, bool visible, bool clearLane, bool cover, float distance, float fireRange, double now)
        {
            if (cover) return FollowRoute(m_CoverPoint, now, .48f);
            if (visible)
            {
                Vector3 toward = Flat(m_LastSeen - LastPosition).normalized;
                if (distance > fireRange * .94f)
                    return FollowRoute(m_LastSeen - Vector3.up * 1.19f, now, 2);
                if (clearLane && now < m_BurstUntil && now >= m_ReactAt) return Vector3.zero;
                float side = ((int)((now + Slot * .21) * 1.35) + Slot) % 2 == 0 ? 1 : -1;
                Vector3 strafe = Vector3.Cross(Vector3.up, toward) * side;
                if (distance < 5) strafe -= toward * .5f;
                if (CanStep(strafe)) return strafe.normalized * .65f;
                return CanStep(-strafe) ? -strafe.normalized * .65f : Vector3.zero;
            }

            if (!order.Guard && now - m_LastSeenAt < 1.55 && distance < 15)
                return FollowRoute(m_LastSeen - Vector3.up * 1.19f, now, 1.5f) * .72f;
            if (!order.Approach.HasValue) return FollowRoute(order.Goal, now, order.Guard ? .55f : .42f);
            if (m_Approach != order.Approach.Value)
            {
                m_Approach = order.Approach.Value;
                m_PassedApproach = false;
            }
            if ((LastPosition - m_Approach).sqrMagnitude < 2.1f * 2.1f || m_Stalls >= 3) m_PassedApproach = true;
            return FollowRoute(m_PassedApproach ? order.Goal : m_Approach, now, m_PassedApproach ? .55f : 1.45f);
        }

        private Vector3 LookDirection(ClassicBotOrder order, Vector3 move, Vector3 aim, bool visible, bool cover, bool corner, Vector3 eye, double now)
        {
            if (visible && !cover) return aim - eye;
            if (now - m_LastSeenAt < 1.4) return m_LastSeen - eye;
            if (now - m_LastNoiseAt < 1.05) return m_NoisePoint - eye;
            if (move.sqrMagnitude > .01f)
            {
                if (corner) return m_Corners[m_Corner + 1] + Vector3.up * 1.15f - eye;
                return move;
            }
            Vector3 watch = order.Watch + Vector3.up * 1.2f - eye;
            return Quaternion.Euler(0, Mathf.Sin((float)now * 1.9f + Slot) * 19, 0) * watch;
        }

        private void UpdateLook(Vector3 direction, bool visible, float dt)
        {
            if (direction.sqrMagnitude < .01f) return;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(direction.y, Flat(direction).magnitude) * Mathf.Rad2Deg;
            m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, yaw, (visible ? 210 : 156) * dt);
            m_Pitch = Mathf.MoveTowardsAngle(m_Pitch, pitch, 115 * dt);
        }

        private bool TakeCover(PhysicsScene physics, double now, bool threatened)
        {
            if (now < m_CoverUntil) return true;
            bool sawThreat = now - m_LastSeenAt < 2.3;
            if (!threatened || now < m_NextCover || !sawThreat && now - m_LastNoiseAt > .8) return false;
            m_NextCover = now + 1.3;
            if (!NavMesh.SamplePosition(LastPosition, out var from, 1.3f, NavMesh.AllAreas)) return false;
            Vector3 threat = sawThreat ? m_LastSeen : m_NoisePoint;
            Vector3 toward = Flat(threat - LastPosition).normalized;
            if (toward.sqrMagnitude < .01f) return false;
            for (int i = 0; i < s_CoverAngles.Length; i++)
            {
                float angle = s_CoverAngles[(i + Slot * 2) % s_CoverAngles.Length];
                Vector3 candidate = from.position + Quaternion.Euler(0, angle, 0) * toward * 3.3f;
                if (!NavMesh.SamplePosition(candidate, out var hit, 1.1f, NavMesh.AllAreas) ||
                    Mathf.Abs(hit.position.y - from.position.y) > 1.25f ||
                    NavMesh.Raycast(from.position, hit.position, out _, NavMesh.AllAreas) ||
                    !WallBetween(physics, hit.position + Vector3.up * 1.16f, threat)) continue;
                m_CoverPoint = hit.position;
                m_CoverUntil = now + 1.75;
                return true;
            }
            return false;
        }

        private Vector3 FollowRoute(Vector3 goal, double now, float arrive)
        {
            if ((goal - LastPosition).sqrMagnitude <= arrive * arrive) return Vector3.zero;
            bool changed = (goal - m_PathGoal).sqrMagnitude > 1.05f * 1.05f;
            if (changed || now >= m_NextPath)
            {
                m_PathGoal = goal;
                m_NextPath = now + .7 + Slot * .031;
                m_Corner = m_CornerCount = 0;
                if (NavMesh.SamplePosition(LastPosition, out var from, 1.65f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(goal, out var to, 2, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, m_Path) && m_Path.status == NavMeshPathStatus.PathComplete)
                    m_CornerCount = m_Path.GetCornersNonAlloc(m_Corners);
            }
            while (m_Corner < m_CornerCount)
            {
                Vector3 offset = Flat(m_Corners[m_Corner] - LastPosition);
                if (offset.sqrMagnitude > .44f * .44f)
                {
                    if (changed || now >= m_CheckProgressAt)
                    {
                        if (!changed && Flat(LastPosition - m_ProgressPosition).sqrMagnitude < .26f * .26f)
                        {
                            m_Stalls++;
                            m_BackOffUntil = now + .38;
                            m_NextPath = m_BackOffUntil;
                        }
                        else m_Stalls = 0;
                        m_ProgressPosition = LastPosition;
                        m_CheckProgressAt = now + 1.1;
                    }
                    if (now < m_BackOffUntil)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, offset.normalized) * ((Slot + m_Stalls) % 2 == 0 ? 1 : -1);
                        if (CanStep(side)) return side * .8f;
                        if (CanStep(-offset.normalized)) return -offset.normalized * .55f;
                    }
                    return offset.normalized;
                }
                m_Corner++;
            }
            if (now >= m_CheckProgressAt)
            {
                m_Stalls++;
                m_CheckProgressAt = now + 1.1;
            }
            return Vector3.zero;
        }

        private Vector3 SpaceFromAllies(ClassicBotTactics tactics, Vector3 move)
        {
            if (move.sqrMagnitude < .01f) return move;
            foreach (var ally in tactics.Roster)
            {
                if (!ally.Alive || ally.Team != Team || ally.Id == Id || Mathf.Abs(ally.Position.y - LastPosition.y) > 1) continue;
                Vector3 offset = Flat(ally.Position - LastPosition);
                if (offset.sqrMagnitude > 1.55f * 1.55f || Vector3.Dot(offset.normalized, move.normalized) < .55f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, move.normalized);
                Vector3 bypass = (move + side * .8f).normalized;
                if (CanStep(bypass)) return bypass * move.magnitude;
                bypass = (move - side * .8f).normalized;
                return CanStep(bypass) ? bypass * move.magnitude : move * .4f;
            }
            return move;
        }

        private bool CanStep(Vector3 direction) =>
            NavMesh.SamplePosition(LastPosition, out var from, .85f, NavMesh.AllAreas) &&
            !NavMesh.Raycast(from.position, from.position + direction * .9f, out _, NavMesh.AllAreas);

        private static Vector3 Flat(Vector3 vector) => new(vector.x, 0, vector.z);

        private static bool WallBetween(PhysicsScene scene, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            return distance > .05f && scene.Raycast(from, delta / distance, distance, s_Obstacles, QueryTriggerInteraction.Ignore);
        }

        private static bool Obscured(PhysicsScene scene, TacticalServerWorld tactical, Vector3 from, Vector3 to) =>
            WallBetween(scene, from, to) || SmokeVisibility.Blocked(tactical, scene, from, to);
    }
}
