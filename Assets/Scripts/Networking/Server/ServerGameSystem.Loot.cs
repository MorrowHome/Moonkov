using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private RaidLootContainers LootContainers(ref SystemState state) => state.EntityManager.GetComponentObject<RaidLootContainers>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
        private bool CanAccessLoot(ref SystemState state,Entity connection,int lootId,RaidSession session)
        {
            var map=MoonRaidMap.Active;
            if(map==null || session.Phase!=RaidPhase.Active || lootId<0 || lootId>=map.LootPositions.Length ||
                SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection) || !SystemAPI.HasComponent<JoinedClient>(connection)) return false;
            var player=SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
            if(!TryGetLivingPosition(ref state,player,out var position) || math.distancesq(position,map.LootPositions[lootId])>RaidRules.PickupRange*RaidRules.PickupRange) return false;
            return !UnityEngine.Physics.Linecast((Vector3)position+Vector3.up*1.4f,map.LootPositions[lootId],
                LayerMask.GetMask("Default","Ground"),QueryTriggerInteraction.Ignore);
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
                        RaidInventoryState.UpdateTotals(inventory.Graph,ref session.ValueRW);
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
