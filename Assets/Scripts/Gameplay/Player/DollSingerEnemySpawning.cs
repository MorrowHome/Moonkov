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
    public partial class DollSingerEnemySystem
    {
        private readonly List<RaidSpawnObserver> m_SpawnObservers = new List<RaidSpawnObserver>();
        private readonly NavMeshPath m_SpawnPath = new NavMeshPath();
        private Unity.Mathematics.Random m_SpawnRandom;
        private bool m_PopulationStarted;
        private int m_InitialRemaining, m_ReinforcementsRemaining, m_SpawnSerial;
        private double m_PopulationStartedAt, m_NextPopulationCheck, m_NextInitialAttempt, m_NextReinforcement;

        private void ResetPopulation()
        {
            m_PopulationStarted = false;
            m_InitialRemaining = m_ReinforcementsRemaining = m_SpawnSerial = 0;
            m_PopulationStartedAt = m_NextPopulationCheck = m_NextInitialAttempt = m_NextReinforcement = 0;
            m_NextBuild = m_NextTargets = 0;
            m_Targets.Clear();
            m_Tracks.Clear();
            m_Events.Clear();
            m_CoverReservations.Clear();
            m_SpawnObservers.Clear();
        }

        private void UpdatePopulation(MoonRaidMap map, double now)
        {
            if (now < m_NextPopulationCheck) return;
            m_NextPopulationCheck = now + 1;
            int alive = CollectSpawnObservers(out bool hasHuman);
            // AI actors never keep the population spawning after the humans have left.
            if (!hasHuman) return;

            int maxAlive = Mathf.Clamp(m_Tuning.MaxAlive > 0 ? m_Tuning.MaxAlive : map.EnemyCount, 1, 12);
            if (!m_PopulationStarted)
            {
                m_PopulationStarted = true;
                m_PopulationStartedAt = now;
                m_InitialRemaining = Mathf.Min(Mathf.Max(0, map.EnemyCount), maxAlive);
                m_ReinforcementsRemaining = Mathf.Clamp(m_Tuning.ReinforcementBudget, 0, 24);
                m_NextInitialAttempt = now + Mathf.Max(0, m_Tuning.InitialSpawnDelay);
                uint seed = unchecked((uint)System.Guid.NewGuid().GetHashCode());
                m_SpawnRandom = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            }
            if (now - m_PopulationStartedAt >= map.RaidDuration * Mathf.Clamp(m_Tuning.SpawnCutoffFraction, .1f, 1)) return;
            if (alive >= maxAlive)
            {
                m_NextReinforcement = 0;
                return;
            }

            if (m_InitialRemaining > 0)
            {
                if (now < m_NextInitialAttempt) return;
                int spawned = SpawnBatch(map, Mathf.Min(m_InitialRemaining, maxAlive - alive), now);
                m_InitialRemaining -= spawned;
                m_NextInitialAttempt = now + Mathf.Max(1, m_Tuning.SpawnRetrySeconds);
                return;
            }
            if (m_ReinforcementsRemaining <= 0) return;
            if (m_NextReinforcement == 0)
                m_NextReinforcement = now + Mathf.Max(5, m_Tuning.ReinforcementDelay);
            if (now < m_NextReinforcement) return;
            int count = Mathf.Min(Mathf.Min(maxAlive - alive, m_ReinforcementsRemaining), Mathf.Clamp(m_Tuning.ReinforcementWaveSize, 1, 4));
            int added = SpawnBatch(map, count, now);
            // Failed placement consumes neither a life nor a reinforcement. Retry only at the configured cadence.
            m_ReinforcementsRemaining -= added;
            m_NextReinforcement = now + (added > 0 ? Mathf.Max(5, m_Tuning.ReinforcementDelay) : Mathf.Max(1, m_Tuning.SpawnRetrySeconds));
        }

        private int CollectSpawnObservers(out bool hasHuman)
        {
            m_SpawnObservers.Clear();
            hasHuman = false;
            int alive = 0;
            foreach (var (pose, health, owner, entity) in SystemAPI.Query<RefRO<LocalTransform>,
                         RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>().WithAll<Simulate>().WithEntityAccess())
            {
                if (health.ValueRO.CurrentHealth <= 0 || owner.ValueRO.NetworkId == 0) continue;
                if (EntityManager.HasComponent<DollSingerEnemy>(entity)) alive++;
                bool human = owner.ValueRO.NetworkId > 0;
                hasHuman |= human;
                Vector3 forward = Vector3.zero;
                if (human)
                {
                    var controller = health.ValueRO.ControllerState;
                    var look = new float2(controller.YawDegrees, controller.PitchDegrees) + controller.FreeLookOffset;
                    forward = Quaternion.Euler(look.y, look.x, 0) * Vector3.forward;
                }
                m_SpawnObservers.Add(new RaidSpawnObserver { Position = pose.ValueRO.Position, Forward = forward, CheckVisibility = human });
            }
            return alive;
        }

        private int SpawnBatch(MoonRaidMap map, int count, double now)
        {
            var prefab = SystemAPI.GetSingleton<PlayerEntityPrefabs>().DollSingerEntityPrefab;
            if (prefab == Entity.Null || WeaponManager.Instance?.WeaponRegistry == null) return 0;
            UnityEngine.Physics.SyncTransforms();
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryChooseSpawn(map, out var position)) break;
                SpawnActor(map, prefab, position, now);
                // Reserve immediately: the new ghost's GameObject does not exist until the bridge updates.
                m_SpawnObservers.Add(new RaidSpawnObserver { Position = position, CheckVisibility = false });
                spawned++;
            }
            if (spawned > 0)
                Debug.Log($"[DollSinger AI] Added {spawned} raid actor(s); {m_SpawnSerial} spawned this map session.");
            return spawned;
        }

        private bool TryChooseSpawn(MoonRaidMap map, out Vector3 position)
        {
            position = default;
            var anchors = map.EnemySpawnPositions;
            if (anchors.Length == 0 || !NavMesh.SamplePosition(map.ExtractionPosition, out var extraction, 3, m_Filter)) return false;
            int start = m_SpawnRandom.NextInt(anchors.Length);
            float inner = Mathf.Max(0, m_Tuning.SpawnInnerRadius);
            float outer = Mathf.Max(inner, m_Tuning.SpawnOuterRadius);
            int attempts = Mathf.Clamp(m_Tuning.SpawnAttempts, 4, 48);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var anchor = anchors[(start + attempt) % anchors.Length];
                float angle = m_SpawnRandom.NextFloat(0, Mathf.PI * 2);
                float radius = Mathf.Sqrt(m_SpawnRandom.NextFloat(inner * inner, outer * outer));
                // Try authored points themselves before searching their surrounding area.
                var candidate = attempt < anchors.Length ? anchor :
                    anchor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (!NavMesh.SamplePosition(candidate, out var sample, 3, m_Filter) ||
                    Mathf.Abs(sample.position.y - candidate.y) > 2.5f) continue;
                var point = sample.position;
                if (HorizontalDistance(point, map.ExtractionPosition) < map.ExtractionRadius + 8) continue;
                if (!RaidSpawnSafety.IsSafeFromObservers(point, m_SpawnObservers,
                    Mathf.Max(8, m_Tuning.SpawnPlayerDistance), Mathf.Max(10, m_Tuning.SpawnVisibilityDistance),
                    Mathf.Max(1, m_Tuning.SpawnActorSpacing))) continue;
                if (!RaidSpawnSafety.TryGroundPosition(point, .35f, 1.8f, 45, out var ground, out _) ||
                    Mathf.Abs(ground.y - point.y) > .6f) continue;
                // Reject disconnected islands and upper floors with no route back to the raid exit.
                if (!NavMesh.CalculatePath(point, extraction.position, m_Filter, m_SpawnPath) ||
                    m_SpawnPath.status != NavMeshPathStatus.PathComplete) continue;
                position = point;
                return true;
            }
            return false;
        }

        private void SpawnActor(MoonRaidMap map, Entity prefab, Vector3 position, double now)
        {
            var player = EntityManager.Instantiate(prefab);
            var input = EntityManager.CreateEntity(typeof(PredictedClientInput));
            int id = m_NextId--;
            int serial = ++m_SpawnSerial;
            var name = new FixedString64Bytes("DollSinger " + serial.ToString("00"));
            float yaw = m_SpawnRandom.NextFloat(0, 360);
            EntityManager.SetComponentData(player, new GhostOwner { NetworkId = id });
            EntityManager.SetComponentData(player, LocalTransform.FromPositionRotation(position + Vector3.up * .1f,
                quaternion.RotateY(math.radians(yaw))));
            int ammo = WeaponManager.Instance.WeaponRegistry.GetWeaponData(2)?.MagazineSize ?? 30;
            var healthState = new PredictedPlayerGhost { MaxHealth = 100, CurrentHealth = 100,
                EquippedWeaponID = DollSingerWeapons.None, CurrentAmmo = 0, InventoryWeapons = true,
                PrimaryWeaponID = DollSingerWeapons.None, SecondaryWeaponID = DollSingerWeapons.None, PistolWeaponID = DollSingerWeapons.None };
            RaidHealth.Initialize(ref healthState);
            EntityManager.SetComponentData(player, healthState);
            EntityManager.SetComponentData(player, new GhostGameObjectGuid { Guid = GhostGameObject.GenerateRandomHash() });
            EntityManager.SetComponentData(player, new PlayerGhost.PlayerData { Name = name });
            EntityManager.AddComponentData(player, new PlayerCharacterInitialized());
            EntityManager.SetComponentEnabled<PlayerCharacterInitialized>(player, false);
            EntityManager.AddComponentData(player, new PlayerClientCommandInputLookup { ClientCommandInputEntity = input });
            EntityManager.AddComponentData(player, new DollSingerEnemy { InputEntity = input });
            var inventory = InventoryGraph.Create(stash: false);
            inventory.Equipped("Primary").LoadedAmmo = ammo;
            // A finite raid loadout; unused medicine remains in the normal death inventory.
            inventory.AddSupply("medkit", 2, parent: InventoryCatalog.Pockets, foundInRaid: true);
            if (map.EnemyCells > 0) inventory.AddSupply("cells", map.EnemyCells, foundInRaid: true);
            EntityManager.AddComponentObject(player, new DollSingerEnemyBrain { Inventory = inventory,
                Seed = serial, Yaw = yaw, Goal = position, ProgressPosition = position, SpawnTime = now,
                NextSense = now + (serial % 3) * .033, NextDecision = now + (serial % 3) * .067,
                Aggression = .4f + ((serial - 1) % 3) * .2f, Caution = .8f - ((serial - 1) % 3) * .2f,
                Random = new Unity.Mathematics.Random(m_SpawnRandom.NextUInt(1, uint.MaxValue)) });
            LeaderboardManager.AddPlayer(id, name);
        }
    }
}
