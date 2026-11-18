// 客户端为本机角色先按输入算移动和部分武器状态。NetCode 可以从服务器快照恢复后重跑若干 tick，这里会按每次预测所处的 tick 重新取操作。
// #define PREDICTION_DEBUG_LOGGING
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.MP_FPS;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial class PlayerPredictionSystem : SingletonSystem<PlayerPredictionSystem> {
    // logging and diagnostics
    private static uint s_PlayerMovementTick;
    private static readonly int s_HitscanLayerMask = ~LayerMask.GetMask("ClientPlayer");
    private static readonly int s_ProjectileTargetLayerMask = ~LayerMask.GetMask("ClientPlayer", "ServerPlayer");
    public static uint PlayerMovementTick => s_PlayerMovementTick;
    private int m_LastSeenUnityFrame;

    [FeatureToggle(Name = "prediction_checkforpredictionerrors", Default = false)]
    public static readonly FeatureToggle CheckForPredictionErrors;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() { s_PlayerMovementTick = 0; }

    private NativeList<ClientCommandInput> m_ProcessedClientInputCommands;

    protected override void OnCreate() {
        base.OnCreate();
        // 一次预测过程中要整理本机指令。这个原生列表按 World 生命周期复用，每拍清空内容，World 退场时才释放内存。
        m_ProcessedClientInputCommands = new NativeList<ClientCommandInput>(32, Allocator.Persistent);
    }

    protected override void OnDestroy() {
        base.OnDestroy();
        m_ProcessedClientInputCommands.Dispose();
        var query = GetEntityQuery(typeof(PlayerControllerLink));
        foreach (var entity in query.ToEntityArray(Allocator.Temp)) {
            var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
            controllerLink.Controller = null;
            EntityManager.SetComponentData(entity, controllerLink);
        }
    }

    protected override void OnUpdate() {
        var networkTime = SystemAPI.GetSingleton<NetworkTime>();
        // 这是本次预测正在算的 tick。发输入时则用 ClientInputSenderSystem 里的 InputTargetTick，延迟高时两者可能对不上。
        uint tick = networkTime.ServerTick.TickIndexForValidTick;
        bool logPredictionWarnings = CheckForPredictionErrors.IsEnabled;
        if (logPredictionWarnings && UnityEngine.Time.frameCount > m_LastSeenUnityFrame && m_LastSeenUnityFrame != 0 &&
            UnityEngine.Time.frameCount - m_LastSeenUnityFrame > 1) {
            string frameCount = UnityEngine.Time.frameCount.ToString();
            Debug.LogWarning(
                $"[{frameCount}] PlayerPredictionSystem missed a Unity frame. Current frame {frameCount} last processed frame {m_LastSeenUnityFrame.ToString()} - server tick {tick.ToString()}");
        }
        m_LastSeenUnityFrame = UnityEngine.Time.frameCount;

        // if (UILoadingScreen.Instance.IsLoading())
        // {
        //     return;
        // }
        // 一个画面帧可能重算不止一拍，也可能只算当前拍的一部分。先记下这是预测的哪一步，用它安排读输入、移动和最终动画。
        bool isFirstPredictionTick = networkTime.IsFirstPredictionTick;
        bool isFinalPredictionTick = networkTime.IsFinalPredictionTick;
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        float dt = World.Time.DeltaTime;
        float frameDT = UnityEngine.Time.deltaTime;
        s_PlayerMovementTick = tick;
        // Simulate 表示 NetCode 让这个对象在当前预测拍参与运算。先推进冷却和已开始的装填，才能判断这一拍新操作是否符合时序。
        foreach (var predictedPlayer in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithAll<Simulate>()) {
            HaloModuleFire.AdvanceTimers(ref predictedPlayer.ValueRW, dt);
            if (predictedPlayer.ValueRO.ControllerState.IsReloadingState) {
                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                    predictedPlayer.ValueRO.EquippedWeaponID, predictedPlayer.ValueRO.EquippedModules);
                HaloReload.Tick(ref predictedPlayer.ValueRW, weaponData, dt);
            }
        }

        // ensure controller has been initialised
        foreach (var (predictedPlayer, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>().WithEntityAccess()
                     // 预测移动需要场景里的角色控制器。等桥接物体生成后把它的引用补到实体上，后面的查询就可以直接取。
                     .WithAll<Simulate, GhostGameObjectLink>().WithNone<PlayerControllerLink>()) {
            var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
            if (gameObjectLink.LinkedInstance != null &&
                gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller))
                ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
        }

        // pull out the client input
        // we process the input and create a list of commandInputs that need to be processed by the movement code
        // the movement code processes the inputs in order
        // 先收集这一拍需要的输入，再统一推进角色。这样输入实体和实际的角色实体可以分别生成、分别查询。
        m_ProcessedClientInputCommands.Clear();
        var commands = m_ProcessedClientInputCommands;
        var clientCommandInputBufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>(true);
        foreach (var (predictedClient, entity) in SystemAPI.Query<RefRW<PredictedClientInput>>().WithEntityAccess()
                     // 只收这一台机器掌管的输入 Ghost。别的玩家主要通过服务器快照在这里呈现。
                     .WithAll<GhostOwnerIsLocal, Simulate>()) {
            // GameDebug.BurstLog("[PlayerPredictionSystem] PredictedClientInput is Simulate");
            if (!clientCommandInputBufferLookup.TryGetBuffer(entity, out var inputCommands) ||
                inputCommands.Length == 0)
                continue;
            if (isFirstPredictionTick) {
                // 预测批次刚开始时，把本机处理起点设到当前拍之前。随后根据这次要模拟的 tick 从历史缓冲读指令。
                predictedClient.ValueRW.LastProcessedServerTick = tick - 1;

                // the server might have missed some input if the snapshot is running behind
                // we need to re-process some of the input the server has missed in this snapshot
                for (uint i = predictedClient.ValueRO.LastProcessedServerTick + 1; i < tick; i++)
                    if (inputCommands.GetDataAtTick(new NetworkTick(i), out var missedCommand)) {
                        if (logPredictionWarnings)
                            Debug.LogWarning(
                                $"[PlayerPredictionSystem] Adding missed command for tick {missedCommand.Tick.TickIndexForValidTick.ToString()} (current is {tick.ToString()})");
                        commands.Add(missedCommand);
                    }

                // and we need to get the latest tick to process
                // if (inputCommands.GetDataAtTick(new NetworkTick(tick), out var inputCommand))
                // {
                //     ClientCommandInput input = inputCommand;
                //     commands.Add(input);
                // }
            }

            // else
            // {
            // 按预测 tick 到缓冲找对应的指令。重跑过去的预测时也能读到当时留下的操作。
            if (inputCommands.GetDataAtTick(new NetworkTick(tick), out var inputCommand)) {
                var input = inputCommand;
                commands.Add(input);
                // commands.Add(inputCommand);
            }
            //}
        }
        bool allowTickBatching = AllowPredictedTickBatching();
        foreach (var (predictedPlayer, controllerConsts, entity) in SystemAPI
                     .Query<RefRW<PredictedPlayerGhost>, RefRO<PredictedPlayerControllerConsts>>().WithEntityAccess()
                     .WithAll<Simulate, PlayerControllerLink>()) {
            var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
            // GameDebug.BurstLog("[PlayerPredictionSystem] PredictedPlayerGhost is Simulate");
            predictedPlayer.ValueRW.RequestApplyMovement = false;
            if (commands.Length > 0) {
                float accumulateDT = dt;

                // process input
                for (int i = 0; i < commands.Length; i++) {
                    var commandInput = commands[i];
                    if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRW.InputIndex, out var input)) {
                        input = RaidHealth.RestrictInput(predictedPlayer.ValueRO, input);
                        HaloEnergyRules.SetDefense(ref predictedPlayer.ValueRW,
                            input.HaloDefense && !input.FireSuppressed && !input.FreeLooking);
                        predictedPlayer.ValueRW.LocalLookYawPitchDegrees = input.LookYawPitchDegrees;
                        // 先让换装请求更新手里武器，再查该武器和模块的配置。切换这一拍会影响能不能开枪以及弹量记在哪一格。
                        bool switchedWeapon = DollSingerWeapons.TryEquip(ref predictedPlayer.ValueRW, input,
                            WeaponManager.Instance.WeaponRegistry);
                        var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                            predictedPlayer.ValueRO.EquippedWeaponID, predictedPlayer.ValueRO.EquippedModules);
                        if (weaponData != null) {
                            var playerGhost = controllerLink.Controller.GetComponent<PlayerGhost>();
                            // 相机交来瞄准方向，角色控制器拿自己的发射位置与武器距离算出效果射线。实际伤害仍由服务器碰撞结算。
                            var shotRay = playerGhost.GetShotRay(input, weaponData.HitscanRange, out var aimPoint);
                            predictedPlayer.ValueRW.AimPoint = aimPoint;
                            bool wantsToReload = input.Reload;
                            bool wantsToShoot = !switchedWeapon && input.Shoot && !input.FireSuppressed;
                            // 普通武器可以先预测装填。光环装填需要服务器预留可用能量并同步目标弹量，看到同步状态后再推进它的装填计时。
                            bool mustReload = (wantsToShoot || weaponData.AutoReloadWhenEmpty) &&
                                              predictedPlayer.ValueRO.CurrentAmmo <= 0;
                            if ((wantsToReload || mustReload) && !GrenadeHandling.Occupied(predictedPlayer.ValueRO) &&
                                // The server reserves accessible energy and synchronizes the target ammo.
                                // Predicting one without that reservation causes empty/full ammo oscillation.
                                !DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID) &&
                                !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                                predictedPlayer.ValueRO.CurrentAmmo < weaponData.MagazineSize) {
                                HaloReload.Begin(ref predictedPlayer.ValueRW, weaponData, weaponData.MagazineSize);
                                predictedPlayer.ValueRW.LastReloadTick = commandInput.Tick.TickIndexForValidTick;
                            }
                            // 本机应用和服务器通用的开火规则，通过后推进预测中的冷却、光环能量和开火编号。收到快照后这些状态可被校正。
                            if (HaloModuleFire.TryFire(ref predictedPlayer.ValueRW, weaponData, input, switchedWeapon,
                                    dt / commands.Length)) {
                                HaloModuleFire.CommitShot(ref predictedPlayer.ValueRW, weaponData, dt);
                                HaloEnergyRules.CommitShot(ref predictedPlayer.ValueRW, weaponData);
                                predictedPlayer.ValueRW.LastShotTick = commandInput.Tick.TickIndexForValidTick;
                                bool aimed = !DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID) ||
                                             input.Aim;
                                // 散布用武器、瞄准状态和开火的 tick 计算。预测重跑同一发时，能得到与第一次一致的偏移。
                                if (weaponData.Type == WeaponType.Hitscan) {
                                    shotRay = new Ray(shotRay.origin,
                                        WeaponSpread.Direction(shotRay.direction, weaponData, 0,
                                            commandInput.Tick.TickIndexForValidTick, aimed));
                                    aimPoint = shotRay.GetPoint(weaponData.HitscanRange);
                                }
                                float3 eyePosition = shotRay.origin;
                                float3 aimDirection = shotRay.direction;
                                var shotOriginPosition = shotRay.origin;

                                // 重跑 tick 仍要计算弹量、冷却等预测数据。光环弹的可见特效只在该 tick 首次完整预测时生成，避免一发在屏幕上变成多发。
                                // Replayed/partial predictions must not create another visible halo bolt.
                                if (VisualEffectManager.ClientInstance != null &&
                                    (!DollSingerWeapons.IsHalo(predictedPlayer.ValueRO.EquippedWeaponID) ||
                                     networkTime.IsFirstTimeFullyPredictingTick))
                                    VisualEffectManager.ClientInstance.SpawnMuzzleFlash(playerGhost,
                                        predictedPlayer.ValueRO.EquippedWeaponID, true, aimPoint,
                                        predictedPlayer.ValueRO.LastShotTick);
                                if (weaponData.Type == WeaponType.Hitscan) {
                                    if (playerGhost.RaycastShot(shotRay, weaponData.HitscanRange, s_HitscanLayerMask,
                                            out var cosmeticHit)) {
                                        Debug.DrawLine(shotOriginPosition, cosmeticHit.point, Color.yellow, 0.3f);
                                    } else {
                                        Vector3 endPoint = eyePosition + aimDirection * weaponData.HitscanRange;
                                        Debug.DrawLine(shotOriginPosition, endPoint, Color.cyan, 0.3f);
                                    }
                                // 飞行弹体第一次完整预测才有自己的本机物体。服务器以后同步同一发时，可以根据身份接管原来的表现。
                                } else if (weaponData.Type == WeaponType.Projectile &&
                                           networkTime.IsFirstTimeFullyPredictingTick) {
                                    for (int pellet = 0;
                                         pellet < Mathf.Clamp(weaponData.PelletCount, 1, 32);
                                         pellet++) {
                                        var spawnRotation = Quaternion.LookRotation(
                                            WeaponSpread.Direction(shotRay.direction, weaponData, pellet,
                                                commandInput.Tick.TickIndexForValidTick, aimed));
                                        controllerLink.Controller.SpawnPredictedProjectile(
                                            commandInput.Tick.TickIndexForValidTick,
                                            predictedPlayer.ValueRO.EquippedWeaponID, shotOriginPosition, spawnRotation,
                                            pellet, predictedPlayer.ValueRO.EquippedModules);
                                    }
                                }
                            }
                        }
                        // 先把手里武器变化过的弹量写回对应槽，再让控制器按输入和受伤后的移动能力累加这一拍的位移。
                        DollSingerWeapons.StoreAmmo(ref predictedPlayer.ValueRW);
                        FirstPersonController.AccumulateMovement(ref predictedPlayer.ValueRW.ControllerState,
                            ref predictedPlayer.ValueRW.AccumulatedMovement,
                            RaidHealth.RestrictInput(predictedPlayer.ValueRO, input),
                            RaidHealth.Movement(predictedPlayer.ValueRO, controllerConsts.ValueRO.ControllerConsts),
                            accumulateDT, controllerLink.Controller,
                            predictedPlayer.ValueRO.MeleeWeapon != 0 && predictedPlayer.ValueRO.CurrentHealth > 0 &&
                            !predictedPlayer.ValueRO.ClashDarkSight);
                        if (predictedPlayer.ValueRW.ControllerState.JumpTriggered) {
                            predictedPlayer.ValueRW.LastJumpTick = commandInput.Tick.TickIndexForValidTick;
                            predictedPlayer.ValueRW.ControllerState.JumpTriggered = false;
                        }
                        if (predictedPlayer.ValueRW.ControllerState.LandTriggered) {
                            predictedPlayer.ValueRW.LastLandTick = commandInput.Tick.TickIndexForValidTick;
                            predictedPlayer.ValueRW.ControllerState.LandTriggered = false;
                        }
                    }
                }

                // apply
                // if tick batching is not allowed then we need to request an apply every tick
                // otherwise it will happen on the last tick anyway
                // 默认每拍应用移动，因为位移请求还要过真实碰撞。开启批处理时可以攒几拍，在这一帧的最后一步应用。
                predictedPlayer.ValueRW.RequestApplyMovement = !allowTickBatching;
            } else if (logPredictionWarnings) {
                Debug.LogWarning("[PlayerPredictionSystem] Player is simulated but there are no commands to process");
            }
        }
        float applyDT = allowTickBatching ? frameDT : dt;
        bool checkForPredictionErrors = CheckForPredictionErrors.IsEnabled;
        var predictedPlayerGhostStatesBufferLookup = SystemAPI.GetBufferLookup<PredictedPlayerGhostState>(true);
        foreach (var (predictedPlayer, transform, playerControllerConsts, entity) in SystemAPI
                     .Query<RefRW<PredictedPlayerGhost>, RefRO<LocalTransform>,
                         RefRO<PredictedPlayerControllerConsts>>().WithAll<Simulate, PlayerControllerLink>()
                     .WithEntityAccess()) {
            var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
            var controllerConsts = playerControllerConsts.ValueRO.ControllerConsts;
            var predictedPlayerGhostStates = predictedPlayerGhostStatesBufferLookup[entity];
            // 是否允许把多个 tick 的位移攒起来，由开关控制。应用位移时复用角色控制器，最后一次预测再写动画，画面就能接上这一帧算出的姿势。
            if (isFinalPredictionTick || predictedPlayer.ValueRO.RequestApplyMovement) {
                if (math.lengthsq(predictedPlayer.ValueRO.AccumulatedMovement) > 0f ||
                    controllerLink.Controller.NeedsLiftCarry(predictedPlayer.ValueRO.ControllerState)) {
                    controllerLink.Controller.ApplyMovementUpdate(ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts, predictedPlayer.ValueRO.AccumulatedMovement, applyDT);
                    // 本机同时有服务器时，可用功能开关比对同一完整 tick 的位置。最后一步可能只模拟了部分时间，跳过它才能做同一时间的对照。
                    if (checkForPredictionErrors && !isFinalPredictionTick)
                        // check for prediction accuracy
                        // we avoid the final tick as it's almost certainly partial
                        // we want to check we are reconstructing the server ticks correctly
                        if (ServerPlayerMovementSystem.TryGetInstance(out var serverPlayerMovementSystem)) {
                            if (serverPlayerMovementSystem.MovementHistory.TryGetTick(tick, out var pos, out var rot))
                                // compare our calculation with the recorded
                                Debug.Assert(
                                    PlayerMovementHistory.HistoryMatches(pos, rot,
                                        predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                        predictedPlayer.ValueRO.ControllerState.CurrentRotation),
                                    $"[PlayerPredictionSystem] Mispredict detected at tick {tick} predicted pos {predictedPlayer.ValueRO.ControllerState.CurrentPosition} recorded {(float3)pos}");
                            else
                                // this is the first time we've processed this tick
                                // let's add our result
                                serverPlayerMovementSystem.MovementHistory.Add(tick,
                                    predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                    predictedPlayer.ValueRO.ControllerState.CurrentRotation);
                        }
                    predictedPlayer.ValueRW.AccumulatedMovement = float3.zero;
                } else {
                    // we should just apply our current state
                    // 没有要执行的位移时也同步控制器的位置和方向，并重新检查着地，避免采用上一拍的过期接触状态。
                    controllerLink.Controller.ApplyPosRotImmediate(predictedPlayer.ValueRO.ControllerState);
                    controllerLink.Controller.GroundedCheck(ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts);
                }
            }
            if (isFinalPredictionTick)
                controllerLink.Controller.ApplyAnimatorState(in predictedPlayer.ValueRO.ControllerState,
                    controllerConsts, frameDT);
        }
        // 遍历结束才把新角色控制器链接写入 World，避免枚举过程中途改变匹配对象。
        ecb.Playback(EntityManager);
    }

    [FeatureToggle(Name = "prediction_enabletickbatching", Default = false)]
    private static readonly FeatureToggle EnableTickBatching;

    private static bool AllowPredictedTickBatching() { return EnableTickBatching.IsEnabled; }

    // private void DisablePrediction()
    // {
    //     // Fetch the singleton as RW as we're modifying singleton collection data.
    //     ref var ghostPredictionSwitchingQueues = ref SystemAPI.GetSingletonRW<GhostOwnerPredictedSwitchingQueue>().ValueRW;
    //
    //     if (PlayerGhostManager.TryGetClientInstance(out var playerGhostManager) && playerGhostManager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players))
    //     {
    //         foreach (var player in players)
    //         {
    //             ghostPredictionSwitchingQueues.SwitchOwnerQueue.Enqueue(new OwnerSwithchingEntry {TargetEntity = player.GhostGameObject.LinkedEntity, CurrentOwner = player.GhostGameObject.Owner, NewOwner = -1});
    //         }
    //     }
    // }
    //
    // private void EnablePrediction()
    // {
    //     // Fetch the singleton as RW as we're modifying singleton collection data.
    //     ref var ghostPredictionSwitchingQueues = ref SystemAPI.GetSingletonRW<GhostOwnerPredictedSwitchingQueue>().ValueRW;
    //
    //     if (PlayerGhostManager.TryGetClientInstance(out var playerGhostManager) && playerGhostManager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players))
    //     {
    //         foreach (var player in players)
    //         {
    //             ghostPredictionSwitchingQueues.SwitchOwnerQueue.Enqueue(new OwnerSwithchingEntry {TargetEntity = player.GhostGameObject.LinkedEntity, CurrentOwner = -1, NewOwner = player.GhostGameObject.Owner});
    //         }
    //     }
    // }
#if !NGS_SUBMISSION_BUILD
    [UnityEngine.Scripting.Preserve]
    public static void DisablePrediction(string[] args) {
        //Instance.DisablePrediction();
    }

    [UnityEngine.Scripting.Preserve]
    public static void EnablePrediction(string[] args) {
        //Instance.EnablePrediction();
    }
#endif
}