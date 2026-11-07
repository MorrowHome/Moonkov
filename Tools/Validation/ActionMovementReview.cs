using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BodyPart=Unity.MP_FPS.BodyPart;

public static class ActionMovementReview
{
    public static object Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use the existing idle Editor.");
        int checks=0;var failures=new List<string>();
        void Check(bool value,string label){checks++;if(!value)failures.Add(label);}
        var health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);
        RaidHealth.Set(ref health,BodyPart.RightArm,10);
        var inventory=new RaidInventoryState{Graph=InventoryGraph.Create(false,false),RaidId=4};
        inventory.Graph.AddSupply("medkit",2,"pockets");
        var med=inventory.Graph.Items.Single(i=>i.Code=="medkit");
        Check(inventory.BeginMedical(health,med.Id,inventory.Graph.Version,BodyPart.RightArm,float3.zero,0)==InventoryError.None,"Medical begins");
        inventory.RefreshMedical(ref health,true,new float3(2,0,0),2);
        Check(inventory.MedicalItemId!=null && inventory.Graph.Count("medkit")==2 && health.RightArmHealth==10,"Walking preserves medical timer without early benefit");
        inventory.RefreshMedical(ref health,true,new float3(3,0,0),3);
        inventory.RefreshMedical(ref health,true,new float3(4,0,0),4);
        Check(inventory.MedicalItemId==null && inventory.Graph.Count("medkit")==1 && health.RightArmHealth==50,"Moving medical completes exactly once");
        var caches=new RaidLootContainers();caches.Initialize(1);
        Check(inventory.BeginDissection(0,0,float3.zero,health,2),"Search begins");
        inventory.RefreshDissection(caches,true,new float3(1,0,0),health,1);
        Check(inventory.DissectingLootId==0 && inventory.OpenedLootId<0,"Walking preserves unfinished search");
        inventory.RefreshDissection(caches,true,new float3(2,0,0),health,2);
        Check(inventory.OpenedLootId==0,"Moving search completes");
        inventory.BeginDissection(0,3,float3.zero,health,2);
        inventory.RefreshDissection(caches,false,new float3(10,0,0),health,5);
        Check(inventory.Error==InventoryError.Inaccessible && inventory.OpenedLootId<0,"Leaving interaction range still cancels search");
        var constants=new FirstPersonController.ControllerConsts{
            Walk=new FirstPersonController.ControllerConsts.StateConsts{Speed=4},
            Sprint=new FirstPersonController.ControllerConsts.StateConsts{Speed=6}};
        var input=new PlayerInput{MoveInput=new float2(1,1)};input.SetFlag(PlayerInput.InputFlag.Sprint,true);
        foreach(bool reload in new[]{false,true})
        {
            health.TimedActionActive=!reload;health.ControllerState.IsReloadingState=reload;
            var limited=RaidHealth.Movement(health,constants);var read=RaidHealth.RestrictInput(health,input);
            Check(limited.Walk.Speed==1.5f && limited.Sprint.Speed==1.5f && !read.Sprint && read.MoveInput.Equals(input.MoveInput),"Timed action / recharge slows movement without blocking direction: "+reload);
        }
        health.TimedActionActive=false;health.ControllerState.IsReloadingState=false;
        Check(RaidHealth.Movement(health,constants).Sprint.Speed==6 && RaidHealth.RestrictInput(health,input).Sprint,"Ending action restores movement");

        var scene=EditorSceneManager.NewPreviewScene();var previous=MoonRaidMap.Active;
        using var world=new World("Moving action authority, no transport",WorldFlags.GameServer);var em=world.EntityManager;
        try
        {
            var mapRoot=new GameObject("Fixture map");mapRoot.SetActive(false);SceneManager.MoveGameObjectToScene(mapRoot,scene);
            var map=mapRoot.AddComponent<MoonRaidMap>();typeof(MoonRaidMap).GetProperty("Active").SetValue(null,map);
            var floor=new GameObject("Fixture floor");SceneManager.MoveGameObjectToScene(floor,scene);floor.transform.position=new Vector3(0,-.1f,0);floor.AddComponent<BoxCollider>().size=new Vector3(30,.2f,30);
            var root=new GameObject("Fixture player");SceneManager.MoveGameObjectToScene(root,scene);var link=root.AddComponent<GhostGameObject>();
            var player=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(GhostOwner));
            RaidHealth.Initialize(ref health);health.ControllerState.PitchDegrees=53.13f;em.SetComponentData(player,health);em.SetComponentData(player,new GhostOwner{NetworkId=7});
            em.AddComponentObject(player,new GhostGameObjectLink{LinkedInstance=link});
            var connection=em.CreateEntity(typeof(RaidSession),typeof(JoinedClient));
            em.SetComponentData(connection,new RaidSession{RaidId=4,Phase=RaidPhase.Active});em.SetComponentData(connection,new JoinedClient{PlayerEntity=player});
            inventory=new RaidInventoryState{Graph=InventoryGraph.Create(false),RaidId=4};em.AddComponentObject(connection,inventory);
            var server=world.GetOrCreateSystem<ServerGameSystem>();var hands=world.GetOrCreateSystemManaged<RaidHandActionSystem>();
            var clock=em.CreateEntity(typeof(NetworkTime));double now=0;uint tick=1,request=0;
            void Step(float seconds=.1f)
            {
                now+=seconds;world.SetTime(new TimeData(now,seconds));em.SetComponentData(clock,new NetworkTime{ServerTick=new NetworkTick(++tick)});
                Physics.SyncTransforms();ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(server);ref var state=ref world.Unmanaged.ResolveSystemStateRef(server);
                using var ecb=new EntityCommandBuffer(Allocator.Temp);system.TacticalTestTick(ref state,ecb);ecb.Playback(em);hands.Update();
                using var sent=em.CreateEntityQuery(typeof(SendRpcCommandRequest));em.DestroyEntity(sent);
            }
            void Send(TacticalItemAction action,string id="")
            {
                var rpc=em.CreateEntity();em.AddComponentData(rpc,new RaidTacticalItemActionRpc{RaidId=4,ExpectedVersion=inventory.Graph.Version,RequestId=++request,ItemId=id,Action=action});
                em.AddComponentData(rpc,new ReceiveRpcCommandRequest{SourceConnection=connection});Step();
            }
            Step();using var tacticalQuery=em.CreateEntityQuery(typeof(TacticalServerWorld));var tactical=tacticalQuery.GetSingleton<TacticalServerWorld>();
            tactical.Statuses[player]=new TacticalServerWorld.Status{Bleed=now+30};
            Send(TacticalItemAction.Extinguish);
            Check(inventory.TacticalPending && em.GetComponentData<PredictedPlayerGhost>(player).TimedActionActive,"Server publishes slowdown for self-treatment");
            root.transform.position=Vector3.right;Step(1);
            Check(inventory.TacticalPending && tactical.Statuses[player].Bleed>now,"Walking does not cancel or finish self-treatment early");
            root.transform.position=Vector3.right*2;Step(2.1f);
            Check(!inventory.TacticalPending && tactical.Statuses[player].Bleed==0 && !em.GetComponentData<PredictedPlayerGhost>(player).TimedActionActive,"Moving self-treatment completes and releases slowdown");
            inventory.Graph.AddSupply("antidote_shot",1,inventory.Graph.Equipped("ChestRig").Id);
            string shot=inventory.Graph.Items.Find(i=>i.Code=="antidote_shot").Id;Send(TacticalItemAction.Use,shot);
            root.transform.position=Vector3.right*4;Step(1);
            Check(inventory.TacticalPending && inventory.Graph.Find(shot)!=null,"Moving injection preserves the real inventory item until completion");
            Step(5);Check(!inventory.TacticalPending && inventory.Graph.Find(shot)==null && tactical.Statuses[player].Antidote>now,"Moving injection settles the authoritative transaction");
            inventory.DissectingLootId=0;hands.Update();Check(em.GetComponentData<PredictedPlayerGhost>(player).TimedActionActive,"Search publishes slowdown");
            inventory.CancelActions();hands.Update();Check(!em.GetComponentData<PredictedPlayerGhost>(player).TimedActionActive,"Cancel clears slowdown");
            inventory.MedicalItemId="test";inventory.MedicalUntil=now+2;hands.Update();Check(em.GetComponentData<PredictedPlayerGhost>(player).TimedActionActive,"Medical publishes slowdown");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.CurrentHealth=0;em.SetComponentData(player,health);hands.Update();
            Check(!em.GetComponentData<PredictedPlayerGhost>(player).TimedActionActive,"Death clears slowdown");
            health.CurrentHealth=100;em.SetComponentData(player,health);em.AddComponent<NetworkStreamRequestDisconnect>(connection);hands.Update();
            Check(!em.GetComponentData<PredictedPlayerGhost>(player).TimedActionActive,"Disconnect clears slowdown");
        }
        finally{typeof(MoonRaidMap).GetProperty("Active").SetValue(null,previous);EditorSceneManager.ClosePreviewScene(scene);}
        return new{passed=failures.Count==0,checks,failures,scope="Actual shared rules and isolated server RPC handling; no Play or transport."};
    }
}
