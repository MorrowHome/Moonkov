using System;
using BodyPart = Unity.MP_FPS.BodyPart;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Executes real action RPC handling and server object/status simulation in a disposable
// World and local PhysicsScene. No transport, account, profile, PlayerPrefs or scene save.
public static class TacticalItemsAuthorityChecks
{
    [MenuItem("Moonkov/Tactical Items/Run Authority and Physics Checks")]
    public static void Run()
    {
        int checks=0;void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;}
        var scene=EditorSceneManager.NewPreviewScene();var previous=MoonRaidMap.Active;
        using var world=new World("Tactical item authority fixture",WorldFlags.GameServer);
        var em=world.EntityManager;
        try
        {
            var mapRoot=new GameObject("Tactical fixture map");mapRoot.SetActive(false);SceneManager.MoveGameObjectToScene(mapRoot,scene);
            var map=mapRoot.AddComponent<MoonRaidMap>();typeof(MoonRaidMap).GetProperty("Active").SetValue(null,map);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(40,.2f,40);
            var handle=world.GetOrCreateSystem<ServerGameSystem>();
            var clockEntity=em.CreateEntity(typeof(NetworkTime));double now=0;uint tick=10;
            var root=new GameObject("Tactical fixture player");SceneManager.MoveGameObjectToScene(root,scene);
            var ghost=root.AddComponent<GhostGameObject>();
            var player=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(GhostOwner));
            var health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);health.ControllerState.PitchDegrees=53.13f;
            em.SetComponentData(player,health);em.SetComponentData(player,new GhostOwner {NetworkId=7});
            em.AddComponentObject(player,new GhostGameObjectLink {LinkedInstance=ghost});
            var connection=em.CreateEntity(typeof(RaidSession),typeof(JoinedClient));
            em.SetComponentData(connection,new RaidSession {RaidId=4,Phase=RaidPhase.Active});
            em.SetComponentData(connection,new JoinedClient {PlayerEntity=player});
            var inventory=new RaidInventoryState {Graph=InventoryGraph.Create(false),RaidId=4};em.AddComponentObject(connection,inventory);
            uint requestId=0;
            void Tick(float dt=.1f)
            {
                now+=dt;tick++;world.SetTime(new TimeData(now,dt));em.SetComponentData(clockEntity,new NetworkTime {ServerTick=new NetworkTick(tick)});
                Physics.SyncTransforms();
                ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
                ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
                var ecb=new EntityCommandBuffer(Allocator.Temp);system.TacticalTestTick(ref state,ecb);ecb.Playback(em);ecb.Dispose();
                using var outgoing=em.CreateEntityQuery(typeof(SendRpcCommandRequest));em.DestroyEntity(outgoing);
            }
            void Wait(float seconds){double end=now+seconds;while(now<end)Tick();}
            TacticalServerWorld Data(){using var query=em.CreateEntityQuery(typeof(TacticalServerWorld));return query.GetSingleton<TacticalServerWorld>();}
            void Clear()
            {
                ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
                var ecb=new EntityCommandBuffer(Allocator.Temp);system.TacticalTestClear(ref state,ecb);ecb.Playback(em);ecb.Dispose();
                root.transform.position=Vector3.zero;health=em.GetComponentData<PredictedPlayerGhost>(player);RaidHealth.Initialize(ref health);health.ControllerState.PitchDegrees=53.13f;em.SetComponentData(player,health);
            }
            string Add(string code,int quantity=1)
            {
                var rig=inventory.Graph.Equipped("ChestRig");
                var result=inventory.Graph.AddSupply(code,quantity,rig.Id);if(result!=InventoryError.None)throw new Exception("Fixture capacity "+code+": "+result);
                return inventory.Graph.Items.Find(i=>i.Code==code).Id;
            }
            void Request(TacticalItemAction action,string id="",uint target=0,int? version=null,int raid=4,uint? replay=null)
            {
                var entity=em.CreateEntity();em.AddComponentData(entity,new RaidTacticalItemActionRpc {RaidId=raid,ItemId=id,TargetId=target,
                    RequestId=replay ?? ++requestId,ExpectedVersion=version ?? inventory.Graph.Version,Action=action});
                em.AddComponentData(entity,new ReceiveRpcCommandRequest {SourceConnection=connection});Tick();
            }
            Tick();
            string antidote=Add("antidote_shot",3);int initial=inventory.Graph.Find(antidote).Quantity;
            Request(TacticalItemAction.Use,antidote);Check(inventory.TacticalPending,"Use starts timed operation");
            Check(inventory.Graph.Find(antidote).Quantity==initial,"No early spend");
            Request(TacticalItemAction.Use,antidote,replay:requestId);Check(inventory.Graph.Find(antidote).Quantity==initial,"Replay no spend");
            Request(TacticalItemAction.Cancel);Check(!inventory.TacticalPending && inventory.Graph.Find(antidote).Quantity==initial,"Cancel no spend");
            Request(TacticalItemAction.Use,antidote,raid:3);Check(!inventory.TacticalPending,"Wrong raid rejected");
            Request(TacticalItemAction.Use,antidote,version:inventory.Graph.Version-1);Check(inventory.Error==InventoryError.Stale,"Old graph rejected");
            Request(TacticalItemAction.Use,antidote);root.transform.position=Vector3.right;Tick();Check(!inventory.TacticalPending && inventory.Graph.Find(antidote).Quantity==initial,"Move interrupts atomically");root.transform.position=Vector3.zero;
            Request(TacticalItemAction.Use,antidote);health=em.GetComponentData<PredictedPlayerGhost>(player);health.LastHitTick++;em.SetComponentData(player,health);Tick();Check(!inventory.TacticalPending,"Damage interrupts");
            Request(TacticalItemAction.Use,antidote);Wait(3.2f);Check(inventory.Graph.Find(antidote).Quantity==initial-1 && Data().Statuses[player].Antidote>now,"Antidote completes once");
            string stamina=Add("stamina_shot");Request(TacticalItemAction.Use,stamina);Wait(3.2f);
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.Energy=0;health.Hydration=0;em.SetComponentData(player,health);Tick();
            health=em.GetComponentData<PredictedPlayerGhost>(player);Check(health.Energy>=80 && health.Hydration>=80 && RaidNutrition.CanSprint(health),"Stamina changes actual exhausted sprint eligibility");
            var status=Data().Statuses[player];status.Fire=now+10;status.Bleed=now+10;Request(TacticalItemAction.Extinguish);Wait(3.2f);Check(status.Fire==0 && status.Bleed==0,"Manual treatment completes while own damage continues");
            Clear();status=new TacticalServerWorld.Status {Fire=now+30,Bleed=now+30};Data().Statuses[player]=status;
            float burnStart=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;for(int i=0;i<10;i++)Tick();
            float normalBurn=burnStart-em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;
            Clear();Data().Statuses[player]=new TacticalServerWorld.Status {Fire=now+30,Bleed=now+30};burnStart=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;
            for(int i=0;i<5;i++){Request(TacticalItemAction.Extinguish);Request(TacticalItemAction.Cancel);}
            float spamBurn=burnStart-em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;
            Check(Math.Abs(spamBurn-normalBurn)<.01f,"Repeated start/cancel cannot avoid any burning or bleed damage");
            Request(TacticalItemAction.Extinguish);health=em.GetComponentData<PredictedPlayerGhost>(player);
            BountyClashDamage.Apply(world,player,8,BodyPart.Chest,10,tick,RaidDamageKind.Bullet);Tick();
            Check(!inventory.TacticalPending,"External hit sharing own DOT tick interrupts treatment");
            Clear();string trap=Add("alert_trip_mine",2);Request(TacticalItemAction.Use,trap);Check(inventory.TacticalPending,"Ground aim accepted");Wait(3.2f);
            Check(Data().Objects.Count==1 && Data().Objects[0].Phase==TacticalWorldPhase.Armed,"Trap really deployed");var deployed=Data().Objects[0];
            root.transform.position=deployed.Position+Vector3.up*2.5f;Wait(1.2f);Check(deployed.Phase==TacticalWorldPhase.Armed,"Upper floor cannot trigger");
            root.transform.position=deployed.Position;Tick();Check(deployed.Phase==TacticalWorldPhase.Alarm,"Owner triggers own alarm");
            Clear();root.transform.position=Vector3.zero;
            var data=Data();var fireDef=TacticalItemRules.Find("fire_bomb");
            var fire=new TacticalServerWorld.Object {Id=50,Owner=7,OwnerPlayer=player,Definition=fireDef,Phase=TacticalWorldPhase.Fire,
                Scene=scene.GetPhysicsScene(),Position=new Vector3(1,0,0),Until=now+20,Created=now};data.Objects.Add(fire);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(wall,scene);wall.transform.position=new Vector3(.5f,1,0);wall.transform.localScale=new Vector3(.1f,2,5);
            float before=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;Tick();Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==before,"Wall blocks fire application");
            UnityEngine.Object.DestroyImmediate(wall);Tick();Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth<before,"Exposed fire inflicts actual damage");
            var choke=new TacticalServerWorld.Object {Id=51,Definition=TacticalItemRules.Find("choke_bomb"),Phase=TacticalWorldPhase.Choke,Scene=scene.GetPhysicsScene(),Position=Vector3.zero,Until=now+20,Created=now};data.Objects.Add(choke);Tick();
            Check(!data.Objects.Contains(fire) && data.Statuses[player].Fire==0 && data.Statuses[player].Cough>now,"Choke extinguishes field and player, causes real cough");
            Check(RaidHealth.HandlingMultiplier(em.GetComponentData<PredictedPlayerGhost>(player))>1,"Cough handling penalty");
            Clear();data=Data();status=new TacticalServerWorld.Status {Poison=now+10};data.Statuses[player]=status;Tick();
            health=em.GetComponentData<PredictedPlayerGhost>(player);Check(health.TacticalPoisonSeconds>0,"Poison replicated into actual health state");
            string med=Add("medkit");Check(RaidHealth.UseMedical(ref health,inventory.Graph,med,inventory.Graph.Version,BodyPart.Auto)==InventoryError.Inaccessible,"Poison blocks legacy healing");
            Clear();uint oldEpoch=data.Epoch;Check(data.Objects.Count==0 && data.Statuses.Count==0 && !inventory.TacticalPending,"Round clears all temporary state");
            Check(oldEpoch>1,"Round epoch advances");
            Clear();string vitality=Add("vitality_shot");health=em.GetComponentData<PredictedPlayerGhost>(player);
            health.ClashEnabled=true;health.ClashMaxHealthFraction=.5f;
            for(int p=0;p<RaidHealth.PartCount;p++)RaidHealth.Set(ref health,(BodyPart)p,RaidHealth.Maximum(health,(BodyPart)p)*.5f);
            RaidHealth.Set(ref health,BodyPart.LeftArm,0);em.SetComponentData(player,health);
            Request(TacticalItemAction.Use,vitality);Wait(3.2f);health=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(health.LeftArmHealth==0 && Math.Abs(health.HeadHealth-RaidHealth.Maximum(health,BodyPart.Head))<.01f && Math.Abs(health.ChestHealth-RaidHealth.Maximum(health,BodyPart.Chest))<.01f,"Vitality fills all recoverable tissue while preserving black arm and Clash cap");
            Check(health.ClashMaxHealthFraction==.5f && health.MaxHealth==220,"Vitality does not erase lost maximum");
            health.ClashEnabled=false;em.SetComponentData(player,health);Clear();
            Check(TacticalItemRules.StaminaDuration==600 && TacticalItemRules.AntidoteDuration==600,"Normal shot durations 600 seconds");
            // Real deploy -> timed disarm; deployed tools do not return free fresh items.
            trap=inventory.Graph.Items.Find(i=>i.Code=="alert_trip_mine").Id;
            Request(TacticalItemAction.Use,trap);Wait(3.2f);deployed=Data().Objects[0];
            int carriedTraps=inventory.Graph.Count("alert_trip_mine",true);
            Request(TacticalItemAction.Disarm,target:deployed.Id);Wait(3.2f);
            Check(Data().Objects.Count==0 && inventory.Graph.Count("alert_trip_mine",true)==carriedTraps,"Disarm is timed and never duplicates inventory");
            Clear();
            var wire=new TacticalServerWorld.Object {Id=60,Owner=7,Definition=TacticalItemRules.Find("concertina_trip_mine"),Phase=TacticalWorldPhase.Wire,
                Scene=scene.GetPhysicsScene(),Position=Vector3.zero,Until=now+20,Created=now};Data().Objects.Add(wire);Tick();
            health=em.GetComponentData<PredictedPlayerGhost>(player);var constants=new FirstPersonController.ControllerConsts();constants.Walk.Speed=5;constants.Sprint.Speed=8;constants.JumpHeight=1;
            var slowed=RaidHealth.Movement(health,constants);
            Check(health.TacticalWireSeconds>0 && slowed.Walk.Speed<2 && slowed.Sprint.Speed<=slowed.Walk.Speed,"Wire changes real authoritative movement constants");
            Clear();
            antidote=inventory.Graph.Items.Find(i=>i.Code=="antidote_shot").Id;Request(TacticalItemAction.Use,antidote);Wait(3.2f);
            var poison=new TacticalServerWorld.Object {Id=61,Owner=7,Definition=TacticalItemRules.Find("poison_trip_mine"),Phase=TacticalWorldPhase.Poison,
                Scene=scene.GetPhysicsScene(),Position=Vector3.zero,Until=now+20,Created=now};Data().Objects.Add(poison);
            before=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;Wait(1);
            Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==before && Data().Statuses[player].Poison==0,"Injected antidote really prevents poison cloud harm");
            Clear();Data().Objects.Add(poison);poison.Until=now+20;Tick();Check(Data().Statuses[player].Poison>now,"Poison cloud affects unprotected player");
            choke.Until=now+20;Data().Objects.Add(choke);Tick();Check(!Data().Objects.Contains(poison) && Data().Statuses[player].Poison==0,"Choke removes poison cloud and poison status");
            Clear();
            // Two independent connections finish on one 100-energy box in the same tick.
            var secondRoot=new GameObject("Second tactical fixture player");SceneManager.MoveGameObjectToScene(secondRoot,scene);secondRoot.transform.position=Vector3.left*.4f;
            var secondGhost=secondRoot.AddComponent<GhostGameObject>();var secondPlayer=em.CreateEntity(typeof(PredictedPlayerGhost));var secondHealth=new PredictedPlayerGhost();RaidHealth.Initialize(ref secondHealth);secondHealth.ControllerState.PitchDegrees=53.13f;em.SetComponentData(secondPlayer,secondHealth);em.AddComponentObject(secondPlayer,new GhostGameObjectLink {LinkedInstance=secondGhost});
            var secondConnection=em.CreateEntity(typeof(RaidSession),typeof(JoinedClient));em.SetComponentData(secondConnection,new RaidSession {RaidId=4,Phase=RaidPhase.Active});em.SetComponentData(secondConnection,new JoinedClient {PlayerEntity=secondPlayer});
            var secondInventory=new RaidInventoryState {Graph=InventoryGraph.Create(false,false),RaidId=4};em.AddComponentObject(secondConnection,secondInventory);
            var box=new TacticalServerWorld.Object {Id=62,Definition=TacticalItemRules.Find("ammo_box"),Phase=TacticalWorldPhase.Supply,Scene=scene.GetPhysicsScene(),Position=new Vector3(0,.05f,1.2f),Energy=100,Until=now+20,Created=now};Data().Objects.Add(box);
            int oldEnergy=inventory.Graph.CellEnergy(true);
            Request(TacticalItemAction.Resupply,target:box.Id);
            var secondRpc=em.CreateEntity();em.AddComponentData(secondRpc,new RaidTacticalItemActionRpc {RaidId=4,ExpectedVersion=secondInventory.Graph.Version,RequestId=1,Action=TacticalItemAction.Resupply,TargetId=box.Id});em.AddComponentData(secondRpc,new ReceiveRpcCommandRequest {SourceConnection=secondConnection});Tick();
            Check(inventory.TacticalPending && secondInventory.TacticalPending,"Both players begin box interaction");Wait(2.2f);
            Check(inventory.Graph.CellEnergy(true)+secondInventory.Graph.CellEnergy(true)-oldEnergy==100 && box.Energy==0,"Concurrent supply cannot mint extra energy");
            Check(Data().Objects.Count==0,"Empty box removed for all players");
            Clear();
            string dynamite=Add("dynamite_bundle");Request(TacticalItemAction.Use,dynamite);Wait(1.2f);
            Check(Data().Objects.Count==1 && Data().Objects[0].Phase==TacticalWorldPhase.Flying,"Actual throw request creates swept flying object");
            var flying=Data().Objects[0];Vector3 initialPosition=flying.Position;Wait(3.4f);
            Check(Vector3.Distance(flying.Position,initialPosition)>1 && flying.Position.y>-.1f,"Projectile advances and floor collision prevents tunneling");
            (Entity,GhostGameObject) Target(string name,Vector3 position)
            {var obj=new GameObject(name);SceneManager.MoveGameObjectToScene(obj,scene);obj.transform.position=position;var link=obj.AddComponent<GhostGameObject>();var victim=em.CreateEntity(typeof(PredictedPlayerGhost));var h=new PredictedPlayerGhost();RaidHealth.Initialize(ref h);em.SetComponentData(victim,h);em.AddComponentObject(victim,new GhostGameObjectLink {LinkedInstance=link});return(victim,link);}
            flying.Velocity=Vector3.zero;flying.Resting=true;
            var(open,_)=Target("Open blast victim",flying.Position+Vector3.right*2-Vector3.up*.8f);
            var(covered,_)=Target("Covered blast victim",flying.Position+Vector3.left*2-Vector3.up*.8f);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(wall,scene);wall.transform.position=flying.Position+Vector3.left;wall.transform.localScale=new Vector3(.1f,4,6);
            Wait(1);
            Check(em.GetComponentData<PredictedPlayerGhost>(open).CurrentHealth<440,"Real fused explosive applies unified damage");
            Check(em.GetComponentData<PredictedPlayerGhost>(covered).CurrentHealth==440,"Real explosive respects cover");
            UnityEngine.Object.DestroyImmediate(wall);Clear();
            em.AddComponentData(player,new ClashPlayer {CanAct=false});int stock=inventory.Graph.Count("antidote_shot",true);Request(TacticalItemAction.Use,antidote);Check(!inventory.TacticalPending && inventory.Graph.Count("antidote_shot",true)==stock,"Downed action rejected without consumption");em.RemoveComponent<ClashPlayer>(player);
            // Exercise the actual client RPC consumer without creating objects in the user's scene.
            var previousOwner=TacticalItemsFeedback.LocalOwner;
            using(var client=new World("Tactical owner RPC fixture",WorldFlags.GameClient))
            {
                var clientSystem=client.GetOrCreateSystemManaged<TacticalItemsClientSystem>();clientSystem.EditorSuppressPresentation=true;var cm=client.EntityManager;
                void Owner(TacticalItemOwnerRpc snapshot)
                {var rpc=cm.CreateEntity();cm.AddComponentData(rpc,snapshot);cm.AddComponentData(rpc,new ReceiveRpcCommandRequest());clientSystem.Update();}
                Owner(new TacticalItemOwnerRpc {RaidId=4,RequestId=10,Remaining=3,Feedback=TacticalItemFeedback.Started});
                using var query=cm.CreateEntityQuery(typeof(TacticalItemClientState));
                Check(query.GetSingleton<TacticalItemClientState>().Owner.Remaining==3 && TacticalItemsFeedback.LocalOwner.Feedback==TacticalItemFeedback.Started,"Client owner RPC shows real pending acknowledgement");
                Owner(new TacticalItemOwnerRpc {RaidId=4,RequestId=10,Remaining=0,Feedback=TacticalItemFeedback.Completed});
                Check(TacticalItemsFeedback.LocalOwner.Remaining==0 && TacticalItemsFeedback.LocalOwner.Feedback==TacticalItemFeedback.Completed,"Same-request completion clears pending");
                Owner(new TacticalItemOwnerRpc {RaidId=3,RequestId=999,Remaining=2});
                Check(TacticalItemsFeedback.LocalOwner.RaidId==4 && TacticalItemsFeedback.LocalOwner.Remaining==0,"Old raid owner snapshot cannot resurrect pending");
                using(var secondary=new World("Secondary tactical owner fixture",WorldFlags.GameClient))
                {
                    var secondarySystem=secondary.GetOrCreateSystemManaged<TacticalItemsClientSystem>();secondarySystem.EditorSuppressPresentation=true;
                    var sm=secondary.EntityManager;var rpc=sm.CreateEntity();sm.AddComponentData(rpc,new TacticalItemOwnerRpc {RaidId=99,RequestId=999,Remaining=7});sm.AddComponentData(rpc,new ReceiveRpcCommandRequest());secondarySystem.Update();
                    using var own=sm.CreateEntityQuery(typeof(TacticalItemClientState));
                    Check(own.GetSingleton<TacticalItemClientState>().Owner.RaidId==99 && TacticalItemsFeedback.LocalOwner.RaidId==4 && TacticalItemsFeedback.LocalOwner.Remaining==0,"Second transport client keeps its own snapshot without overwriting viewed HUD");
                }
            }
            TacticalItemsFeedback.SetOwner(previousOwner);
            Debug.Log("Tactical authority and physics: "+checks+" checks passed.");
        }
        finally {typeof(MoonRaidMap).GetProperty("Active").SetValue(null,previous);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
