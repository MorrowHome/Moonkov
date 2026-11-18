// 服务器每拍驱动人形 AI 的入口。地图换了就清理旧角色和导航，有可用路线后才维护人形敌人、队伍与玩家随从。
// Think 给活着的角色生成 PlayerInput，死亡或撤离则按不同结局清理。
using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    // 这些组件仅在服务器上存在。对客户端仍发送普通玩家 Ghost。
    public struct DollSingerEnemy : IComponentData {
        public Entity InputEntity;
        public int CharacterIndex;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateBefore(typeof(ServerPlayerMovementSystem))]
    public partial class DollSingerEnemySystem : SystemBase {
        private static readonly Profiling.ProfilerMarker s_DropProfile = new("Moonkov.Pmc.DropCorpse");

        private struct Target {
            public Entity Entity;
            public Vector3 Position;
            public Transform Root;
            public Vector3 Velocity;
            public Vector3 AimOffset;
            public Vector3 HeadOffset, HipOffset;
            public bool Alien, PlayerControlled;
        }

        private sealed class SensorTrack {
            public Vector3 Position;
            public float StepDistance;
            public uint Shot, Reload, Land, Loot, Consumable;
            public double SeenAt, NextCough;
        }

        private struct SensorEvent {
            public Entity Source;
            public Vector3 Position;
            public float Range;
            public bool Shot;
        }

        private readonly List<Target> m_Targets = new();
        private readonly Dictionary<Entity, SensorTrack> m_Tracks = new();
        private readonly List<Entity> m_ExpiredTracks = new();
        private readonly List<SensorEvent> m_Events = new();
        private readonly Dictionary<int, (Vector3 position, double expiry)> m_CoverReservations = new();
        private TacticalServerWorld m_TacticalWorld;
        private EnergyGrenadeServerSystem m_EnergyGrenades;
        private EntityQuery m_TacticalQuery;
        private DollSingerAITuning m_Tuning;
        private DollSingerAITuning m_DefaultTuning;
        private readonly RaycastHit[] m_Hits = new RaycastHit[32];
        private readonly List<NavMeshBuildSource> m_Sources = new();
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

        protected override void OnCreate() {
            m_TacticalQuery = GetEntityQuery(typeof(TacticalServerWorld));
            m_Tuning = Resources.Load<DollSingerAITuning>("Moonkov/DollSingerAI");
            if (m_Tuning == null) m_Tuning = ScriptableObject.CreateInstance<DollSingerAITuning>();
            m_DefaultTuning = m_Tuning;
            RequireForUpdate<PlayerEntityPrefabs>();
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<RaidLootWorld>();
        }

        protected override void OnDestroy() {
            DestroyEnemies();
            ReleaseNavigation();
            if (m_DefaultTuning != null && string.IsNullOrEmpty(m_DefaultTuning.name)) Object.Destroy(m_DefaultTuning);
        }

        protected override void OnStopRunning() {
            DestroyEnemies();
            ReleaseNavigation();
            m_Targets.Clear();
            m_Tracks.Clear();
            m_Events.Clear();
            m_CoverReservations.Clear();
            m_SpawnObservers.Clear();
            m_ExpeditionAgents.Clear();
            m_NextBuild = m_NextTargets = 0;
            m_ClashRegistry = null;
            m_ClashArena = null;
            m_ClashAgents.Clear();
            m_ClashSquads.Clear();
            m_ClashNavChecked = m_ClashNavReady = false;
            // 即使所有连接都断开，地图和 World 也可能继续存在。
            // 维护本局剩余预算和截止时间，重连不能平白再带出一批增援。
        }

        private void ReleaseNavigation() {
            ReleaseMissionNavigation();
            if (m_NavData != null) {
                if (m_Build != null && !m_Build.isDone) NavMeshBuilder.Cancel(m_NavData);
                if (m_NavInstance.valid) m_NavInstance.Remove();
                Object.Destroy(m_NavData);
            }
            m_NavData = null;
            m_Build = null;
        }

        protected override void OnUpdate() {
            m_TacticalWorld = m_TacticalQuery.IsEmptyIgnoreFilter
                ? null
                : EntityManager.GetComponentObject<TacticalServerWorld>(m_TacticalQuery.GetSingletonEntity());
            // 悬赏有独立的对局注册表和入场条件。满足时提前转到悬赏循环，不继续推进普通远征人群。
            if (BountyClashRuntime.Enabled(EntityManager)) {
                if (ClashSoloRuntime.TryGetRegistry(EntityManager, out var registry) && MoonRaidMap.Active != null) {
                    UpdateClashBots(MoonRaidMap.Active, registry, SystemAPI.Time.ElapsedTime);
                    return;
                }
                if (m_Map != null || m_NavData != null) {
                    DestroyEnemies();
                    ReleaseNavigation();
                    ResetPopulation();
                    m_Map = null;
                }
                return;
            }
            m_ClashArena = null;
            m_ClashRegistry = null;
            m_ClashAgents.Clear();
            var map = MoonRaidMap.Active;
            double now = SystemAPI.Time.ElapsedTime;
            // 引用比较也能识别已卸载的 Unity 对象，即使它与 null 比较时结果为真。
            // Unity 对象销毁后比较起来可能像 null。这里用对象身份识别地图确实换过，从而清理上局。
            if (!ReferenceEquals(m_Map, map) || (map != null && map.EnemyCount <= 0 && m_NavData != null)) {
                DestroyEnemies();
                ReleaseNavigation();
                m_Map = map;
                m_Tuning = map && map.EnemyTuning ? map.EnemyTuning : m_DefaultTuning;
                ResetPopulation();
            }
            if (map == null || map.EnemyCount <= 0) return;
            // 正式驱动 AI 之前要有可寻路场景。异步网格还没就绪时晚几拍启动，不在未知地形发出直走指令。
            if (!NavigationReady(map, now)) return;
            // 刷新潜在目标和队伍信息按较低频率，具体碰撞移动仍每拍推进。
            if (now >= m_NextTargets) {
                CollectTargets(now);
                UpdateExpeditionSquads(now);
                m_NextTargets = now + .1;
            }
            using var deaths = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (enemy, pose, health, input, entity) in SystemAPI
                         .Query<RefRO<DollSingerEnemy>, RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRW<PlayerInputComponent>>().WithAll<Simulate>().WithNone<ClashBot>()
                         .WithEntityAccess()) {
                var brain = EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                if (health.ValueRO.CurrentHealth <= 0) {
                    // 最后一次装备同步后还可能收到致命伤害。
                    // 护盾的实际剩余能量必须跟物品一起进入尸体。
                    // 死亡可能刚发生在两次装备刷新之间。先把护盾真实剩余能量写回库存，再生成尸体容器。
                    EnergyShieldEquipment.Flush(ref health.ValueRW, brain.Inventory);
                    DropCorpse(brain, pose.ValueRO, now, enemy.ValueRO.CharacterIndex);
                    RetireEnemy(entity, brain);
                    deaths.DestroyEntity(enemy.ValueRO.InputEntity);
                    deaths.DestroyEntity(entity);
                    continue;
                }
                // 随行队友和创建它的玩家同进同出。主人不再是有效局内参与者就清掉队友输入和实体。
                if (brain.IsCompanion && !CompanionOwnerActive(brain)) {
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
                // 机器人和玩家在角色系统里读的是同一种输入。这里给出这一拍想做什么，后面的系统执行它。
                input.ValueRW.Input = Think(entity, brain, ghost, pose.ValueRO, ref health.ValueRW, map, now,
                    (float)SystemAPI.Time.DeltaTime);
                // 安全撤离只移除角色并清掉记分项，不把活人撤离变成一个可搜的尸体。
                if (brain.Extracted) {
                    // 成功撤离后移除这个战局角色，无需生成尸体。
                    deaths.DestroyEntity(enemy.ValueRO.InputEntity);
                    deaths.DestroyEntity(entity);
                    RetireEnemy(entity, brain);
                }
            }
            // 先完成当前遍历，再批量落实实体销毁。剩余人数变动之后才能判断是否补刷。
            deaths.Playback(EntityManager);
            UpdatePopulation(map, now);
            UpdateCompanionPopulation(map, now);
        }

        private void DestroyEnemies() {
            using var query = EntityManager.CreateEntityQuery(typeof(DollSingerEnemy));
            using var enemies = query.ToEntityArray(Allocator.Temp);
            foreach (var enemy in enemies) {
                if (EntityManager.HasComponent<ClashBot>(enemy)) continue;
                RetireEnemy(enemy, EntityManager.GetComponentObject<DollSingerEnemyBrain>(enemy));
                var input = EntityManager.GetComponentData<DollSingerEnemy>(enemy).InputEntity;
                if (EntityManager.Exists(input)) EntityManager.DestroyEntity(input);
                EntityManager.DestroyEntity(enemy);
            }
        }

        private void RetireEnemy(Entity entity, DollSingerEnemyBrain brain) {
            m_CoverReservations.Remove(brain.Seed);
            LeaderboardManager.RetireRaidActor(EntityManager.GetComponentData<GhostOwner>(entity).NetworkId);
        }

        // 用场景边界与玩法路线约束构建范围。大地图用较粗的采样，研究站等局部再单独补细节。
        private bool NavigationReady(MoonRaidMap map, double now) {
            if (m_NavInstance.valid) return MissionNavigationReady();
            if (m_Build != null) {
                if (!m_Build.isDone) return false;
                m_NavInstance = NavMesh.AddNavMeshData(m_NavData);
                Debug.Log("[DollSinger AI] Server navigation ready.");
                return m_NavInstance.valid && MissionNavigationReady();
            }
            if (now < m_NextBuild) return false;
            m_NextBuild = now + 1;
            // 场景设定的入口决定可玩的远征范围，整张地图的路线也从这些位置衔接。
            var bounds = new Bounds(map.LootPositions[0], Vector3.one);
            foreach (var point in map.LootPositions) bounds.Encapsulate(point);
            foreach (var point in map.EnemySpawnPositions) bounds.Encapsulate(point);
            for (int i = 0; i < map.ExtractionCount; i++)
                if (map.TryExtractionPosition(i, out var exit))
                    bounds.Encapsulate(exit);
            bounds.Expand(new Vector3(100, 100, 100));
            if (m_ClashArena != null)
                bounds = new Bounds(BountyClashArenaSettings.Position(m_ClashArena.Center),
                    new Vector3(m_ClashArena.Radius * 2 + 12, 100, m_ClashArena.Radius * 2 + 12));
            UnityEngine.Physics.SyncTransforms();
            m_Sources.Clear();
            NavMeshBuilder.CollectSources(bounds, s_WorldMask, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), m_Sources);
            if (m_Sources.Count == 0) return false; // 分区流式加载的地形现在可能还没齐。
            for (int i = 0; i < m_Sources.Count; i++) {
                var source = m_Sources[i];
                if (source.shape != NavMeshBuildSourceShape.Mesh || !(source.sourceObject is Mesh mesh) ||
                    mesh.isReadable)
                    continue;
                // 导入岩石在 Player 构建中会丢弃 CPU 端顶点。
                // 使用各自局部包围盒保守挡住导航，不需要把每块岩石网格存在内存里。
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
            // 四公里的远征图不能以 20 厘米体素重算全部 DEM 地形。
            // 小区间留精细导航，大地图用更粗且有上限的分辨率。
            settings.voxelSize = Mathf.Max(bounds.size.x, bounds.size.z) > 1000 ? .75f : .2f;
            m_Filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            m_NavData = new NavMeshData(settings.agentTypeID) { name = "DollSinger server expedition navigation" };
            m_Build = NavMeshBuilder.UpdateNavMeshDataAsync(m_NavData, settings, m_Sources, bounds);
            return false;
        }

        // 建立可被 AI 感知的候选和声音表。收集位置、身体姿势和动作事件供各观察者独立判定。
        private void CollectTargets(double now) {
            m_Targets.Clear();
            m_Events.Clear();
            m_SensorBatch = now;
            foreach (var (pose, health, owner, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>()
                         .WithAll<GhostGameObjectLink, Simulate>().WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0 || owner.ValueRO.NetworkId == 0) continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (link.LinkedInstance == null) continue;
                var alien = link.LinkedInstance.GetComponent<ProceduralAlienEnemy>();
                Vector3 position = pose.ValueRO.Position;
                var velocity = Vector3.zero;
                if (!m_Tracks.TryGetValue(entity, out var track)) {
                    track = new SensorTrack {
                        Position = position, SeenAt = now,
                        Shot = health.ValueRO.LastShotTick, Reload = health.ValueRO.LastReloadTick,
                        Land = health.ValueRO.LastLandTick, Loot = health.ValueRO.LootSearchSequence,
                        Consumable = health.ValueRO.ConsumableUseSequence
                    };
                    m_Tracks.Add(entity, track);
                } else {
                    float elapsed = Mathf.Max(.05f, (float)(now - track.SeenAt));
                    velocity = Vector3.ClampMagnitude((position - track.Position) / elapsed, 8);
                    float movement = HorizontalDistance(position, track.Position);
                    if (health.ValueRO.ControllerState.MovementType != FirstPersonController.MovementType.Standing ||
                        elapsed > 1 || movement > Mathf.Max(2, elapsed * 12))
                        track.StepDistance = 0;
                    else
                        track.StepDistance += movement;
                    if (track.Shot != health.ValueRO.LastShotTick)
                        m_Events.Add(new SensorEvent
                            { Source = entity, Position = position, Range = m_Tuning.ShotSensorRange, Shot = true });
                    if (track.Reload != health.ValueRO.LastReloadTick)
                        m_Events.Add(new SensorEvent { Source = entity, Position = position, Range = 7 });
                    if (track.Loot != health.ValueRO.LootSearchSequence)
                        m_Events.Add(new SensorEvent
                            { Source = entity, Position = position, Range = RaidLootAcoustics.HearingRange });
                    if (track.Consumable != health.ValueRO.ConsumableUseSequence)
                        m_Events.Add(new SensorEvent
                            { Source = entity, Position = position, Range = RaidConsumableAcoustics.HearingRange });
                    if (track.Land != health.ValueRO.LastLandTick) {
                        m_Events.Add(new SensorEvent {
                            Source = entity, Position = position,
                            Range = RaidFootsteps.LandingRange(m_Tuning.FootstepSensorRange,
                                health.ValueRO.CarriedWeight, health.ValueRO.ClashEnabled)
                        });
                        track.StepDistance = 0;
                    } else if (track.StepDistance >= RaidFootsteps.StepDistance) {
                        m_Events.Add(new SensorEvent {
                            Source = entity, Position = position,
                            Range = RaidFootsteps.HearingRange(m_Tuning.FootstepSensorRange,
                                health.ValueRO.ControllerState.Stance,
                                health.ValueRO.ControllerState.Sprinting || (alien && velocity.magnitude > 4),
                                health.ValueRO.CarriedWeight, health.ValueRO.ClashEnabled)
                        });
                        track.StepDistance = 0;
                    }
                    track.Position = position;
                    track.SeenAt = now;
                    track.Shot = health.ValueRO.LastShotTick;
                    track.Reload = health.ValueRO.LastReloadTick;
                    track.Land = health.ValueRO.LastLandTick;
                    track.Loot = health.ValueRO.LootSearchSequence;
                    track.Consumable = health.ValueRO.ConsumableUseSequence;
                }
                if (RaidCoughSound.TryEmit(ref track.NextCough, now, health.ValueRO.TacticalCoughSeconds,
                        health.ValueRO.ClashEnabled || m_ClashRegistry != null))
                    m_Events.Add(new SensorEvent
                        { Source = entity, Position = position, Range = RaidCoughSound.Range });
                var player = link.LinkedInstance.GetComponent<PlayerGhost>();
                var capsule = player != null && player.Controller != null
                    ? player.Controller.CharacterController
                    : null;
                // 使用角色当前实际控制器姿态。趴着的人不能按一处假想的站立高度当作射击点。
                var center = capsule ? capsule.transform.TransformPoint(capsule.center) - position : Vector3.up * .9f;
                float height = capsule ? capsule.height * capsule.transform.lossyScale.y : 1.8f;
                m_Targets.Add(new Target {
                    Entity = entity, Position = position, Velocity = velocity, Root = link.LinkedInstance.transform,
                    PlayerControlled = owner.ValueRO.NetworkId > 0,
                    Alien = alien != null,
                    AimOffset = alien ? alien.AimPoint - position : center + Vector3.up * (height * .14f),
                    HeadOffset = center + Vector3.up * (height * .36f),
                    HipOffset = center - Vector3.up * (height * .08f)
                });
            }
            m_ExpiredTracks.Clear();
            foreach (var pair in m_Tracks)
                if (now - pair.Value.SeenAt > 1)
                    m_ExpiredTracks.Add(pair.Key);
            foreach (var entity in m_ExpiredTracks) m_Tracks.Remove(entity);
        }

        // 射线查询需要确认目标属于对应角色，并且实际中间没障碍。挡在前面的墙才决定是否看到目标。
        private bool ClearSight(Transform root, Vector3 eye, Vector3 targetPoint, Entity target) {
            if (SmokeVisibility.Blocked(m_TacticalWorld, root.gameObject.scene.GetPhysicsScene(), eye, targetPoint))
                return false;
            var offset = targetPoint - eye;
            int count = UnityEngine.Physics.RaycastNonAlloc(eye, offset.normalized, m_Hits, offset.magnitude + .2f,
                s_SightMask, QueryTriggerInteraction.Ignore);
            if (count == m_Hits.Length || !EntityManager.Exists(target) ||
                !EntityManager.HasComponent<GhostGameObjectLink>(target))
                return false;
            var targetRoot = EntityManager.GetComponentObject<GhostGameObjectLink>(target).LinkedInstance;
            if (targetRoot == null) return false;
            float nearest = float.MaxValue;
            Transform hit = null;
            for (int i = 0; i < count; i++) {
                if (m_Hits[i].transform.IsChildOf(root) || m_Hits[i].distance >= nearest) continue;
                nearest = m_Hits[i].distance;
                hit = m_Hits[i].transform;
            }
            return hit != null && hit.IsChildOf(targetRoot.transform);
        }

        // 导航点只在目标变化、间隔到了或移动停滞时重算。路线不全就退回决策，不把角色传送到目标。
        private void UpdatePath(DollSingerEnemyBrain brain, Vector3 position, Vector3 goal, double now) {
            if (now < brain.NextPath && HorizontalDistance(brain.Goal, goal) < 3) return;
            brain.NextPath = now + .6 + brain.Seed % 7 * .03;
            brain.Goal = goal;
            brain.CornerCount = 0;
            brain.Corner = 1;
            if (!NavMesh.SamplePosition(position, out var start, 3, m_Filter) ||
                !NavMesh.SamplePosition(goal, out var end, 3, m_Filter) ||
                Mathf.Abs(start.position.y - position.y) > .75f || Mathf.Abs(end.position.y - goal.y) > 1.2f ||
                !NavMesh.CalculatePath(start.position, end.position, m_Filter, brain.Path) ||
                brain.Path.status != NavMeshPathStatus.PathComplete) {
                PathFailure(brain, goal, now);
                if (brain.Action == PmcAction.Scavenge) AbandonCache(brain);
                return;
            }
            brain.PathFailed = false;
            brain.CornerCount = brain.Path.GetCornersNonAlloc(brain.Corners);
            if (brain.CornerCount == 0 || brain.CornerCount == brain.Corners.Length ||
                (brain.IsClashBot && !SafeClashRoute(brain.Corners, brain.CornerCount))) {
                brain.CornerCount = 0;
                PathFailure(brain, goal, now);
                if (brain.Action == PmcAction.Scavenge) AbandonCache(brain);
                return;
            }
            // 控制器停滞时重新寻路，不绕过碰撞直接传送。
            if (now >= brain.NextProgress) {
                if (brain.MovementRequested && Vector3.Distance(position, brain.ProgressPosition) < .3f &&
                    Vector3.Distance(position, goal) > 2) {
                    PathFailure(brain, goal, now);
                    brain.NextCover = brain.NextDecision = 0;
                    if (brain.Action == PmcAction.Scavenge && brain.Cache >= 0) AbandonCache(brain);
                } else if (Vector3.Distance(position, brain.ProgressPosition) >= .6f) {
                    brain.PathFailures = brain.RecoveryAttempts = 0;
                }
                brain.ProgressPosition = position;
                brain.NextProgress = now + 3;
            }
        }

        private static readonly Profiling.ProfilerMarker s_ProbePathProfile = new("Moonkov.Pmc.ProbePath");

        private static void PathFailure(DollSingerEnemyBrain brain, Vector3 goal, double now) {
            brain.PathFailed = true;
            if (!brain.IsClashBot) return;
            brain.PathFailures++;
            brain.FailedGoal = goal;
            brain.FailedGoalUntil = now + Mathf.Min(12, 2 + brain.PathFailures * 2);
            brain.NextMission = brain.NextDecision = brain.NextCover = 0;
        }

        // 到达一个拐点再沿下一个方向移动。路线耗尽就给零方向，留机会让选目标逻辑更新。
        private static Vector3 FollowPath(DollSingerEnemyBrain brain, Vector3 position) {
            while (brain.Corner < brain.CornerCount &&
                   HorizontalDistance(position, brain.Corners[brain.Corner]) < .7f &&
                   Mathf.Abs(position.y - brain.Corners[brain.Corner].y) < .6f)
                brain.Corner++;
            if (brain.Corner >= brain.CornerCount) return Vector3.zero;
            var direction = brain.Corners[brain.Corner] - position;
            direction.y = 0;
            return direction.normalized;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b) {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        // 阵亡后把真实掉落交给本局容器，记录角色类型和位置给客户端生成对应尸体外观。
        private void DropCorpse(DollSingerEnemyBrain brain, LocalTransform pose, double now, int characterIndex) {
            using var profile = s_DropProfile.Auto();
            Vector3 position = pose.Position;
            Quaternion rotation = pose.Rotation;
            if (UnityEngine.Physics.Raycast(position + Vector3.up, Vector3.down, out var ground, 4, s_WorldMask,
                    QueryTriggerInteraction.Ignore)) {
                position = ground.point;
                rotation = Quaternion.FromToRotation(Vector3.up, ground.normal) * rotation;
            }
            var loot = EntityManager.GetComponentObject<RaidLootContainers>(
                SystemAPI.GetSingletonEntity<RaidLootWorld>());
            var carried = brain.IsCompanion ? Inventory.InventoryGraph.Create(false, false) : brain.Inventory;
            loot.Drop(carried, position, rotation, GameSettings.NormalizeCharacterIndex(characterIndex), now);
        }
    }
}