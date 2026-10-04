using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // These components exist only on the server. The normal player ghost is the network contract.
    public struct DollSingerEnemy : IComponentData
    {
        public Entity InputEntity;
    }

    public sealed class DollSingerEnemyBrain : IComponentData
    {
        public Entity Target;
        public InventoryGraph Inventory;
        public readonly NavMeshPath Path = new NavMeshPath();
        public readonly Vector3[] Corners = new Vector3[64];
        public int CornerCount, Corner, PatrolPoint, Seed;
        public Vector3 Goal, LastSeen, ProgressPosition;
        public float Yaw, Pitch, LastHealth = 100;
        public double NextSense, NextPath, LastSeenTime = -100, VisibleSince, NextPatrol, NextProgress, AlertUntil;
        public bool Visible;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateBefore(typeof(ServerPlayerMovementSystem))]
    public partial class DollSingerEnemySystem : SystemBase
    {
        private struct Target
        {
            public Entity Entity;
            public Vector3 Position;
            public Transform Root;
        }

        private readonly List<Target> m_Targets = new List<Target>();
        private readonly RaycastHit[] m_Hits = new RaycastHit[32];
        private readonly List<NavMeshBuildSource> m_Sources = new List<NavMeshBuildSource>();
        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");
        private static readonly int s_SightMask = LayerMask.GetMask("Default", "Ground", "ServerPlayer");
        private NavMeshData m_NavData;
        private NavMeshDataInstance m_NavInstance;
        private AsyncOperation m_Build;
        private MoonRaidMap m_Map;
        private bool m_Spawned;
        private double m_NextBuild, m_NextTargets;
        private int m_NextId = -1000;
        private NavMeshQueryFilter m_Filter;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerEntityPrefabs>();
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<RaidLootWorld>();
        }

        protected override void OnDestroy() => ReleaseNavigation();

        private void ReleaseNavigation()
        {
            if (m_NavData != null)
            {
                if (m_Build != null && !m_Build.isDone) NavMeshBuilder.Cancel(m_NavData);
                if (m_NavInstance.valid) m_NavInstance.Remove();
                Object.Destroy(m_NavData);
            }
            m_NavData = null;
            m_Build = null;
        }

        protected override void OnUpdate()
        {
            var map = MoonRaidMap.Active;
            double now = SystemAPI.Time.ElapsedTime;
            // Reference identity also detects an unloaded Unity object that now compares equal to null.
            if (!object.ReferenceEquals(m_Map, map) || map != null && map.EnemyCount == 0 && m_NavData != null)
            {
                DestroyEnemies();
                ReleaseNavigation();
                m_Map = map;
                m_Spawned = false;
                m_NextBuild = 0;
            }
            if (map == null || map.EnemyCount == 0) return;
            if (!NavigationReady(map, now)) return;
            if (now >= m_NextTargets)
            {
                CollectTargets();
                m_NextTargets = now + .1;
            }
            if (!m_Spawned && m_Targets.Count > 0) Spawn(map);

            using var deaths = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (enemy, pose, health, input, entity) in SystemAPI.Query<RefRO<DollSingerEnemy>,
                         RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>, RefRW<PlayerInputComponent>>()
                         .WithAll<Simulate>().WithEntityAccess())
            {
                var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                if (health.ValueRO.CurrentHealth <= 0)
                {
                    DropCorpse(brain, pose.ValueRO, now);
                    deaths.DestroyEntity(enemy.ValueRO.InputEntity);
                    deaths.DestroyEntity(entity);
                    continue;
                }
                if (!EntityManager.HasComponent<GhostGameObjectLink>(entity)) continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (link.LinkedInstance == null) continue;
                var ghost = link.LinkedInstance.GetComponent<PlayerGhost>();
                if (ghost == null) continue;
                input.ValueRW.Input = Think(brain, ghost, pose.ValueRO, health.ValueRO, map, now,
                    (float)SystemAPI.Time.DeltaTime);
            }
            deaths.Playback(EntityManager);
        }

        private void DestroyEnemies()
        {
            using var query = EntityManager.CreateEntityQuery(typeof(DollSingerEnemy));
            using var enemies = query.ToEntityArray(Allocator.Temp);
            foreach (var enemy in enemies)
            {
                var input = EntityManager.GetComponentData<DollSingerEnemy>(enemy).InputEntity;
                if (EntityManager.Exists(input)) EntityManager.DestroyEntity(input);
                EntityManager.DestroyEntity(enemy);
            }
        }

        private bool NavigationReady(MoonRaidMap map, double now)
        {
            if (m_NavInstance.valid) return true;
            if (m_Build != null)
            {
                if (!m_Build.isDone) return false;
                m_NavInstance = NavMesh.AddNavMeshData(m_NavData);
                Debug.Log("[DollSinger AI] Server navigation ready.");
                return m_NavInstance.valid;
            }
            if (now < m_NextBuild) return false;
            m_NextBuild = now + 1;
            // Bound the bake to the expedition, rather than processing the entire lunar terrain.
            var bounds = new Bounds(map.LootPositions[0], Vector3.one);
            foreach (var point in map.LootPositions) bounds.Encapsulate(point);
            bounds.Encapsulate(map.ExtractionPosition);
            bounds.Expand(new Vector3(100, 100, 100));
            UnityEngine.Physics.SyncTransforms();
            m_Sources.Clear();
            NavMeshBuilder.CollectSources(bounds, s_WorldMask, NavMeshCollectGeometry.PhysicsColliders,
                0, new List<NavMeshBuildMarkup>(), m_Sources);
            if (m_Sources.Count == 0) return false; // The streamed terrain may still be loading.
            for (int i = 0; i < m_Sources.Count; i++)
            {
                var source = m_Sources[i];
                if (source.shape != NavMeshBuildSourceShape.Mesh || !(source.sourceObject is Mesh mesh) || mesh.isReadable) continue;
                // Imported rocks discard CPU vertices in a Player build. Their local bounds
                // conservatively block navigation without retaining every rock mesh in RAM.
                source.shape = NavMeshBuildSourceShape.Box;
                source.transform *= Matrix4x4.Translate(mesh.bounds.center);
                source.size = mesh.bounds.size;
                source.sourceObject = null;
                m_Sources[i] = source;
            }
            var settings = NavMesh.GetSettingsByIndex(0);
            settings.agentRadius = .35f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = .35f;
            settings.agentSlope = 45;
            settings.overrideVoxelSize = true;
            settings.voxelSize = .2f;
            m_Filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            m_NavData = new NavMeshData(settings.agentTypeID) { name = "DollSinger server expedition navigation" };
            m_Build = NavMeshBuilder.UpdateNavMeshDataAsync(m_NavData, settings, m_Sources, bounds);
            return false;
        }

        private void CollectTargets()
        {
            m_Targets.Clear();
            foreach (var (pose, health, owner, entity) in SystemAPI.Query<RefRO<LocalTransform>,
                         RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>().WithNone<DollSingerEnemy>()
                         .WithAll<GhostGameObjectLink, Simulate>().WithEntityAccess())
            {
                if (health.ValueRO.CurrentHealth <= 0 || owner.ValueRO.NetworkId <= 0) continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (link.LinkedInstance != null)
                    m_Targets.Add(new Target { Entity = entity, Position = pose.ValueRO.Position, Root = link.LinkedInstance.transform });
            }
        }

        private void Spawn(MoonRaidMap map)
        {
            var prefab = SystemAPI.GetSingleton<PlayerEntityPrefabs>().DollSingerEntityPrefab;
            if (prefab == Entity.Null || WeaponManager.Instance == null) return;
            int spawned = 0;
            for (int i = 0; i < map.EnemyCount; i++)
            {
                int patrol = (i * map.LootPositions.Length / map.EnemyCount + 2) % map.LootPositions.Length;
                bool found = false;
                Vector3 position = default;
                for (int attempt = 0; attempt < map.LootPositions.Length; attempt++)
                {
                    var point = map.LootPositions[(patrol + attempt) % map.LootPositions.Length];
                    var offset = Quaternion.Euler(0, i * 137 + attempt * 47, 0) * Vector3.forward * 5;
                    if (!NavMesh.SamplePosition(point + offset, out var sample, 8, m_Filter)) continue;
                    position = sample.position;
                    if (m_Targets.Exists(t => Vector3.Distance(t.Position, position) < 12)) continue;
                    found = true;
                    break;
                }
                if (!found) continue;
                var player = EntityManager.Instantiate(prefab);
                var input = EntityManager.CreateEntity(typeof(PredictedClientInput));
                int id = m_NextId--;
                var name = new FixedString64Bytes("DollSinger " + (i + 1).ToString("00"));
                EntityManager.SetComponentData(player, new GhostOwner { NetworkId = id });
                EntityManager.SetComponentData(player, LocalTransform.FromPositionRotation(position + Vector3.up * .1f, quaternion.identity));
                int ammo = WeaponManager.Instance.WeaponRegistry.GetWeaponData(2)?.MagazineSize ?? 30;
                EntityManager.SetComponentData(player, new PredictedPlayerGhost { MaxHealth = 100, CurrentHealth = 100,
                    EquippedWeaponID = 2, CurrentAmmo = ammo, StoredHaloAmmo = ammo,
                    StoredRevolverAmmo = WeaponManager.Instance.WeaponRegistry.GetWeaponData(3)?.MagazineSize ?? 6 });
                EntityManager.SetComponentData(player, new GhostGameObjectGuid { Guid = GhostGameObject.GenerateRandomHash() });
                EntityManager.SetComponentData(player, new PlayerGhost.PlayerData { Name = name });
                EntityManager.AddComponentData(player, new PlayerCharacterInitialized());
                EntityManager.SetComponentEnabled<PlayerCharacterInitialized>(player, false);
                EntityManager.AddComponentData(player, new PlayerClientCommandInputLookup { ClientCommandInputEntity = input });
                EntityManager.AddComponentData(player, new DollSingerEnemy { InputEntity = input });
                var inventory = InventoryGraph.Create(stash: false);
                if (map.EnemyCells > 0) inventory.AddSupply("cells", map.EnemyCells, foundInRaid: true);
                EntityManager.AddComponentObject(player, new DollSingerEnemyBrain { Inventory = inventory,
                    PatrolPoint = patrol, Seed = i + 1, Goal = position, ProgressPosition = position });
                LeaderboardManager.AddPlayer(id, name);
                spawned++;
            }
            m_Spawned = true;
            Debug.Log($"[DollSinger AI] Spawned {spawned}/{map.EnemyCount} enemies on the server.");
        }

        private PlayerInput Think(DollSingerEnemyBrain brain, PlayerGhost ghost, LocalTransform pose,
            PredictedPlayerGhost health, MoonRaidMap map, double now, float dt)
        {
            Vector3 position = pose.Position;
            Vector3 eye = ghost.ShotOrigin.position;
            if (health.CurrentHealth < brain.LastHealth) brain.AlertUntil = now + 3;
            brain.LastHealth = health.CurrentHealth;
            if (now >= brain.NextSense)
            {
                Sense(brain, ghost.transform, eye, map.EnemySightRange, now);
                brain.NextSense = now + .1;
            }
            bool pursuing = brain.Target != Entity.Null && now - brain.LastSeenTime < 6;
            if (pursuing && (!EntityManager.Exists(brain.Target) ||
                !EntityManager.HasComponent<PredictedPlayerGhost>(brain.Target) ||
                EntityManager.GetComponentData<PredictedPlayerGhost>(brain.Target).CurrentHealth <= 0))
            {
                pursuing = brain.Visible = false;
                brain.Target = Entity.Null;
            }
            Vector3 goal;
            if (pursuing)
            {
                // Chase the last observed position, then weave at medium range while in sight.
                goal = brain.LastSeen;
                Vector3 away = position - brain.LastSeen;
                away.y = 0;
                if (brain.Visible && away.sqrMagnitude < 32 * 32 && away.sqrMagnitude > .01f)
                {
                    away.Normalize();
                    float side = Mathf.Sin((float)now * .65f + brain.Seed * 2) * 3;
                    goal += away * (health.ControllerState.IsReloadingState ? 20 : 16) + Vector3.Cross(Vector3.up, away) * side;
                }
            }
            else
            {
                brain.Target = Entity.Null;
                brain.Visible = false;
                goal = map.LootPositions[brain.PatrolPoint];
                if (HorizontalDistance(position, goal) < 2 && now >= brain.NextPatrol)
                {
                    brain.PatrolPoint = (brain.PatrolPoint + 1) % map.LootPositions.Length;
                    brain.NextPatrol = now + 1.5;
                    goal = map.LootPositions[brain.PatrolPoint];
                }
            }
            UpdatePath(brain, position, goal, now);
            Vector3 move = FollowPath(brain, position);
            Vector3 facing = brain.Visible ? brain.LastSeen + Vector3.up * 1.2f - eye : move;
            if (facing.sqrMagnitude > .001f)
            {
                float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                float pitch = brain.Visible ? -Mathf.Atan2(facing.y, new Vector2(facing.x, facing.z).magnitude) * Mathf.Rad2Deg : 0;
                brain.Yaw = Mathf.MoveTowardsAngle(brain.Yaw, yaw, 180 * dt);
                brain.Pitch = Mathf.MoveTowardsAngle(brain.Pitch, pitch, 120 * dt);
            }
            // Aim has a small changing error and a reaction delay; it never snaps through walls.
            float error = brain.Visible ? Mathf.Sin((float)now * 2.7f + brain.Seed) * 1.1f : 0;
            var look = new float2(brain.Yaw + error, brain.Pitch + error * .5f);
            Vector3 direction = Quaternion.Euler(look.y, look.x, 0) * Vector3.forward;
            float distance = pursuing ? Vector3.Distance(eye, brain.LastSeen + Vector3.up * 1.2f) : 50;
            Vector3 localMove = Quaternion.Euler(0, -look.x, 0) * move;
            var input = new PlayerInput { MoveInput = new float2(localMove.x, localMove.z),
                LookYawPitchDegrees = look, AimPoint = eye + direction * Mathf.Max(1, distance) };
            input.SetFlag(PlayerInput.InputFlag.ThirdPerson, true);
            input.SetFlag(PlayerInput.InputFlag.Aim, brain.Visible);
            input.SetFlag(PlayerInput.InputFlag.Sprint, pursuing && !brain.Visible && move.sqrMagnitude > .1f);
            input.SetFlag(PlayerInput.InputFlag.Reload, health.CurrentAmmo == 0 && !health.ControllerState.IsReloadingState);
            bool aligned = brain.Visible && Vector3.Angle(direction, facing) < 6;
            bool burst = ((float)now + brain.Seed * .31f) % 1.3f < .65f;
            input.SetFlag(PlayerInput.InputFlag.Shoot, aligned && distance < 35 && now - brain.VisibleSince > .4 && burst &&
                ClearSight(ghost.transform, eye, brain.LastSeen + Vector3.up * 1.2f, brain.Target));
            return input;
        }

        private void Sense(DollSingerEnemyBrain brain, Transform root, Vector3 eye, float range, double now)
        {
            Entity target = Entity.Null;
            Vector3 observed = default;
            float nearest = range * range;
            foreach (var candidate in m_Targets)
            {
                if (!EntityManager.Exists(candidate.Entity) || candidate.Root == null ||
                    !EntityManager.HasComponent<PredictedPlayerGhost>(candidate.Entity) ||
                    EntityManager.GetComponentData<PredictedPlayerGhost>(candidate.Entity).CurrentHealth <= 0) continue;
                Vector3 offset = candidate.Position + Vector3.up * 1.2f - eye;
                float sqr = offset.sqrMagnitude;
                if (sqr >= nearest) continue;
                if (sqr > 8 * 8 && now >= brain.AlertUntil && candidate.Entity != brain.Target &&
                    Vector3.Angle(Quaternion.Euler(0, brain.Yaw, 0) * Vector3.forward, offset) > 70) continue;
                if (!ClearSight(root, eye, eye + offset, candidate.Entity)) continue;
                target = candidate.Entity;
                observed = candidate.Position;
                nearest = sqr;
            }
            bool visible = target != Entity.Null;
            if (visible)
            {
                if (!brain.Visible || brain.Target != target) brain.VisibleSince = now;
                brain.Target = target;
                brain.LastSeen = observed;
                brain.LastSeenTime = now;
            }
            brain.Visible = visible;
        }

        private bool ClearSight(Transform root, Vector3 eye, Vector3 targetPoint, Entity target)
        {
            Vector3 offset = targetPoint - eye;
            int count = UnityEngine.Physics.RaycastNonAlloc(eye, offset.normalized, m_Hits, offset.magnitude + .2f,
                s_SightMask, QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length || !EntityManager.Exists(target) ||
                !EntityManager.HasComponent<GhostGameObjectLink>(target)) return false;
            var targetRoot = EntityManager.GetComponentObject<GhostGameObjectLink>(target).LinkedInstance;
            if (targetRoot == null) return false;
            float nearest = float.MaxValue;
            Transform hit = null;
            for (int i = 0; i < count; i++)
            {
                if (m_Hits[i].transform.IsChildOf(root) || m_Hits[i].distance >= nearest) continue;
                nearest = m_Hits[i].distance;
                hit = m_Hits[i].transform;
            }
            return hit != null && hit.IsChildOf(targetRoot.transform);
        }

        private void UpdatePath(DollSingerEnemyBrain brain, Vector3 position, Vector3 goal, double now)
        {
            if (now < brain.NextPath && HorizontalDistance(brain.Goal, goal) < 3) return;
            brain.NextPath = now + .6 + brain.Seed * .03;
            brain.Goal = goal;
            brain.CornerCount = 0;
            brain.Corner = 1;
            if (!NavMesh.SamplePosition(position, out var start, 3, m_Filter) ||
                !NavMesh.SamplePosition(goal, out var end, 6, m_Filter) ||
                !NavMesh.CalculatePath(start.position, end.position, m_Filter, brain.Path)) return;
            brain.CornerCount = brain.Path.GetCornersNonAlloc(brain.Corners);
            // A stalled controller repaths instead of teleporting through an obstruction.
            if (now >= brain.NextProgress)
            {
                if (HorizontalDistance(position, brain.ProgressPosition) < .3f && HorizontalDistance(position, goal) > 2)
                    brain.PatrolPoint = (brain.PatrolPoint + 1) % m_Map.LootPositions.Length;
                brain.ProgressPosition = position;
                brain.NextProgress = now + 3;
            }
        }

        private static Vector3 FollowPath(DollSingerEnemyBrain brain, Vector3 position)
        {
            while (brain.Corner < brain.CornerCount && HorizontalDistance(position, brain.Corners[brain.Corner]) < .7f)
                brain.Corner++;
            if (brain.Corner >= brain.CornerCount) return Vector3.zero;
            Vector3 direction = brain.Corners[brain.Corner] - position;
            direction.y = 0;
            return direction.normalized;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private void DropCorpse(DollSingerEnemyBrain brain, LocalTransform pose, double now)
        {
            Vector3 position = pose.Position;
            Quaternion rotation = pose.Rotation;
            if (UnityEngine.Physics.Raycast(position + Vector3.up, Vector3.down, out var ground, 4, s_WorldMask, QueryTriggerInteraction.Ignore))
            {
                position = ground.point;
                rotation = Quaternion.FromToRotation(Vector3.up, ground.normal) * rotation;
            }
            var loot = EntityManager.GetComponentObject<RaidLootContainers>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
            loot.Drop(brain.Inventory, position, rotation, 2, now);
        }
    }
}
