using System;
using System.Reflection;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;

public static class InventoryTrafficReview
{
    delegate void Protection(ref ServerGameSystem server, ref SystemState state);
    delegate void Send(ref ServerGameSystem server, ref SystemState state, EntityCommandBuffer ecb, Entity connection, int raid);
    public static object Run()
    {
        using var world = new World("Inventory bandwidth review, no transport", WorldFlags.GameServer);
        var em = world.EntityManager;
        var serverHandle = world.GetOrCreateSystem<ServerGameSystem>();
        var protection = (Protection)typeof(ServerGameSystem).GetMethod("UpdateProtectionGear", BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Protection));
        var send = (Send)typeof(ServerGameSystem).GetMethod("SendInventory", BindingFlags.Instance|BindingFlags.NonPublic).CreateDelegate(typeof(Send));
        var graph = InventoryGraph.Create(false);
        graph.Items.Add(new InventoryItem { Id=Guid.NewGuid().ToString("D"), Code="helmet", Parent="equipment", Region="Helmet", SuitOxygen=100 });
        graph.AddSupply("cells",6); graph.AddSupply("medkit",2); graph.AddSupply("ration",2); graph.AddSupply("water",2);
        var inventory = new RaidInventoryState { Graph=graph, RaidId=7 };
        var actor = em.CreateEntity(typeof(PredictedPlayerGhost));
        var health = default(PredictedPlayerGhost); RaidHealth.Initialize(ref health); em.SetComponentData(actor,health);
        var connection=em.CreateEntity(typeof(RaidSession),typeof(JoinedClient));
        em.SetComponentData(connection,new RaidSession{RaidId=7,Phase=RaidPhase.Active});
        em.SetComponentData(connection,new JoinedClient{PlayerEntity=actor}); em.AddComponentObject(connection,inventory);
        int initialBytes=0, initialFragments=0, repeatedBytes=0, repeatedFragments=0;
        int checks=0;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks++;}
        using var chunks=em.CreateEntityQuery(typeof(RaidInventoryChunkV2Rpc));
        for(int step=0; step<=120; step++)
        {
            world.SetTime(new TimeData(step*.5,.5f));
            ref var server=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(serverHandle);
            ref var state=ref world.Unmanaged.ResolveSystemStateRef(serverHandle);
            protection(ref server,ref state);
            using var ecb=new EntityCommandBuffer(Allocator.Temp);
            send(ref server,ref state,ecb,connection,7);ecb.Playback(em);
            using var payloads=chunks.ToComponentDataArray<RaidInventoryChunkV2Rpc>(Allocator.Temp);
            foreach(var chunk in payloads)
            { if(step==0){initialBytes+=chunk.Json.Length;initialFragments++;}else{repeatedBytes+=chunk.Json.Length;repeatedFragments++;} }
            em.DestroyEntity(chunks);
        }
        Check(graph.Validate()==InventoryError.None,"Fixture is a legal carried inventory");
        Check(initialFragments>0 && initialBytes>0,"Initial inventory still sends in full");
        Check(repeatedFragments==0 && repeatedBytes==0,"Unchanged resources send no duplicate inventory for sixty seconds");
        double now=60;
        int Step(float? oxygen=null)
        {
            if(oxygen.HasValue){var live=em.GetComponentData<PredictedPlayerGhost>(actor);live.Oxygen=oxygen.Value;em.SetComponentData(actor,live);}
            world.SetTime(new TimeData(now+=.5,.5f));
            ref var server=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(serverHandle);
            ref var state=ref world.Unmanaged.ResolveSystemStateRef(serverHandle);
            protection(ref server,ref state);using var ecb=new EntityCommandBuffer(Allocator.Temp);
            send(ref server,ref state,ecb,connection,7);ecb.Playback(em);
            int count=chunks.CalculateEntityCount();em.DestroyEntity(chunks);return count;
        }
        int version=graph.Version;
        Check(Step(99.7f)==0,"Sub-percent oxygen changes do not resend the inventory");
        Check(Math.Abs(em.GetComponentData<PredictedPlayerGhost>(actor).Oxygen-99.7f)<.001f,"Live Ghost oxygen retains full precision");
        Check(Step(99.4f)>0,"Changed displayed oxygen sends the current item value");
        Check(Math.Abs(graph.Equipped("Helmet").SuitOxygen-99.4f)<.001f,"Authoritative item stores the exact oxygen for transfers and saves");
        Check(graph.Version==version,"Resource refresh does not invalidate inventory transactions");
        Check(Step(99.1f)==0,"The same displayed percentage stays coalesced");
        inventory.RequestId=9;inventory.Error=InventoryError.Stale;inventory.LastSentVersion=-1;
        Check(Step()>0,"Action errors and acknowledgements still send immediately");
        Check(inventory.RequestId==9 && inventory.Error==InventoryError.Stale,"Resource suppression preserves request results");
        Check(graph.AddSupply("water",1)==InventoryError.None,"An actual inventory change succeeds");
        Check(Step()>0,"Structural inventory changes send immediately");
        Check(Step()==0,"A sent inventory refresh also updates the resource baseline");
        Check(Step(100)>0,"An oxygen refill is shown");
        graph.Equipped("Helmet").Id=Guid.NewGuid().ToString("D");
        Check(Step()>0,"A newly bound collar with the same percentage is not suppressed");
        graph.Items.Remove(graph.Equipped("Helmet"));graph.Version++;
        Check(Step()>0,"Unequipping the collar sends the changed inventory");
        var halo=em.GetComponentData<PredictedPlayerGhost>(actor);halo.HaloEnergy=20;halo.HaloRecoveryDelay=0;em.SetComponentData(actor,halo);
        Check(Step()==0 && Step()==0,"Halo-only recovery causes no inventory snapshots");
        Check(em.GetComponentData<PredictedPlayerGhost>(actor).HaloEnergy>20,"Halo recovery still runs");
        return new { checks, validInventory=graph.Validate().ToString(), items=graph.Items.Count, seconds=60,
            initialBytes,initialFragments,repeatedBytes,repeatedFragments,
            scope="Actual server protection and inventory send methods in one isolated World; no networking or Play." };
    }
}
