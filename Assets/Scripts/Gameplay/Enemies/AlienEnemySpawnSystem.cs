// 服务端管理本局异形的生成、死亡和补刷。地图配置提供候选点，系统按冷却、同屏可见性和体型空间检查选择位置。
// 生成后每只异形的追猎与攻击由 ProceduralAlienEnemy 驱动。
using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.MP_FPS.SpiderLab;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AlienEnemySpawnSystem : SystemBase {
        private sealed class EncounterState {
            public Entity Entity;
            public int OwnerId;
            public int SpawnCount;
            public int CandidateCursor;
            public double ReadyAt;
            public bool DeathObserved;
        }

        private AlienEncounterSettings m_Settings;
        private EntityQuery m_TutorialModeQuery;
        private readonly List<EncounterState> m_Encounters = new();
        private readonly List<RaidSpawnObserver> m_Observers = new();
        private System.Random m_Random;
        private double m_NextCheck;
        private int m_NextEncounter;
        private int m_NextId = -10000;
        private int m_AliveCount;
        private int m_AliveBossCount;
        private bool m_ClashSuppressed;
        private int m_TotalSpawned;
        public int SpawnedCount => m_AliveCount;
        public int TotalSpawnedCount => m_TotalSpawned;

        protected override void OnCreate() {
            m_TutorialModeQuery = GetEntityQuery(typeof(TutorialRaidMode));
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<RaidLootWorld>();
        }

        protected override void OnUpdate() {
            // 悬赏状态与常规远征的异形刷新隔开。模式启用时清掉之前的遭遇，避免留一批先前地图的怪。
            if (BountyClashRuntime.Enabled(EntityManager)) {
                if (!m_ClashSuppressed) {
                    ResetEncounters();
                    m_Settings = null;
                    m_ClashSuppressed = true;
                }
                return;
            }
            m_ClashSuppressed = false;
            if (!m_TutorialModeQuery.IsEmptyIgnoreFilter) return;
            var settings = AlienEncounterSettings.Active;
            double now = SystemAPI.Time.ElapsedTime;
            // 换地图场景后活动配置对象会变。重置本局槽位、预算与起始轮询位置。
            if (!ReferenceEquals(settings, m_Settings)) {
                ResetEncounters();
                m_Settings = settings;
                m_Random = new System.Random(System.Environment.TickCount);
                if (settings) m_NextEncounter = m_Random.Next(Mathf.Max(1, settings.Count));
            }
            if (!settings) return;
            // 人口和候选点不用每帧查。按间隔检查一次，地图几何查询的开销更稳定。
            if (now < m_NextCheck) return;
            m_NextCheck = now + 1;
            SynchronizeEncounters(settings, now);
            RefreshPopulation(settings, now);
            if (!settings.Enabled || settings.MaximumAliens == 0) {
                RetireActors();
                return;
            }
            if (settings.Count == 0 || m_AliveCount >= settings.MaximumAliens ||
                m_TotalSpawned >= settings.MaximumTotalSpawns)
                return;
            m_Observers.Clear();
            bool hasPlayer = false;
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>>()
                         .WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0) continue;
                // 场上角色都参与占位判断，只有真实玩家的方向参与出生可见性判断。
                bool player = EntityManager.HasComponent<GhostOwner>(entity) &&
                              EntityManager.GetComponentData<GhostOwner>(entity).NetworkId > 0 &&
                              !EntityManager.HasComponent<DollSingerEnemy>(entity) &&
                              ProceduralAlienEnemy.IsLivingHumanoid(EntityManager, entity);
                hasPlayer |= player;
                var forward = Vector3.zero;
                if (player) {
                    var controller = health.ValueRO.ControllerState;
                    forward = Quaternion.Euler(controller.PitchDegrees + controller.FreeLookOffset.y,
                        controller.YawDegrees + controller.FreeLookOffset.x, 0) * Vector3.forward;
                }
                m_Observers.Add(new RaidSpawnObserver {
                    Position = pose.ValueRO.Position, Forward = forward, CheckVisibility = player
                });
            }
            // 没有真实玩家或预制体还未装入当前世界就暂停生成。预制体缺失的那一拍不耗费刷新次数。
            if (!hasPlayer || !GhostEntityPrefabSystem.TryGetServerInstance(out var prefabs) || !prefabs.PrefabsLoaded)
                return;
            UnityEngine.Physics.SyncTransforms();
            int spawnedThisCheck = 0;
            int start = m_NextEncounter % settings.Count;
            for (int offset = 0; offset < settings.Count; offset++) {
                int index = (start + offset) % settings.Count;
                var state = m_Encounters[index];
                if (!CanSpawnEncounter(settings, state, now)) continue;
                var spawn = settings[index];
                // 这次槽位的位置找不到或资源没好，延后下次尝试。只有确实创建了实体才扣预算。
                state.ReadyAt = now + settings.RetryDelay;
                if (spawn == null || !spawn.Profile || spawn.Prefab == null) continue;
                if (spawn.Profile.Boss &&
                    (m_AliveBossCount >= settings.MaximumBosses || HasSpawnedBoss(settings, spawn.Profile)))
                    continue;
                // 伏击与 Boss 槽一局只触发一次。普通怪死亡后仍可按槽位设置补刷。
                if ((spawn.Ambush || spawn.Profile.Boss) && state.SpawnCount > 0) continue;
                var prefab = prefabs.GetEntityPrefab(spawn.Prefab.GhostGuid);
                if (prefab == Entity.Null ||
                    !TrySelectPosition(settings, spawn, state, out var position, out var rotation))
                    continue;
                float scale = Mathf.Clamp(spawn.Profile.Scale, .1f, 4f);
                // 候选通过检查后才复制网络实体，设置姿势、无客户端所有者的编号和首次同步状态。
                var entity = EntityManager.Instantiate(prefab);
                int id = m_NextId--;
                EntityManager.SetComponentData(entity, LocalTransform.FromPositionRotation(position, rotation));
                EntityManager.SetComponentData(entity, new GhostOwner { NetworkId = id });
                EntityManager.SetComponentData(entity,
                    new GhostGameObjectGuid { Guid = GhostGameObject.GenerateRandomHash() });
                EntityManager.SetComponentData(entity, new ProceduralAlienEnemy.State {
                    Scale = scale, AmbushEnabled = spawn.Profile.IsCeilingSpider,
                    Action = spawn.Profile.IsCeilingSpider ? AlienAction.Ambush : AlienAction.Roam,
                    BossPhase = 1
                });
                state.Entity = entity;
                state.OwnerId = id;
                state.DeathObserved = false;
                state.SpawnCount++;
                m_TotalSpawned++;
                m_AliveCount++;
                if (spawn.Profile.Boss) m_AliveBossCount++;
                spawnedThisCheck++;
                m_NextEncounter = (index + 1) % settings.Count;
                // Ghost presentation is linked later; reserve this position immediately.
                // 新怪的模型和碰撞体还没链接，先把它的位置占上，防同一次检查又选中同一个点。
                m_Observers.Add(new RaidSpawnObserver { Position = position });
                LeaderboardManager.AddPlayer(id, new FixedString64Bytes(spawn.Profile.CreatureName));
                if (m_AliveCount >= settings.MaximumAliens || m_TotalSpawned >= settings.MaximumTotalSpawns ||
                    spawnedThisCheck >= settings.MaximumSpawnsPerCheck)
                    break;
            }
        }

        // 场上的存活上限和本局剩余刷新次数是两回事。槽位还要等各自的补刷时间。
        private bool CanSpawnEncounter(AlienEncounterSettings settings, EncounterState state, double now) {
            return settings.Enabled && m_AliveCount < settings.MaximumAliens &&
                   m_TotalSpawned < settings.MaximumTotalSpawns && state.Entity == Entity.Null &&
                   now >= state.ReadyAt && state.SpawnCount < settings.SpawnsPerEncounter;
        }

        private bool HasSpawnedBoss(AlienEncounterSettings settings, AlienCreatureProfile profile) {
            // Multiple authored anchors for the same boss are alternatives, not
            // extra lives. This survives death/retirement for the current raid.
            for (int i = 0; i < m_Encounters.Count; i++)
                if (m_Encounters[i].SpawnCount > 0 && settings[i].Profile == profile)
                    return true;
            return false;
        }

        private void SynchronizeEncounters(AlienEncounterSettings settings, double now) {
            while (m_Encounters.Count > settings.Count) {
                int last = m_Encounters.Count - 1;
                RetireActor(m_Encounters[last]);
                m_Encounters.RemoveAt(last);
            }
            while (m_Encounters.Count < settings.Count)
                m_Encounters.Add(new EncounterState {
                    ReadyAt = now + settings.InitialSpawnDelay,
                    CandidateCursor = m_Random.Next(0, 1024)
                });
        }

        // 观察角色是否刚死亡，开始计算槽位补刷时间。已死的旧实体仍交给它自己的表现流程收尾。
        private void RefreshPopulation(AlienEncounterSettings settings, double now) {
            m_AliveCount = 0;
            m_AliveBossCount = 0;
            for (int index = 0; index < m_Encounters.Count; index++) {
                var state = m_Encounters[index];
                if (state.Entity == Entity.Null) continue;
                bool hasHealth = EntityManager.Exists(state.Entity) &&
                                 EntityManager.HasComponent<PredictedPlayerGhost>(state.Entity);
                if (hasHealth && EntityManager.GetComponentData<PredictedPlayerGhost>(state.Entity).CurrentHealth > 0) {
                    m_AliveCount++;
                    if (settings[index].Profile && settings[index].Profile.Boss) m_AliveBossCount++;
                    continue;
                }
                // Death owns its existing two-second animation/despawn. Begin the slot's
                // cooldown once, even when GhostBridge retains a cleanup entity briefly.
                if (!state.DeathObserved) {
                    state.DeathObserved = true;
                    state.ReadyAt = now + settings.RespawnDelay + m_Random.NextDouble() * settings.RespawnDelayJitter;
                    if (state.OwnerId != 0) LeaderboardManager.RetireRaidActor(state.OwnerId);
                }
                // Keep ownership until the corpse exits, so unloading also cleans it.
                // The cooldown is also a bound if an unlinked ghost cannot animate death.
                if (!hasHealth || now >= state.ReadyAt) RetireActor(state);
            }
        }

        // 优先尝试附近已配置的出生点。限制现场物理检查次数，并在候选之间旋转起点。
        private bool TrySelectPosition(
            AlienEncounterSettings settings,
            AlienEncounterSettings.Spawn spawn,
            EncounterState state,
            out Vector3 position,
            out Quaternion rotation) {
            position = default;
            rotation = Quaternion.identity;
            float scale = Mathf.Clamp(spawn.Profile.Scale, .1f, 4f);
            float mantleRadius = spawn.Profile.IsChain
                ?
                Mathf.Max(SurfaceMotor.BodyRadius, spawn.Profile.Chain.radius)
                : spawn.Profile.Species == AlienSpecies.Spider || spawn.Profile.Species == AlienSpecies.FaceHugger
                    ? SurfaceMotor.BodyRadius
                    : Mathf.Max(SurfaceMotor.BodyRadius,
                        Mathf.Max(spawn.Profile.Branch.bodyRadii.x, spawn.Profile.Branch.bodyRadii.z) * .7f);
            float radius = spawn.Profile.IsUpright ? spawn.Profile.UprightRadius(scale) : mantleRadius * scale;
            float height = spawn.Profile.Boss || spawn.Profile.IsUpright
                ? spawn.Profile.GroundBodyHeight(scale)
                : Mathf.Max(radius * 2, (SurfaceMotor.Height + mantleRadius) * scale);
            int anchors = 1 + (spawn.CandidatePositions?.Length ?? 0);
            int nearbyAttempts = 0;
            // Scan distant anchors cheaply; only nearby physical checks consume the budget.
            for (int attempt = 0;
                 attempt < anchors + settings.CandidateAttempts && nearbyAttempts < settings.CandidateAttempts;
                 attempt++) {
                int cursor = state.CandidateCursor++;
                int anchor = cursor % anchors;
                var candidate = anchor == 0 ? spawn.Position : spawn.CandidatePositions[anchor - 1];
                if (!HasNearbyPlayer(candidate, settings.ActivationDistance)) continue;
                nearbyAttempts++;
                if (spawn.Profile.IsCeilingSpider) {
                    if (TryAmbushPosition(settings, spawn, scale, candidate, anchor > 0, out position, out rotation))
                        return true;
                    continue;
                }
                // Rotate a bounded search over all authored anchors; a new raid randomizes
                // its starting point without changing Unity's global random stream.
                int ringPoint = cursor / anchors % 9;
                if (ringPoint > 0) {
                    float angle = (ringPoint - 1) * 45f * Mathf.Deg2Rad;
                    float distance = settings.CandidateSearchRadius * (cursor / anchors / 9 % 2 == 0 ? .5f : 1f);
                    candidate += new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * distance;
                }
                if (!HasNearbyPlayer(candidate, settings.ActivationDistance) ||
                    !RaidSpawnSafety.TryGroundPosition(candidate, radius, height, settings.MaximumSpawnSlope,
                        out var ground, out var normal))
                    continue;
                var forward = Vector3.ProjectOnPlane(Quaternion.Euler(0, spawn.Yaw, 0) * Vector3.forward, normal)
                    .normalized;
                var bodyPosition = ground + normal * SurfaceMotor.Height * scale;
                if (!RaidSpawnSafety.IsSafeFromObservers(bodyPosition, m_Observers, settings.MinimumPlayerSpawnDistance,
                        settings.VisibilityDistance, Mathf.Max(3f, scale * 2f)) || !HasBodyClearance(spawn.Profile,
                        scale, ground, normal, forward, settings.MaximumSpawnSlope))
                    continue;
                if (spawn.Profile.IsUpright && !RaidSpawnSafety.IsSafeFromObservers(
                        ground + Vector3.up * (height - 1.5f), m_Observers, settings.MinimumPlayerSpawnDistance,
                        settings.VisibilityDistance, Mathf.Max(3f, scale * 2f)))
                    continue;
                if (spawn.Profile.Boss && !BossHiddenFromObservers(settings, spawn.Profile, ground, height, scale))
                    continue;
                position = bodyPosition;
                rotation = Quaternion.LookRotation(forward, normal);
                return true;
            }
            return false;
        }

        // Boss 身体很高，要检查观察者能否看见它的身体，而不只检查脚底那个坐标。
        private bool BossHiddenFromObservers(
            AlienEncounterSettings settings,
            AlienCreatureProfile profile,
            Vector3 ground,
            float height,
            float scale) {
            if (!RaidSpawnSafety.IsSafeFromObservers(ground + Vector3.up * (height - 1.5f), m_Observers,
                    settings.MinimumPlayerSpawnDistance, settings.VisibilityDistance, profile.Branch.reach * scale))
                return false;
            // Check the outer silhouette too: hiding the mantle behind a rock does
            // not make twenty metres of visible appendages safe to spawn.
            for (int side = 0; side < 4; side++) {
                var tip = ground + Quaternion.Euler(0, side * 90, 0) * Vector3.forward * (profile.Branch.reach * scale);
                if (!RaidSpawnSafety.IsSafeFromObservers(tip, m_Observers, settings.MinimumPlayerSpawnDistance,
                        settings.VisibilityDistance, 3))
                    return false;
            }
            return true;
        }

        // 贴顶生成点要能挂住、站位附近有人且不会直接暴露。角色还要按表面法线摆正。
        private bool TryAmbushPosition(
            AlienEncounterSettings settings,
            AlienEncounterSettings.Spawn spawn,
            float scale,
            Vector3 anchor,
            bool groundCandidate,
            out Vector3 position,
            out Quaternion rotation) {
            position = default;
            rotation = Quaternion.identity;
            var normal = groundCandidate ? Vector3.up :
                spawn.SurfaceNormal.sqrMagnitude > .5f ? spawn.SurfaceNormal.normalized : Vector3.up;
            if (groundCandidate && !RaidSpawnSafety.TryGroundPosition(anchor, SurfaceMotor.BodyRadius * scale,
                    SurfaceMotor.Height * scale * 2, settings.MaximumSpawnSlope, out anchor, out normal))
                return false;
            int mask = LayerMask.GetMask("Default", "Ground");
            if (!groundCandidate && !spawn.Ambush) {
                if (!UnityEngine.Physics.Raycast(anchor + Vector3.up, Vector3.up, out var ceiling, 14, mask,
                        QueryTriggerInteraction.Ignore) || ceiling.normal.y > -.65f)
                    return false;
                anchor = ceiling.point;
                normal = ceiling.normal;
            }
            if (!HasNearbyPlayer(anchor, settings.ActivationDistance) ||
                !UnityEngine.Physics.Raycast(anchor + normal * .5f, -normal, out var support, 1, mask,
                    QueryTriggerInteraction.Ignore) || Vector3.Dot(support.normal, normal) < .85f)
                return false;
            position = support.point + support.normal * SurfaceMotor.Height * scale;
            if (UnityEngine.Physics.CheckSphere(position, SurfaceMotor.BodyRadius * scale, mask,
                    QueryTriggerInteraction.Ignore))
                return false;
            foreach (var observer in m_Observers) {
                float distance = Vector3.Distance(position, observer.Position);
                if (distance <
                    (observer.CheckVisibility ? settings.MinimumPlayerSpawnDistance : Mathf.Max(3, scale * 2)))
                    return false;
                if (!observer.CheckVisibility || distance > settings.VisibilityDistance) continue;
                var eye = observer.Position + Vector3.up * 1.6f;
                if (observer.Forward.sqrMagnitude > .01f &&
                    Vector3.Dot(observer.Forward.normalized, (position - eye).normalized) < .5f)
                    continue;
                // Do not offset above a ceiling perch: that would hide the visibility
                // ray inside the ceiling and allow a visible pop-in underneath it.
                if (!UnityEngine.Physics.Linecast(eye, position, mask, QueryTriggerInteraction.Ignore)) return false;
            }
            // A perched spider needs real support underneath its initial leg footprint.
            var forward = Vector3.ProjectOnPlane(Quaternion.Euler(0, spawn.Yaw, 0) * Vector3.forward, normal);
            if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(Vector3.up, normal);
            forward.Normalize();
            var right = Vector3.Cross(normal, forward);
            for (int i = 0; i < 4; i++) {
                var foot = support.point + (i < 2 ? forward : right) * (i % 2 == 0 ? 1 : -1) * .65f * scale;
                if (!UnityEngine.Physics.Raycast(foot + normal * .35f, -normal, out var leg, .7f, mask,
                        QueryTriggerInteraction.Ignore) || Vector3.Dot(leg.normal, normal) < .75f)
                    return false;
            }
            rotation = Quaternion.LookRotation(forward, normal);
            return true;
        }

        private bool HasNearbyPlayer(Vector3 candidate, float distance) {
            float squared = distance * distance;
            foreach (var observer in m_Observers)
                if (observer.CheckVisibility && (observer.Position - candidate).sqrMagnitude <= squared)
                    return true;
            return false;
        }

        // 检查实际体型而非只查头部的小球。长身体还要沿将要出现的方向确认支撑和空间。
        private static bool HasBodyClearance(
            AlienCreatureProfile profile,
            float scale,
            Vector3 ground,
            Vector3 normal,
            Vector3 forward,
            float maxSlope) {
            if (profile.IsUpright)
                return RaidSpawnSafety.TryGroundPosition(ground, profile.UprightRadius(scale),
                           profile.UprightHeight(scale), maxSlope, out var uprightFloor, out _) &&
                       Mathf.Abs(uprightFloor.y - ground.y) < .15f;
            // A head-sized sphere alone admits long chains through walls or across the
            // shaft. Sample support and clearance along the initial body footprint.
            int samples = profile.IsChain ? 4 : 8;
            float extent = profile.IsChain ? profile.Chain.length * scale :
                profile.Species == AlienSpecies.Spider || profile.Species == AlienSpecies.FaceHugger ? 1.25f * scale :
                profile.Branch.reach * scale;
            float radius = profile.IsChain ? Mathf.Max(SurfaceMotor.BodyRadius, profile.Chain.radius) * scale :
                profile.Species == AlienSpecies.Spider || profile.Species == AlienSpecies.FaceHugger ? .12f * scale :
                profile.Branch.rootRadius * scale;
            float height = Mathf.Max(radius * 2f + .1f, (SurfaceMotor.Height + SurfaceMotor.BodyRadius) * scale);
            var right = Vector3.Cross(normal, forward);
            for (int i = 0; i < samples; i++) {
                float angle = i * Mathf.PI * 2f / samples;
                var offset = profile.IsChain
                    ? -forward * extent * (i + 1f) / samples
                    : (forward * Mathf.Cos(angle) + right * Mathf.Sin(angle)) * extent;
                var expected = ground + offset;
                if (!RaidSpawnSafety.TryGroundPosition(expected, radius, height, maxSlope, out var support, out _) ||
                    Mathf.Abs(Vector3.Dot(support - expected, normal)) > .6f * scale)
                    return false;
                // Check the tissue path above the support surface as well as each sample.
                if (UnityEngine.Physics.CheckCapsule(ground + normal * (height + radius) * .65f,
                        support + normal * (height + radius) * .65f, radius, LayerMask.GetMask("Default", "Ground"),
                        QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
        }

        private void RetireActor(EncounterState state) {
            if (state.OwnerId != 0) LeaderboardManager.RetireRaidActor(state.OwnerId);
            if (state.Entity != Entity.Null && EntityManager.Exists(state.Entity))
                EntityManager.DestroyEntity(state.Entity);
            state.Entity = Entity.Null;
            state.OwnerId = 0;
        }

        private void RetireActors() {
            foreach (var state in m_Encounters) RetireActor(state);
            m_AliveCount = m_AliveBossCount = 0;
        }

        private void ResetEncounters() {
            RetireActors();
            m_Encounters.Clear();
            m_Observers.Clear();
            m_TotalSpawned = 0;
            m_NextCheck = 0;
            m_NextEncounter = 0;
        }

        protected override void OnStopRunning() {
            // A temporarily empty dedicated server keeps the map's spent budget.
            // Only changing the authored settings/map starts a fresh population.
            RetireActors();
            m_NextCheck = 0;
        }

        protected override void OnDestroy() { RetireActors(); }
    }
}