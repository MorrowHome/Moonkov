using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

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
        private readonly NavMeshPath m_Path = new();
        private readonly Vector3[] m_Corners = new Vector3[96];
        private int m_Corner, m_CornerCount;
        private double m_NextPath, m_NextSense, m_SawAt;
        private float m_Yaw, m_Pitch;
        private Entity m_Target;
        private static readonly int s_Obstacles = LayerMask.GetMask("Default", "Ground");

        public ClassicBot(Entity actor, Entity input, int id, int team, int slot, Vector3 position, float yaw)
        {
            Actor = actor; Input = input; Id = id; Team = team; Slot = slot; LastPosition = position; m_Yaw = yaw;
            m_NextSense = slot * .021;
        }

        public void MoveAndFight(EntityManager manager, ArenaMatch match, ClassicDust2Map map, double now, float dt, bool holding)
        {
            if (!manager.HasComponent<PlayerInputComponent>(Actor)) return;
            var health = manager.GetComponentData<PredictedPlayerGhost>(Actor);
            if (!health.ClashCanAct) return;
            var input = new PlayerInputComponent();
            Vector3 eye = LastPosition + Vector3.up * 1.5f;
            if (manager.HasComponent<GhostGameObjectLink>(Actor))
            {
                var link = manager.GetComponentObject<GhostGameObjectLink>(Actor);
                var ghost = link.LinkedInstance ? link.LinkedInstance.GetComponent<PlayerGhost>() : null;
                if (ghost && ghost.ShotOrigin) eye = ghost.ShotOrigin.position;
            }

            if (now >= m_NextSense)
            {
                m_NextSense = now + .17;
                Sense(manager, match, eye, now);
            }
            bool contact = TryTarget(manager, m_Target, out var target);
            Vector3 movement = !holding && !contact ? FollowRoute(match.Classic, map, now) : Vector3.zero;
            Vector3 direction = contact ? target - eye : movement;
            if (direction.sqrMagnitude > .01f)
            {
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
                m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, yaw, 145 * dt);
                m_Pitch = Mathf.MoveTowardsAngle(m_Pitch, pitch, 95 * dt);
            }
            float sway = contact ? Mathf.Sin((float)now * 9 + Slot) * .9f : 0;
            var forward = Quaternion.Euler(m_Pitch, m_Yaw + sway, 0) * Vector3.forward;
            var local = Quaternion.Euler(0, -m_Yaw, 0) * movement;
            input.Input.LookYawPitchDegrees = new float2(m_Yaw + sway, m_Pitch);
            input.Input.MoveInput = new float2(local.x, local.z);
            input.Input.AimPoint = eye + forward * (contact ? Mathf.Max(1, direction.magnitude) : 40);
            input.Input.SetFlag(PlayerInput.InputFlag.ThirdPerson, true);
            input.Input.SetFlag(PlayerInput.InputFlag.Aim, contact && !holding);
            bool burst = (now + Slot * .07) % .85 < .44;
            bool fire = !holding && contact && burst && now - m_SawAt >= .23 && health.CurrentAmmo > 0 &&
                Vector3.Dot(forward, direction.normalized) > .995f &&
                !UnityEngine.Physics.Linecast(eye, target, s_Obstacles, QueryTriggerInteraction.Ignore);
            input.Input.SetFlag(PlayerInput.InputFlag.Shoot, fire);
            input.Input.SetFlag(PlayerInput.InputFlag.FireHeld, fire);
            input.Input.SetFlag(PlayerInput.InputFlag.Reload, !holding && health.CurrentAmmo == 0 && !health.ControllerState.IsReloadingState);
            manager.SetComponentData(Actor, input);
        }

        private void Sense(EntityManager manager, ArenaMatch match, Vector3 eye, double now)
        {
            var old = m_Target;
            m_Target = Entity.Null;
            float nearest = 48 * 48;
            Vector3 facing = Quaternion.Euler(0, m_Yaw, 0) * Vector3.forward;
            void Consider(Entity entity, int team)
            {
                if (Team == team || !TryTarget(manager, entity, out var point)) return;
                Vector3 to = point - eye;
                if (to.sqrMagnitude >= nearest || Vector3.Dot(facing, to.normalized) < .25f ||
                    UnityEngine.Physics.Linecast(eye, point, s_Obstacles, QueryTriggerInteraction.Ignore)) return;
                nearest = to.sqrMagnitude;
                m_Target = entity;
            }
            foreach (var (connection, player) in match.Players)
                if (manager.HasComponent<JoinedClient>(connection)) Consider(manager.GetComponentData<JoinedClient>(connection).PlayerEntity, player.Team);
            foreach (var bot in match.Bots.Values) Consider(bot.Actor, bot.Team);
            if (m_Target != old) m_SawAt = now;
        }

        private static bool TryTarget(EntityManager manager, Entity entity, out Vector3 position)
        {
            position = default;
            if (!manager.HasComponent<LocalTransform>(entity) || !manager.HasComponent<PredictedPlayerGhost>(entity) ||
                manager.GetComponentData<PredictedPlayerGhost>(entity).CurrentHealth <= 0) return false;
            position = (Vector3)manager.GetComponentData<LocalTransform>(entity).Position + Vector3.up * 1.25f;
            return true;
        }

        private Vector3 FollowRoute(ClassicMatch rules, ClassicDust2Map map, double now)
        {
            if (now >= m_NextPath)
            {
                m_NextPath = now + .65 + .035 * Slot;
                int site = Team == rules.AttackTeam ? (rules.Round + (rules.Carrier == Id ? 0 : Slot % 2)) % 2 : Slot % 2;
                Vector3 goal = rules.Phase == ClassicPhase.Planted || rules.BombOnGround && Team == rules.AttackTeam
                    ? rules.BombPosition : map.Site(site);
                m_Corner = m_CornerCount = 0;
                if (NavMesh.SamplePosition(LastPosition, out var from, 1.75f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(goal, out var to, 2.25f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, m_Path) && m_Path.status == NavMeshPathStatus.PathComplete)
                    m_CornerCount = m_Path.GetCornersNonAlloc(m_Corners);
            }
            while (m_Corner < m_CornerCount)
            {
                Vector3 offset = m_Corners[m_Corner] - LastPosition;
                offset.y = 0;
                if (offset.sqrMagnitude > .35f * .35f) return offset.normalized;
                m_Corner++;
            }
            return Vector3.zero;
        }
    }
}
