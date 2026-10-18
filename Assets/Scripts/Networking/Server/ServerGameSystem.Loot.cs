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
            foreach(var (session,connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess())
            {
                if(!state.EntityManager.HasComponent<RaidInventoryState>(connection))continue;
                var inventory=GetRaidInventory(ref state,connection);
                inventory.OpenedLootId=-1;inventory.DissectingLootId=-1;inventory.LastSentVersion=-1;inventory.LastSentLootVersion=-1;
                inventory.SentDeathBags.Clear();
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
                bool blocked=!hasActivePlayer;
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
            LootContainers(ref state).Drop(inventory.Graph,position,rotation,SystemAPI.GetComponent<JoinedClient>(connection).CharacterIndex,SystemAPI.Time.ElapsedTime);
            inventory.OpenedLootId=-1;inventory.LastSentVersion=-1;
        }
        private bool CanAccessLoot(ref SystemState state,Entity connection,int lootId,RaidSession session)
        {
            var map=MoonRaidMap.Active;
            if(map==null || session.Phase!=RaidPhase.Active || !TryGetLootPosition(ref state,lootId,out var target) ||
                SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) || !SystemAPI.HasComponent<JoinedClient>(connection)) return false;
            var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if(!TryGetLivingPosition(ref state,player,out var position) || math.distancesq(position,target)>RaidRules.PickupRange*RaidRules.PickupRange) return false;
            return !UnityEngine.Physics.Linecast((Vector3)position+Vector3.up*1.4f,(Vector3)target,
                LayerMask.GetMask("Default","Ground"),QueryTriggerInteraction.Ignore);
        }
        private bool TryGetLootPosition(ref SystemState state,int id,out float3 position)
        {
            position=default;var map=MoonRaidMap.Active;
            if(map!=null && id>=0 && id<map.LootPositions.Length){position=map.LootPositions[id];return true;}
            if(!LootContainers(ref state).DeathBags.TryGetValue(id,out var corpse))return false;
            position=corpse.Position+new float3(0,.25f,0);return true;
        }
        private void SendDeathBags(ref SystemState state,EntityCommandBuffer ecb,Entity connection)
        {
            var loot=LootContainers(ref state);var inventory=GetRaidInventory(ref state,connection);
            foreach(var bag in loot.DeathBags)
            {
                var graph=loot.Containers[bag.Key];
                if(inventory.SentDeathBags.TryGetValue(bag.Key,out var sent) && sent==graph.Version)continue;
                inventory.SentDeathBags[bag.Key]=graph.Version;
                var rpc=ecb.CreateEntity();
                ecb.AddComponent(rpc,new RaidCorpseRpc {LootId=bag.Key,Version=graph.Version,Position=bag.Value.Position,
                    Rotation=bag.Value.Rotation,CharacterIndex=bag.Value.CharacterIndex,Age=(float)math.max(0,SystemAPI.Time.ElapsedTime-bag.Value.CreatedAt),
                    Empty=!graph.Items.Exists(i=>i.Parent==LootInventoryExchange.Root)});
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
                        bool accepted=r.RaidId==session.RaidId && (r.LootId==-1 || CanAccessLoot(ref state,connection,r.LootId,session));
                        inventory.DissectingLootId=-1;
                        inventory.OpenedLootId=accepted ? r.LootId : -1;
                        if(accepted && caches.RequiresDissection(r.LootId))
                        {
                            var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                            inventory.BeginDissection(r.LootId,SystemAPI.Time.ElapsedTime,SystemAPI.GetComponent<LocalTransform>(player).Position,
                                SystemAPI.GetComponent<PredictedPlayerGhost>(player).LastHitTick);
                        }
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
                        inventory.RequestId=r.RequestId;
                        inventory.Error=r.RaidId!=session.ValueRO.RaidId || inventory.OpenedLootId!=r.LootId ||
                            !CanAccessLoot(ref state,connection,r.LootId,session.ValueRO) || caches.RequiresDissection(r.LootId) ||
                            !caches.Containers.TryGetValue(r.LootId,out var cache)
                            ? InventoryError.Inaccessible
                            : LootInventoryExchange.TryApply(inventory.Graph,cache,new InventoryCommand {ExpectedVersion=r.ExpectedVersion,Operation=r.Operation,
                                ItemId=r.ItemId.ToString(),Parent=r.Parent.ToString(),Region=r.Region.ToString(),TargetId=r.TargetId.ToString(),
                                X=r.X,Y=r.Y,Rotated=r.Rotated,Quantity=r.Quantity},r.ExpectedLootVersion);
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
            if(inventory.DissectingLootId>=0)
            {
                int id=inventory.DissectingLootId;
                bool accessible=CanAccessLoot(ref state,connection,id,session) && inventory.RaidId==session.RaidId;
                var position=inventory.DissectionOrigin;uint damageTick=inventory.DissectionDamageTick;
                if(accessible)
                {
                    var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                    position=SystemAPI.GetComponent<LocalTransform>(player).Position;
                    damageTick=SystemAPI.GetComponent<PredictedPlayerGhost>(player).LastHitTick;
                }
                inventory.RefreshDissection(LootContainers(ref state),accessible,position,damageTick,SystemAPI.Time.ElapsedTime);
            }
            if(inventory.OpenedLootId<0 || session.Phase==RaidPhase.Active && session.SnapshotTimer>0) return;
            if(CanAccessLoot(ref state,connection,inventory.OpenedLootId,session)) return;
            inventory.OpenedLootId=-1;inventory.Error=InventoryError.Inaccessible;inventory.LastSentVersion=-1;
        }
    }
}
