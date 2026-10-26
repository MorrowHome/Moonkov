using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        public static string LootCode(int index) => InventoryCatalog.LootCode(index);
        private RaidInventoryState SetRaidInventory(ref SystemState state, Entity connection, InventoryGraph graph, int raidId)
        {
            RaidInventoryState inventory;
            if (state.EntityManager.HasComponent<RaidInventoryState>(connection)) inventory = state.EntityManager.GetComponentObject<RaidInventoryState>(connection);
            else { inventory = new RaidInventoryState(); state.EntityManager.AddComponentObject(connection, inventory); }
            inventory.CancelActions();inventory.ClearSearchRecords();inventory.Result=null;
            inventory.SurvivalMotion=default;
            inventory.Graph = graph; inventory.RaidId=raidId; inventory.LastSentVersion = -1; inventory.OpenedLootId=-1;inventory.LastSentLootVersion=-1;
            inventory.MissionFirstsBefore = inventory.MissionFirstsSaved = Persistence(ref state).Profiles.TryGetValue(connection, out var profile) &&
                !string.IsNullOrEmpty(profile.InventoryJson) ? RaidInventoryTransport.Decode(profile.InventoryJson).MissionFirsts : 0;
            Persistence(ref state).Inventories[connection] = inventory;
            return inventory;
        }
        private RaidInventoryState GetRaidInventory(ref SystemState state, Entity connection)
        {
            return state.EntityManager.GetComponentObject<RaidInventoryState>(connection);
        }
        private static bool ReloadWeaponMoveBlocked(ref SystemState state,Entity connection,InventoryOperation operation,string itemId,string parent=null,string region=null)
        {
            var manager=state.EntityManager;
            if(!manager.Exists(connection) || !manager.HasComponent<JoinedClient>(connection)) return false;
            var player=manager.GetComponentData<JoinedClient>(connection).PlayerEntity;
            return manager.Exists(player) && manager.HasComponent<PredictedPlayerGhost>(player) &&
                DollSingerWeapons.BlocksReloadWeaponMove(manager.GetComponentData<PredictedPlayerGhost>(player),operation,itemId,parent,region);
        }
        private void EnsureRaidInventories(ref SystemState state)
        {
            // Structural additions happen before SystemAPI enumerators acquire component references.
            using var query=state.EntityManager.CreateEntityQuery(typeof(RaidSession));
            using var connections=query.ToEntityArray(Allocator.Temp);
            foreach (var connection in connections)
            {
                var session=state.EntityManager.GetComponentData<RaidSession>(connection);
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection) && GetRaidInventory(ref state,connection).RaidId==session.RaidId) continue;
            var graph=InventoryGraph.Create(stash:false); graph.AddSupply("cells",session.Cells);
            if (!session.PersistentDeployment)
            { graph.AddSupply("medkit", 2); graph.AddSupply("ration", 2); graph.AddSupply("water", 2); graph.AddSupply("oxygen_canister", 1); graph.AddSupply("energy_grenade", 2); }
                SetRaidInventory(ref state,connection,graph,session.RaidId);
            }
        }
        private void HandleInventoryRequests(ref SystemState state, EntityCommandBuffer ecb)
        {
            foreach(var (request,received,entity) in SystemAPI.Query<RefRO<RaidActionCancelRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection=received.ValueRO.SourceConnection;var r=request.ValueRO;
                if(SystemAPI.HasComponent<RaidSession>(connection) && state.EntityManager.HasComponent<RaidInventoryState>(connection))
                {
                    var inventory=GetRaidInventory(ref state,connection);
                    if(r.RaidId==inventory.RaidId && r.RequestId>inventory.RequestId)
                    {inventory.RequestId=r.RequestId;inventory.CancelActions("已取消操作；未消耗物品或获得收益。");inventory.Error=InventoryError.None;}
                }
                ecb.DestroyEntity(entity);
            }
            foreach(var (request,received,entity) in SystemAPI.Query<RefRO<RaidInventoryDropRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection=received.ValueRO.SourceConnection;var r=request.ValueRO;
                if(SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection))
                {
                    var inventory=GetRaidInventory(ref state,connection);var session=SystemAPI.GetComponentRW<RaidSession>(connection);
                    if(r.RequestId>inventory.RequestId)
                    {
                        inventory.RequestId=r.RequestId;inventory.ActionFeedback=null;inventory.Error=InventoryError.Inaccessible;
                        var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if(session.ValueRO.Phase==RaidPhase.Active && r.RaidId==session.ValueRO.RaidId && r.RaidId==inventory.RaidId &&
                            !inventory.ActionPending && !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) &&
                            BountyClashRuntime.CanAct(state.EntityManager,player) && TryGetLivingPosition(ref state,player,out var position))
                        {
                            UpdateInventoryProtection(ref state,connection);
                            if(ReloadWeaponMoveBlocked(ref state,connection,InventoryOperation.Move,r.ItemId.ToString()))
                                inventory.ActionFeedback="充能中不能移动当前武器。";
                            else if(TryFindDropGround(position,out var grounded))
                            {
                                inventory.Error=LootContainers(ref state).DropItem(inventory.Graph,r.ItemId.ToString(),r.ExpectedVersion,
                                    grounded,SystemAPI.Time.ElapsedTime,out _);
                                if(inventory.Error==InventoryError.None)UpdateInventoryProtection(ref state,connection,true);
                            }
                            else inventory.ActionFeedback="无法丢弃：脚下没有安全、可达的地面；物品仍在原容器。";
                        }
                        inventory.LastSentVersion=-1;RaidInventoryState.UpdateTotals(inventory.Graph,ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidConsumableUseRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection) && !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection); var r = request.ValueRO;
                    // The source connection owns the player. Consumption shares the
                    // same monotonic request stream as medical use and item moves.
                    if (r.RequestId > inventory.RequestId)
                    {
                        inventory.RequestId = r.RequestId; inventory.ActionFeedback=null; inventory.Error = InventoryError.Inaccessible;
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if (session.Phase == RaidPhase.Active && session.RaidId == r.RaidId && inventory.RaidId == r.RaidId && !inventory.ActionPending &&
                            SystemAPI.Exists(player) && SystemAPI.HasComponent<PredictedPlayerGhost>(player) && BountyClashRuntime.CanAct(state.EntityManager,player))
                        {
                            var ghost = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                            string itemId = r.ItemId.ToString();
                            var item = inventory.Graph.Find(itemId);
                            if(item?.Code=="cells")
                                inventory.Error=EnergyShieldInventory.Recharge(ref ghost.ValueRW,inventory.Graph,itemId,r.ExpectedVersion);
                            else if(ConsumableCatalog.Get(item?.Code)?.Oxygen>0)
                                inventory.Error=RaidOxygenConsumables.Consume(ref ghost.ValueRW,inventory.Graph,itemId,r.ExpectedVersion);
                            else if(TryGetLivingPosition(ref state,player,out var usePosition))
                            {
                                inventory.Error=inventory.BeginConsumable(ghost.ValueRO,itemId,r.ExpectedVersion,usePosition,SystemAPI.Time.ElapsedTime);
                                if(inventory.Error==InventoryError.None)
                                    CancelManualMission(ref state,connection,"任务操作中断：开始使用补给；维修或下载进度未完成。");
                                else if(RaidHealth.HandsBusy(ghost.ValueRO))inventory.ActionFeedback="操作未开始：正在换弹或切换武器。";
                            }
                            EnergyShieldEquipment.Flush(ref ghost.ValueRW,inventory.Graph);
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidMedicalUseRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection) &&
                    state.EntityManager.HasComponent<RaidInventoryState>(connection) && !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    var inventory = GetRaidInventory(ref state, connection);
                    var r = request.ValueRO;
                    // Request IDs share the inventory operation stream. Delayed/replayed
                    // intents cannot heal again, including after respawn/deployment.
                    if (r.RequestId > inventory.RequestId)
                    {
                        inventory.RequestId = r.RequestId;inventory.ActionFeedback=null;
                        inventory.Error = InventoryError.Inaccessible;
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        if (session.Phase == RaidPhase.Active && session.RaidId == r.RaidId &&
                            inventory.RaidId == r.RaidId && BountyClashRuntime.CanAct(state.EntityManager,player) && TryGetLivingPosition(ref state,player,out var medicalPosition))
                        {
                            var ghost = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                            inventory.Error = inventory.BeginMedical(ghost.ValueRO,r.ItemId.ToString(),r.ExpectedVersion,r.Part,
                                medicalPosition,SystemAPI.Time.ElapsedTime,MoonRaidMap.Active.MedicalSeconds);
                            if(inventory.Error==InventoryError.None)
                                CancelManualMission(ref state,connection,"任务操作中断：开始治疗；维修或下载进度未完成。");
                            else if(RaidHealth.HandsBusy(ghost.ValueRO))inventory.ActionFeedback="操作未开始：正在换弹或切换武器。";
                        }
                        inventory.LastSentVersion = -1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidInventoryMoveRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                if (SystemAPI.HasComponent<RaidSession>(connection))
                {
                    var session = SystemAPI.GetComponentRW<RaidSession>(connection); var inventory = GetRaidInventory(ref state, connection); var r = request.ValueRO;
                    if (r.RequestId > inventory.RequestId)
                    {
                        inventory.RequestId = r.RequestId;inventory.ActionFeedback=null;
                        UpdateInventoryProtection(ref state,connection);
                        bool chargeBlocked=ReloadWeaponMoveBlocked(ref state,connection,r.Operation,r.ItemId.ToString(),r.Parent.ToString(),r.Region.ToString());
                        inventory.Error = session.ValueRO.Phase != RaidPhase.Active || session.ValueRO.RaidId != r.RaidId || inventory.ActionPending ||
                            SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) || chargeBlocked ||
                            (BountyClashRuntime.Enabled(state.EntityManager) && (!SystemAPI.HasComponent<JoinedClient>(connection) ||
                                !BountyClashRuntime.CanAct(state.EntityManager,SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity))) ? InventoryError.Inaccessible
                            : inventory.Graph.TryApply(new InventoryCommand { ExpectedVersion=r.ExpectedVersion, Operation=r.Operation, ItemId=r.ItemId.ToString(),
                                Parent=r.Parent.ToString(), Region=r.Region.ToString(), TargetId=r.TargetId.ToString(), X=r.X, Y=r.Y, Rotated=r.Rotated, Quantity=r.Quantity });
                        if(chargeBlocked)inventory.ActionFeedback="充能中不能移动当前武器。";
                        if(inventory.Error==InventoryError.None)UpdateInventoryProtection(ref state,connection,true);
                        inventory.LastSentVersion = -1;
                        if(session.ValueRO.Phase==RaidPhase.Active)RaidInventoryState.UpdateTotals(inventory.Graph, ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
        }
        public static bool TryFindDropGround(Unity.Mathematics.float3 origin,out Unity.Mathematics.float3 position)
        {
            position=default;
            if(!Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(origin)))return false;
            var source=(UnityEngine.Vector3)origin;
            int mask=UnityEngine.LayerMask.GetMask("Default","Ground");
            // Only the nearby surface directly under the player is eligible. A lower
            // floor reached through a gap, or a long jump, must not consume inventory.
            if(!UnityEngine.Physics.Raycast(source+UnityEngine.Vector3.up*.3f,UnityEngine.Vector3.down,out var ground,1.55f,
                mask,UnityEngine.QueryTriggerInteraction.Ignore) || ground.normal.y<.65f ||
                ground.point.y>source.y+.15f || ground.point.y<source.y-1.25f)return false;
            var target=ground.point+UnityEngine.Vector3.up*.2f;
            if(UnityEngine.Physics.Linecast(source+UnityEngine.Vector3.up*.3f,target,mask,UnityEngine.QueryTriggerInteraction.Ignore) ||
                UnityEngine.Physics.CheckSphere(target,.12f,mask,UnityEngine.QueryTriggerInteraction.Ignore))return false;
            for(int i=0;i<4;i++)
            {
                var offset=i==0 ? UnityEngine.Vector3.right : i==1 ? UnityEngine.Vector3.left :
                    i==2 ? UnityEngine.Vector3.forward : UnityEngine.Vector3.back;
                if(!UnityEngine.Physics.Raycast(ground.point+offset*.18f+UnityEngine.Vector3.up*.3f,UnityEngine.Vector3.down,
                    out var support,.5f,mask,UnityEngine.QueryTriggerInteraction.Ignore) || support.normal.y<.65f ||
                    UnityEngine.Mathf.Abs(support.point.y-ground.point.y)>.18f)return false;
            }
            position=ground.point;return true;
        }
        private void SendInventory(ref SystemState state, EntityCommandBuffer ecb, Entity connection, int raidId)
        {
            var inventory = GetRaidInventory(ref state, connection);
            InventoryGraph cache=null;
            if(inventory.OpenedLootId>=0)LootContainers(ref state).Containers.TryGetValue(inventory.OpenedLootId,out cache);
            int lootVersion=cache?.Version ?? 0;
            if (inventory.LastSentVersion == inventory.Graph.Version && inventory.LastSentLootVersion==lootVersion) return;
            inventory.LastSentVersion = inventory.Graph.Version; inventory.Sequence++;
            inventory.LastSentLootVersion=lootVersion;
            var snapshot=cache==null ? inventory.Graph : LootInventoryExchange.Snapshot(inventory.Graph,cache);
            RaidCorpseRecord corpse = null;
            if (cache != null) LootContainers(ref state).DeathBags.TryGetValue(inventory.OpenedLootId, out corpse);
            var json = RaidInventoryTransport.EncodeSnapshot(snapshot,cache==null ? -1 : inventory.OpenedLootId,lootVersion,
                inventory.DissectingLootId,(float)System.Math.Max(0,inventory.DissectionUntil-SystemAPI.Time.ElapsedTime),
                inventory.MedicalItemId,(float)System.Math.Max(0,inventory.MedicalUntil-SystemAPI.Time.ElapsedTime),inventory.ActionFeedback,
                corpse?.CharacterIndex ?? -1, corpse?.EquipmentSlots,inventory.Result,inventory.MedicalIsConsumable);
            const int fragmentSize = RaidInventoryTransport.ChunkCharacters;
            int count = (json.Length + fragmentSize - 1) / fragmentSize;
            for (int index = 0; index < count; index++)
            {
                var entity = ecb.CreateEntity(); ecb.AddComponent(entity, new RaidInventoryChunkV2Rpc { RaidId=raidId, Sequence=inventory.Sequence,
                    RequestId=inventory.RequestId, Error=inventory.Error, Index=index, Count=count,
                    Json=json.Substring(index * fragmentSize, System.Math.Min(fragmentSize, json.Length-index*fragmentSize)) });
                ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection=connection });
            }
        }
    }
}
