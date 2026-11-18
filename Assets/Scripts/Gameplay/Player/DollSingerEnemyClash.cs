// 悬赏模式下把机器人接入对局。在服务器接受生成请求、更新阵营状态，并让可行动的机器人使用共用的感知和输入流程。
// 这一部分还负责队友线索共享、场地边界和射击通道检查。
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS {
    public enum ClashBotGoal {
        Target,
        GroundBounty,
        Rescue,
        Escort,
        Extract,
        Hold
    }

    public partial class DollSingerEnemySystem {
        private struct ClashAgent {
            public Entity Entity;
            public DollSingerEnemyBrain Brain;
            public Vector3 Position;
            public ClashBotView View;
        }

        private ClashBotRegistry m_ClashRegistry;
        private ClashArenaDefinition m_ClashArena;
        private int m_ClashEpoch;
        private bool m_ClashNavChecked, m_ClashNavReady;
        private readonly NavMeshPath m_ClashRoute = new();
        private readonly Vector3[] m_ClashRouteCorners = new Vector3[64];
        private readonly List<ClashAgent> m_ClashAgents = new(6);

        // 同一局的模式状态来自注册表。对局或回合变化时清掉之前的导航、队友和任务状态。
        private void UpdateClashBots(MoonRaidMap map, ClashBotRegistry registry, double now) {
            if (!ReferenceEquals(m_Map, map) || !ReferenceEquals(m_ClashRegistry, registry)) {
                DestroyEnemies(); // Excludes registered Clash actors: the server owns their lifetime.
                ReleaseNavigation();
                ResetPopulation();
                m_Map = map;
                m_Tuning = map.EnemyTuning ? map.EnemyTuning : m_DefaultTuning;
                m_ClashRegistry = registry;
                m_ClashArena = BountyClashArenaSettings.Active?.Definition;
                m_ClashNavChecked = m_ClashNavReady = false;
                m_ClashEpoch = 0;
            }
            if (m_ClashEpoch != registry.Epoch) {
                m_ClashEpoch = registry.Epoch;
                m_ClashAgents.Clear();
                m_ClashSquads.Clear();
                m_CoverReservations.Clear();
                m_Tracks.Clear();
                m_Events.Clear();
                m_Targets.Clear();
                m_NextTargets = 0;
                m_ClashNavChecked = m_ClashNavReady = false;
                m_SpawnRandom = new Mathematics.Random(unchecked((uint)Math.Max(1, registry.Epoch * 977 + 17)));
                m_Characters.Reset();
            }
            if (m_ClashArena == null) m_ClashArena = BountyClashArenaSettings.Active?.Definition;
            if (m_ClashArena == null) {
                registry.SetNavigation(false, "等待速战场地加载…");
                return;
            }
            if (!NavigationReady(map, now)) {
                registry.SetNavigation(false, "正在生成 AI 导航…");
                return;
            }
            if (!m_ClashNavChecked) {
                m_ClashNavChecked = true;
                m_ClashNavReady = ValidateClashNavigation(out string failure);
                registry.SetNavigation(m_ClashNavReady, m_ClashNavReady ? null : "AI 路线不可达，请重新进入地图。", !m_ClashNavReady);
                if (!m_ClashNavReady) Debug.LogError("[Clash AI] Navigation rejected: " + failure);
            }
            if (!m_ClashNavReady) return;
            registry.SetNavigation(true);
            SpawnClashRequests(map, registry, now);
            if (now >= m_NextTargets) {
                CollectTargets(now);
                m_NextTargets = now + .1;
            }
            m_ClashAgents.Clear();
            foreach (var (tag, pose, entity) in SystemAPI.Query<RefRO<ClashBot>, RefRO<LocalTransform>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithEntityAccess()) {
                if (tag.ValueRO.Epoch != registry.Epoch ||
                    !registry.TryGetView(tag.ValueRO.ParticipantId, out var view))
                    continue;
                m_ClashAgents.Add(new ClashAgent {
                    Entity = entity, Position = pose.ValueRO.Position,
                    Brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity), View = view
                });
            }
            ShareClashObservations(now);
            foreach (var agent in m_ClashAgents) {
                var input = EntityManager.GetComponentData<PlayerInputComponent>(agent.Entity);
                // 回合编号或存活状态已经变化的机器人不能留下上一拍输入。只推进仍在本回合可行动的对象。
                if (agent.View.Epoch != registry.Epoch || agent.View.Phase != ClashPhase.Active ||
                    agent.View.Life != ClashLife.Alive || !BountyClashRuntime.CanAct(EntityManager, agent.Entity)) {
                    input.Input = default;
                    agent.Brain.Interacting = false;
                    agent.Brain.HealUntil = 0;
                    EntityManager.SetComponentData(agent.Entity, input);
                    continue;
                }
                if (!EntityManager.HasComponent<GhostGameObjectLink>(agent.Entity)) continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(agent.Entity);
                var ghost = link.LinkedInstance ? link.LinkedInstance.GetComponent<PlayerGhost>() : null;
                if (!ghost || !ghost.ShotOrigin) continue;
                var health = EntityManager.GetComponentData<PredictedPlayerGhost>(agent.Entity);
                input.Input = Think(agent.Entity, agent.Brain, ghost,
                    EntityManager.GetComponentData<LocalTransform>(agent.Entity), ref health, map, now,
                    (float)SystemAPI.Time.DeltaTime);
                EntityManager.SetComponentData(agent.Entity, input);
                EntityManager.SetComponentData(agent.Entity, health);
            }
        }

        // 对局先给出合法席位和请求。创建后交注册表核对，没被采用的实体由本系统清理。
        private void SpawnClashRequests(MoonRaidMap map, ClashBotRegistry registry, double now) {
            if (registry.PendingSpawns.Count == 0) return;
            var prefabs = SystemAPI.GetSingleton<PlayerEntityPrefabs>();
            if (WeaponManager.Instance?.WeaponRegistry == null) return;
            foreach (var request in registry.PendingSpawns.ToArray()) {
                if (request.Epoch != registry.Epoch || registry.IsRegistered(request.ParticipantId)) continue;
                Vector3 point = request.Position;
                if (!InsideClashArena(point) ||
                    !RaidSpawnSafety.TryGroundPosition(point, .35f, 1.8f, 45, out var ground, out _) ||
                    Mathf.Abs(point.y - ground.y) > .6f) {
                    m_ClashNavReady = false;
                    registry.SetNavigation(false, "AI 出生点受阻，请重新进入地图。", true);
                    Debug.LogError("[Clash AI] Spawn clearance rejected for participant " + request.ParticipantId +
                                   " at " + point);
                    return;
                }
                if (!m_Characters.TryChoose(prefabs, ref m_SpawnRandom, out var prefab, out int characterIndex)) return;
                var entity = SpawnActor(map, prefab, point, now, characterIndex, request);
                var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                var input = EntityManager.GetComponentData<DollSingerEnemy>(entity).InputEntity;
                if (!registry.Register(request, entity, input, brain.Inventory)) {
                    // The registry never took ownership of this rejected creation.
                    RetireEnemy(entity, brain);
                    EntityManager.DestroyEntity(input);
                    EntityManager.DestroyEntity(entity);
                }
            }
        }

        // 先验证起点、目标和撤离之间的路线。如果地图配置不可达，尽早报告并停掉本局机器人的选路。
        private bool ValidateClashNavigation(out string failure) {
            failure = null;
            var target = BountyClashArenaSettings.Position(m_ClashArena.Target);
            if (!NavMesh.SamplePosition(target, out var objective, 2, m_Filter) ||
                Mathf.Abs(objective.position.y - target.y) > 1.2f) {
                failure = "objective_not_on_compound_navigation";
                return false;
            }
            for (int i = 0; i < m_ClashArena.Spawns.Length; i++) {
                var spawn = BountyClashArenaSettings.Position(m_ClashArena.Spawns[i]);
                if (!RaidSpawnSafety.TryGroundPosition(spawn, .35f, 1.8f, 45, out _, out _) ||
                    !ValidatedClashRoute(spawn, objective.position)) {
                    failure = "spawn_" + i + "_cannot_reach_objective_inside_arena";
                    return false;
                }
            }
            foreach (var exit in m_ClashArena.Exits)
                if (ValidatedClashRoute(objective.position, BountyClashArenaSettings.Position(exit)))
                    return true;
            failure = "objective_cannot_reach_either_exit_inside_arena";
            return false;
        }

        private bool ValidatedClashRoute(Vector3 start, Vector3 end) {
            if (!NavMesh.SamplePosition(start, out var from, 2, m_Filter) ||
                !NavMesh.SamplePosition(end, out var to, 2, m_Filter) || Mathf.Abs(from.position.y - start.y) > 1.2f ||
                Mathf.Abs(to.position.y - end.y) > 1.2f ||
                !NavMesh.CalculatePath(from.position, to.position, m_Filter, m_ClashRoute) ||
                m_ClashRoute.status != NavMeshPathStatus.PathComplete)
                return false;
            int count = m_ClashRoute.GetCornersNonAlloc(m_ClashRouteCorners);
            return count > 0 && count < m_ClashRouteCorners.Length && SafeClashRoute(m_ClashRouteCorners, count);
        }

        private bool InsideClashArena(Vector3 point, float margin = .5f) {
            if (m_ClashArena == null) return false;
            float radius = m_ClashArena.Radius - margin;
            return ClashPoint.HorizontalSquared(new ClashPoint(point.x, point.y, point.z), m_ClashArena.Center) <=
                   radius * (double)radius;
        }

        // 检查路径拐点都留在圆形场地内。角色已经出界时只允许第一段朝圈里走。
        private bool SafeClashRoute(Vector3[] corners, int count) {
            // A convex circle also contains each segment between its inside corners.
            // If knocked outside, permit only a strictly inward first segment, never an excursion.
            double previous = double.MaxValue;
            bool entered = false;
            for (int i = 0; i < count; i++) {
                double distance = ClashPoint.HorizontalSquared(new ClashPoint(corners[i].x, corners[i].y, corners[i].z),
                    m_ClashArena.Center);
                if (InsideClashArena(corners[i]))
                    entered = true;
                else if (entered || (i > 0 && distance >= previous)) return false;
                previous = distance;
            }
            return entered;
        }

        // 队伍关系先于视线查询生效。同队成员用来协调，不会被当作敌方瞄准目标。
        private bool CanObserveOpponent(DollSingerEnemyBrain brain, Entity candidate) {
            if (!brain.IsClashBot)
                return !ExpeditionSquadMember.BelongsTo(EntityManager, candidate, brain.ExpeditionSquadId,
                    brain.LanCompanion);
            if (!EntityManager.Exists(candidate) || !EntityManager.HasComponent<ClashPlayer>(candidate) ||
                !EntityManager.HasComponent<PredictedPlayerGhost>(candidate))
                return false;
            var identity = EntityManager.GetComponentData<ClashPlayer>(candidate);
            var health = EntityManager.GetComponentData<PredictedPlayerGhost>(candidate);
            return identity.Epoch == brain.ClashEpoch && identity.TeamId != brain.ClashTeamId &&
                   identity.ParticipantId != brain.ClashParticipantId && health.ClashLife == ClashLife.Alive;
        }

        private void ShareClashObservations(double now) {
            foreach (var receiver in m_ClashAgents) {
                if (receiver.View.Life != ClashLife.Alive || now - receiver.Brain.LastRadio < .5) continue;
                receiver.Brain.LastRadio = now;
                foreach (var sender in m_ClashAgents) {
                    if (sender.Entity == receiver.Entity || sender.View.Life != ClashLife.Alive ||
                        sender.View.TeamId != receiver.View.TeamId ||
                        Vector3.Distance(sender.Position, receiver.Position) > 60)
                        continue;
                    ShareSquadContacts(receiver.Brain, sender.Brain, now);
                }
            }
        }

        // 无线电报告保留原观察时间与置信度。对方真的没看到什么新情况，传来传去也不能刷成新目击。
        private void ShareSquadContacts(DollSingerEnemyBrain receiver, DollSingerEnemyBrain sender, double now) {
            foreach (var observation in sender.Contacts) {
                var source = observation.Value;
                // A report is a clue. Only a new observation can refresh it or grant sight.
                double observed = Math.Max(source.LastSeen, source.LastHeard);
                if (now - observed > 1.5 || source.LastReported > observed ||
                    !CanObserveOpponent(receiver, observation.Key))
                    continue;
                if (!receiver.Contacts.TryGetValue(observation.Key, out var contact)) {
                    contact = new PmcContact();
                    receiver.Contacts.Add(observation.Key, contact);
                }
                if (contact.Visible ||
                    observed <= Math.Max(contact.LastReported, Math.Max(contact.LastSeen, contact.LastHeard)))
                    continue;
                float angle = receiver.Random.NextFloat(0, Mathf.PI * 2);
                contact.Position = source.Position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 2;
                contact.Velocity = source.Velocity;
                contact.PositionUncertainty = source.PositionUncertainty + 2;
                contact.AimOffset = source.AimOffset;
                contact.Confirmed |= source.Confirmed;
                contact.Confidence = Mathf.Max(contact.Confidence, Mathf.Min(.55f, source.Confidence));
                contact.LastReported = observed;
                receiver.NextSense = 0;
            }
        }

        // 两名机器人在有限时段轮流掩护，避免同一时间一起放弃火线去绕侧。
        private bool HasClashCoveringAlly(DollSingerEnemyBrain brain, double now) {
            // Alternate roles in bounded windows; both actors must not leave cover together.
            int coveringSlot = ((int)(now / 6) + brain.ClashTeamId) % 2;
            if (brain.ClashSlot == coveringSlot || now < brain.NextFlank) return false;
            foreach (var ally in m_ClashAgents)
                if (ally.Brain != brain && ally.View.TeamId == brain.ClashTeamId && ally.View.Life == ClashLife.Alive &&
                    ally.Brain.ClashSlot == coveringSlot && IsCoveringAlly(ally.Entity, ally.Brain, brain, now))
                    return true;
            return false;
        }

        private bool IsCoveringAlly(Entity actor, DollSingerEnemyBrain ally, DollSingerEnemyBrain brain, double now) {
            if (!EntityManager.HasComponent<PredictedPlayerGhost>(actor) || !ally.Visible ||
                ally.Target != brain.Target || now - ally.LastSeenTime > .3 || now - ally.LastDamage < 1 ||
                ally.HealUntil > 0 || now - ally.VisibleSince < m_Tuning.ReactionSeconds)
                return false;
            var health = EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
            bool shootingPosture = ally.Action == PmcAction.Engage || (ally.Action == PmcAction.Cover &&
                                                                       ally.CoverPhase == PmcCoverPhase.Holding);
            return shootingPosture && health.CurrentHealth > 0 && health.CurrentAmmo > 0 &&
                   !health.ControllerState.IsReloadingState;
        }

        // 既检查理想目标点的线，也检查受瞄准误差影响的实际开火方向。目标看得见不代表枪口没被卡住。
        private bool ClashShotLaneClear(DollSingerEnemyBrain brain, Vector3 eye, Vector3 direction, float range) {
            // Check the actual imperfect shot ray as well as the ideal target visibility ray.
            int count = UnityEngine.Physics.SphereCastNonAlloc(eye, .09f, direction, m_Hits, range, s_SightMask,
                QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length) return false;
            foreach (var ally in m_ClashAgents) {
                if (ally.Brain == brain || ally.View.TeamId != brain.ClashTeamId ||
                    !EntityManager.Exists(ally.Entity) || !EntityManager.HasComponent<GhostGameObjectLink>(ally.Entity))
                    continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(ally.Entity);
                if (!link.LinkedInstance) continue;
                for (int i = 0; i < count; i++)
                    if (m_Hits[i].transform.IsChildOf(link.LinkedInstance.transform))
                        return false;
            }
            return true;
        }
    }
}