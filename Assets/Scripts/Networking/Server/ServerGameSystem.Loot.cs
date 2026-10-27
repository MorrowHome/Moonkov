using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private RaidLootContainers LootContainers(ref SystemState state) => state.EntityManager.GetComponentObject<RaidLootContainers>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
        private void InitializeLootContainers(ref SystemState state)
        {
            var map=MoonRaidMap.Active;
            if(map==null)return;
            var caches=LootContainers(ref state);
            if(caches.Initialized && ReferenceEquals(caches.MapIdentity,map))return;
            caches.Initialize(map.LootPositions.Length,map.GetLootProfile,map);
            if (RaidFurnitureLoot.Active && RaidFurnitureLoot.Active.gameObject.scene == map.gameObject.scene)
                RaidFurnitureLoot.Active.Initialize(caches);
            foreach(var (session,connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess())
            {
                if(!state.EntityManager.HasComponent<RaidInventoryState>(connection))continue;
                var inventory=GetRaidInventory(ref state,connection);
                inventory.OpenedLootId=-1;inventory.CancelActions();inventory.ClearSearchRecords();inventory.LastSentVersion=-1;inventory.LastSentLootVersion=-1;
                inventory.NextDeathBagResync=0;
            }
        }
        private void RefreshLootContainers(ref SystemState state)
        {
            var map=MoonRaidMap.Active;
            if(map==null)return;
            var caches=LootContainers(ref state);
            double now=SystemAPI.Time.ElapsedTime;
            if(now<caches.NextRefillCheck)return;
            caches.NextRefillCheck=now+1;
            var protectedLoot=new System.Collections.Generic.HashSet<int>();
            foreach(var (session,connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess())
            {
                if(session.ValueRO.Phase!=RaidPhase.Active || !state.EntityManager.HasComponent<RaidInventoryState>(connection) ||
                    SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))continue;
                var inventory=GetRaidInventory(ref state,connection);
                if(inventory.OpenedLootId>=0)protectedLoot.Add(inventory.OpenedLootId);
                if(inventory.DissectingLootId>=0)protectedLoot.Add(inventory.DissectingLootId);
            }
            foreach(var (health,entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>>()
                        .WithAll<DollSingerEnemy,Simulate>().WithNone<ClashBot>().WithEntityAccess())
            {
                if(health.ValueRO.CurrentHealth<=0 || !state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity))continue;
                var brain=state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                if(PmcLootRules.SearchingCache(brain))protectedLoot.Add(brain.Cache);
            }
            caches.RecycleEmptyDrops(now,protectedLoot);
            if(ClashEnabled(ref state))return; // Keep empty-drop cleanup without replenishing Clash supplies.
            bool hasActivePlayer=false;
            foreach(var (session,joined,connection) in SystemAPI.Query<RefRO<RaidSession>,RefRO<JoinedClient>>().WithEntityAccess())
            {
                if(session.ValueRO.Phase==RaidPhase.Active && !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) &&
                    SystemAPI.HasComponent<NetworkId>(connection) && SystemAPI.GetComponent<NetworkId>(connection).Value>0 &&
                    TryGetLivingPosition(ref state,joined.ValueRO.PlayerEntity,out _))
                {hasActivePlayer=true;break;}
            }
            float radius=math.max(RaidRules.PickupRange,map.LootRefillPlayerRadius);
            foreach(var refill in caches.RefillStates)
            {
                if(!caches.Containers.TryGetValue(refill.Key,out var cache) || refill.Key>=map.LootPositions.Length)continue;
                bool blocked=!hasActivePlayer || protectedLoot.Contains(refill.Key);
                if(!blocked && !RaidLootRefillState.HasContents(cache))
                {
                    float3 target=map.LootPositions[refill.Key];
                    foreach(var (session,joined,connection) in SystemAPI.Query<RefRO<RaidSession>,RefRO<JoinedClient>>().WithEntityAccess())
                    {
                        if(session.ValueRO.Phase!=RaidPhase.Active || SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) ||
                            !TryGetLivingPosition(ref state,joined.ValueRO.PlayerEntity,out var position))continue;
                        if(math.distancesq(position,target)<=radius*radius ||
                            state.EntityManager.HasComponent<RaidInventoryState>(connection) && GetRaidInventory(ref state,connection).OpenedLootId==refill.Key)
                        {blocked=true;break;}
                    }
                }
                refill.Value.TryRefill(cache,now,map.LootRespawnSeconds,map.LootRefillLimit,blocked,caches.LootRandom);
            }
        }
        private void DropDeathInventory(ref SystemState state,Entity connection)
        {
            if(MoonRaidMap.Active==null || !state.EntityManager.HasComponent<RaidInventoryState>(connection) || !SystemAPI.HasComponent<JoinedClient>(connection))return;
            InitializeLootContainers(ref state);
            var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if(!SystemAPI.Exists(player) || !SystemAPI.HasComponent<LocalTransform>(player))return;
            var inventory=GetRaidInventory(ref state,connection);
            var pose=SystemAPI.GetComponent<LocalTransform>(player);
            var position=pose.Position;
            var rotation=pose.Rotation;
            if(UnityEngine.Physics.Raycast((Vector3)position+Vector3.up,Vector3.down,out var ground,4,
                LayerMask.GetMask("Default","Ground"),QueryTriggerInteraction.Ignore))
            {
                position=ground.point;
                rotation=Quaternion.FromToRotation(Vector3.up,ground.normal)*(Quaternion)pose.Rotation;
            }
            UpdateInventoryProtection(ref state,connection);
            LootContainers(ref state).Drop(inventory.Graph,position,rotation,SystemAPI.GetComponent<JoinedClient>(connection).CharacterIndex,SystemAPI.Time.ElapsedTime);
            inventory.OpenedLootId=-1;inventory.LastSentVersion=-1;
        }
        private bool CanAccessLoot(ref SystemState state,Entity connection,int lootId,RaidSession session)
        {
            var map=MoonRaidMap.Active;
            if(map==null || session.Phase!=RaidPhase.Active || !TryGetLootPosition(ref state,lootId,out var target) ||
                SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) || !SystemAPI.HasComponent<JoinedClient>(connection)) return false;
            var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if(!BountyClashRuntime.CanAct(state.EntityManager,player) || !TryGetLivingPosition(ref state,player,out var position) ||
                !BountyClashRuntime.InArena(state.EntityManager,position) || !BountyClashRuntime.InArena(state.EntityManager,target) || math.distancesq(position,target)>RaidRules.PickupRange*RaidRules.PickupRange) return false;
            return !UnityEngine.Physics.Linecast((Vector3)position+Vector3.up*1.4f,(Vector3)target,
                LayerMask.GetMask("Default","Ground"),QueryTriggerInteraction.Ignore);
        }
        private bool TryGetLootPosition(ref SystemState state,int id,out float3 position)
        {
            position=default;var map=MoonRaidMap.Active;
            if(map!=null && id>=0 && id<map.LootPositions.Length){position=map.LootPositions[id];return true;}
            if(LootContainers(ref state).Containers.ContainsKey(id) && LootContainers(ref state).FurnitureCaches.TryGetValue(id,out position))return true;
            if(LootContainers(ref state).MissionCaches.TryGetValue(id,out position))return true;
            if(!LootContainers(ref state).DeathBags.TryGetValue(id,out var corpse))return false;
            position=corpse.Position+new float3(0,.25f,0);return true;
        }
        private void SendDeathBags(ref SystemState state,EntityCommandBuffer ecb,Entity connection)
        {
            var loot=LootContainers(ref state);var inventory=GetRaidInventory(ref state,connection);
            bool resync=SystemAPI.Time.ElapsedTime>=inventory.NextDeathBagResync;
            if(resync)inventory.NextDeathBagResync=SystemAPI.Time.ElapsedTime+5;
            float3 position=default;
            bool living=SystemAPI.HasComponent<JoinedClient>(connection) &&
                TryGetLivingPosition(ref state,SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity,out position);
            inventory.HiddenDeathBags.Clear();
            foreach(var sent in inventory.SentDeathBags)
            {
                if(!loot.DeathBags.TryGetValue(sent.Key,out var bag) || bag.CharacterIndex==RaidLootContainers.DroppedItemCharacterIndex &&
                    (!living || math.distancesq(position,bag.Position)>RaidLootContainers.DropInterestRadius*RaidLootContainers.DropInterestRadius) &&
                    inventory.OpenedLootId!=sent.Key && inventory.DissectingLootId!=sent.Key)
                    inventory.HiddenDeathBags.Add(sent.Key);
            }
            foreach(int id in inventory.HiddenDeathBags)
            {
                var removed=ecb.CreateEntity();
                ecb.AddComponent(removed,new RaidGroundItemMarkerRpc {LootId=id,Sequence=++inventory.DeathBagSequence,Removed=true,Deleted=!loot.DeathBags.ContainsKey(id)});
                ecb.AddComponent(removed,new SendRpcCommandRequest {TargetConnection=connection});
                inventory.SentDeathBags.Remove(id);
                if(!loot.DeathBags.ContainsKey(id))inventory.SearchedLoot.Remove(id);
            }
            inventory.RemovedSearches.Clear();
            foreach(int id in inventory.SearchedLoot)
                if(id>=RaidLootContainers.FirstDeathBagId && id<RaidLootContainers.FirstMissionCacheId && !loot.DeathBags.ContainsKey(id))
                    inventory.RemovedSearches.Add(id);
            foreach(int id in inventory.RemovedSearches)inventory.SearchedLoot.Remove(id);
            foreach(var bag in loot.DeathBags)
            {
                if(bag.Value.CharacterIndex==RaidLootContainers.DroppedItemCharacterIndex &&
                    (!living || math.distancesq(position,bag.Value.Position)>RaidLootContainers.DropInterestRadius*RaidLootContainers.DropInterestRadius) &&
                    inventory.OpenedLootId!=bag.Key && inventory.DissectingLootId!=bag.Key)continue;
                var graph=loot.Containers[bag.Key];
                if(!resync && inventory.SentDeathBags.TryGetValue(bag.Key,out var sent) && sent==graph.Version)continue;
                inventory.SentDeathBags[bag.Key]=graph.Version;
                var rpc=ecb.CreateEntity();
                ecb.AddComponent(rpc,new RaidGroundItemMarkerRpc {Sequence=++inventory.DeathBagSequence,LootId=bag.Key,Version=graph.Version,Position=bag.Value.Position,
                    Rotation=bag.Value.Rotation,CharacterIndex=bag.Value.CharacterIndex,Age=(float)math.max(0,SystemAPI.Time.ElapsedTime-bag.Value.CreatedAt),
                    Empty=!RaidLootRefillState.HasContents(graph),
                    ItemCode=bag.Value.CharacterIndex==RaidLootContainers.DroppedItemCharacterIndex
                        ? new FixedString64Bytes(graph.Items.Find(item=>item.Parent==LootInventoryExchange.Root)?.Code ?? "") : default});
                ecb.AddComponent(rpc,new SendRpcCommandRequest {TargetConnection=connection});
            }
        }

        private void HandleLootRequests(ref SystemState state,EntityCommandBuffer ecb)
        {
            var caches=LootContainers(ref state);
            foreach(var (request,received,entity) in SystemAPI.Query<RefRO<RaidLootOpenRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection=received.ValueRO.SourceConnection;var r=request.ValueRO;
                if(SystemAPI.HasComponent<RaidSession>(connection) && state.EntityManager.HasComponent<RaidInventoryState>(connection))
                {
                    var session=SystemAPI.GetComponent<RaidSession>(connection);var inventory=GetRaidInventory(ref state,connection);
                    if(r.RequestId>inventory.RequestId)
                    {
                        inventory.RequestId=r.RequestId;
                        bool eligible=r.RaidId==session.RaidId && inventory.RaidId==r.RaidId && session.Phase==RaidPhase.Active &&
                            !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) && (r.LootId==-1 ||
                            !caches.IsEmptyDrop(r.LootId) && CanAccessLoot(ref state,connection,r.LootId,session));
                        bool weaponBusy=eligible && r.LootId>=0 && RaidHealth.HandsBusy(SystemAPI.GetComponent<PredictedPlayerGhost>(
                            SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity));
                        bool accepted=eligible && !weaponBusy;
                        if(accepted)
                        {
                            if(r.LootId<0)inventory.CloseLoot();
                            else
                            {
                                inventory.CancelActions(inventory.ActionPending ? "原操作已取消：切换搜刮目标；未消耗物品。" : null);
                                CancelManualMission(ref state,connection,"任务操作中断：开始搜索。");
                                inventory.OpenedLootId=r.LootId;
                            }
                            double now=SystemAPI.Time.ElapsedTime;
                            if(r.LootId>=0 && now>=inventory.NextLootNoiseAt)
                            {
                                var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                                var health=SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                                RaidLootAcoustics.Mark(ref health.ValueRW);
                                inventory.NextLootNoiseAt=now+RaidLootAcoustics.MinimumInterval;
                            }
                        }
                        if(accepted && r.LootId>=0 && inventory.NeedsSearch(caches,r.LootId))
                        {
                            var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                            inventory.BeginDissection(r.LootId,SystemAPI.Time.ElapsedTime,SystemAPI.GetComponent<LocalTransform>(player).Position,
                                SystemAPI.GetComponent<PredictedPlayerGhost>(player),
                                caches.RequiresDissection(r.LootId) ? AlienHarvest.DissectionSeconds : MoonRaidMap.Active.SearchSeconds,
                                caches.RefillGeneration(r.LootId));
                        }
                        if(weaponBusy)inventory.ActionFeedback="操作未开始：正在换弹或切换武器。";
                        inventory.Error=accepted ? InventoryError.None : InventoryError.Inaccessible;
                        inventory.LastSentVersion=-1;
                    }
                }
                ecb.DestroyEntity(entity);
            }
            foreach(var (request,received,entity) in SystemAPI.Query<RefRO<RaidLootMoveRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection=received.ValueRO.SourceConnection;var r=request.ValueRO;
                if(SystemAPI.HasComponent<RaidSession>(connection) && state.EntityManager.HasComponent<RaidInventoryState>(connection))
                {
                    var session=SystemAPI.GetComponentRW<RaidSession>(connection);var inventory=GetRaidInventory(ref state,connection);
                    if(r.RequestId>inventory.RequestId)
                    {
                        inventory.RequestId=r.RequestId;inventory.ActionFeedback=null;
                        UpdateInventoryProtection(ref state,connection);
                        bool chargeBlocked=ReloadWeaponMoveBlocked(ref state,connection,r.Operation,r.ItemId.ToString(),r.Parent.ToString(),r.Region.ToString());
                        inventory.Error=r.RaidId!=session.ValueRO.RaidId || inventory.RaidId!=r.RaidId || inventory.ActionPending || chargeBlocked || inventory.OpenedLootId!=r.LootId ||
                            !CanAccessLoot(ref state,connection,r.LootId,session.ValueRO) || caches.RequiresDissection(r.LootId) ||
                            !caches.Containers.TryGetValue(r.LootId,out var cache)
                            ? InventoryError.Inaccessible
                            : LootInventoryExchange.TryApply(inventory.Graph,cache,new InventoryCommand {ExpectedVersion=r.ExpectedVersion,Operation=r.Operation,
                                ItemId=r.ItemId.ToString(),Parent=r.Parent.ToString(),Region=r.Region.ToString(),TargetId=r.TargetId.ToString(),
                                X=r.X,Y=r.Y,Rotated=r.Rotated,Quantity=r.Quantity},r.ExpectedLootVersion);
                        if(chargeBlocked)inventory.ActionFeedback="充能中不能移动当前武器。";
                        if(inventory.Error==InventoryError.None)UpdateInventoryProtection(ref state,connection,true);
                        inventory.LastSentVersion=-1;
                        if(session.ValueRO.Phase==RaidPhase.Active)RaidInventoryState.UpdateTotals(inventory.Graph,ref session.ValueRW);
                    }
                }
                ecb.DestroyEntity(entity);
            }
        }
        private void RefreshLootAccess(ref SystemState state,Entity connection,RaidSession session)
        {
            var inventory=GetRaidInventory(ref state,connection);
            if(inventory.MedicalItemId!=null)
            {
                var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                bool accessible=session.Phase==RaidPhase.Active && inventory.RaidId==session.RaidId &&
                    BountyClashRuntime.CanAct(state.EntityManager,player) && TryGetLivingPosition(ref state,player,out _);
                var health=accessible ? SystemAPI.GetComponent<PredictedPlayerGhost>(player) : default;
                var position=accessible ? SystemAPI.GetComponent<LocalTransform>(player).Position : inventory.MedicalOrigin;
                inventory.RefreshMedical(ref health,accessible,position,SystemAPI.Time.ElapsedTime);
                if(accessible)SystemAPI.SetComponent(player,health);
            }
            if(inventory.DissectingLootId>=0)
            {
                int id=inventory.DissectingLootId;
                bool accessible=CanAccessLoot(ref state,connection,id,session) && inventory.RaidId==session.RaidId;
                var position=inventory.DissectionOrigin;var health=default(PredictedPlayerGhost);
                if(accessible)
                {
                    var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                    position=SystemAPI.GetComponent<LocalTransform>(player).Position;
                    health=SystemAPI.GetComponent<PredictedPlayerGhost>(player);
                }
                inventory.RefreshDissection(LootContainers(ref state),accessible,position,health,SystemAPI.Time.ElapsedTime);
            }
            if(inventory.OpenedLootId<0 || session.Phase==RaidPhase.Active && session.SnapshotTimer>0) return;
            if(CanAccessLoot(ref state,connection,inventory.OpenedLootId,session)) return;
            inventory.OpenedLootId=-1;inventory.Error=InventoryError.Inaccessible;inventory.LastSentVersion=-1;
        }
    }
}
