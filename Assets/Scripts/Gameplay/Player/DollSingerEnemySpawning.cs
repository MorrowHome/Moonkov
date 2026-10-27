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
        private readonly List<Vector3> m_SpawnExitSamples = new List<Vector3>(RaidExtractionRules.MaxExits);
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
            if(BountyClashRuntime.Enabled(EntityManager))return;
            if (now < m_NextPopulationCheck) return;
            m_NextPopulationCheck = now + 1;
            bool tutorial = TutorialRuntime.Enabled(EntityManager);
            if (tutorial && (!RaidMissionSite.Active || !RaidMissionSite.Active.FirstChapter ||
                !TutorialReported())) return;
            int alive = CollectSpawnObservers(out bool hasHuman);
            // AI actors never keep the population spawning after the humans have left.
            if (!hasHuman) return;

            int maxAlive = tutorial ? 1 : Mathf.Clamp(m_Tuning.MaxAlive > 0 ? m_Tuning.MaxAlive : map.EnemyCount, 1, 12);
            if (!m_PopulationStarted)
            {
                m_PopulationStarted = true;
                m_PopulationStartedAt = now;
                m_InitialRemaining = tutorial ? 1 : Mathf.Min(Mathf.Max(0, map.EnemyCount), maxAlive);
                m_ReinforcementsRemaining = tutorial ? 0 : Mathf.Clamp(m_Tuning.ReinforcementBudget, 0, 24);
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

        private bool TutorialReported()
        {
            using var query = EntityManager.CreateEntityQuery(typeof(RaidPersistenceContext));
            if (query.IsEmptyIgnoreFilter) return false;
            var context = EntityManager.GetComponentObject<RaidPersistenceContext>(query.GetSingletonEntity());
            foreach (var run in context.CampaignRuns.Values)
                if (Campaign.CampaignRules.NodeCompleted(run, "CM01", 1)) return true;
            return false;
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
            if (TutorialRuntime.Enabled(EntityManager) && RaidMissionSite.Active)
            {
                var anchor = RaidMissionSite.Active.Anchor("power_a");
                if (anchor != null) anchors = new[] { anchor.Approach };
            }
            if (anchors.Length == 0) return false;
            m_SpawnExitSamples.Clear();
            for (int i = 0; i < map.ExtractionCount; i++)
                if (map.TryExtractionPosition(i, out var exit) && NavMesh.SamplePosition(exit, out var sample, 3, m_Filter))
                    m_SpawnExitSamples.Add(sample.position);
            int start = m_SpawnRandom.NextInt(anchors.Length);
            float inner = Mathf.Max(0, m_Tuning.SpawnInnerRadius);
            float outer = Mathf.Max(inner, m_Tuning.SpawnOuterRadius);
            int attempts = Mathf.Clamp(m_Tuning.SpawnAttempts, 4, 48);
            int nearbyAttempts = 0;
            for (int attempt = 0; attempt < anchors.Length + attempts && nearbyAttempts < attempts; attempt++)
            {
                var anchor = anchors[(start + attempt) % anchors.Length];
                if (map.EnemyActivationDistance > 0)
                {
                    bool nearby = false;
                    foreach (var observer in m_SpawnObservers)
                        if (observer.CheckVisibility && HorizontalDistance(anchor, observer.Position) <= map.EnemyActivationDistance)
                        { nearby = true; break; }
                    if (!nearby) continue;
                }
                nearbyAttempts++;
                float angle = m_SpawnRandom.NextFloat(0, Mathf.PI * 2);
                float radius = Mathf.Sqrt(m_SpawnRandom.NextFloat(inner * inner, outer * outer));
                // Try authored points themselves before searching their surrounding area.
                var candidate = attempt < anchors.Length ? anchor :
                    anchor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (!NavMesh.SamplePosition(candidate, out var sample, 3, m_Filter) ||
                    Mathf.Abs(sample.position.y - candidate.y) > 2.5f) continue;
                var point = sample.position;
                bool nearExit = false;
                for (int i = 0; i < map.ExtractionCount; i++)
                    if (map.TryExtractionPosition(i, out var exit) && HorizontalDistance(point, exit) < map.ExtractionRadius + 8)
                    { nearExit = true; break; }
                if (nearExit) continue;
                if (!RaidSpawnSafety.IsSafeFromObservers(point, m_SpawnObservers,
                    Mathf.Max(8, m_Tuning.SpawnPlayerDistance), Mathf.Max(10, m_Tuning.SpawnVisibilityDistance),
                    Mathf.Max(1, m_Tuning.SpawnActorSpacing))) continue;
                if (!RaidSpawnSafety.TryGroundPosition(point, .35f, 1.8f, 45, out var ground, out _) ||
                    Mathf.Abs(ground.y - point.y) > .6f) continue;
                bool reachesExit = false;
                int nearestExit = -1; float nearestDistance = float.MaxValue;
                for (int i = 0; i < m_SpawnExitSamples.Count; i++)
                {
                    float distance = HorizontalDistance(point, m_SpawnExitSamples[i]);
                    if (distance < nearestDistance) { nearestDistance = distance; nearestExit = i; }
                }
                if (nearestExit >= 0)
                    reachesExit = NavMesh.CalculatePath(point, m_SpawnExitSamples[nearestExit], m_Filter, m_SpawnPath) &&
                        m_SpawnPath.status == NavMeshPathStatus.PathComplete;
                for (int i = 0; !reachesExit && i < m_SpawnExitSamples.Count; i++)
                    if (i != nearestExit && NavMesh.CalculatePath(point, m_SpawnExitSamples[i], m_Filter, m_SpawnPath) &&
                        m_SpawnPath.status == NavMeshPathStatus.PathComplete) reachesExit = true;
                // Full-map entries can serve separate crater basins. Require local walking
                // space and let these raiders leave through their own entry instead.
                if (!reachesExit && (map.EnemyActivationDistance <= 0 || !HasLocalSpawnRoute(point))) continue;
                position = point;
                return true;
            }
            return false;
        }

        private bool HasLocalSpawnRoute(Vector3 position)
        {
            for (int side = 0; side < 4; side++)
            {
                float angle = side * Mathf.PI * .5f;
                var candidate = position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 8;
                if (NavMesh.SamplePosition(candidate, out var nearby, 4, m_Filter) &&
                    HorizontalDistance(position, nearby.position) >= 4 &&
                    NavMesh.CalculatePath(position, nearby.position, m_Filter, m_SpawnPath) &&
                    m_SpawnPath.status == NavMeshPathStatus.PathComplete) return true;
            }
            return false;
        }

        private Vector3 SelectRaidExit(MoonRaidMap map, Vector3 position, out bool localExit)
        {
            Vector3 chosen = position; float nearest = float.MaxValue;
            for (int i = 0; i < map.ExtractionCount; i++)
            {
                if (!map.TryExtractionPosition(i, out var candidate)) continue;
                float distance = HorizontalDistance(position, candidate);
                if (distance >= nearest || !NavMesh.SamplePosition(candidate, out var exit, 3, m_Filter) ||
                    !NavMesh.CalculatePath(position, exit.position, m_Filter, m_SpawnPath) ||
                    m_SpawnPath.status != NavMeshPathStatus.PathComplete) continue;
                chosen = candidate; nearest = distance;
            }
            localExit = nearest == float.MaxValue;
            return chosen;
        }

        private Entity SpawnActor(MoonRaidMap map, Entity prefab, Vector3 position, double now, ClashBotSpawnRequest? clash = null)
        {
            var player = EntityManager.Instantiate(prefab);
            var input = EntityManager.CreateEntity(typeof(PredictedClientInput));
            int id = clash.HasValue ? clash.Value.OwnerNetworkId : m_NextId--;
            int serial = ++m_SpawnSerial;
            var name = new FixedString64Bytes(clash.HasValue ? clash.Value.Name : "DollSinger " + serial.ToString("00"));
            float yaw = m_SpawnRandom.NextFloat(0, 360);
            EntityManager.SetComponentData(player, new GhostOwner { NetworkId = id });
            EntityManager.SetComponentData(player, LocalTransform.FromPositionRotation(position + Vector3.up * (clash.HasValue ? 0 : .1f),
                quaternion.RotateY(math.radians(yaw))));
            int ammo = WeaponManager.Instance.WeaponRegistry.GetWeaponData(2)?.MagazineSize ?? 30;
            var healthState = new PredictedPlayerGhost { MaxHealth = 100, CurrentHealth = 100,
                EquippedWeaponID = DollSingerWeapons.None, CurrentAmmo = 0, InventoryWeapons = true,
                PrimaryWeaponID = DollSingerWeapons.None, SecondaryWeaponID = DollSingerWeapons.None, PistolWeaponID = DollSingerWeapons.None };
            if (clash.HasValue)
            {
                healthState.ClashEnabled = true;healthState.ClashTeam = clash.Value.TeamId;
                healthState.ClashLife = ClashLife.Waiting;healthState.ClashCanAct = false;
                healthState.ClashMaxHealthFraction = 1;
            }
            RaidHealth.Initialize(ref healthState);
            if (clash.HasValue) healthState.CurrentHealth = 0;
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
            if (!clash.HasValue && PmcProtectionLoadout.TryEquip(inventory, false,
                    TutorialRuntime.Enabled(EntityManager), m_SpawnRandom.NextFloat()))
            {
                PmcProtectionLoadout.Initialize(ref healthState, inventory);
                EntityManager.SetComponentData(player, healthState);
            }
            Vector3 raidExit = SelectRaidExit(map, position, out bool localExit);
            var brain = new DollSingerEnemyBrain { Inventory = inventory, RaidExit = raidExit, LocalRaidExit = localExit,
                Seed = serial, Yaw = yaw, Goal = position, ProgressPosition = position, SpawnTime = now,
                NextSense = now + (serial % 3) * .033, NextDecision = now + (serial % 3) * .067,
                Aggression = .4f + ((serial - 1) % 3) * .2f, Caution = .8f - ((serial - 1) % 3) * .2f,
                Random = new Unity.Mathematics.Random(m_SpawnRandom.NextUInt(1, uint.MaxValue)) };
            if (clash.HasValue)
            {
                var request = clash.Value;
                brain.ClashParticipantId = request.ParticipantId;brain.ClashTeamId = request.TeamId;
                brain.ClashEpoch = request.Epoch;brain.ClashSlot = request.Slot;
                brain.Action = PmcAction.ClashObjective;brain.Reason = "approach the public bounty objective with squad";
                brain.Seed = request.ParticipantId;
                EntityManager.AddComponentData(player, new ClashBot { ParticipantId = request.ParticipantId,
                    TeamId = request.TeamId, Slot = request.Slot, Epoch = request.Epoch });
                EntityManager.AddComponentData(player, new ClashPlayer { ParticipantId = request.ParticipantId,
                    TeamId = request.TeamId, Epoch = request.Epoch, CanAct = false });
            }
            EntityManager.AddComponentObject(player, brain);
            LeaderboardManager.AddPlayer(id, name);
            return player;
        }
    }
}
