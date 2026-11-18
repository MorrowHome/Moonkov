// 为玩家生成自己带进远征的 AI 队友。根据连接对应的战局请求和可用名额，在玩家附近找可达位置，登记队伍和关联玩家。
// 战局会记已生成的数量，队友阵亡后不会把配额刷新出来。
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS {
    public partial class DollSingerEnemySystem {
        private const int MaxRaidCompanions = 12;
        private readonly List<Entity> m_CompanionDeployments = new();
        private double m_NextCompanionSpawn;

        // 请求队友数量来自服务器接受的战局会话。按活跃的真实玩家和剩余上限生成。
        private void UpdateCompanionPopulation(MoonRaidMap map, double now) {
            if (!m_PopulationStarted || now < m_NextCompanionSpawn) return;
            m_NextCompanionSpawn = now + 1;
            m_CompanionDeployments.Clear();
            foreach (var (raid, connection) in SystemAPI.Query<RefRO<RaidSession>>()
                         .WithAll<JoinedClient, NetworkStreamInGame>().WithEntityAccess()) {
                var session = raid.ValueRO;
                if (session.Phase == RaidPhase.Active && session.RequestedCompanions > session.SpawnedCompanions &&
                    (session.CompanionSpawnUntil == 0 || now < session.CompanionSpawnUntil))
                    m_CompanionDeployments.Add(connection);
            }
            if (m_CompanionDeployments.Count == 0 || WeaponManager.Instance?.WeaponRegistry == null) return;
            var prefabs = SystemAPI.GetSingleton<PlayerEntityPrefabs>();
            bool lanCoop = SystemAPI.HasSingleton<LanRaidMode>();
            CollectSpawnObservers(out _, out int companions);
            int seats = MaxRaidCompanions - companions;
            UnityEngine.Physics.SyncTransforms();
            foreach (var connection in m_CompanionDeployments) {
                var session = EntityManager.GetComponentData<RaidSession>(connection);
                var owner = EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity;
                if (!EntityManager.HasComponent<LocalTransform>(owner) ||
                    !EntityManager.HasComponent<PredictedPlayerGhost>(owner))
                    continue;
                var health = EntityManager.GetComponentData<PredictedPlayerGhost>(owner);
                if (health.CurrentHealth <= 0 || health.ClashEnabled) continue;
                if (session.CompanionSpawnUntil == 0) session.CompanionSpawnUntil = now + 12;
                Vector3 origin = EntityManager.GetComponentData<LocalTransform>(owner).Position;
                float yaw = health.ControllerState.YawDegrees;
                while (seats > 0 && session.SpawnedCompanions < session.RequestedCompanions &&
                       TryChooseCompanionSpawn(origin, yaw, session.SpawnedCompanions, out var point) &&
                       m_Characters.TryChoose(prefabs, ref m_SpawnRandom, out var prefab, out int character)) {
                    // 主人首次带队友时分配队伍 ID，并同时写在玩家和队友状态中，后面才能识别友军。
                    if (session.ExpeditionSquadId == 0) {
                        session.ExpeditionSquadId = ++m_NextExpeditionSquadId;
                        var membership = new ExpeditionSquadMember
                            { SquadId = session.ExpeditionSquadId, LanCoop = lanCoop };
                        if (EntityManager.HasComponent<ExpeditionSquadMember>(owner))
                            EntityManager.SetComponentData(owner, membership);
                        else
                            EntityManager.AddComponentData(owner, membership);
                        health.CompanionSquadId = session.ExpeditionSquadId;
                        EntityManager.SetComponentData(owner, health);
                    }
                    var actor = SpawnActor(map, prefab, point, now, character);
                    SetExpeditionSquad(actor, session.ExpeditionSquadId, session.SpawnedCompanions,
                        session.RequestedCompanions, owner, lanCoop);
                    var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(actor);
                    brain.CompanionConnection = connection;
                    brain.Yaw = yaw;
                    var companion = EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
                    companion.CompanionSquadId = session.ExpeditionSquadId;
                    EntityManager.SetComponentData(actor, companion);
                    m_SpawnObservers.Add(new RaidSpawnObserver { Position = point });
                    // 消耗的是本局已生成名额，不是当前还活着的个数。死亡或脱离不会把名额还回去。
                    session.SpawnedCompanions++;
                    seats--;
                }
                EntityManager.SetComponentData(connection, session);
            }
        }

        // 在主人后侧试不同位置，要求脚底有导航、主人走得到、附近也未被别的角色占用。
        private bool TryChooseCompanionSpawn(Vector3 origin, float yaw, int slot, out Vector3 position) {
            position = default;
            if (!NavMesh.SamplePosition(origin, out var from, 3, m_Filter) ||
                Mathf.Abs(from.position.y - origin.y) > 1.2f)
                return false;
            float angle = yaw + (slot == 0 ? 140 : slot == 1 ? 220 : 180);
            for (int i = 0; i < 16; i++) {
                float radius = slot == 2 || i >= 8 ? 7 : 4.5f;
                var candidate = origin + Quaternion.Euler(0, angle + i % 8 * 45, 0) * Vector3.forward * radius;
                if (!TryGroundedSpawn(candidate, out var point) || !ShortSpawnRoute(from.position, point, 16)) continue;
                bool occupied = false;
                foreach (var observer in m_SpawnObservers)
                    if ((observer.Position - point).sqrMagnitude < 2.5f * 2.5f) {
                        occupied = true;
                        break;
                    }
                if (occupied) continue;
                position = point;
                return true;
            }
            return false;
        }

        // 持续验证主人连接的当前角色和战局。主人退出后不留这批随行对象在局里活动。
        private bool CompanionOwnerActive(DollSingerEnemyBrain brain) {
            var connection = brain.CompanionConnection;
            if (!EntityManager.HasComponent<RaidSession>(connection) ||
                !EntityManager.HasComponent<PredictedPlayerGhost>(brain.SquadLeader))
                return false;
            var session = EntityManager.GetComponentData<RaidSession>(connection);
            return session.Phase == RaidPhase.Active && session.ExpeditionSquadId == brain.ExpeditionSquadId &&
                   EntityManager.GetComponentData<PredictedPlayerGhost>(brain.SquadLeader).CurrentHealth > 0;
        }
    }
}