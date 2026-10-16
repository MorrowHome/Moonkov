using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS
{
    // These components exist only on the server. The normal player ghost is the network contract.
    public struct DollSingerEnemy : IComponentData
    {
        public Entity InputEntity;
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
            public Vector3 Velocity;
            public Vector3 AimOffset;
            public bool Alien;
        }

        private sealed class SensorTrack
        {
            public Vector3 Position;
            public float StepDistance;
            public uint Shot, Reload;
            public double SeenAt;
        }
        private struct SensorEvent
        {
            public Entity Source;
            public Vector3 Position;
            public float Range;
            public bool Shot;
        }

        private readonly List<Target> m_Targets = new List<Target>();
        private readonly Dictionary<Entity, SensorTrack> m_Tracks = new Dictionary<Entity, SensorTrack>();
        private readonly List<Entity> m_ExpiredTracks = new List<Entity>();
        private readonly List<SensorEvent> m_Events = new List<SensorEvent>();
        private readonly Dictionary<int, (Vector3 position, double expiry)> m_CoverReservations = new Dictionary<int, (Vector3, double)>();
        private DollSingerAITuning m_Tuning;
        private DollSingerAITuning m_DefaultTuning;
        private readonly RaycastHit[] m_Hits = new RaycastHit[32];
        private readonly List<NavMeshBuildSource> m_Sources = new List<NavMeshBuildSource>();
        private static readonly int s_WorldMask = LayerMask.GetMask("Default", "Ground");
        private static readonly int s_SightMask = LayerMask.GetMask("Default", "Ground", "ServerPlayer");
        private NavMeshData m_NavData;
        private NavMeshDataInstance m_NavInstance;
        private AsyncOperation m_Build;
        private MoonRaidMap m_Map;
        private double m_NextBuild, m_NextTargets;
        private double m_SensorBatch;
        private int m_NextId = -1000;
        private NavMeshQueryFilter m_Filter;

        protected override void OnCreate()
        {
            m_Tuning = Resources.Load<DollSingerAITuning>("Moonkov/DollSingerAI");
            if (m_Tuning == null) m_Tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
            m_DefaultTuning = m_Tuning;
            RequireForUpdate<PlayerEntityPrefabs>();
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<RaidLootWorld>();
        }

        protected override void OnDestroy()
        {
            DestroyEnemies();
            ReleaseNavigation();
            if (m_DefaultTuning != null && string.IsNullOrEmpty(m_DefaultTuning.name)) Object.Destroy(m_DefaultTuning);
        }

        protected override void OnStopRunning()
        {
            DestroyEnemies();
            ReleaseNavigation();
            m_Targets.Clear();
            m_Tracks.Clear();
            m_Events.Clear();
            m_CoverReservations.Clear();
            m_SpawnObservers.Clear();
            m_NextBuild = m_NextTargets = 0;
            // All transports can disconnect while the map/world remain alive. Retain the
            // session budget and cutoff so reconnecting cannot manufacture another wave.
        }

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
            if (!object.ReferenceEquals(m_Map, map) || map != null && map.EnemyCount <= 0 && m_NavData != null)
            {
                DestroyEnemies();
                ReleaseNavigation();
                m_Map = map;
                m_Tuning = map && map.EnemyTuning ? map.EnemyTuning : m_DefaultTuning;
                ResetPopulation();
            }
            if (map == null || map.EnemyCount <= 0) return;
            if (!NavigationReady(map, now)) return;
            if (now >= m_NextTargets)
            {
                CollectTargets(now);
                m_NextTargets = now + .1;
            }

            using var deaths = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (enemy, pose, health, input, entity) in SystemAPI.Query<RefRO<DollSingerEnemy>,
                         RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>, RefRW<PlayerInputComponent>>()
                         .WithAll<Simulate>().WithEntityAccess())
            {
                var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                if (health.ValueRO.CurrentHealth <= 0)
                {
                    DropCorpse(brain, pose.ValueRO, now);
                    RetireEnemy(entity, brain);
                    deaths.DestroyEntity(enemy.ValueRO.InputEntity);
                    deaths.DestroyEntity(entity);
                    continue;
                }
                if (!EntityManager.HasComponent<GhostGameObjectLink>(entity)) continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (link.LinkedInstance == null) continue;
                var ghost = link.LinkedInstance.GetComponent<PlayerGhost>();
                if (ghost == null) continue;
                input.ValueRW.Input = Think(brain, ghost, pose.ValueRO, ref health.ValueRW, map, now,
                    (float)SystemAPI.Time.DeltaTime);
                if (brain.Extracted)
                {
                    // Successful extraction removes this raid actor, without producing a corpse.
                    deaths.DestroyEntity(enemy.ValueRO.InputEntity);
                    deaths.DestroyEntity(entity);
                    RetireEnemy(entity, brain);
                }
            }
            deaths.Playback(EntityManager);
            UpdatePopulation(map, now);
        }

        private void DestroyEnemies()
        {
            using var query = EntityManager.CreateEntityQuery(typeof(DollSingerEnemy));
            using var enemies = query.ToEntityArray(Allocator.Temp);
            foreach (var enemy in enemies)
            {
                RetireEnemy(enemy, EntityManager.GetComponentObject<DollSingerEnemyBrain>(enemy));
                var input = EntityManager.GetComponentData<DollSingerEnemy>(enemy).InputEntity;
                if (EntityManager.Exists(input)) EntityManager.DestroyEntity(input);
                EntityManager.DestroyEntity(enemy);
            }
        }

        private void RetireEnemy(Entity entity, DollSingerEnemyBrain brain)
        {
            m_CoverReservations.Remove(brain.Seed);
            LeaderboardManager.RetireRaidActor(EntityManager.GetComponentData<GhostOwner>(entity).NetworkId);
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
            foreach (var point in map.EnemySpawnPositions) bounds.Encapsulate(point);
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

        private void CollectTargets(double now)
        {
            m_Targets.Clear();
            m_Events.Clear();
            m_SensorBatch = now;
            foreach (var (pose, health, owner, entity) in SystemAPI.Query<RefRO<LocalTransform>,
                         RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>()
                         .WithAll<GhostGameObjectLink, Simulate>().WithEntityAccess())
            {
                if (health.ValueRO.CurrentHealth <= 0 || owner.ValueRO.NetworkId == 0) continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (link.LinkedInstance == null) continue;
                Vector3 position = pose.ValueRO.Position;
                Vector3 velocity = Vector3.zero;
                if (!m_Tracks.TryGetValue(entity, out var track))
                {
                    track = new SensorTrack { Position = position, SeenAt = now,
                        Shot = health.ValueRO.LastShotTick, Reload = health.ValueRO.LastReloadTick };
                    m_Tracks.Add(entity, track);
                }
                else
                {
                    velocity = Vector3.ClampMagnitude((position - track.Position) / Mathf.Max(.05f, (float)(now - track.SeenAt)), 8);
                    track.StepDistance += HorizontalDistance(position, track.Position);
                    if (track.Shot != health.ValueRO.LastShotTick)
                        m_Events.Add(new SensorEvent { Source = entity, Position = position, Range = m_Tuning.ShotSensorRange, Shot = true });
                    if (track.Reload != health.ValueRO.LastReloadTick)
                        m_Events.Add(new SensorEvent { Source = entity, Position = position, Range = 7 });
                    if (track.StepDistance >= 2.2f)
                    {
                        m_Events.Add(new SensorEvent { Source = entity, Position = position,
                            Range = m_Tuning.FootstepSensorRange * (velocity.magnitude > 4 ? 1.3f : .65f) });
                        track.StepDistance = 0;
                    }
                    track.Position = position;
                    track.SeenAt = now;
                    track.Shot = health.ValueRO.LastShotTick;
                    track.Reload = health.ValueRO.LastReloadTick;
                }
                var alien = link.LinkedInstance.GetComponent<ProceduralAlienEnemy>();
                m_Targets.Add(new Target { Entity = entity, Position = position, Velocity = velocity, Root = link.LinkedInstance.transform,
                    Alien = alien != null, AimOffset = alien ? alien.AimPoint - position : Vector3.up * 1.15f });
            }
            m_ExpiredTracks.Clear();
            foreach (var pair in m_Tracks) if (now - pair.Value.SeenAt > 1) m_ExpiredTracks.Add(pair.Key);
            foreach (var entity in m_ExpiredTracks) m_Tracks.Remove(entity);
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
                !NavMesh.SamplePosition(goal, out var end, 3, m_Filter) ||
                !NavMesh.CalculatePath(start.position, end.position, m_Filter, brain.Path) ||
                brain.Path.status != NavMeshPathStatus.PathComplete)
            { brain.PathFailed = true; return; }
            brain.PathFailed = false;
            brain.CornerCount = brain.Path.GetCornersNonAlloc(brain.Corners);
            // A stalled controller repaths instead of teleporting through an obstruction.
            if (now >= brain.NextProgress)
            {
                if (HorizontalDistance(position, brain.ProgressPosition) < .3f && HorizontalDistance(position, goal) > 2)
                {
                    brain.PathFailed = true;
                    brain.NextCover = brain.NextDecision = 0;
                    if (brain.Action == PmcAction.Scavenge && brain.Cache >= 0)
                    { brain.VisitedCaches.Add(brain.Cache); brain.Cache = -1; brain.LootUntil = 0; }
                }
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
