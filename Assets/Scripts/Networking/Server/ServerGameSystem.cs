// 战局服务端的总入口。连接、角色出生和每帧的玩法分流先经过这里，库存、结算、任务、断线等具体逻辑分在同名的其他文件中。
using UnityEngine;
using System;
using Gameplay.Leaderboard;
using Unity.Burst;
using Unity.CharacterController;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using Random = Unity.Mathematics.Random;
using Unity.Transforms;
using Collider = UnityEngine.Collider;

namespace Unity.MP_FPS {
    /// <summary>
    /// 处理客户端入场请求，并为通过入场的客户端创建角色。
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(ServerGhostTransformWriteSystem))]
    //[UpdateInGroup(typeof(SimulationSystemGroup))]  //默认就在该更新组中，可省略显式声明。
    [BurstCompile]
    public partial struct ServerGameSystem : ISystem {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() { _overlapColliders = new Collider[16]; }

        private static Collider[] _overlapColliders = new Collider[16];
        private ComponentLookup<JoinedClient> _joinedClientLookup;

        public void OnCreate(ref SystemState state) {
            // 地图预制体、物理世界和网络驱动都准备后才开始跑对局。没有这些数据时不能替客户端生成角色。
            state.RequireForUpdate<PlayerEntityPrefabs>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<NetworkStreamDriver>();
            state.RequireForUpdate<ClientsMap>();
            var randomEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(randomEntity, new FixedRandom {
                Random = Random.CreateFromIndex((uint)DateTime.Now.Millisecond)
            });
            // 用 NetCode 的整数 NetworkId 直接索引连接。第零格留给服务器，后面每个来宾在连上时扩表。
            var mapSingleton = state.EntityManager.CreateSingletonBuffer<ClientsMap>();
            state.EntityManager.GetBuffer<ClientsMap>(mapSingleton).Add(default); //服务器的 NetworkId 为 0。
            _joinedClientLookup = state.GetComponentLookup<JoinedClient>();
            // 本局共用的箱子、任务、在线列表和机器人注册表留在服务端自己的 World 里。不同 World 不会共享一份对局记录。
            var lootEntity = state.EntityManager.CreateSingleton(new RaidLootWorld());
            state.EntityManager.AddComponentObject(lootEntity, new RaidLootContainers());
            state.EntityManager.AddComponentObject(lootEntity, new RaidMissionWorld());
            state.EntityManager.AddComponentObject(lootEntity, new BountyClashWorld());
            state.EntityManager.AddComponentObject(lootEntity, new OnlineRaidWorld());
            state.EntityManager.AddComponentObject(lootEntity, new ClashBotRegistry());
            m_PersistenceEntity = state.EntityManager.CreateEntity();
        }

        [BurstDiscard]
        public void OnUpdate(ref SystemState state) {
            // 先为本 World 选好档案通道。添加这份 ECS 数据会改变实体结构，所以要做完再拿本帧用的组件查询和缓冲。
            // 取得缓冲区或组件查询前先初始化档案。加上托管上下文会改变实体结构，
            // 而此时 World 所用的玩法标记已经设好。
            var persistence = Persistence(ref state);
            _joinedClientLookup.Update(ref state);
            // 每帧查询角色和 RPC 时，把增删 ECS 实体的操作记进命令缓冲，之后统一回放，避免遍历时改变实体结构。
            var ecb = SystemAPI.GetSingletonRW<BeginSimulationEntityCommandBufferSystem.Singleton>().ValueRW
                .CreateCommandBuffer(state.WorldUnmanaged);
            var clientsMap = SystemAPI.GetSingletonBuffer<ClientsMap>();
            var gameplayMapsEntity = SystemAPI.GetSingletonEntity<ClientsMap>();
            var connectionEventsForTick = SystemAPI.GetSingleton<NetworkStreamDriver>().ConnectionEventsForTick;
            RefreshClientsMap(ref state, ecb, clientsMap, connectionEventsForTick);
            if (!SystemAPI.TryGetSingleton(out PlayerEntityPrefabs playerEntityPrefabs)) return;
            if (SystemAPI.HasSingleton<DisableCharacterDynamicContacts>())
                state.EntityManager.DestroyEntity(SystemAPI.GetSingletonEntity<DisableCharacterDynamicContacts>());
            // 有竞技场标记时走临时装备玩法分支。远征和极速赏金继续经过下面的档案、背包、生存与战局步骤。
            if (SystemAPI.ManagedAPI.HasSingleton<ArenaMatch>()) {
                HandleArena(ref state, ecb);
                return;
            }
            // 先接收存档任务的回执并检查断线保留期限，再读本帧的新入场请求。真正可用的出战数据这时才进入匹配。
            PollPersistence(ref state, ecb);
            PollRetainedRaids(ref state, ecb);
            _joinedClientLookup.Update(ref state);
            HandleJoinRequests(ref state, gameplayMapsEntity, playerEntityPrefabs, ecb);
            state.EntityManager.GetComponentObject<RaidMissionWorld>(SystemAPI.GetSingletonEntity<RaidLootWorld>())
                .BackendCompatible = persistence.MissionsSupported;
            if (MoonRaidMap.Active != null) EnsureRaidInventories(ref state);
            // 角色活着时先扣本帧生存资源、处理战术物品，然后处理死亡和撤出。死亡已经发生的人不会又同时走撤离成功。
            UpdateSurvival(ref state);
            UpdateTacticalItems(ref state, ecb);
            HandleBountyClash(ref state, ecb);
            HandlePlayerDeathAndRespawn(ref state, ecb);
            HandleRaids(ref state, ecb);
        }

        private void RefreshClientsMap(
            ref SystemState state,
            EntityCommandBuffer ecb,
            DynamicBuffer<ClientsMap> clientsMap,
            NativeArray<NetCodeConnectionEvent>.ReadOnly connectionEventsForTick) {
            //维护客户端索引表容量。
            foreach (var evt in connectionEventsForTick) {
                // 首次上报连接时把编号和连接实体塞进映射。玩家角色还要等入场流程，映射里的角色位置可以暂时是空的。
                if (evt.State == ConnectionState.State.Connected) {
                    int lengthNeeded = evt.Id.Value + 1;
                    if (clientsMap.Length < lengthNeeded)
                        clientsMap.Resize(lengthNeeded, NativeArrayOptions.ClearMemory);
                    clientsMap.ElementAt(evt.Id.Value).ConnectionEntity = evt.ConnectionEntity;
                }
                if (evt.State == ConnectionState.State.Disconnected) {
                    int networkId = evt.Id.Value;
                    if (ClashEnabled(ref state)) {
                        // 接收阶段的断线钩子已在 NetCode 清理前接管了原角色。
                        DetachClashConnection(ref state, ecb, evt.ConnectionEntity, networkId);
                        clientsMap = SystemAPI.GetSingletonBuffer<ClientsMap>();
                        if (networkId < clientsMap.Length &&
                            clientsMap[networkId].ConnectionEntity == evt.ConnectionEntity)
                            clientsMap.ElementAt(networkId) = default;
                        _joinedClientLookup.Update(ref state);
                        continue;
                    }
                    Debug.Log($"[Server] Client with NetworkId {networkId} has disconnected.");
                    // 一般由 RaidDisconnectSystem 在 NetCode 清理之前处理，
                    // 赶在删除 RaidSession 和关联角色之前。这个补救入口也要保证
                    // 尚未移除的连接重复经过此处时不会再次结算。
                    // 普通远征的提前断线钩子通常已经接管。如果旧连接还留有进行中出战，从这里补结算，状态检查会挡住重复执行。
                    SettleDisconnectedRaid(ref state, evt.ConnectionEntity);

                    // 查询 GhostOwner 找到并删除该客户端的角色实体。
                    foreach (var (ghostOwner, entity) in SystemAPI.Query<RefRO<GhostOwner>>()
                                 .WithAll<PredictedPlayerGhost>().WithEntityAccess())
                        if (ghostOwner.ValueRO.NetworkId == networkId) {
                            Debug.Log(
                                $"[Server] Found and destroying PlayerEntity {entity} for disconnected client {networkId}.");
                            ecb.DestroyEntity(entity);
                            break;
                        }

                    // 查询 PlayerCommandTarget 找到并删除该客户端的 clientInputEntity。
                    // 旧输入实体也要回收。它和玩家角色是两个 Ghost，只有删除模型还会留下无人控制的命令目标。
                    foreach (var (commandTarget, entity) in SystemAPI.Query<RefRO<PlayerCommandTarget>>()
                                 .WithEntityAccess())
                        if (commandTarget.ValueRO.NetworkId == networkId) {
                            Debug.Log(
                                $"[Server] Found and destroying ClientInputEntity {entity} for disconnected client {networkId}.");
                            ecb.DestroyEntity(entity);
                            break;
                        }
                    RemovePlayerFromLeaderboard(networkId);
                    clientsMap.ElementAt(networkId) = default;
                }
            }

            // 通过 ECB 新建时先得到临时实体 ID，回放后需要把最终实体引用补回索引表。
            // 通过命令缓冲排队新建实体时，先拿到的是临时编号。回放之后再把能长期使用的实体引用补回 NetworkId 索引。
            for (int i = clientsMap.Length - 1; i >= 0; --i) {
                ref var map = ref clientsMap.ElementAt(i);
                if (map.OwnerNetworkId.Value == default) break;
                ref var dest = ref clientsMap.ElementAt(map.OwnerNetworkId.Value);
                Patch(map.PlayerEntity, ref dest.PlayerEntity);
                Patch(map.CharacterControllerEntity, ref dest.CharacterControllerEntity);
                map = default;

                static void Patch(Entity possibleRemapValue, ref Entity destination) {
                    if (possibleRemapValue != Entity.Null) destination = possibleRemapValue;
                }

                ;
            }
        }

        private Entity GetChildWithComponent<T>(EntityManager em, Entity parentEntity)
            where T : unmanaged, IComponentData {
            if (!em.HasComponent<Child>(parentEntity)) return Entity.Null;
            var children = em.GetBuffer<Child>(parentEntity);
            foreach (var child in children)
                if (em.HasComponent<T>(child.Value))
                    return child.Value;
            return Entity.Null;
        }

        [BurstDiscard]
        private void AddDeathToLeaderboard(int networkId) { LeaderboardManager.Instance.AddDeath(networkId); }

        // 可以放在结构或类的开头附近。
        [BurstDiscard]
        private void AddPlayerToLeaderboard(int networkId, FixedString64Bytes playerName) {
            LeaderboardManager.AddPlayer(networkId, playerName);
        }

        [BurstDiscard]
        private void RemovePlayerFromLeaderboard(int networkId) {
            LeaderboardManager.Instance?.RemovePlayer(networkId);
        }

        private void SpawnPlayerCharacter(
            ref SystemState state,
            EntityCommandBuffer ecb,
            Entity connectionEntity,
            FixedString64Bytes playerName,
            int characterIndex,
            ArenaMatch.Player arenaPlayer = null,
            LocalTransform? matchSpawn = null) {
            var playerEntityPrefabs = SystemAPI.GetSingleton<PlayerEntityPrefabs>();
            // 客户端选择的角色编号不一定合法，先规整并确认对应实体预制体存在。个别角色缺资源可退回默认角色。
            characterIndex = GameSettings.NormalizeCharacterIndex(characterIndex);
            if (playerEntityPrefabs.DollSingerEntityPrefab == Entity.Null)
                throw new InvalidOperationException("Moonkov requires a registered DollSinger player prefab.");
            var playerEntityPrefab = characterIndex switch {
                GameSettings.HatsuneMikuCharacterIndex => playerEntityPrefabs.HatsuneMikuEntityPrefab,
                GameSettings.ShianCharacterIndex => playerEntityPrefabs.ShianEntityPrefab,
                _ => playerEntityPrefabs.DollSingerEntityPrefab
            };
            if (playerEntityPrefab == Entity.Null) {
                characterIndex = GameSettings.DollSingerCharacterIndex;
                playerEntityPrefab = playerEntityPrefabs.DollSingerEntityPrefab;
            }
            var ownerNetworkId = SystemAPI.GetComponent<NetworkId>(connectionEntity);

            // 创建客户端输入实体。
            // 一条连接同时关联两类实体。命令实体负责接玩家输入，角色实体负责移动和生命值。给两边标上同一个 NetworkId 才能配对。
            var clientInputEntity = ecb.Instantiate(playerEntityPrefabs.ClientInputEntityPrefab);
            ecb.SetComponent(clientInputEntity, new GhostOwner { NetworkId = ownerNetworkId.Value });
            ecb.SetComponent(connectionEntity, new CommandTarget { targetEntity = clientInputEntity });
            ecb.AddBuffer<ClientCommandInput>(clientInputEntity);
            ecb.SetComponent(clientInputEntity, new PlayerCommandTarget { NetworkId = ownerNetworkId.Value });

            // 创建玩家角色实体。
            // 输入 Ghost 的所有者已绑定到连接，现在才实例化有动作和血量的玩家角色。两者靠组件里的实体引用衔接。
            var playerEntity = ecb.Instantiate(playerEntityPrefab);
            // 未使用正式远征地图时可能没有出战库存。此处按该连接已读到的档案提取随身装备，拿不到档案才从空图开始。
            if (MoonRaidMap.Active == null && !state.EntityManager.HasComponent<RaidInventoryState>(connectionEntity)) {
                var context = Persistence(ref state);
                var graph =
                    context.Profiles.TryGetValue(connectionEntity, out var profile) &&
                    !string.IsNullOrEmpty(profile.InventoryJson)
                        ? RaidInventoryTransport.Decode(profile.InventoryJson).ExtractLoadout()
                        : Inventory.InventoryGraph.Create(false);
                SetRaidInventory(ref state, connectionEntity, graph, 0);
            }
            uint weaponId = DollSingerWeapons.None;
            var weaponData = WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(weaponId);
            int magazineSize = weaponData != null ? weaponData.MagazineSize : 0;
            ecb.SetComponent(playerEntity, new GhostOwner { NetworkId = ownerNetworkId.Value });
            if (arenaPlayer == null && SystemAPI.HasSingleton<LanRaidMode>() && !ClashEnabled(ref state))
                ecb.AddComponent(playerEntity, new ExpeditionSquadMember { LanCoop = true });
            if (Persistence(ref state).Profiles.TryGetValue(connectionEntity, out var onlineProfile) &&
                !string.IsNullOrEmpty(onlineProfile.OnlineInstanceId))
                ecb.AddComponent(playerEntity, new OnlineRaidMember { Team = onlineProfile.OnlineTeam });
            ecb.AddComponent(playerEntity,
                new PlayerClientCommandInputLookup { ClientCommandInputEntity = clientInputEntity });
            var playerState = new PredictedPlayerGhost {
                InputIndex = 0,
                MaxHealth = 100f,
                CurrentHealth = 100f,
                EquippedWeaponID = weaponId,
                CurrentAmmo = magazineSize,
                InventoryWeapons = true,
                PrimaryWeaponID = DollSingerWeapons.None, SecondaryWeaponID = DollSingerWeapons.None,
                PistolWeaponID = DollSingerWeapons.None
            };
            // 出生时设置完整分部位血量与生存字段，在线小队和竞技场队伍信息再按服务器认证档案添进角色。
            RaidHealth.Initialize(ref playerState);
            playerState.OnlineHuman = !string.IsNullOrEmpty(onlineProfile?.OnlineInstanceId);
            playerState.OnlineTeam = onlineProfile?.OnlineTeam ?? 0;
            if (arenaPlayer != null) {
                playerState.ClashEnabled = true;
                playerState.ClashTeam = arenaPlayer.Team;
                playerState.ClashLife = ClashLife.Waiting;
                playerState.ClashMaxHealthFraction = 1;
                var match = SystemAPI.ManagedAPI.GetSingleton<ArenaMatch>();
                if (match.Classic != null) ClassicDust2Map.InitializeAir(ref playerState);
                ecb.AddComponent(playerEntity,
                    new ClashPlayer { ParticipantId = arenaPlayer.Id, TeamId = arenaPlayer.Team, Epoch = match.Epoch });
            } else { ConfigureClashSpawn(ref state, ecb, connectionEntity, playerEntity, ref playerState); }
            ecb.SetComponent(playerEntity, playerState);
            ecb.AddComponent(playerEntity, new PlayerCharacterInitialized());
            ecb.SetComponentEnabled<PlayerCharacterInitialized>(playerEntity, false);
            // 比赛玩法可以提前安排出生点。一般远征则查烘焙的 SpawnPoint，在线同队玩家进一步分配到相邻位置。
            if (matchSpawn.HasValue) { ecb.SetComponent(playerEntity, matchSpawn.Value); } else if
                (!ClashEnabled(ref state) && FindSpawnPoint(ref state, out var spawnPoint)) {
                var spawn = new LocalTransform
                    { Position = spawnPoint.Position, Rotation = spawnPoint.Rotation, Scale = 1.0f };
                if (!string.IsNullOrEmpty(onlineProfile?.OnlineInstanceId))
                    spawn = state.EntityManager
                        .GetComponentObject<OnlineRaidWorld>(SystemAPI.GetSingletonEntity<RaidLootWorld>())
                        .Spawn(onlineProfile.OnlineTeam, spawn);
                ecb.SetComponent(playerEntity, spawn);
            }
            ecb.SetComponent(playerEntity, new GhostGameObjectGuid { Guid = GhostGameObject.GenerateRandomHash() });
            ecb.SetComponent(playerEntity, new PlayerGhost.PlayerData { Name = playerName });

            // 更新客户端索引表。
            var clientsMap = SystemAPI.GetSingletonBuffer<ClientsMap>();
            clientsMap.ElementAt(ownerNetworkId.Value).PlayerEntity = playerEntity;
            // 第一次出生把角色、名字和模型编号记到连接上。下次复活还用原连接而换角色实体。
            if (!SystemAPI.HasComponent<JoinedClient>(connectionEntity)) {
                // 将连接上的 JoinedClient 改为新的角色实体。
                ecb.AddComponent(connectionEntity, new JoinedClient {
                    PlayerEntity = playerEntity, PlayerName = playerName, CharacterIndex = characterIndex
                });
                if (MoonRaidMap.Active != null && !SystemAPI.HasComponent<RaidSession>(connectionEntity)) {
                    var raid = new RaidSession();
                    var persistence = Persistence(ref state);
                    if (arenaPlayer != null) {
                        raid = new RaidSession { RaidId = 1, Phase = RaidPhase.Active, ExtractionIndex = -1 };
                    // 入场档案流程已准备好 RaidSession 时直接接过来。里面保留这次扣装的编号和初始计时，不能临时生成另一份。
                    } else if (persistence.ReadyRaids.TryGetValue(connectionEntity, out var prepared)) {
                        raid = prepared;
                        persistence.ReadyRaids.Remove(connectionEntity);
                    } else { RaidRules.BeginNext(ref raid, MoonRaidMap.Active.RaidDuration); }
                    ecb.AddComponent(connectionEntity, raid);
                }
            } else {
                var joined = SystemAPI.GetComponent<JoinedClient>(connectionEntity);
                joined.PlayerEntity = playerEntity;
                joined.CharacterIndex = characterIndex;
                ecb.SetComponent(connectionEntity, joined);
            }
            if (ClashEnabled(ref state)) {
                var persistence = Persistence(ref state);
                if (persistence.ReadyRaids.TryGetValue(connectionEntity, out var prepared)) {
                    if (SystemAPI.HasComponent<RaidSession>(connectionEntity))
                        ecb.SetComponent(connectionEntity, prepared);
                    else
                        ecb.AddComponent(connectionEntity, prepared);
                    persistence.ReadyRaids.Remove(connectionEntity);
                }
            }
            // 把角色挂到连接的关联组，随连接正常清理。断线保留要先主动从这组解除，不然旧连接会把原角色一并删掉。
            ecb.AppendToBuffer(connectionEntity, new LinkedEntityGroup { Value = playerEntity });
            if (!SystemAPI.HasComponent<NetworkStreamInGame>(connectionEntity))
                ecb.AddComponent(connectionEntity, new NetworkStreamInGame());
        }

        private void HandlePlayerDeathAndRespawn(ref SystemState state, EntityCommandBuffer ecb) {
            var clientsMap = SystemAPI.GetSingletonBuffer<ClientsMap>();

            // 第一步，检测死亡并删除玩家角色。
            foreach (var (playerGhost, ghostOwner, entity) in SystemAPI
                         .Query<RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>()
                         .WithNone<DollSingerEnemy, ProceduralAlienEnemy.State>().WithEntityAccess()) {
                // 赏金等比赛玩法由各自状态机处理击倒与死亡。这里仅接管普通角色，远征玩家走死亡结算。
                if (playerGhost.ValueRO.ClashEnabled) continue;
                if (playerGhost.ValueRO.CurrentHealth <= 0) {
                    int networkId = ghostOwner.ValueRO.NetworkId;
                    var connectionEntity = clientsMap[ghostOwner.ValueRO.NetworkId].ConnectionEntity;
                    if (!SystemAPI.Exists(connectionEntity)) continue;
                    if (SystemAPI.HasComponent<RaidSession>(connectionEntity)) {
                        FinishRaid(ref state, ecb, connectionEntity, RaidPhase.Dead);
                        continue;
                    }

                    // 在连接上添加重生计时。
                    if (!SystemAPI.HasComponent<PendingRespawn>(connectionEntity)) {
                        Debug.Log(
                            $"[Server] Player {entity} has died. Starting respawn timer for connection {connectionEntity}.");
                        ecb.AddComponent(connectionEntity, new PendingRespawn { RespawnTimer = 5f });
                    }
                    if (SystemAPI.HasComponent<PlayerClientCommandInputLookup>(entity)) {
                        var inputLookup = SystemAPI.GetComponent<PlayerClientCommandInputLookup>(entity);
                        if (SystemAPI.Exists(inputLookup.ClientCommandInputEntity))
                            ecb.DestroyEntity(inputLookup.ClientCommandInputEntity);
                    }

                    // 删除玩家角色实体。
                    ecb.DestroyEntity(entity);
                }
            }

            // 第二步，更新倒计时并重生玩家。
            foreach (var (pendingRespawn, connection, entity) in SystemAPI
                         .Query<RefRW<PendingRespawn>, RefRO<NetworkId>>().WithEntityAccess()) {
                // 只有无 RaidSession 的普通死亡才在连接上排复活计时。倒计时结束，仍是已加入玩家时重新生成角色。
                pendingRespawn.ValueRW.RespawnTimer -= SystemAPI.Time.DeltaTime;
                if (pendingRespawn.ValueRO.RespawnTimer <= 0f) {
                    Debug.Log($"[Server] Respawning player for connection {entity}.");
                    if (_joinedClientLookup.HasComponent(entity)) {
                        var joinedClientData = _joinedClientLookup[entity];

                        // 再次调用出生函数。
                        SpawnPlayerCharacter(ref state, ecb, entity, joinedClientData.PlayerName,
                            joinedClientData.CharacterIndex);

                        // 移除计时组件。
                        ecb.RemoveComponent<PendingRespawn>(entity);
                    } else {
                        // 这个分支意味着状态异常，按无效重生任务清掉。
                        Debug.LogError($"Connection entity {entity} is pending respawn but has no JoinedClient data!");
                        ecb.RemoveComponent<PendingRespawn>(entity);
                    }
                }
            }
        }

        // 用 NetCode 给的实际发包连接定位玩家，丢掉重复入场请求。玩法与请求版本先过关，之后才验证票据和锁定出战装备。
        private void HandleJoinRequests(
            ref SystemState state,
            Entity gameplayMapsEntity,
            PlayerEntityPrefabs playerEntityPrefabs,
            EntityCommandBuffer ecb) {
            var handled = new System.Collections.Generic.HashSet<Entity>();
            foreach (var (request, rpcReceive, entity) in SystemAPI
                         .Query<RefRO<ClientJoinRequestV2Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var source = rpcReceive.ValueRO.SourceConnection;
                if (!SystemAPI.Exists(source) || !handled.Add(source) ||
                    SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(source) ||
                    SystemAPI.HasComponent<NetworkStreamInGame>(source) ||
                    Persistence(ref state).Joins.ContainsKey(source) ||
                    Persistence(ref state).Deployments.ContainsKey(source)) {
                    ecb.DestroyEntity(entity);
                    continue;
                }
                bool clash = ClashEnabled(ref state);
                // 客户端和服务端的玩法、规则版本不一致时明确发拒绝理由，再断开旧连接，免得进入错误地图。
                if (request.ValueRO.Mode != (clash ? RaidGameMode.BountyClash : RaidGameMode.Expedition) ||
                    request.ValueRO.RulesVersion != (clash ? BountyClashRules.Version : 0)) {
                    var reject = ecb.CreateEntity();
                    ecb.AddComponent(reject, new ClashJoinAcceptedRpc {
                        Mode = clash ? RaidGameMode.BountyClash : RaidGameMode.Expedition,
                        RulesVersion = clash ? BountyClashRules.Version : 0, Accepted = false, Reason = "玩法或规则版本不匹配"
                    });
                    ecb.AddComponent(reject,
                        new SendRpcCommandRequest { TargetConnection = rpcReceive.ValueRO.SourceConnection });
                    ecb.AddComponent(rpcReceive.ValueRO.SourceConnection,
                        new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    ecb.DestroyEntity(entity);
                    continue;
                }
                if (clash && (!Persistence(ref state).Enabled || (SystemAPI.HasSingleton<OfflineRaidMode>() &&
                                                                  !ClashSoloRuntime.Enabled(state.EntityManager)))) {
                    ClashAccepted(ecb, rpcReceive.ValueRO.SourceConnection, false, "赏金速战需要已启用的联网持久化服务");
                    ecb.DestroyEntity(entity);
                    continue;
                }
                if (SystemAPI.HasComponent<NetworkId>(rpcReceive.ValueRW.SourceConnection) &&
                    !SystemAPI.HasComponent<NetworkStreamInGame>(rpcReceive.ValueRW.SourceConnection)) {
                    var persistence = Persistence(ref state);
                    if (!persistence.Enabled ||
                        (SystemAPI.HasSingleton<OfflineRaidMode>() && (persistence.Joins.Count != 0 ||
                                                                       persistence.Profiles.Count != 0 ||
                                                                       persistence.Deployments.Count != 0)) ||
                        !RaidRules.ValidLoadout(request.ValueRO.CarryCells) ||
                        (!persistence.Enabled && request.ValueRO.CarryCells != 0)) {
                        ecb.AddComponent(rpcReceive.ValueRW.SourceConnection,
                            new NetworkStreamRequestDisconnect
                                { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    } else if (persistence.Enabled) {
                        // 同伴人数只接受允许该功能的单人和好友房远征。先做人数上限校验，再把入场请求交给档案加载。
                        byte companions =
                            !clash && !SystemAPI.HasSingleton<TutorialRaidMode>() &&
                            (SystemAPI.HasSingleton<OfflineRaidMode>() || SystemAPI.HasSingleton<LanRaidMode>())
                                ? (byte)System.Math.Min((int)request.ValueRO.ExpeditionCompanions,
                                    RaidRules.MaxCompanions)
                                : (byte)0;
                        persistence.BeginJoin(rpcReceive.ValueRW.SourceConnection, request.ValueRO.PlayerName,
                            request.ValueRO.CharacterIndex, request.ValueRO.LoginToken.ToString(),
                            request.ValueRO.CarryCells, request.ValueRO.ResumeOnly, companions);
                    } else {
                        SpawnPlayerCharacter(ref state, ecb, rpcReceive.ValueRW.SourceConnection,
                            request.ValueRO.PlayerName, request.ValueRO.CharacterIndex);
                        var ownerNetworkId = SystemAPI.GetComponent<NetworkId>(rpcReceive.ValueRW.SourceConnection);
                        AddPlayerToLeaderboard(ownerNetworkId.Value, request.ValueRO.PlayerName);
                    }
                }
                ecb.DestroyEntity(entity);
            }
        }

        [BurstDiscard]
        private bool FindSpawnPoint(
            ref SystemState state,
            out LocalToWorld spawnPoint,
            System.Collections.Generic.HashSet<Mathematics.float3> reserved = null) {
            // 出生点由场景烘焙成带位置的 ECS 标记。随机找候选后还要根据本帧预留和碰撞过滤，避免两个角色叠在一起。
            var spawnPointsQuery = SystemAPI.QueryBuilder().WithAll<SpawnPoint, LocalToWorld>().Build();
            var spawnPoints = spawnPointsQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
            ref var random = ref SystemAPI.GetSingletonRW<FixedRandom>().ValueRW;
            if (spawnPoints.Length == 0) {
                spawnPoint = default;
                spawnPoints.Dispose();
                return false;
            }
            if (SystemAPI.HasSingleton<TutorialRaidMode>() && RaidMissionSite.Active) {
                var entry = RaidMissionSite.Active.Anchor("entry");
                if (entry != null) {
                    int closest = 0;
                    float best = float.MaxValue;
                    for (int i = 0; i < spawnPoints.Length; i++) {
                        float d = ((Vector3)spawnPoints[i].Position - entry.Approach).sqrMagnitude;
                        if (d < best) {
                            best = d;
                            closest = i;
                        }
                    }
                    spawnPoint = spawnPoints[closest];
                    spawnPoints.Dispose();
                    return true;
                }
            }

            // 先打乱候选点列表。附近玩家同样少的点有多个时，
            // 从这些点里随机选择。
            for (int i = spawnPoints.Length - 1; i > 0; i--) {
                int k = random.Random.NextInt(0, i + 1);
                (spawnPoints[k], spawnPoints[i]) = (spawnPoints[i], spawnPoints[k]);
            }
            int bestSpawnPointIndex = -1;
            int minColliderCount = int.MaxValue;
            for (int i = 0; i < spawnPoints.Length; i++) {
                if (reserved != null && reserved.Contains(spawnPoints[i].Position)) continue;
                int numColliders = UnityEngine.Physics.OverlapSphereNonAlloc(spawnPoints[i].Position, 2f,
                    _overlapColliders, LayerMask.GetMask("ServerPlayer"));
                if (numColliders == 0) {
                    bestSpawnPointIndex = i;
                    break;
                }
                if (numColliders < minColliderCount) {
                    minColliderCount = numColliders;
                    bestSpawnPointIndex = i;
                }
            }
            spawnPoint = bestSpawnPointIndex >= 0 ? spawnPoints[bestSpawnPointIndex] : default;
            spawnPoints.Dispose();
            return bestSpawnPointIndex >= 0;
        }
    }
}