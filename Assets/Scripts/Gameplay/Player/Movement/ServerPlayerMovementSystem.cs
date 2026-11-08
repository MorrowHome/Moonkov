using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Unity.MP_FPS
{
    public class PlayerControllerLink : IComponentData
    {
        public FirstPersonController Controller;
    }

    struct ProjectileSpawnData
    {
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

    struct VfxSpawnData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class ServerPlayerMovementSystem : SingletonSystem<ServerPlayerMovementSystem>
    {
        private const int k_NumHistoryTicks = 20;
        private static readonly int s_ShootableLayerMask = LayerMask.GetMask("Ground", "Default");
        private static readonly int s_HitscanLayerMask = LayerMask.GetMask("ServerPlayer", "Default", "Ground");
        private NativeList<ClientCommandInput> m_ProcessedClientInputCommands;
        private ComponentLookup<PredictedClientInput> m_PredictedClientInputComponentLookup;
        private ComponentLookup<PredictedPlayerGhost> m_PlayerGhostLookup;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_PlayerMovementActive = false;
            s_PlayerMovementTick = 0;
        }

        // logging and diagnostics
        private static bool s_PlayerMovementActive = false;
        private static uint s_PlayerMovementTick;
        public static bool PlayerMovementActive => s_PlayerMovementActive;
        public static uint PlayerMovementTick => s_PlayerMovementTick;

        private ComponentLookup<GhostOwner> ghostOwnerLookup;
        private sealed class CarriedWeightSample
        {
            public int Version = int.MinValue;
            public System.Collections.Generic.List<Inventory.InventoryItem> Items;
            public float Weight;
        }
        private readonly ConditionalWeakTable<Inventory.InventoryGraph, CarriedWeightSample> m_CarriedWeights = new();

        private float CarriedWeight(Inventory.InventoryGraph inventory)
        {
            if (inventory == null) return 0;
            var sample = m_CarriedWeights.GetValue(inventory, _ => new CarriedWeightSample());
            if (sample.Version != inventory.Version || !ReferenceEquals(sample.Items, inventory.Items))
            {
                sample.Version = inventory.Version; sample.Items = inventory.Items;
                // Match Ghost quantization so prediction uses the server's exact rule input.
                sample.Weight = Mathf.Round(inventory.CarriedWeight * 100) / 100;
            }
            return sample.Weight;
        }

        private Inventory.InventoryGraph WeaponInventory(Entity player)
        {
            if (EntityManager.HasComponent<DollSingerEnemyBrain>(player)) return EntityManager.GetComponentObject<DollSingerEnemyBrain>(player).Inventory;
            if (!ghostOwnerLookup.HasComponent(player) || !SystemAPI.TryGetSingletonBuffer<ClientsMap>(out var clients)) return null;
            int owner = ghostOwnerLookup[player].NetworkId;
            if (owner <= 0 || owner >= clients.Length) return null;
            var connection = clients[owner].ConnectionEntity;
            return EntityManager.Exists(connection) && EntityManager.HasComponent<RaidInventoryState>(connection)
                ? EntityManager.GetComponentObject<RaidInventoryState>(connection).Graph : null;
        }

        private bool TryRechargeHalo(Entity player, ref PredictedPlayerGhost state, WeaponData weapon)
        {
            if (!DollSingerWeapons.IsHalo(state.EquippedWeaponID)) return false;
            if (EntityManager.HasComponent<DollSingerEnemyBrain>(player))
                return HaloEnergyInventory.RechargeFirst(ref state, EntityManager.GetComponentObject<DollSingerEnemyBrain>(player).Inventory, weapon);
            if (!ghostOwnerLookup.HasComponent(player) || !SystemAPI.TryGetSingletonBuffer<ClientsMap>(out var clients)) return false;
            int owner = ghostOwnerLookup[player].NetworkId;
            if (owner <= 0 || owner >= clients.Length) return false;
            var connection = clients[owner].ConnectionEntity;
            if (!EntityManager.Exists(connection) || !EntityManager.HasComponent<RaidSession>(connection) ||
                !EntityManager.HasComponent<JoinedClient>(connection) || !EntityManager.HasComponent<RaidInventoryState>(connection) ||
                EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                EntityManager.GetComponentData<JoinedClient>(connection).PlayerEntity != player) return false;
            var session = EntityManager.GetComponentData<RaidSession>(connection);
            var inventory = EntityManager.GetComponentObject<RaidInventoryState>(connection);
            if (session.Phase != RaidPhase.Active || inventory.RaidId != session.RaidId || inventory.ActionPending ||
                !HaloEnergyInventory.RechargeFirst(ref state, inventory.Graph, weapon)) return false;
            RaidInventoryState.UpdateTotals(inventory.Graph, ref session);
            inventory.LastSentVersion = -1;
            EntityManager.SetComponentData(connection, session);
            return true;
        }

        public PlayerMovementHistory MovementHistory { get; private set; } =
            new PlayerMovementHistory(k_NumHistoryTicks);

        protected override void OnCreate()
        {
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

        protected override void OnDestroy()
        {
            m_ProcessedClientInputCommands.Dispose();

            var query = GetEntityQuery(typeof(PlayerControllerLink));
            foreach (var entity in query.ToEntityArray(Allocator.Temp))
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller = null;
                EntityManager.SetComponentData(entity, controllerLink);
            }

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            float deltaTime = World.Time.DeltaTime;

            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            if (!networkTime.ServerTick.IsValid)
            {
                return;
            }

            uint serverTick = networkTime.ServerTick.TickIndexForValidTick;

            s_PlayerMovementActive = true;
            s_PlayerMovementTick = serverTick;

            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (predictedPlayer, localTransform, entity) in
                     SystemAPI.Query<RefRW<PredictedPlayerGhost>, RefRO<LocalTransform>>()
                         .WithEntityAccess()
                         .WithAll<PlayerInputComponent, GhostGameObjectLink>()
                         .WithNone<PlayerControllerLink>())
            {
                var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (gameObjectLink.LinkedInstance != null)
                {
                    if (gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller))
                    {
                        ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
                        predictedPlayer.ValueRW.ControllerState.Init(localTransform.ValueRO.Position,
                            localTransform.ValueRO.Rotation);
                    }
                }
            }

            // for each client
            // we process the input and create a list of commandInputs that need to be processed by the movement code
            // the movement code processes the inputs in order
            m_ProcessedClientInputCommands.Clear();
            var commands = m_ProcessedClientInputCommands;
            var unityFrameCount = UnityEngine.Time.frameCount;

            var logPredictionWarnings = PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;
            var clientCommandInputBufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>(true);

            foreach (var (predictedClient, entity)
                     in SystemAPI.Query<RefRW<PredictedClientInput>>()
                         .WithEntityAccess()
                         .WithAll<GhostOwner, Simulate>())
            {
                if (!clientCommandInputBufferLookup.TryGetBuffer(entity, out var buffer) || buffer.Length == 0)
                {
                    continue;
                }

                predictedClient.ValueRW.BeginInputIndex = commands.Length;

                if (predictedClient.ValueRO.LastProcessedServerTick == 0)
                {
                    // first frame, let's just get the latest data and set it
                    if (buffer.GetDataAtTick(new NetworkTick(serverTick), out var commandInput))
                    {
                        ClientCommandInput input = commandInput;
                        commands.Add(input);
                        predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                    }
                }
                else
                {
                    if (logPredictionWarnings && serverTick - predictedClient.ValueRO.LastProcessedServerTick > 1)
                    {
                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                         $"[ServerPlayerMovementSystem] Server tick has skipped a frame of input. " +
                                         $"Current server tick {serverTick.ToString()}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }

                    // we need to check we aren't missing data
                    // so let's check for any data from the ticks in between
                    for (uint tick = predictedClient.ValueRO.LastProcessedServerTick + 1; tick <= serverTick; tick++)
                    {
                        if (buffer.GetDataAtTick(new NetworkTick(tick), out var commandInput)
                            && commandInput.Tick.TickIndexForValidTick !=
                            predictedClient.ValueRO.LastProcessedServerTick)
                        {
                            ClientCommandInput input = commandInput;
                            commands.Add(input);

                            if (commandInput.Tick.TickIndexForValidTick >
                                predictedClient.ValueRO.LastProcessedServerTick + 1)
                            {
                                // we are missing input from the client which means we might mispredict
                                for (uint i = predictedClient.ValueRO.LastProcessedServerTick + 1;
                                     i < commandInput.Tick.TickIndexForValidTick;
                                     i++)
                                {
                                    // if we have missing ticks here, it means the server did not (and will not) receive it.
                                    // So the best we can do is assume the input is the same and accumulate our movement based on that
                                    if (logPredictionWarnings)
                                    {
                                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                                         $"[ServerPlayerMovementSystem] Missing client input for tick {i.ToString()} - " +
                                                         $"using the input from tick {commandInput.Tick.TickIndexForValidTick.ToString()}");
                                    }

                                    commands.Add(commandInput);
                                }
                            }

                            predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                        }
                    }
                }

                predictedClient.ValueRW.InputCount = commands.Length - predictedClient.ValueRO.BeginInputIndex;

                if (logPredictionWarnings)
                {
                    var unityFrameCountString = unityFrameCount.ToString();
                    var serverTickString = serverTick.ToString();
                    if (predictedClient.ValueRO.InputCount == 0)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] No input to process for tick {serverTickString}.");
                    }
                    else if (predictedClient.ValueRO.LastProcessedServerTick != serverTick)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] Last processed tick not set to current. Current server tick {serverTickString}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }
                }
            }

            // Bots produce the same command stream as players, without a fake transport connection.
            foreach (var (enemy, input, health) in SystemAPI.Query<RefRO<DollSingerEnemy>,
                         RefRO<PlayerInputComponent>, RefRO<PredictedPlayerGhost>>().WithAll<Simulate>())
            {
                var entity = enemy.ValueRO.InputEntity;
                var predicted = EntityManager.GetComponentData<PredictedClientInput>(entity);
                predicted.BeginInputIndex = commands.Length;
                predicted.InputCount = health.ValueRO.CurrentHealth > 0 && (!health.ValueRO.ClashEnabled || health.ValueRO.ClashCanAct) && predicted.LastProcessedServerTick != serverTick ? 1 : 0;
                if (predicted.InputCount != 0)
                    commands.Add(new ClientCommandInput { Tick = new NetworkTick(serverTick), PlayerInput = input.ValueRO.Input });
                predicted.LastProcessedServerTick = serverTick;
                EntityManager.SetComponentData(entity, predicted);
            }

            ghostOwnerLookup.Update(this);
            m_PlayerGhostLookup.Update(this);
            m_PredictedClientInputComponentLookup.Update(this);

            var predictedClientInputComponentLookup = m_PredictedClientInputComponentLookup;
            var playerGhostLookup = m_PlayerGhostLookup;

            var projectileSpawnList = new NativeList<ProjectileSpawnData>(Allocator.Temp);
            var vfxSpawnList = new NativeList<VfxSpawnData>(Allocator.Temp);
            // The project disables automatic transform syncing. Query the current poses,
            // including newly spawned head hitboxes, rather than the previous physics step.
            UnityEngine.Physics.SyncTransforms();

            foreach (var (player, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithAll<Simulate>().WithEntityAccess())
                if (player.ValueRO.InventoryWeapons)
                {
                    var inventory = WeaponInventory(entity);
                    DollSingerWeapons.SyncEquipment(ref player.ValueRW, inventory, WeaponManager.Instance.WeaponRegistry);
                    player.ValueRW.CarriedWeight = MoonRaidMap.Active && !player.ValueRO.ClashEnabled ? CarriedWeight(inventory) : 0;
                }

            foreach (var predictedPlayer in SystemAPI.Query<RefRW<PredictedPlayerGhost>>()
                         .WithAll<Simulate>())
            {
                HaloModuleFire.AdvanceTimers(ref predictedPlayer.ValueRW, deltaTime);

                if (predictedPlayer.ValueRO.ControllerState.IsReloadingState)
                {
                    var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                        predictedPlayer.ValueRO.EquippedWeaponID, predictedPlayer.ValueRO.EquippedModules);
                    HaloReload.Tick(ref predictedPlayer.ValueRW, weaponData, deltaTime);
                }
            }

            // Resolve every actor's defense before any hitscan shot. Entity iteration order
            // must not decide whether this tick's held defense protects the target.
            foreach (var (defender, lookup) in SystemAPI.Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>>().WithAll<Simulate>())
            {
                bool held = false;
                if (lookup.ValueRO.ClientCommandInputEntity != Entity.Null && predictedClientInputComponentLookup.HasComponent(lookup.ValueRO.ClientCommandInputEntity))
                {
                    var pending = predictedClientInputComponentLookup[lookup.ValueRO.ClientCommandInputEntity];
                    if (pending.InputCount > 0 && commands[pending.BeginInputIndex + pending.InputCount - 1].TryGetPlayerMovementInput(defender.ValueRO.InputIndex, out var intent))
                        held = intent.HaloDefense && !intent.HaloTransfer && !intent.FireSuppressed && !intent.FreeLooking;
                }
                HaloEnergyRules.SetDefense(ref defender.ValueRW, held);
            }

            UpdateHaloTransfers(serverTick, deltaTime);
            UpdateHaloScans(serverTick, deltaTime, ref ecb);

            foreach (var (predictedPlayer, inputLookup,
                         entity)
                     in SystemAPI.Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>>()
                         .WithEntityAccess()
                         .WithAll<Simulate, GhostGameObjectLink>())
            {
                var ghostLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                var playerGhost = ghostLink.LinkedInstance.GetComponent<PlayerGhost>();
                if(inputLookup.ValueRO.ClientCommandInputEntity==Entity.Null || !predictedClientInputComponentLookup.HasComponent(inputLookup.ValueRO.ClientCommandInputEntity))continue;
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0)
                {
                    // get movement index for the last frame we are going to process
                    var commandInput = commands[predictedClient.BeginInputIndex + predictedClient.InputCount - 1];
                    if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input))
                    {
                        input=RaidHealth.RestrictInput(predictedPlayer.ValueRO,input);
                        playerGhost.ServerMovementInput = input;
                    }

                    predictedPlayer.ValueRW.ControllerState.Shoot = false;

                    bool switchedWeapon = DollSingerWeapons.TryEquip(ref predictedPlayer.ValueRW, input,
                        WeaponManager.Instance.WeaponRegistry);
                    if (switchedWeapon) DollSingerWeapons.SyncEquipment(ref predictedPlayer.ValueRW, WeaponInventory(entity), WeaponManager.Instance.WeaponRegistry);
                    var weaponData =
                        WeaponManager.Instance.WeaponRegistry.GetWeaponData(predictedPlayer.ValueRO.EquippedWeaponID, predictedPlayer.ValueRO.EquippedModules);
                    if (weaponData != null)
                    {
                        var shotRay = playerGhost.GetShotRay(input, weaponData.HitscanRange, out var aimPoint);
                        predictedPlayer.ValueRW.AimPoint = predictedPlayer.ValueRO.HaloDeploymentMode == HaloDeploymentMode.Sentry
                            ? SentryLookPoint(predictedPlayer.ValueRO) : (float3)aimPoint;
                        bool wantsToReload = input.Reload;
                        bool wantsToShoot = !switchedWeapon && input.Shoot && !input.FireSuppressed;
                        bool mustReload = (wantsToShoot || weaponData.AutoReloadWhenEmpty) && predictedPlayer.ValueRO.CurrentAmmo <= 0;

                        bool halo = DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID);
                        if (halo && wantsToReload && TryRechargeHalo(entity, ref predictedPlayer.ValueRW, weaponData))
                        {
                            predictedPlayer.ValueRW.LastReloadTick = serverTick;
                            HaloEnergyRules.RefreshAmmo(ref predictedPlayer.ValueRW, weaponData);
                        }
                        else if (!halo && HaloModuleFire.ReadyToReload(ref predictedPlayer.ValueRW, wantsToReload || mustReload, weaponData.MagazineSize))
                        {
                            HaloReload.Begin(ref predictedPlayer.ValueRW, weaponData, weaponData.MagazineSize);
                            predictedPlayer.ValueRW.LastReloadTick = serverTick;
                        }

                        bool sentryShot = TrySentryShot(entity, ref predictedPlayer.ValueRW, weaponData, serverTick, out var sentryRay);
                        if (sentryShot || HaloModuleFire.TryFire(ref predictedPlayer.ValueRW, weaponData, input, switchedWeapon, deltaTime))
                        {
                            if (sentryShot)
                            {
                                shotRay = sentryRay;
                                aimPoint = shotRay.GetPoint(weaponData.HitscanRange);
                                predictedPlayer.ValueRW.AimPoint = aimPoint;
                            }
                            HaloModuleFire.CommitShot(ref predictedPlayer.ValueRW, weaponData, deltaTime);
                            if (sentryShot) predictedPlayer.ValueRW.WeaponCooldown = 0;
                            HaloEnergyRules.CommitShot(ref predictedPlayer.ValueRW, weaponData);
                            predictedPlayer.ValueRW.LastShotTick = serverTick;
                            if (sentryShot) predictedPlayer.ValueRW.LastSentryShotTick = serverTick;

                            bool aimed = sentryShot || !DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID) || input.Aim;
                            if (weaponData.Type == WeaponType.Hitscan)
                            {
                                shotRay = new Ray(shotRay.origin, WeaponSpread.Direction(shotRay.direction, weaponData, 0,
                                    commandInput.Tick.TickIndexForValidTick, aimed));
                                aimPoint = shotRay.GetPoint(weaponData.HitscanRange);
                            }

                            var shooterNetworkId = ghostOwnerLookup[entity].NetworkId;
                            float3 eyePosition = shotRay.origin;
                            float3 aimDirection = shotRay.direction;
                            float3 shotOriginPosition = eyePosition;

                            if (VisualEffectManager.ServerInstance != null)
                            {
                                VisualEffectManager.ServerInstance.Server_RequestVfx(shooterNetworkId,
                                    predictedPlayer.ValueRO.EquippedWeaponID, aimPoint, serverTick, sentryShot, shotOriginPosition);
                            }

                            var shotSource = ResearchQuestRuntime.TagShot(World, entity,
                                DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID), sentryShot, BountyClashDamage.Capture(World, entity));
                            switch (weaponData.Type)
                            {
                                case WeaponType.Hitscan:
                                {
                                    // Use the calculated eyePosition and aimDirection
                                    if (playerGhost.RaycastShot(shotRay,weaponData.HitscanRange,s_HitscanLayerMask,out var hit))
                                    {
                                        if (hit.collider.gameObject.layer == LayerMask.NameToLayer("ServerPlayer"))
                                        {
                                            if (GhostGameObject.TryFindGhostGameObject(hit.collider.gameObject,
                                                    out var hitGhostObject) &&
                                                playerGhostLookup.HasComponent(hitGhostObject.LinkedEntity))
                                            {
                                                var targetPredictedPlayer =
                                                    playerGhostLookup.GetRefRW(hitGhostObject.LinkedEntity);
                                                var targetNetworkId = ghostOwnerLookup[hitGhostObject.LinkedEntity]
                                                    .NetworkId;

                                                if (BountyClashDamage.IsSelf(World, hitGhostObject.LinkedEntity, shotSource))
                                                {
                                                    //skip hitting self
                                                    continue;
                                                }

                                                var healthBeforeDamage = targetPredictedPlayer.ValueRO;
                                                var hitPlayer = hitGhostObject.GetComponent<PlayerGhost>();
                                                BountyClashDamage.Apply(World,hitGhostObject.LinkedEntity,shotSource,
                                                    RaidHealth.ResolveHit(hitPlayer, hit.collider, hit.point),
                                                    Ballistics.DamageAtDistance(weaponData, hit.distance),serverTick,RaidDamageKind.Bullet,(float3)hit.point);
                                                if (RaidHealth.ImpactFlags(healthBeforeDamage, targetPredictedPlayer.ValueRO) != 0 &&
                                                    ((HaloModule)predictedPlayer.ValueRO.EquippedModules & HaloModule.HitMarker) != 0)
                                                    VisualEffectManager.ServerInstance?.Server_RequestHaloMark(shooterNetworkId, commandInput.Tick.TickIndexForValidTick, hit.point);

                                                if (!targetPredictedPlayer.ValueRO.ClashEnabled && healthBeforeDamage.CurrentHealth > 0 &&
                                                    targetPredictedPlayer.ValueRO.CurrentHealth <= 0)
                                                {
                                                    if (LeaderboardManager.Instance != null)
                                                    {
                                                        Debug.Log(
                                                            $"[Server] Player {shooterNetworkId.ToString()} killed player {targetNetworkId.ToString()}.");
                                                    }
                                                    else
                                                    {
                                                        Debug.LogWarning(
                                                            "[Server] LeaderboardManager instance not found. Cannot add kill.");
                                                    }
                                                }
                                            }
                                        }

                                        if (weaponData.ProjectileHitVfxPrefab != null)
                                        {
                                            vfxSpawnList.Add(new VfxSpawnData()
                                            {
                                                Position = hit.point,
                                                Rotation = Quaternion.LookRotation(hit.normal),
                                                Prefab = GhostSpawner.FindGhostPrefabEntity(weaponData
                                                    .ProjectileHitVfxPrefab.GhostGuid)
                                            });
                                        }
                                    }

                                    break;
                                }
                                case WeaponType.Projectile:
                                {
                                    var prefabEntity =
                                        GhostSpawner.FindGhostPrefabEntity(weaponData.ProjectileGhostPrefab.GhostGuid);
                                    if (prefabEntity != Entity.Null)
                                    {
                                        for (int pellet = 0; pellet < Mathf.Clamp(weaponData.PelletCount, 1, 32); pellet++)
                                        {
                                            Vector3 pelletDirection = WeaponSpread.Direction(aimDirection, weaponData, pellet, commandInput.Tick.TickIndexForValidTick, aimed);
                                            Quaternion spawnRotation = Quaternion.LookRotation(pelletDirection);
                                            projectileSpawnList.Add(new ProjectileSpawnData
                                            {
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
                                    }

                                    break;
                                }
                            }
                        }
                    }
                }
            }

            foreach (var (player, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithAll<Simulate>().WithEntityAccess())
                if (player.ValueRO.InventoryWeapons) DollSingerWeapons.SaveActiveAmmo(ref player.ValueRW, WeaponInventory(entity));

            foreach (var spawnData in projectileSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    spawnData.Prefab,
                    spawnData.Position,
                    spawnData.Rotation,
                    GhostGameObject.GenerateRandomHash(),
                    1.0f,
                    (spawnedEntity, ecb2) =>
                    {
                        ecb2.SetComponent(spawnedEntity, new Projectile.ProjectileData
                        {
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
            }

            foreach (var vfxData in vfxSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    vfxData.Prefab,
                    vfxData.Position,
                    vfxData.Rotation,
                    GhostGameObject.GenerateRandomHash());
            }

            predictedClientInputComponentLookup.Update(this);

            foreach (var (predictedPlayer, inputLookup, controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink>()
                         .WithEntityAccess())
            {
                if(inputLookup.ValueRO.ClientCommandInputEntity==Entity.Null || !predictedClientInputComponentLookup.HasComponent(inputLookup.ValueRO.ClientCommandInputEntity))continue;
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0)
                {
                    float movementDt = deltaTime / predictedClient.InputCount;
                    for (int i = 0; i < predictedClient.InputCount; i++)
                    {
                        var commandInput = commands[i + predictedClient.BeginInputIndex];
                        if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input))
                        {
                            FirstPersonController.AccumulateMovement(ref predictedPlayer.ValueRW.ControllerState,
                                ref predictedPlayer.ValueRW.AccumulatedMovement,
                                RaidHealth.RestrictInput(predictedPlayer.ValueRO, input),
                                RaidHealth.Movement(predictedPlayer.ValueRO, controllerConsts.ValueRO.ControllerConsts),
                                movementDt, SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity).Controller,
                                predictedPlayer.ValueRO.MeleeWeapon != 0 && predictedPlayer.ValueRO.CurrentHealth > 0 && !predictedPlayer.ValueRO.ClashDarkSight);
                        }
                    }

                    if (predictedPlayer.ValueRW.ControllerState.JumpTriggered)
                    {
                        predictedPlayer.ValueRW.LastJumpTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.JumpTriggered = false;
                    }

                    if (predictedPlayer.ValueRW.ControllerState.LandTriggered)
                    {
                        predictedPlayer.ValueRW.LastLandTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.LandTriggered = false;
                    }
                }
            }

            foreach (var (transform,
                         predictedPlayer,
                         controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>()
                         .WithNone<GhostGameObjectDeferredActivation>()
                         .WithEntityAccess())
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                if (math.lengthsq(predictedPlayer.ValueRO.AccumulatedMovement) > 0f ||
                    controllerLink.Controller.NeedsLiftCarry(predictedPlayer.ValueRO.ControllerState))
                {
                    bool wasLanding = predictedPlayer.ValueRO.ControllerState.LandTriggered;
                    bool wasGrounded = predictedPlayer.ValueRO.ControllerState.MovementType == FirstPersonController.MovementType.Standing;
                    float3 beforePosition = predictedPlayer.ValueRO.ControllerState.CurrentPosition;
                    controllerLink.Controller.ApplyMovementUpdate(ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts.ValueRO.ControllerConsts,
                        predictedPlayer.ValueRO.AccumulatedMovement,
                        deltaTime);
                    if (MoonRaidMap.Active)
                    {
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

            foreach (var (transform, predictedPlayer, controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>()
                         .WithEntityAccess())
            {
                transform.ValueRW.Position = predictedPlayer.ValueRO.ControllerState.CurrentPosition;
                transform.ValueRW.Rotation = predictedPlayer.ValueRO.ControllerState.CurrentRotation;

                if (checkForPredictionErrors)
                {
                    if (MovementHistory.TryGetTick(serverTick, out var pos, out var rot))
                    {
                        // compare our calculation with the recorded
                        Debug.Assert(PlayerMovementHistory.HistoryMatches(pos,
                                rot,
                                predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                predictedPlayer.ValueRO.ControllerState.CurrentRotation),
                            $"[ServerPlayerMovementSystem] Mispredict detected at tick {serverTick} predicted pos {predictedPlayer.ValueRO.ControllerState.CurrentPosition} recorded {(float3)pos}");
                    }
                    else
                    {
                        // this is the first time we've processed this tick
                        // let's add our result
                        MovementHistory.Add(serverTick, predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                            predictedPlayer.ValueRO.ControllerState.CurrentRotation);
                    }
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            UnityEngine.Physics.SyncTransforms();

            s_PlayerMovementActive = false;
        }
    }
}
