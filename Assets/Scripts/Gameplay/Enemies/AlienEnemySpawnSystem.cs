using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AlienEnemySpawnSystem : SystemBase
    {
        private AlienEncounterSettings m_Settings;
        private readonly Dictionary<int, Entity> m_Spawned = new Dictionary<int, Entity>();
        private readonly List<Vector3> m_Humans = new List<Vector3>();
        private double m_NextSpawn;
        private int m_NextId = -10000;
        public int SpawnedCount => m_Spawned.Count;
        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>(); RequireForUpdate<RaidLootWorld>();
        }
        protected override void OnUpdate()
        {
            var settings = AlienEncounterSettings.Active;
            if (!ReferenceEquals(settings, m_Settings))
            {
                Retire(); m_Settings = settings; m_NextSpawn = 0;
            }
            if (!settings || !settings.Enabled || settings.Count == 0) { Retire(); return; }
            if (SystemAPI.Time.ElapsedTime < m_NextSpawn) return;
            m_NextSpawn = SystemAPI.Time.ElapsedTime + 1;
            m_Humans.Clear();
            foreach (var (pose, entity) in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PredictedPlayerGhost, GhostGameObjectLink>().WithEntityAccess())
                if (ProceduralAlienEnemy.IsLivingHumanoid(EntityManager, entity)) m_Humans.Add(pose.ValueRO.Position);
            if (m_Humans.Count == 0 || !GhostEntityPrefabSystem.TryGetServerInstance(out var prefabs) || !prefabs.PrefabsLoaded) return;
            UnityEngine.Physics.SyncTransforms();
            for (int i = 0; i < settings.Count; i++)
            {
                if (m_Spawned.ContainsKey(i)) continue;
                var spawn = settings[i]; if (!spawn.Profile || spawn.Prefab == null) continue;
                var prefab = prefabs.GetEntityPrefab(spawn.Prefab.GhostGuid); if (prefab == Entity.Null) continue;
                bool tooClose = false;
                foreach (var point in m_Humans) if (Vector3.Distance(point, spawn.Position) < settings.MinimumPlayerSpawnDistance) { tooClose = true; break; }
                if (tooClose) continue;
                float scale = Mathf.Clamp(spawn.Profile.Scale, .1f, 4);
                if (!UnityEngine.Physics.Raycast(spawn.Position + Vector3.up * 2, Vector3.down, out var floor, 6,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                Vector3 position = floor.point + floor.normal * SpiderLab.SurfaceMotor.Height * scale;
                if (UnityEngine.Physics.CheckSphere(position, SpiderLab.SurfaceMotor.BodyRadius * scale,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) continue;
                var entity = EntityManager.Instantiate(prefab); int id = m_NextId--;
                EntityManager.SetComponentData(entity, LocalTransform.FromPositionRotation(position,
                    Quaternion.LookRotation(Quaternion.Euler(0, spawn.Yaw, 0) * Vector3.forward, floor.normal)));
                EntityManager.SetComponentData(entity, new GhostOwner { NetworkId = id });
                EntityManager.SetComponentData(entity, new GhostGameObjectGuid { Guid = GhostGameObject.GenerateRandomHash() });
                EntityManager.SetComponentData(entity, new ProceduralAlienEnemy.State { Scale = scale });
                m_Spawned.Add(i, entity);
                LeaderboardManager.AddPlayer(id, new FixedString64Bytes("Xeno " + spawn.Profile.Species));
            }
        }
        private void Retire()
        {
            foreach (var entity in m_Spawned.Values)
                if (EntityManager.Exists(entity))
                {
                    if (EntityManager.HasComponent<GhostOwner>(entity)) LeaderboardManager.RetireRaidActor(EntityManager.GetComponentData<GhostOwner>(entity).NetworkId);
                    EntityManager.DestroyEntity(entity);
                }
            m_Spawned.Clear();
        }
    }
}
