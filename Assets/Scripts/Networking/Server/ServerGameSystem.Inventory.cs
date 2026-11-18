// 服务端处理局内背包的权威状态。拖格、丢弃、治疗和补给等请求都要查本局与库存，成功结果和失败原因会随库存快照回客户端。
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public partial struct ServerGameSystem {
        public static string LootCode(int index) { return InventoryCatalog.LootCode(index); }

        private RaidInventoryState SetRaidInventory(
            ref SystemState state,
            Entity connection,
            InventoryGraph graph,
            int raidId) {
            RaidInventoryState inventory;
            // 库存跟服务器实际的连接走。开始新局时复用容器对象，但清掉前一局的动作、搜索记录和结算清单。
            if (state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                inventory = state.EntityManager.GetComponentObject<RaidInventoryState>(connection);
            } else {
                inventory = new RaidInventoryState();
                state.EntityManager.AddComponentObject(connection, inventory);
            }
            inventory.CancelActions();
            inventory.ClearSearchRecords();
            inventory.Result = null;
            inventory.SurvivalMotion = default;
            inventory.Graph = graph;
            inventory.RaidId = raidId;
            inventory.LastSentVersion = -1;
            inventory.OpenedLootId = -1;
            inventory.LastSentLootVersion = -1;
            inventory.MissionFirstsBefore = inventory.MissionFirstsSaved =
                Persistence(ref state).Profiles.TryGetValue(connection, out var profile) &&
                !string.IsNullOrEmpty(profile.InventoryJson)
                    ? RaidInventoryTransport.Decode(profile.InventoryJson).MissionFirsts
                    : 0;
            // 持久化要能在连接消失前找到同一份随身物品。这里登记对象引用，场内操作和最终写盘读取的就是同一份。
            Persistence(ref state).Inventories[connection] = inventory;
            return inventory;
        }

        private RaidInventoryState GetRaidInventory(ref SystemState state, Entity connection) {
            return state.EntityManager.GetComponentObject<RaidInventoryState>(connection);
        }

        private static bool ReloadWeaponMoveBlocked(
            ref SystemState state,
            Entity connection,
            InventoryOperation operation,
            string itemId,
            string parent = null,
            string region = null) {
            var manager = state.EntityManager;
            if (!manager.Exists(connection) || !manager.HasComponent<JoinedClient>(connection)) return false;
            var player = manager.GetComponentData<JoinedClient>(connection).PlayerEntity;
            return manager.Exists(player) && manager.HasComponent<PredictedPlayerGhost>(player) &&
                   DollSingerWeapons.BlocksReloadWeaponMove(manager.GetComponentData<PredictedPlayerGhost>(player),
                       operation, itemId, parent, region);
        }

        private void EnsureRaidInventories(ref SystemState state) {
            // 在 SystemAPI 开始持有组件引用并遍历之前，完成增添组件等结构改动。
            using var query = state.EntityManager.CreateEntityQuery(typeof(RaidSession));
            // 给缺库存状态的连接补组件前先留一份实体列表。直接在正在遍历的 ECS 查询里加组件会破坏已有引用。
            using var connections = query.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections) {
                var session = state.EntityManager.GetComponentData<RaidSession>(connection);
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection) &&
                    GetRaidInventory(ref state, connection).RaidId == session.RaidId)
                    continue;
                var graph = InventoryGraph.Create(false);
                graph.AddSupply("cells", session.Cells);
                // 开发用的非持久出战可以直接发临时物资。正式账号出装需要已经由档案确认，不能拿这批临时物资替代正式扣装。
                if (!session.PersistentDeployment) {
                    graph.AddSupply("medkit", 2);
                    graph.AddSupply("ration", 2);
                    graph.AddSupply("water", 2);
                    graph.AddSupply("oxygen_canister", 1);
                    graph.AddSupply("energy_grenade", 2);
                }
                SetRaidInventory(ref state, connection, graph, session.RaidId);
            }
        }

        private void HandleInventoryRequests(ref SystemState state, EntityCommandBuffer ecb, bool discard = false) {
            // 取消也走同一个请求编号。新取消到达后，之前排在它前面的重复操作都不能再补执行。
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidActionCancelRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (SystemAPI.HasComponent<RaidSession>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                    var inventory = GetRaidInventory(ref state, connection);
                    if (r.RaidId == inventory.RaidId && r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        inventory.CancelActions("已取消操作；未消耗物品或获得收益。");
                        inventory.Error = InventoryError.None;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            // 丢弃从发包连接找实际背包与活角色。确定动作不被读条占用、位置合法、还是这一局后才允许把所属权转到场景。
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidInventoryDropRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (SystemAPI.HasComponent<RaidSession>(connection) &&
                    SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                    var inventory = GetRaidInventory(ref state, connection);
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection);
                    if (r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        inventory.ActionFeedback = null;
                        inventory.Error = InventoryError.Inaccessible;
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if (session.ValueRO.Phase == RaidPhase.Active && r.RaidId == session.ValueRO.RaidId &&
                            r.RaidId == inventory.RaidId && !inventory.InventoryMoveBlocked &&
                            !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) &&
                            BountyClashRuntime.CanAct(state.EntityManager, player) &&
                            TryGetLivingPosition(ref state, player, out var position)) {
                            UpdateInventoryProtection(ref state, connection);
                            // 充能或换手的武器还会被进行中的动作访问，先拒绝移出它。普通丢弃成功后再同步装备给角色。
                            if (ReloadWeaponMoveBlocked(ref state, connection, InventoryOperation.Move,
                                    r.ItemId.ToString())) {
                                inventory.ActionFeedback = "充能中不能移动当前武器。";
                            // 临时玩法可直接丢弃，不生成可被别人带走的地面包。仍用同一套事务预备代码来核对版本并移走整棵物品树。
                            } else if (discard) {
                                inventory.Error = LootInventoryExchange.PrepareDrop(inventory.Graph,
                                    r.ItemId.ToString(), r.ExpectedVersion, out var remaining, out _);
                                if (inventory.Error == InventoryError.None) {
                                    inventory.Graph = remaining;
                                    UpdateInventoryProtection(ref state, connection, true);
                                }
                            // 远征要找人在附近可达的落点。找不到先返回失败，这时物品还在原容器里。
                            } else if (TryFindDropGround(position, out var grounded)) {
                                inventory.Error = LootContainers(ref state).DropItem(inventory.Graph,
                                    r.ItemId.ToString(), r.ExpectedVersion, grounded, SystemAPI.Time.ElapsedTime,
                                    out _);
                                if (inventory.Error == InventoryError.None) {
                                    ResearchQuestRuntime.InventoryChanged(state.World, connection,
                                        session.ValueRO.RaidId);
                                    UpdateInventoryProtection(ref state, connection, true);
                                }
                            } else { inventory.ActionFeedback = "无法丢弃：脚下没有安全、可达的地面；物品仍在原容器。"; }
                        }
                        inventory.LastSentVersion = -1;
                        RaidInventoryState.UpdateTotals(inventory.Graph, ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
            // 使用物品、治疗和拖放共用递增的请求号，防止延迟的旧操作在手部已切换时又生效一次。
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidConsumableUseRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) &&
                    SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection) &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection);
                    var r = request.ValueRO;
                    // 玩家归属由实际发包连接确定。使用补给与
                    // 治疗、移动物品共用只增不减的请求编号。
                    if (r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        inventory.ActionFeedback = null;
                        inventory.Error = InventoryError.Inaccessible;
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if (session.Phase == RaidPhase.Active && session.RaidId == r.RaidId &&
                            inventory.RaidId == r.RaidId && !inventory.ActionPending && SystemAPI.Exists(player) &&
                            SystemAPI.HasComponent<PredictedPlayerGhost>(player) &&
                            BountyClashRuntime.CanAct(state.EntityManager, player)) {
                            var ghost = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                            string itemId = r.ItemId.ToString();
                            var item = inventory.Graph.Find(itemId);
                            // 电池走光环充能规则并更新当前武器的装填事件。食物和氧气罐走下方的延时消耗。
                            if (item?.Code == "cells") {
                                var weapon =
                                    WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(
                                        ghost.ValueRO.EquippedWeaponID, ghost.ValueRO.EquippedModules);
                                inventory.Error = HaloEnergyInventory.Recharge(ref ghost.ValueRW, inventory.Graph,
                                    itemId, r.ExpectedVersion, weapon);
                                if (inventory.Error == InventoryError.None)
                                    ghost.ValueRW.LastReloadTick = SystemAPI.GetSingleton<NetworkTime>().ServerTick
                                        .TickIndexForValidTick;
                            } else if (TryGetLivingPosition(ref state, player, out var usePosition)) {
                                // 请求通过只代表开始读条，实际消耗还要等完整步骤结束。先标记正在使用，给手部音效和移动限制读取。
                                inventory.Error = inventory.BeginConsumable(ghost.ValueRO, itemId, r.ExpectedVersion,
                                    usePosition, SystemAPI.Time.ElapsedTime);
                                if (inventory.Error == InventoryError.None) {
                                    ghost.ValueRW.ConsumingSupply = true;
                                    RaidConsumableAcoustics.Mark(ref ghost.ValueRW);
                                    CancelManualMission(ref state, connection, "任务操作中断：开始使用补给；维修或下载进度未完成。");
                                } else if (RaidHealth.HandsBusy(ghost.ValueRO)) {
                                    inventory.ActionFeedback = "操作未开始：正在换弹或切换武器。";
                                }
                            }
                            EnergyShieldEquipment.Flush(ref ghost.ValueRW, inventory.Graph);
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidMedicalUseRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) &&
                    SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection) &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection);
                    var r = request.ValueRO;
                    // 各库存操作共用一套请求号。延迟或重复送达的
                    // 请求不能再次治疗，重生或再次出战后也一样。
                    if (r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        inventory.ActionFeedback = null;
                        inventory.Error = InventoryError.Inaccessible;
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if (session.Phase == RaidPhase.Active && session.RaidId == r.RaidId &&
                            inventory.RaidId == r.RaidId && BountyClashRuntime.CanAct(state.EntityManager, player) &&
                            TryGetLivingPosition(ref state, player, out var medicalPosition)) {
                            var ghost = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                            // 治疗记录当时的目标部位、物品 ID 和库存版本。读条结束时再从仍存在的同一件物品结算。
                            inventory.Error = inventory.BeginMedical(ghost.ValueRO, r.ItemId.ToString(),
                                r.ExpectedVersion, r.Part, medicalPosition, SystemAPI.Time.ElapsedTime,
                                MoonRaidMap.Active.MedicalSeconds);
                            if (inventory.Error == InventoryError.None) {
                                RaidConsumableAcoustics.Mark(ref ghost.ValueRW);
                                CancelManualMission(ref state, connection, "任务操作中断：开始治疗；维修或下载进度未完成。");
                            } else if (RaidHealth.HandsBusy(ghost.ValueRO)) {
                                inventory.ActionFeedback = "操作未开始：正在换弹或切换武器。";
                            }
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            // 移动请求从实际发包连接找背包，先核对这局和请求编号。InventoryGraph 再检查库存版本、格子和装备规则，客户端的预览不能直接改背包。
            foreach (var (request, received, entity) in SystemAPI
                         .Query<RefRO<RaidInventoryMoveRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection)) {
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection);
                    var r = request.ValueRO;
                    if (r.RequestId > inventory.RequestId) {
                        inventory.RequestId = r.RequestId;
                        inventory.ActionFeedback = null;
                        UpdateInventoryProtection(ref state, connection);
                        bool chargeBlocked = ReloadWeaponMoveBlocked(ref state, connection, r.Operation,
                            r.ItemId.ToString(), r.Parent.ToString(), r.Region.ToString());
                        inventory.Error =
                            session.ValueRO.Phase != RaidPhase.Active || session.ValueRO.RaidId != r.RaidId ||
                            inventory.InventoryMoveBlocked ||
                            SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) || chargeBlocked ||
                            (BountyClashRuntime.Enabled(state.EntityManager) &&
                             (!SystemAPI.HasComponent<JoinedClient>(connection) ||
                              !BountyClashRuntime.CanAct(state.EntityManager,
                                  SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity)))
                                ? InventoryError.Inaccessible
                                : inventory.Graph.TryApply(new InventoryCommand {
                                    ExpectedVersion = r.ExpectedVersion, Operation = r.Operation,
                                    ItemId = r.ItemId.ToString(),
                                    Parent = r.Parent.ToString(), Region = r.Region.ToString(),
                                    TargetId = r.TargetId.ToString(), X = r.X, Y = r.Y, Rotated = r.Rotated,
                                    Quantity = r.Quantity
                                });
                        if (chargeBlocked) inventory.ActionFeedback = "充能中不能移动当前武器。";
                        if (inventory.Error == InventoryError.None) {
                            ResearchQuestRuntime.InventoryChanged(state.World, connection, session.ValueRO.RaidId);
                            UpdateInventoryProtection(ref state, connection, true);
                        }
                        inventory.LastSentVersion = -1;
                        if (session.ValueRO.Phase == RaidPhase.Active)
                            RaidInventoryState.UpdateTotals(inventory.Graph, ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
        }

        public static bool TryFindDropGround(Mathematics.float3 origin, out Mathematics.float3 position) {
            position = default;
            if (!Mathematics.math.all(Mathematics.math.isfinite(origin))) return false;
            var source = (UnityEngine.Vector3)origin;
            int mask = UnityEngine.LayerMask.GetMask("Default", "Ground");
            // 只允许丢到角色正下方的邻近表面。跳跃很远，
            // 或隔着缺口射线打到下一层时，不能先扣背包物品。
            if (!UnityEngine.Physics.Raycast(source + UnityEngine.Vector3.up * .3f, UnityEngine.Vector3.down,
                    out var ground, 1.55f, mask, UnityEngine.QueryTriggerInteraction.Ignore) ||
                ground.normal.y < .65f || ground.point.y > source.y + .15f || ground.point.y < source.y - 1.25f)
                return false;
            var target = ground.point + UnityEngine.Vector3.up * .2f;
            if (UnityEngine.Physics.Linecast(source + UnityEngine.Vector3.up * .3f, target, mask,
                    UnityEngine.QueryTriggerInteraction.Ignore) ||
                UnityEngine.Physics.CheckSphere(target, .12f, mask, UnityEngine.QueryTriggerInteraction.Ignore))
                return false;
            for (int i = 0; i < 4; i++) {
                var offset = i == 0 ? UnityEngine.Vector3.right :
                    i == 1 ? UnityEngine.Vector3.left :
                    i == 2 ? UnityEngine.Vector3.forward : UnityEngine.Vector3.back;
                if (!UnityEngine.Physics.Raycast(ground.point + offset * .18f + UnityEngine.Vector3.up * .3f,
                        UnityEngine.Vector3.down, out var support, .5f, mask,
                        UnityEngine.QueryTriggerInteraction.Ignore) || support.normal.y < .65f ||
                    UnityEngine.Mathf.Abs(support.point.y - ground.point.y) > .18f)
                    return false;
            }
            position = ground.point;
            return true;
        }

        // 包里有什么、现在看的是哪个箱子，以及请求是否成功，统一做成一份快照。包与箱的版本都没变时可跳过，较大的 JSON 要拆成多个 RPC。
        private void SendInventory(ref SystemState state, EntityCommandBuffer ecb, Entity connection, int raidId) {
            var inventory = GetRaidInventory(ref state, connection);
            InventoryGraph cache = null;
            if (inventory.OpenedLootId >= 0)
                LootContainers(ref state).Containers.TryGetValue(inventory.OpenedLootId, out cache);
            int viewing = inventory.OpenedLootId;
            int searchTotal = -1;
            if (inventory.DissectingLootId >= 0 && LootContainers(ref state).Containers
                    .TryGetValue(inventory.DissectingLootId, out var searching)) {
                viewing = inventory.DissectingLootId;
                searchTotal = searching.Children(LootInventoryExchange.Root).Count();
                cache = searching;
            }
            int lootVersion = cache?.Version ?? 0;
            // 随身背包和搜刮箱都有版本。只有两边上次内容都没变且没有强制重发，才跳过本次序列化。
            if (inventory.LastSentVersion == inventory.Graph.Version && inventory.LastSentLootVersion == lootVersion)
                return;
            inventory.LastSentVersion = inventory.Graph.Version;
            inventory.Sequence++;
            inventory.LastSentLootVersion = lootVersion;
            var visible = cache != null && searchTotal >= 0 ? inventory.SearchPreview(cache) : cache;
            // 如果仍在搜索，临时快照只能带看得见的箱物品。已打开箱或尸体的全内容也只是供该玩家显示的合并图。
            var snapshot = visible == null ? inventory.Graph : LootInventoryExchange.Snapshot(inventory.Graph, visible);
            RaidCorpseRecord corpse = null;
            if (cache != null && searchTotal < 0)
                LootContainers(ref state).DeathBags.TryGetValue(inventory.OpenedLootId, out corpse);
            string json = RaidInventoryTransport.EncodeSnapshot(snapshot, cache == null ? -1 : viewing, lootVersion,
                inventory.DissectingLootId, inventory.SearchItemRemaining(SystemAPI.Time.ElapsedTime),
                inventory.MedicalItemId, (float)System.Math.Max(0, inventory.MedicalUntil - SystemAPI.Time.ElapsedTime),
                inventory.ActionFeedback, corpse?.CharacterIndex ?? -1, corpse?.EquipmentSlots, inventory.Result,
                inventory.MedicalIsConsumable, searchTotal, searchTotal < 0
                    ? null
                    : cache.Children(LootInventoryExchange.Root).Select(item => {
                        var definition = InventoryCatalog.Get(item.Code);
                        return new RaidSearchFootprint {
                            Id = item.Id, Region = item.Region, X = item.X, Y = item.Y,
                            Width = item.Rotated ? definition.Height : definition.Width,
                            Height = item.Rotated ? definition.Width : definition.Height
                        };
                    }).ToList(), searchTotal < 0 ? 0 : inventory.SearchSecondsPerItem);
            inventory.RecordProtectionSnapshot();
            // 同一份快照的所有分片带相同序号、请求回执和总块数。客户端能等齐后一次替换，不会先看到半份背包。
            const int fragmentSize = RaidInventoryTransport.ChunkCharacters;
            int count = (json.Length + fragmentSize - 1) / fragmentSize;
            for (int index = 0; index < count; index++) {
                var entity = ecb.CreateEntity();
                ecb.AddComponent(entity, new RaidInventoryChunkV2Rpc {
                    RaidId = raidId, Sequence = inventory.Sequence,
                    RequestId = inventory.RequestId, Error = inventory.Error, Index = index, Count = count,
                    Json = json.Substring(index * fragmentSize,
                        System.Math.Min(fragmentSize, json.Length - index * fragmentSize))
                });
                ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }
    }
}