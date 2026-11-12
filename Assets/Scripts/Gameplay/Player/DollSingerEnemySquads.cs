using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public struct ExpeditionSquadMember : IComponentData
    {
        public int SquadId;

        public static bool BelongsTo(EntityManager manager, Entity actor, int squad) =>
            squad != 0 && manager.HasComponent<ExpeditionSquadMember>(actor) &&
            manager.GetComponentData<ExpeditionSquadMember>(actor).SquadId == squad;
    }

    public partial class DollSingerEnemySystem
    {
        private struct ExpeditionAgent
        {
            public Entity Entity;
            public int SquadId;
            public DollSingerEnemyBrain Brain;
            public Vector3 Position;
            public float Yaw;
            public Transform Root;
        }

        private readonly List<ExpeditionAgent> m_ExpeditionAgents = new(12);
        private int m_NextExpeditionSquadId;

        private void SetExpeditionSquad(Entity actor, int squad, int slot, int size, Entity leader)
        {
            var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(actor);
            brain.ExpeditionSquadId = squad;
            brain.SquadSlot = slot;
            brain.SquadSize = size;
            brain.SquadLeader = leader;
            if (leader != Entity.Null)
            {
                brain.Action = PmcAction.FollowSquad;
                brain.TacticalGoal = brain.Goal;
            }
            EntityManager.AddComponentData(actor, new ExpeditionSquadMember { SquadId = squad });
        }

        private void UpdateExpeditionSquads(double now)
        {
            m_ExpeditionAgents.Clear();
            foreach (var (squad, pose, health, entity) in SystemAPI.Query<RefRO<ExpeditionSquadMember>,
                         RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>>().WithAll<Simulate>().WithEntityAccess())
            {
                if (health.ValueRO.CurrentHealth <= 0) continue;
                var brain = EntityManager.HasComponent<DollSingerEnemyBrain>(entity)
                    ? EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity) : null;
                var instance = EntityManager.HasComponent<GhostGameObjectLink>(entity)
                    ? EntityManager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance : null;
                m_ExpeditionAgents.Add(new ExpeditionAgent { Entity = entity, SquadId = squad.ValueRO.SquadId,
                    Brain = brain, Position = pose.ValueRO.Position, Yaw = brain?.Yaw ?? health.ValueRO.ControllerState.YawDegrees,
                    Root = instance ? instance.transform : null });
            }
            foreach (var receiver in m_ExpeditionAgents)
            {
                var brain = receiver.Brain;
                if (brain == null) continue;
                brain.HasSquadLeader = false;
                brain.SquadPartnerNearby = false;
                bool share = now - brain.LastRadio >= .5;
                if (share) brain.LastRadio = now;
                foreach (var ally in m_ExpeditionAgents)
                {
                    if (ally.Entity == receiver.Entity || ally.SquadId != receiver.SquadId) continue;
                    if (ally.Entity == brain.SquadLeader)
                    {
                        brain.HasSquadLeader = true;
                        brain.SquadLeaderPosition = ally.Position;
                        brain.SquadLeaderYaw = ally.Yaw;
                        if (ally.Brain is { Leaving: true } leader && !brain.Leaving)
                        {
                            brain.Leaving = true;
                            brain.RaidExit = leader.RaidExit;
                            brain.LocalRaidExit = leader.LocalRaidExit;
                            brain.CommitUntil = brain.NextDecision = 0;
                        }
                    }
                    if (ally.Brain == null || (ally.Position - receiver.Position).sqrMagnitude > 60 * 60) continue;
                    brain.SquadPartnerNearby = true;
                    if (share) ShareSquadContacts(brain, ally.Brain, now);
                }
            }
        }

        private bool TryGetExpeditionCoveringAlly(DollSingerEnemyBrain brain, Vector3 position, double now,
            out Vector3 coveringPosition)
        {
            coveringPosition = default;
            if (!brain.SquadPartnerNearby) return false;
            int coveringSlot = ((int)(now / 6) + brain.ExpeditionSquadId) % brain.SquadSize;
            if (brain.SquadSlot == coveringSlot) return false;
            foreach (var ally in m_ExpeditionAgents)
            {
                if (ally.SquadId != brain.ExpeditionSquadId || ally.Brain == null || ally.Brain.SquadSlot != coveringSlot ||
                    (ally.Position - position).sqrMagnitude > 45 * 45 || !IsCoveringAlly(ally.Entity, ally.Brain, brain, now))
                    continue;
                coveringPosition = ally.Position;
                return true;
            }
            return false;
        }

        private bool ExpeditionShotLaneClear(DollSingerEnemyBrain brain, Transform root, Vector3 eye, Vector3 direction, float range)
        {
            int count = root.gameObject.scene.GetPhysicsScene().SphereCast(eye, .09f, direction, m_Hits, range,
                s_SightMask, QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length) return false;
            for (int i = 0; i < count; i++)
                foreach (var ally in m_ExpeditionAgents)
                    if (ally.SquadId == brain.ExpeditionSquadId && ally.Root && ally.Root != root &&
                        m_Hits[i].transform.IsChildOf(ally.Root)) return false;
            return true;
        }

        private static float SquadWatchYaw(DollSingerEnemyBrain brain) =>
            brain.SquadLeaderYaw + (brain.SquadSlot % 2 == 0 ? 80 : -80);

        private Vector3 FollowSquadGoal(DollSingerEnemyBrain brain, Vector3 position, double now)
        {
            if (!brain.HasSquadLeader) return position;
            if (now < brain.NextCover) return brain.TacticalGoal;
            brain.NextCover = now + .8;
            var leader = brain.SquadLeaderPosition;
            var facing = Quaternion.Euler(0, brain.SquadLeaderYaw, 0);
            float side = brain.SquadSlot % 2 == 0 ? 1 : -1;
            Vector3 formation = new Vector3(side * 4, 0, -4);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                bool useLeaderPosition = attempt == 3;
                if (useLeaderPosition && Vector3.Distance(position, leader) < 3) return position;
                Vector3 offset = attempt switch
                {
                    0 => formation,
                    1 => new Vector3(-side * 3, 0, -3),
                    2 => new Vector3(0, 0, -3),
                    _ => Vector3.zero
                };
                Vector3 desired = leader + facing * offset;
                if (NavMesh.SamplePosition(desired, out var sample, 2, m_Filter) &&
                    Mathf.Abs(sample.position.y - leader.y) < 1.2f &&
                    (useLeaderPosition || HorizontalDistance(sample.position, leader) >= 2.5f) &&
                    CompletePath(brain, position, sample.position, out _))
                    return Vector3.Distance(position, sample.position) < 1.5f ? position : sample.position;
            }
            return position;
        }
    }
}
