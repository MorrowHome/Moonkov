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
        private void DropDeathInventory(ref SystemState state,Entity connection)
        {
            if(MoonRaidMap.Active==null || !state.EntityManager.HasComponent<RaidInventoryState>(connection) || !SystemAPI.HasComponent<JoinedClient>(connection))return;
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
                        inventory.OpenedLootId=accepted ? r.LootId : -1;
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
                            !CanAccessLoot(ref state,connection,r.LootId,session.ValueRO) || !caches.Containers.TryGetValue(r.LootId,out var cache)
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
            if(inventory.OpenedLootId<0 || session.Phase==RaidPhase.Active && session.SnapshotTimer>0) return;
            if(CanAccessLoot(ref state,connection,inventory.OpenedLootId,session)) return;
            inventory.OpenedLootId=-1;inventory.Error=InventoryError.Inaccessible;inventory.LastSentVersion=-1;
        }
    }
}
