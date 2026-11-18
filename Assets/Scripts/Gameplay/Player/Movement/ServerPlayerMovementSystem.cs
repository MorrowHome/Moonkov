// 服务器处理一拍角色移动和射击的入口。真人输入来自命令缓冲，机器人产生同格式输入。移动由 FirstPersonController 算，开火先查时序和能量，再做服务端命中和伤害。
// 回写位置和事件的 tick，客户端会收到这些确认值。
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Unity.MP_FPS {
    public class PlayerControllerLink : IComponentData {
        public FirstPersonController Controller;
    }

    internal struct ProjectileSpawnData {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
        public int OwnerNetworkId;
        public ClashDamageSource DamageSource;
        public uint SpawnTick;
        public uint WeaponId;
        public uint FireTick;
        public int PelletIndex;
        public ushort ModuleMask;
        public float3 InitialVelocity;
    }

    internal struct VfxSpawnData {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class ServerPlayerMovementSystem : SingletonSystem<ServerPlayerMovementSystem> {
        private const int k_NumHistoryTicks = 20;
        private static readonly int s_ShootableLayerMask = LayerMask.GetMask("Ground", "Default");
        private static readonly int s_HitscanLayerMask = LayerMask.GetMask("ServerPlayer", "Default", "Ground");
        private NativeList<ClientCommandInput> m_ProcessedClientInputCommands;
        private ComponentLookup<PredictedClientInput> m_PredictedClientInputComponentLookup;
        private ComponentLookup<PredictedPlayerGhost> m_PlayerGhostLookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init() {
            s_PlayerMovementActive = false;
            s_PlayerMovementTick = 0;
        }

        // logging and diagnostics
        private static bool s_PlayerMovementActive = false;
        private static uint s_PlayerMovementTick;
        public static bool PlayerMovementActive => s_PlayerMovementActive;
        public static uint PlayerMovementTick => s_PlayerMovementTick;
        private ComponentLookup<GhostOwner> ghostOwnerLookup;

        private sealed class CarriedWeightSample {
            public int Version = int.MinValue;
            public System.Collections.Generic.List<Inventory.InventoryItem> Items;
            public float Weight;
        }

        private readonly ConditionalWeakTable<Inventory.InventoryGraph, CarriedWeightSample> m_CarriedWeights = new();

        private float CarriedWeight(Inventory.InventoryGraph inventory) {
            if (inventory == null) return 0;
            var sample = m_CarriedWeights.GetValue(inventory, _ => new CarriedWeightSample());
            // 库存结构版本没变时复用上次负重。重新算后按网络同步精度取整，避免客户端和服务器用到不同速度上限。
            if (sample.Version != inventory.Version || !ReferenceEquals(sample.Items, inventory.Items)) {
                sample.Version = inventory.Version;
                sample.Items = inventory.Items;
                // Match Ghost quantization so prediction uses the server's exact rule input.
                sample.Weight = Mathf.Round(inventory.CarriedWeight * 100) / 100;
            }
            return sample.Weight;
        }

        // 机器人自己的库存挂在脑组件，真人的库存挂在网络连接。二者最终都把库存交给同一套武器和负重规则。
        private Inventory.InventoryGraph WeaponInventory(Entity player) {
            if (EntityManager.HasComponent<DollSingerEnemyBrain>(player))
                return EntityManager.GetComponentObject<DollSingerEnemyBrain>(player).Inventory;
            if (!ghostOwnerLookup.HasComponent(player) || !SystemAPI.TryGetSingletonBuffer<ClientsMap>(out var clients))
                return null;
            int owner = ghostOwnerLookup[player].NetworkId;
            if (owner <= 0 || owner >= clients.Length) return null;
            var connection = clients[owner].ConnectionEntity;
            return EntityManager.Exists(connection) && EntityManager.HasComponent<RaidInventoryState>(connection)
                ? EntityManager.GetComponentObject<RaidInventoryState>(connection).Graph
                : null;
        }

        private bool TryRechargeHalo(Entity player, ref PredictedPlayerGhost state, WeaponData weapon) {
            if (!DollSingerWeapons.IsHalo(state.EquippedWeaponID)) return false;
            if (EntityManager.HasComponent<DollSingerEnemyBrain>(player))
                return HaloEnergyInventory.RechargeFirst(ref state,
                    EntityManager.GetComponentObject<DollSingerEnemyBrain>(player).Inventory, weapon);
            if (!ghostOwnerLookup.HasComponent(player) || !SystemAPI.TryGetSingletonBuffer<ClientsMap>(out var clients))
                return false;
            int owner = ghostOwnerLookup[player].NetworkId;
            if (owner <= 0 || owner >= clients.Length) return false;
            var connection = clients[owner].ConnectionEntity;
            if (!EntityManager.Exists(connection) || !EntityManager.HasComponent<RaidSession>(connection) ||
                !EntityManager.HasComponent<JoinedClient>(connection) ||
                !EntityManager.HasComponent<RaidInventoryState>(connection) ||
                EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity != player)
                return false;
            var session = EntityManager.GetComponentData<RaidSession>(connection);
            var inventory = EntityManager.GetComponentObject<RaidInventoryState>(connection);
            // 充能要属于当前战局，不能插进进行中的库存操作。扣电池成功后再让库存重发，并保留这次的预留量给计时。
            if (session.Phase != RaidPhase.Active || inventory.RaidId != session.RaidId || inventory.ActionPending ||
                !HaloEnergyInventory.RechargeFirst(ref state, inventory.Graph, weapon))
                return false;
            RaidInventoryState.UpdateTotals(inventory.Graph, ref session);
            inventory.LastSentVersion = -1;
            EntityManager.SetComponentData(connection, session);
            return true;
        }

        public PlayerMovementHistory MovementHistory { get; private set; } = new(k_NumHistoryTicks);

        protected override void OnCreate() {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();
            m_PredictedClientInputComponentLookup = GetComponentLookup<PredictedClientInput>(true);
            m_PlayerGhostLookup = GetComponentLookup<PredictedPlayerGhost>();
            const int maxLocalPlayers = 4; //PlayerGhostManager.k_MaxLocalPlayers
            m_ProcessedClientInputCommands =
                new NativeList<ClientCommandInput>(maxLocalPlayers * 32, Allocator.Persistent);
            ghostOwnerLookup = GetComponentLookup<GhostOwner>(true);
        }

        protected override void OnDestroy() {
            m_ProcessedClientInputCommands.Dispose();
            var query = GetEntityQuery(typeof(PlayerControllerLink));
            foreach (var entity in query.ToEntityArray(Allocator.Temp)) {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller = null;
                EntityManager.SetComponentData(entity, controllerLink);
            }
            base.OnDestroy();
        }

        protected override void OnUpdate() {
            float deltaTime = World.Time.DeltaTime;
            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            if (!networkTime.ServerTick.IsValid) return;
            uint serverTick = networkTime.ServerTick.TickIndexForValidTick;
            s_PlayerMovementActive = true;
            s_PlayerMovementTick = serverTick;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (predictedPlayer, localTransform, entity) in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<LocalTransform>>().WithEntityAccess()
                         .WithAll<PlayerInputComponent, GhostGameObjectLink>().WithNone<PlayerControllerLink>()) {
                var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (gameObjectLink.LinkedInstance != null)
                    if (gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller)) {
                        ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
                        predictedPlayer.ValueRW.ControllerState.Init(localTransform.ValueRO.Position,
                            localTransform.ValueRO.Rotation);
                    }
            }

            // for each client
            // we process the input and create a list of commandInputs that need to be processed by the movement code
            // the movement code processes the inputs in order
            m_ProcessedClientInputCommands.Clear();
            var commands = m_ProcessedClientInputCommands;
            int unityFrameCount = UnityEngine.Time.frameCount;
            bool logPredictionWarnings = PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;
            var clientCommandInputBufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>(true);
            foreach (var (predictedClient, entity) in SystemAPI.Query<RefRW<PredictedClientInput>>().WithEntityAccess()
                         .WithAll<GhostOwner, Simulate>()) {
                if (!clientCommandInputBufferLookup.TryGetBuffer(entity, out var buffer) || buffer.Length == 0)
                    continue;
                predictedClient.ValueRW.BeginInputIndex = commands.Length;
                // 第一次收到这个玩家输入时，从查到的指令起步。已有上次处理记录才从上次编号追赶。
                if (predictedClient.ValueRO.LastProcessedServerTick == 0) {
                    // first frame, let's just get the latest data and set it
                    if (buffer.GetDataAtTick(new NetworkTick(serverTick), out var commandInput)) {
                        var input = commandInput;
                        commands.Add(input);
                        predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                    }
                } else {
                    if (logPredictionWarnings && serverTick - predictedClient.ValueRO.LastProcessedServerTick > 1)
                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                         $"[ServerPlayerMovementSystem] Server tick has skipped a frame of input. " +
                                         $"Current server tick {serverTick.ToString()}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");

                    // we need to check we aren't missing data
                    // so let's check for any data from the ticks in between
                    // 从上次处理的输入拍追到本拍。中间缺包时用当前能找到的指令近似补齐，客户端预测可能因此需要校正。
                    for (uint tick = predictedClient.ValueRO.LastProcessedServerTick + 1; tick <= serverTick; tick++)
                        if (buffer.GetDataAtTick(new NetworkTick(tick), out var commandInput) &&
                            commandInput.Tick.TickIndexForValidTick !=
                            predictedClient.ValueRO.LastProcessedServerTick) {
                            var input = commandInput;
                            commands.Add(input);
                            if (commandInput.Tick.TickIndexForValidTick >
                                predictedClient.ValueRO.LastProcessedServerTick + 1)
                                // we are missing input from the client which means we might mispredict
                                for (uint i = predictedClient.ValueRO.LastProcessedServerTick + 1;
                                     i < commandInput.Tick.TickIndexForValidTick;
                                     i++) {
                                    // if we have missing ticks here, it means the server did not (and will not) receive it.
                                    // So the best we can do is assume the input is the same and accumulate our movement based on that
                                    if (logPredictionWarnings)
                                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                                         $"[ServerPlayerMovementSystem] Missing client input for tick {i.ToString()} - " +
                                                         $"using the input from tick {commandInput.Tick.TickIndexForValidTick.ToString()}");
                                    commands.Add(commandInput);
                                }
                            predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                        }
                }
                predictedClient.ValueRW.InputCount = commands.Length - predictedClient.ValueRO.BeginInputIndex;
                if (logPredictionWarnings) {
                    string unityFrameCountString = unityFrameCount.ToString();
                    string serverTickString = serverTick.ToString();
                    if (predictedClient.ValueRO.InputCount == 0)
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] No input to process for tick {serverTickString}.");
                    else if (predictedClient.ValueRO.LastProcessedServerTick != serverTick)
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] Last processed tick not set to current. Current server tick {serverTickString}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                }
            }

            // Bots produce the same command stream as players, without a fake transport connection.
            foreach (var (enemy, input, health) in SystemAPI
                         .Query<RefRO<DollSingerEnemy>, RefRO<PlayerInputComponent>, RefRO<PredictedPlayerGhost>>()
                         .WithAll<Simulate>()) {
                var entity = enemy.ValueRO.InputEntity;
                var predicted = EntityManager.GetComponentData<PredictedClientInput>(entity);
                predicted.BeginInputIndex = commands.Length;
                predicted.InputCount = health.ValueRO.CurrentHealth > 0 &&
                                       (!health.ValueRO.ClashEnabled || health.ValueRO.ClashCanAct) &&
                                       predicted.LastProcessedServerTick != serverTick
                    ? 1
                    : 0;
                if (predicted.InputCount != 0)
                    commands.Add(new ClientCommandInput
                        { Tick = new NetworkTick(serverTick), PlayerInput = input.ValueRO.Input });
                predicted.LastProcessedServerTick = serverTick;
                EntityManager.SetComponentData(entity, predicted);
            }
            ghostOwnerLookup.Update(this);
            m_PlayerGhostLookup.Update(this);
            m_PredictedClientInputComponentLookup.Update(this);
            var predictedClientInputComponentLookup = m_PredictedClientInputComponentLookup;
            // 采集网络指令后先准备查询角色和物理碰撞。Host 同一进程存在两套角色，使用各自的组件和碰撞层。
            var playerGhostLookup = m_PlayerGhostLookup;
            var projectileSpawnList = new NativeList<ProjectileSpawnData>(Allocator.Temp);
            var vfxSpawnList = new NativeList<VfxSpawnData>(Allocator.Temp);
            // The project disables automatic transform syncing. Query the current poses,
            // including newly spawned head hitboxes, rather than the previous physics step.
            UnityEngine.Physics.SyncTransforms();
            foreach (var (player, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithAll<Simulate>()
                         .WithEntityAccess())
                if (player.ValueRO.InventoryWeapons) {
                    var inventory = WeaponInventory(entity);
                    DollSingerWeapons.SyncEquipment(ref player.ValueRW, inventory,
                        WeaponManager.Instance.WeaponRegistry);
                    player.ValueRW.CarriedWeight = MoonRaidMap.Active && !player.ValueRO.ClashEnabled
                        ? CarriedWeight(inventory)
                        : 0;
                }
            foreach (var predictedPlayer in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithAll<Simulate>()) {
                HaloModuleFire.AdvanceTimers(ref predictedPlayer.ValueRW, deltaTime);
                // 已有的充能和装填先继续计时。这拍归零了才能把新入账的能量用于下一步射击。
                if (predictedPlayer.ValueRO.ControllerState.IsReloadingState) {
                    var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                        predictedPlayer.ValueRO.EquippedWeaponID, predictedPlayer.ValueRO.EquippedModules);
                    HaloReload.Tick(ref predictedPlayer.ValueRW, weaponData, deltaTime);
                }
            }

            // Resolve every actor's defense before any hitscan shot. Entity iteration order
            // must not decide whether this tick's held defense protects the target.
            // 先把所有角色这一拍的防御状态结算完，再检查射线命中。否则先跑到的角色可能绕过同拍举起的盾。
            foreach (var (defender, lookup) in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>>()
                         .WithAll<Simulate>()) {
                bool held = false;
                if (lookup.ValueRO.ClientCommandInputEntity != Entity.Null &&
                    predictedClientInputComponentLookup.HasComponent(lookup.ValueRO.ClientCommandInputEntity)) {
                    var pending = predictedClientInputComponentLookup[lookup.ValueRO.ClientCommandInputEntity];
                    if (pending.InputCount > 0 && commands[pending.BeginInputIndex + pending.InputCount - 1]
                            .TryGetPlayerMovementInput(defender.ValueRO.InputIndex, out var intent))
                        held = intent.HaloDefense && !intent.HaloTransfer && !intent.FireSuppressed &&
                               !intent.FreeLooking;
                }
                HaloEnergyRules.SetDefense(ref defender.ValueRW, held);
            }
            UpdateHaloTransfers(serverTick, deltaTime);
            UpdateHaloScans(serverTick, deltaTime, ref ecb);
            foreach (var (predictedPlayer, inputLookup, entity) in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>>().WithEntityAccess()
                         .WithAll<Simulate, GhostGameObjectLink>()) {
                var ghostLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                var playerGhost = ghostLink.LinkedInstance.GetComponent<PlayerGhost>();
                if (inputLookup.ValueRO.ClientCommandInputEntity == Entity.Null ||
                    !predictedClientInputComponentLookup.HasComponent(inputLookup.ValueRO.ClientCommandInputEntity))
                    continue;
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0) {
                    // get movement index for the last frame we are going to process
                    // 这一拍的装备、射击用最新一条有效输入。运动会稍后按这一拍收到的多段输入分摊时间。
                    var commandInput = commands[predictedClient.BeginInputIndex + predictedClient.InputCount - 1];
                    if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input)) {
                        input = RaidHealth.RestrictInput(predictedPlayer.ValueRO, input);
                        playerGhost.ServerMovementInput = input;
                    }
                    predictedPlayer.ValueRW.ControllerState.Shoot = false;
                    // 先处理拿哪把武器，再根据实际武器和模块读参数。切换的这一拍有取枪等待。
                    bool switchedWeapon = DollSingerWeapons.TryEquip(ref predictedPlayer.ValueRW, input,
                        WeaponManager.Instance.WeaponRegistry);
                    if (switchedWeapon)
                        DollSingerWeapons.SyncEquipment(ref predictedPlayer.ValueRW, WeaponInventory(entity),
                            WeaponManager.Instance.WeaponRegistry);
                    var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                        predictedPlayer.ValueRO.EquippedWeaponID, predictedPlayer.ValueRO.EquippedModules);
                    if (weaponData != null) {
                        var shotRay = playerGhost.GetShotRay(input, weaponData.HitscanRange, out var aimPoint);
                        predictedPlayer.ValueRW.AimPoint =
                            predictedPlayer.ValueRO.HaloDeploymentMode == HaloDeploymentMode.Sentry
                                ? SentryLookPoint(predictedPlayer.ValueRO)
                                : (float3)aimPoint;
                        bool wantsToReload = input.Reload;
                        bool wantsToShoot = !switchedWeapon && input.Shoot && !input.FireSuppressed;
                        bool mustReload = (wantsToShoot || weaponData.AutoReloadWhenEmpty) &&
                                          predictedPlayer.ValueRO.CurrentAmmo <= 0;
                        bool halo = DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID);
                        // 光环按装填键从库存预留电池能量。传统武器还可因空仓自动走旧的弹仓装填逻辑。
                        if (halo && wantsToReload && TryRechargeHalo(entity, ref predictedPlayer.ValueRW, weaponData)) {
                            predictedPlayer.ValueRW.LastReloadTick = serverTick;
                            HaloEnergyRules.RefreshAmmo(ref predictedPlayer.ValueRW, weaponData);
                        } else if (!halo && HaloModuleFire.ReadyToReload(ref predictedPlayer.ValueRW,
                                       wantsToReload || mustReload, weaponData.MagazineSize)) {
                            HaloReload.Begin(ref predictedPlayer.ValueRW, weaponData, weaponData.MagazineSize);
                            predictedPlayer.ValueRW.LastReloadTick = serverTick;
                        }
                        // 部署在身外的哨戒光环也通过一次正式开火检查。真开火时以锚点当射线起点，记录它专用的事件拍。
                        bool sentryShot = TrySentryShot(entity, ref predictedPlayer.ValueRW, weaponData, serverTick,
                            out var sentryRay);
                        if (sentryShot || HaloModuleFire.TryFire(ref predictedPlayer.ValueRW, weaponData, input,
                                switchedWeapon, deltaTime)) {
                            if (sentryShot) {
                                shotRay = sentryRay;
                                aimPoint = shotRay.GetPoint(weaponData.HitscanRange);
                                predictedPlayer.ValueRW.AimPoint = aimPoint;
                            }
                            HaloModuleFire.CommitShot(ref predictedPlayer.ValueRW, weaponData, deltaTime);
                            if (sentryShot) predictedPlayer.ValueRW.WeaponCooldown = 0;
                            // 确认能开火之后才扣能量并标记开火拍。接下来按武器类型直接查射线，或生成由服务器负责碰撞的弹体。
                            HaloEnergyRules.CommitShot(ref predictedPlayer.ValueRW, weaponData);
                            predictedPlayer.ValueRW.LastShotTick = serverTick;
                            if (sentryShot) predictedPlayer.ValueRW.LastSentryShotTick = serverTick;
                            bool aimed = sentryShot ||
                                         !DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID) ||
                                         input.Aim;
                            // 射线武器直接改这条射线的散布方向。弹体武器在生成每颗弹丸时按编号分别取散布。
                            if (weaponData.Type == WeaponType.Hitscan) {
                                shotRay = new Ray(shotRay.origin,
                                    WeaponSpread.Direction(shotRay.direction, weaponData, 0,
                                        commandInput.Tick.TickIndexForValidTick, aimed));
                                aimPoint = shotRay.GetPoint(weaponData.HitscanRange);
                            }
                            int shooterNetworkId = ghostOwnerLookup[entity].NetworkId;
                            float3 eyePosition = shotRay.origin;
                            float3 aimDirection = shotRay.direction;
                            var shotOriginPosition = eyePosition;
                            if (VisualEffectManager.ServerInstance != null)
                                VisualEffectManager.ServerInstance.Server_RequestVfx(shooterNetworkId,
                                    predictedPlayer.ValueRO.EquippedWeaponID, aimPoint, serverTick, sentryShot,
                                    shotOriginPosition);
                            // 发射时就记录伤害来源和归属。晚到的弹体命中也用这一发的身份来判断，而不看后来换了什么阵营或模式。
                            var shotSource = ResearchQuestRuntime.TagShot(World, entity,
                                DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID), sentryShot,
                                BountyClashDamage.Capture(World, entity));
                            switch (weaponData.Type) {
                                case WeaponType.Hitscan: {
                                    // Use the calculated eyePosition and aimDirection
                                    if (playerGhost.RaycastShot(shotRay, weaponData.HitscanRange, s_HitscanLayerMask,
                                            out var hit)) {
                                        if (hit.collider.gameObject.layer == LayerMask.NameToLayer("ServerPlayer"))
                                            if (GhostGameObject.TryFindGhostGameObject(hit.collider.gameObject,
                                                    out var hitGhostObject) &&
                                                playerGhostLookup.HasComponent(hitGhostObject.LinkedEntity)) {
                                                var targetPredictedPlayer =
                                                    playerGhostLookup.GetRefRW(hitGhostObject.LinkedEntity);
                                                int targetNetworkId = ghostOwnerLookup[hitGhostObject.LinkedEntity]
                                                    .NetworkId;
                                                if (BountyClashDamage.IsSelf(World, hitGhostObject.LinkedEntity,
                                                        shotSource))
                                                    //skip hitting self
                                                    continue;
                                                var healthBeforeDamage = targetPredictedPlayer.ValueRO;
                                                // 用碰撞体确定身体部位，再按距离衰减，把最终请求交给统一伤害入口做护盾、队伍关系和模式判断。
                                                var hitPlayer = hitGhostObject.GetComponent<PlayerGhost>();
                                                BountyClashDamage.Apply(World, hitGhostObject.LinkedEntity, shotSource,
                                                    RaidHealth.ResolveHit(hitPlayer, hit.collider, hit.point),
                                                    Ballistics.DamageAtDistance(weaponData, hit.distance), serverTick,
                                                    RaidDamageKind.Bullet, (float3)hit.point);
                                                if (RaidHealth.ImpactFlags(healthBeforeDamage,
                                                        targetPredictedPlayer.ValueRO) != 0 &&
                                                    ((HaloModule)predictedPlayer.ValueRO.EquippedModules &
                                                     HaloModule.HitMarker) != 0)
                                                    VisualEffectManager.ServerInstance?.Server_RequestHaloMark(
                                                        shooterNetworkId, commandInput.Tick.TickIndexForValidTick,
                                                        hit.point);
                                                if (!targetPredictedPlayer.ValueRO.ClashEnabled &&
                                                    healthBeforeDamage.CurrentHealth > 0 &&
                                                    targetPredictedPlayer.ValueRO.CurrentHealth <= 0) {
                                                    if (LeaderboardManager.Instance != null)
                                                        Debug.Log(
                                                            $"[Server] Player {shooterNetworkId.ToString()} killed player {targetNetworkId.ToString()}.");
                                                    else
                                                        Debug.LogWarning(
                                                            "[Server] LeaderboardManager instance not found. Cannot add kill.");
                                                }
                                            }
                                        if (weaponData.ProjectileHitVfxPrefab != null)
                                            vfxSpawnList.Add(new VfxSpawnData {
                                                Position = hit.point,
                                                Rotation = Quaternion.LookRotation(hit.normal),
                                                Prefab = GhostSpawner.FindGhostPrefabEntity(weaponData
                                                    .ProjectileHitVfxPrefab.GhostGuid)
                                            });
                                    }
                                    break;
                                }
                                // 弹体记录射出时的所有者和生成编号，之后自己推进飞行。对应事件可供本机预测找到同一颗弹丸。
                                case WeaponType.Projectile: {
                                    var prefabEntity =
                                        GhostSpawner.FindGhostPrefabEntity(weaponData.ProjectileGhostPrefab.GhostGuid);
                                    if (prefabEntity != Entity.Null)
                                        for (int pellet = 0;
                                             pellet < Mathf.Clamp(weaponData.PelletCount, 1, 32);
                                             pellet++) {
                                            var pelletDirection = WeaponSpread.Direction(aimDirection, weaponData,
                                                pellet, commandInput.Tick.TickIndexForValidTick, aimed);
                                            var spawnRotation = Quaternion.LookRotation(pelletDirection);
                                            projectileSpawnList.Add(new ProjectileSpawnData {
                                                Prefab = prefabEntity,
                                                Position = shotOriginPosition,
                                                Rotation = spawnRotation,
                                                OwnerNetworkId = ghostOwnerLookup[entity].NetworkId,
                                                DamageSource = shotSource,
                                                SpawnTick = commandInput.Tick.TickIndexForValidTick,
                                                WeaponId = predictedPlayer.ValueRO.EquippedWeaponID,
                                                FireTick = serverTick,
                                                PelletIndex = pellet,
                                                ModuleMask = predictedPlayer.ValueRO.EquippedModules,
                                                InitialVelocity = pelletDirection * weaponData.ProjectileSpeed
                                            });
                                        }
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            foreach (var (player, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithAll<Simulate>()
                         .WithEntityAccess())
                if (player.ValueRO.InventoryWeapons)
                    DollSingerWeapons.SaveActiveAmmo(ref player.ValueRW, WeaponInventory(entity));
            foreach (var spawnData in projectileSpawnList)
                GhostSpawner.SpawnGhostPrefab(spawnData.Prefab, spawnData.Position, spawnData.Rotation,
                    GhostGameObject.GenerateRandomHash(), 1.0f, (spawnedEntity, ecb2) => {
                        ecb2.SetComponent(spawnedEntity, new Projectile.ProjectileData {
                            OwnerNetworkId = spawnData.OwnerNetworkId,
                            DamageSource = spawnData.DamageSource,
                            SpawnTick = spawnData.SpawnTick,
                            WeaponID = spawnData.WeaponId,
                            FireTick = spawnData.FireTick,
                            PelletIndex = spawnData.PelletIndex,
                            ModuleMask = spawnData.ModuleMask,
                            Origin = spawnData.Position,
                            InitialVelocity = spawnData.InitialVelocity
                        });
                    });
            foreach (var vfxData in vfxSpawnList)
                GhostSpawner.SpawnGhostPrefab(vfxData.Prefab, vfxData.Position, vfxData.Rotation,
                    GhostGameObject.GenerateRandomHash());
            predictedClientInputComponentLookup.Update(this);
            foreach (var (predictedPlayer, inputLookup, controllerConsts, entity) in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>,
                             RefRO<PredictedPlayerControllerConsts>>().WithAll<Simulate, PlayerControllerLink>()
                         .WithEntityAccess()) {
                if (inputLookup.ValueRO.ClientCommandInputEntity == Entity.Null ||
                    !predictedClientInputComponentLookup.HasComponent(inputLookup.ValueRO.ClientCommandInputEntity))
                    continue;
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0) {
                    // 这一拍收到多段输入时，把固定模拟时间分给这些操作，再累加位移。命令多不等于人物能在这一拍多走一倍。
                    float movementDt = deltaTime / predictedClient.InputCount;
                    for (int i = 0; i < predictedClient.InputCount; i++) {
                        var commandInput = commands[i + predictedClient.BeginInputIndex];
                        if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input))
                            FirstPersonController.AccumulateMovement(ref predictedPlayer.ValueRW.ControllerState,
                                ref predictedPlayer.ValueRW.AccumulatedMovement,
                                RaidHealth.RestrictInput(predictedPlayer.ValueRO, input),
                                RaidHealth.Movement(predictedPlayer.ValueRO, controllerConsts.ValueRO.ControllerConsts),
                                movementDt, SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity).Controller,
                                predictedPlayer.ValueRO.MeleeWeapon != 0 && predictedPlayer.ValueRO.CurrentHealth > 0 &&
                                !predictedPlayer.ValueRO.ClashDarkSight);
                    }
                    // 把刚起跳或刚落地的动作转成服务器 tick，远端看到新编号才播一次。
                    if (predictedPlayer.ValueRW.ControllerState.JumpTriggered) {
                        predictedPlayer.ValueRW.LastJumpTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.JumpTriggered = false;
                    }
                    if (predictedPlayer.ValueRW.ControllerState.LandTriggered) {
                        predictedPlayer.ValueRW.LastLandTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.LandTriggered = false;
                    }
                }
            }
            foreach (var (transform, predictedPlayer, controllerConsts, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>()
                         .WithNone<GhostGameObjectDeferredActivation>().WithEntityAccess()) {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                if (math.lengthsq(predictedPlayer.ValueRO.AccumulatedMovement) > 0f ||
                    controllerLink.Controller.NeedsLiftCarry(predictedPlayer.ValueRO.ControllerState)) {
                    // 落地和伤腿冲刺的后果放在真实碰撞移动后结算。移动请求被墙挡住时，不按原本想走的距离扣状态。
                    bool wasLanding = predictedPlayer.ValueRO.ControllerState.LandTriggered;
                    bool wasGrounded = predictedPlayer.ValueRO.ControllerState.MovementType ==
                                       FirstPersonController.MovementType.Standing;
                    var beforePosition = predictedPlayer.ValueRO.ControllerState.CurrentPosition;
                    controllerLink.Controller.ApplyMovementUpdate(ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts.ValueRO.ControllerConsts, predictedPlayer.ValueRO.AccumulatedMovement,
                        deltaTime);
                    if (MoonRaidMap.Active) {
                        RaidHealth.ApplyLandingDamage(ref predictedPlayer.ValueRW, wasLanding,
                            controllerConsts.ValueRO.ControllerConsts.Gravity, serverTick);
                        RaidHealth.ApplyInjuredSprintDamage(ref predictedPlayer.ValueRW,
                            (Vector3)(predictedPlayer.ValueRO.ControllerState.CurrentPosition - beforePosition),
                            wasGrounded, deltaTime, serverTick);
                    }
                }
                predictedPlayer.ValueRW.AccumulatedMovement = float3.zero;
            }
            bool checkForPredictionErrors = PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;
            foreach (var (transform, predictedPlayer, controllerConsts, entity) in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>().WithEntityAccess()) {
                // 最后把碰撞后的姿势写回 ECS LocalTransform。这是快照和客户端回滚对齐的位置。
                transform.ValueRW.Position = predictedPlayer.ValueRO.ControllerState.CurrentPosition;
                transform.ValueRW.Rotation = predictedPlayer.ValueRO.ControllerState.CurrentRotation;
                // 可选诊断把这一拍结果与历史记录比较。找到不同可定位具体哪一拍出现差异。
                if (checkForPredictionErrors) {
                    if (MovementHistory.TryGetTick(serverTick, out var pos, out var rot))
                        // compare our calculation with the recorded
                        Debug.Assert(
                            PlayerMovementHistory.HistoryMatches(pos, rot,
                                predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                predictedPlayer.ValueRO.ControllerState.CurrentRotation),
                            $"[ServerPlayerMovementSystem] Mispredict detected at tick {serverTick} predicted pos {predictedPlayer.ValueRO.ControllerState.CurrentPosition} recorded {(float3)pos}");
                    else
                        // this is the first time we've processed this tick
                        // let's add our result
                        MovementHistory.Add(serverTick, predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                            predictedPlayer.ValueRO.ControllerState.CurrentRotation);
                }
            }
            ecb.Playback(EntityManager);
            ecb.Dispose();
            UnityEngine.Physics.SyncTransforms();
            s_PlayerMovementActive = false;
        }
    }
}