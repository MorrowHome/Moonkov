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
            uint requestId=0;bool sprintMotion=false;
            void Tick(float dt=.1f)
            {
                now+=dt;tick++;world.SetTime(new TimeData(now,dt));em.SetComponentData(clockEntity,new NetworkTime {ServerTick=new NetworkTick(tick)});
                var motion=em.GetComponentData<PredictedPlayerGhost>(player);if(sprintMotion && motion.ControllerState.Sprinting)root.transform.position+=Vector3.forward*((motion.TacticalSprintExhausted ? 3.2f : 4)*dt);motion.ControllerState.CurrentPosition=root.transform.position;em.SetComponentData(player,motion);
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
                root.transform.position=Vector3.zero;health=em.GetComponentData<PredictedPlayerGhost>(player);RaidHealth.Initialize(ref health);health.ControllerState.PitchDegrees=53.13f;health.ControllerState.Sprinting=false;health.ControllerState.MovementSpeed=0;em.SetComponentData(player,health);
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
            // Real damage enters the same server status and self-treatment path as existing traps.
            Clear();
            BountyClashDamage.Apply(world,player,8,BodyPart.LeftArm,12,tick,RaidDamageKind.Bullet);Tick();
            Check(!Data().Statuses.TryGetValue(player,out var lightWound) || lightWound.Bleed<=now,
                "A light flesh hit caused bleeding");
            Clear();
            var source=new ClashDamageSource {NetworkId=8,ParticipantId=13,Epoch=3};
            BountyClashDamage.Apply(world,player,source,BodyPart.LeftArm,12,tick,RaidDamageKind.Bullet);
            BountyClashDamage.Apply(world,player,source,BodyPart.LeftArm,10,tick,RaidDamageKind.Bullet);Tick();
            Check(Data().Statuses.TryGetValue(player,out var bulletWound) && bulletWound.Bleed-now>29.8 &&
                bulletWound.BleedSource.NetworkId==8 && bulletWound.BleedSource.ParticipantId==13 && bulletWound.BleedSource.Epoch==3,
                "Actual pellet wounds did not combine or lost attacker attribution");
            float woundedHealth=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;
            Request(TacticalItemAction.Extinguish);Wait(3.2f);
            Check(bulletWound.Bleed==0 && !inventory.TacticalPending && em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth<woundedHealth,
                "Real self-treatment did not stop the wound while damage continued");
            Clear();
            BountyClashDamage.Apply(world,player,8,BodyPart.LeftArm,25,tick,RaidDamageKind.Bullet);Tick();
            Check(Data().Statuses.TryGetValue(player,out var starvingWound) && starvingWound.Bleed>now,"Starving wound fixture");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.Energy=0;health.Hydration=0;em.SetComponentData(player,health);
            Request(TacticalItemAction.Extinguish);float starvingHp=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;
            for(int i=0;i<33;i++)
            {
                health=em.GetComponentData<PredictedPlayerGhost>(player);
                RaidNutrition.Tick(ref health,.1f,1000,1000,300,0,2,2,tick);
                em.SetComponentData(player,health);Tick();
            }
            Check(starvingWound.Bleed==0 && !inventory.TacticalPending && em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth<starvingHp,
                "Starvation and dehydration interrupted real self-treatment");
            Clear();
            BountyClashDamage.Apply(world,player,8,BodyPart.LeftArm,25,tick,RaidDamageKind.Bullet);Tick();
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.Energy=0;em.SetComponentData(player,health);
            Request(TacticalItemAction.Extinguish);
            health=em.GetComponentData<PredictedPlayerGhost>(player);RaidNutrition.Tick(ref health,.1f,1000,1000,300,0,2,0,tick);em.SetComponentData(player,health);
            BountyClashDamage.Apply(world,player,8,BodyPart.RightArm,5,tick,RaidDamageKind.Bullet);Tick();
            Check(!inventory.TacticalPending,"Same-tick metabolic damage hid an external injury during self-treatment");
            Clear();health=em.GetComponentData<PredictedPlayerGhost>(player);health.ShieldEquipped=true;health.ShieldCharge=100;em.SetComponentData(player,health);
            BountyClashDamage.Apply(world,player,8,BodyPart.LeftArm,30,tick,RaidDamageKind.Bullet);Tick();
            health=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(health.CurrentHealth==440 && health.ShieldCharge==70 &&
                (!Data().Statuses.TryGetValue(player,out var shieldWound) || shieldWound.Bleed<=now),
                "A fully absorbed shield hit caused bleeding");
            Clear();health=em.GetComponentData<PredictedPlayerGhost>(player);health.ShieldEquipped=true;health.ShieldCharge=10;em.SetComponentData(player,health);
            BountyClashDamage.Apply(world,player,8,BodyPart.LeftArm,30,tick,RaidDamageKind.Bullet);Tick();
            Check(Data().Statuses.TryGetValue(player,out var penetrated) && penetrated.Bleed>now,
                "A penetrating shield hit failed to produce a wound");
            double oldBleed=penetrated.Bleed;Wait(1);
            BountyClashDamage.Apply(world,player,8,BodyPart.RightArm,25,tick,RaidDamageKind.Bullet);Tick();
            Check(penetrated.Bleed>oldBleed && penetrated.Bleed-now<=TacticalItemRules.BulletWoundDuration,
                "A new wound did not refresh the single bleed timer");
            Clear();health=em.GetComponentData<PredictedPlayerGhost>(player);health.ShieldEquipped=false;em.SetComponentData(player,health);
            BountyClashDamage.Apply(world,player,8,BodyPart.LeftArm,30,tick,RaidDamageKind.Explosion);Tick();
            Check(!Data().Statuses.TryGetValue(player,out var explosionWound) || explosionWound.Bleed<=now,
                "Non-bullet damage incorrectly created a bullet wound");
            Clear();health=em.GetComponentData<PredictedPlayerGhost>(player);health.ShieldEquipped=false;em.SetComponentData(player,health);
            BountyClashDamage.Apply(world,player,8,BodyPart.Head,50,tick,RaidDamageKind.Bullet);Tick();
            Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==0 &&
                (!Data().Statuses.TryGetValue(player,out var fatalWound) || fatalWound.Bleed<=now),
                "A fatal shot created a persistent wound on a dead body");
            Clear();health=em.GetComponentData<PredictedPlayerGhost>(player);health.ShieldEquipped=false;em.SetComponentData(player,health);
            string antidote=Add("antidote_shot",3);int initial=inventory.Graph.Find(antidote).Quantity;
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.ControllerState.IsReloadingState=true;em.SetComponentData(player,health);
            Request(TacticalItemAction.Use,antidote);Check(!inventory.TacticalPending && inventory.Graph.Find(antidote).Quantity==initial,"Already reloading cannot start tactical use");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.ControllerState.IsReloadingState=false;health.WeaponEquipRemaining=.2f;em.SetComponentData(player,health);
            Request(TacticalItemAction.Use,antidote);Check(!inventory.TacticalPending && inventory.Graph.Find(antidote).Quantity==initial,"Weapon switch cannot start tactical use");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.WeaponEquipRemaining=0;em.SetComponentData(player,health);
            Request(TacticalItemAction.Use,antidote);health=em.GetComponentData<PredictedPlayerGhost>(player);health.ControllerState.IsReloadingState=true;em.SetComponentData(player,health);Tick();
            Check(!inventory.TacticalPending && inventory.Graph.Find(antidote).Quantity==initial,"Same-tick reload interrupts tactical use");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.ControllerState.IsReloadingState=false;em.SetComponentData(player,health);
            Request(TacticalItemAction.Use,antidote);Check(inventory.TacticalPending,"Use starts timed operation");
            Check(inventory.Graph.Find(antidote).Quantity==initial,"No early spend");
            Request(TacticalItemAction.Use,antidote,replay:requestId);Check(inventory.Graph.Find(antidote).Quantity==initial,"Replay no spend");
            Request(TacticalItemAction.Cancel);Check(!inventory.TacticalPending && inventory.Graph.Find(antidote).Quantity==initial,"Cancel no spend");
            Request(TacticalItemAction.Use,antidote,raid:3);Check(!inventory.TacticalPending,"Wrong raid rejected");
            Request(TacticalItemAction.Use,antidote,version:inventory.Graph.Version-1);Check(inventory.Error==InventoryError.Stale,"Old graph rejected");
            Request(TacticalItemAction.Use,antidote);root.transform.position=Vector3.right;Tick();Check(!inventory.TacticalPending && inventory.Graph.Find(antidote).Quantity==initial,"Move interrupts atomically");root.transform.position=Vector3.zero;
            Request(TacticalItemAction.Use,antidote);health=em.GetComponentData<PredictedPlayerGhost>(player);health.LastHitTick++;em.SetComponentData(player,health);Tick();Check(!inventory.TacticalPending,"Damage interrupts");
            Request(TacticalItemAction.Use,antidote);Wait(3.2f);Check(inventory.Graph.Find(antidote).Quantity==initial-1 && Data().Statuses[player].Antidote>now,"Antidote completes once");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.ControllerState.Sprinting=true;health.ControllerState.MovementSpeed=4;em.SetComponentData(player,health);
            Tick(2);Check(em.GetComponentData<PredictedPlayerGhost>(player).TacticalSprintStamina==100,"Shift and blocked movement never consume sprint stamina");
            sprintMotion=true;for(int i=0;i<200;i++)Tick(.25f);health=em.GetComponentData<PredictedPlayerGhost>(player);
            var sprintInput=new PlayerInput();sprintInput.SetFlag(PlayerInput.InputFlag.Sprint,true);
            Check(health.TacticalSprintExhausted && health.TacticalSprintStamina==0 && RaidHealth.RestrictInput(health,sprintInput).Sprint,"Ordinary server sprint fatigues after 50s while sprint input remains available");
            var running=new FirstPersonController.ControllerConsts();running.Walk.Speed=4;running.Sprint.Speed=7;var tired=TacticalItemEffects.Movement(health,running);
            Check(tired.Sprint.Speed>tired.Walk.Speed && Math.Abs(tired.Sprint.Speed-5.6f)<.001f,"Fatigue uses shared 0.8 sprint speed while still faster than walking");
            health.ControllerState.Sprinting=false;health.ControllerState.MovementSpeed=0;em.SetComponentData(player,health);Tick(3);
            health=em.GetComponentData<PredictedPlayerGhost>(player);Check(!health.TacticalSprintExhausted && health.TacticalSprintStamina>=25,"Stopping actually recovers sprint after delay");
            string stamina=Add("stamina_shot");Request(TacticalItemAction.Use,stamina);Wait(3.2f);
            Check(Data().Statuses[player].Stamina-now>419 && Data().Statuses[player].Stamina-now<=420,"Actual injection grants 420-second fatigue prevention");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.Energy=42;health.Hydration=37;health.ControllerState.Sprinting=true;health.ControllerState.MovementSpeed=4;em.SetComponentData(player,health);Tick(55);
            health=em.GetComponentData<PredictedPlayerGhost>(player);Check(health.TacticalSprintStamina==100 && !health.TacticalSprintExhausted,"Shot prevents real independent sprint exhaustion");
            Check(health.Energy==42 && health.Hydration==37,"Stamina shot never fabricates hunger or hydration recovery");
            Data().Statuses[player].Stamina=now+.01;Tick(.1f);Tick(50);health=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(health.TacticalSprintStamina==0 && health.TacticalSprintExhausted,"Expired shot permits normal fatigue again");
            health.ClashEnabled=true;health.ClashLife=ClashLife.Alive;health.ClashMaxHealthFraction=1;health.ClashCanAct=false;em.SetComponentData(player,health);em.AddComponentData(player,new ClashPlayer {CanAct=false});
            Data().Statuses[player].Sprint.Value=10;Data().Statuses[player].Sprint.Exhausted=true;Data().Statuses[player].Sprint.RestDelay=1.5f;Tick();health=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(health.TacticalSprintStamina==10 && health.TacticalSprintExhausted,"Disconnected alive body cannot instantly refill fatigue");Tick(3);health=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(health.TacticalSprintStamina>10 && health.TacticalSprintStamina<100,"Disconnected alive body recovers only through normal rest time");
            health.ClashLife=ClashLife.Downed;em.SetComponentData(player,health);Tick();health=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(health.TacticalSprintStamina==100 && !health.TacticalSprintExhausted,"Downed player resets transient fatigue");
            em.RemoveComponent<ClashPlayer>(player);sprintMotion=false;health.ClashEnabled=false;health.ControllerState.Sprinting=false;health.ControllerState.MovementSpeed=0;em.SetComponentData(player,health);
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
            health.ClashEnabled=true;health.ClashLife=ClashLife.Alive;health.ClashCanAct=true;health.ClashMaxHealthFraction=.5f;
            for(int p=0;p<RaidHealth.PartCount;p++)RaidHealth.Set(ref health,(BodyPart)p,RaidHealth.Maximum(health,(BodyPart)p)*.5f);
            RaidHealth.Set(ref health,BodyPart.LeftArm,0);em.SetComponentData(player,health);
            Request(TacticalItemAction.Use,vitality);Wait(3.2f);health=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(health.LeftArmHealth==0 && Math.Abs(health.HeadHealth-RaidHealth.Maximum(health,BodyPart.Head))<.01f && Math.Abs(health.ChestHealth-RaidHealth.Maximum(health,BodyPart.Chest))<.01f,"Vitality fills all recoverable tissue while preserving black arm and Clash cap");
            Check(health.ClashMaxHealthFraction==.5f && health.MaxHealth==220,"Vitality does not erase lost maximum");
            health.ClashEnabled=false;em.SetComponentData(player,health);Clear();
            Check(TacticalItemRules.StaminaDuration==420 && TacticalItemRules.AntidoteDuration==600,"Stamina 420s and antidote 600s durations");
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
            string smoke=Add("smoke_grenade");Request(TacticalItemAction.Use,smoke);Wait(1.2f);
            Check(inventory.Graph.Find(smoke)==null && Data().Objects.Count==1 && Data().Objects[0].Phase==TacticalWorldPhase.Flying,"Smoke throw spends exactly one real item");
            var cloud=Data().Objects[0];Wait(3.1f);
            Check(cloud.Phase==TacticalWorldPhase.Smoke,"Smoke fuse deploys a cloud");Wait(1.3f);
            Vector3 center=SmokeVisibility.Center(cloud.Position);
            Check(SmokeVisibility.Blocked(Data(),cloud.Scene,center-Vector3.right*8,center+Vector3.right*8),"Smoke hides targets across cloud");
            Check(SmokeVisibility.Blocked(Data(),cloud.Scene,center,center+Vector3.right*8),"Smoke hides targets from inside");
            Check(!SmokeVisibility.Blocked(Data(),cloud.Scene,center,center+Vector3.right*.2f),"Point blank contact remains visible");
            Check(!SmokeVisibility.Blocked(Data(),cloud.Scene,center+Vector3.up*5,center+Vector3.up*5+Vector3.right*8),"Sight above smoke remains clear");
            Check(!SmokeVisibility.Blocked(Data(),default,center-Vector3.right*8,center+Vector3.right*8),"Smoke isolated by physics scene");
            Check(EnergyGrenadePhysics.ClearSight(cloud.Scene,center-Vector3.right*8,center+Vector3.right*8,null),"Smoke does not block physical attack traces");
            Wait(20);Check(Data().Objects.Count==0 && !SmokeVisibility.Blocked(Data(),cloud.Scene,center-Vector3.right*8,center+Vector3.right*8),"Expired smoke restores sight and removes state");
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
            // A friendly trap still triggers, but its harmful fields must obey the
            // same release-time team rule as direct damage.
            em.AddComponentData(player,new OnlineRaidMember {Team=4});
            var(ally,allyRoot)=Target("Friendly tactical target",Vector3.zero);
            em.AddComponentData(ally,new OnlineRaidMember {Team=4});
            var onlineSource=BountyClashDamage.Capture(world,player);
            foreach(var phase in new[]{TacticalWorldPhase.Fire,TacticalWorldPhase.Poison,TacticalWorldPhase.Wire})
            {
                Clear();
                var code=phase==TacticalWorldPhase.Fire ? "fire_bomb" : phase==TacticalWorldPhase.Poison ? "poison_trip_mine" : "concertina_trip_mine";
                Data().Objects.Add(new TacticalServerWorld.Object {Id=80,Owner=7,Source=onlineSource,Definition=TacticalItemRules.Find(code),
                    Phase=phase,Scene=scene.GetPhysicsScene(),Position=Vector3.zero,Until=now+20,Created=now});
                Tick();
                var own=Data().Statuses[player];Data().Statuses.TryGetValue(ally,out var friendly);
                double ownEffect=phase==TacticalWorldPhase.Fire ? own.Fire : phase==TacticalWorldPhase.Poison ? own.Poison : own.Wire;
                double friendEffect=friendly==null ? 0 : phase==TacticalWorldPhase.Fire ? friendly.Fire : phase==TacticalWorldPhase.Poison ? friendly.Poison : friendly.Wire;
                Check(ownEffect>now && friendEffect<=now,"Owner affected while friendly is protected: "+phase);
            }
            Clear();root.transform.position=Vector3.right*8;
            var armed=new TacticalServerWorld.Object {Id=81,Owner=7,Source=onlineSource,Definition=TacticalItemRules.Find("concertina_trip_mine"),
                Phase=TacticalWorldPhase.Armed,Scene=scene.GetPhysicsScene(),Position=Vector3.zero,Until=now+20,ArmAt=now-1,Created=now};
            Data().Objects.Add(armed);Tick();Data().Statuses.TryGetValue(ally,out var triggeredAlly);
            Check(armed.Phase==TacticalWorldPhase.Wire && (triggeredAlly==null || triggeredAlly.Wire<=now),
                "Friendly contact must trigger a trap without applying its harmful field");
            Clear();Data().Statuses[ally]=new TacticalServerWorld.Status {Fire=now+5,Poison=now+5};
            Data().Objects.Add(new TacticalServerWorld.Object {Id=82,Owner=7,Source=onlineSource,Definition=TacticalItemRules.Find("choke_bomb"),
                Phase=TacticalWorldPhase.Choke,Scene=scene.GetPhysicsScene(),Position=Vector3.zero,Until=now+20,Created=now});
            Tick();var cleaned=Data().Statuses[ally];
            Check(cleaned.Fire==0 && cleaned.Poison==0 && cleaned.Cough>now,"Friendly choke must still clear hazards and cause cough");
            em.RemoveComponent<OnlineRaidMember>(player);em.DestroyEntity(ally);UnityEngine.Object.DestroyImmediate(allyRoot.gameObject);Clear();
            em.AddComponentData(player,new ClashPlayer {CanAct=false});int stock=inventory.Graph.Count("antidote_shot",true);Request(TacticalItemAction.Use,antidote);Check(!inventory.TacticalPending && inventory.Graph.Count("antidote_shot",true)==stock,"Downed action rejected without consumption");em.RemoveComponent<ClashPlayer>(player);
            // Capture on release, never resolve a delayed hit from a newly reused NetworkId.
            Clear();em.AddComponentData(player,new ClashPlayer {CanAct=true,ParticipantId=31,TeamId=0,Epoch=7});em.SetComponentData(player,new GhostOwner {NetworkId=7});
            string frag=Add("frag_bomb");Request(TacticalItemAction.Use,frag);Wait(1.2f);var released=Data().Objects[0];
            Check(released.Source.ParticipantId==31 && released.Source.Epoch==7 && released.Source.NetworkId==7,"Release captures immutable participant and epoch");
            em.SetComponentData(player,new GhostOwner {NetworkId=0});em.SetComponentData(player,new ClashPlayer {CanAct=false,ParticipantId=31,TeamId=0,Epoch=7});
            var stranger=em.CreateEntity(typeof(GhostOwner),typeof(ClashPlayer));em.SetComponentData(stranger,new GhostOwner {NetworkId=7});em.SetComponentData(stranger,new ClashPlayer {ParticipantId=42,TeamId=1,Epoch=7,CanAct=true});
            Wait(3.4f);released.Velocity=Vector3.zero;released.Resting=true;
            var(fragmentVictim,_)=Target("Fragment source victim",released.Position+Vector3.right*2-Vector3.up*.8f);
            var(longFragmentVictim,_)=Target("Long wound fragment victim",released.Position+Vector3.forward*3-Vector3.up*.8f);
            var longSource=new ClashDamageSource {NetworkId=20,ParticipantId=70,Epoch=7};
            var longWound=new TacticalServerWorld.Status {Bleed=now+30,BleedSource=longSource};Data().Statuses[longFragmentVictim]=longWound;
            Wait(.8f);
            Check(Data().Statuses.TryGetValue(fragmentVictim,out var bleeding) && bleeding.BleedSource.ParticipantId==31 && bleeding.BleedSource.Epoch==7,"Disconnect and reused NetworkId cannot steal delayed fragment bleeding source");
            Check(longWound.Bleed>now+28 && longWound.BleedSource.ParticipantId==70,
                "A shorter fragment wound shortened or stole an existing gunshot wound");
            var areaPoint=new Vector3(released.Position.x,.05f,released.Position.z);var(statusVictim,statusRoot)=Target("Status source victim",areaPoint);
            foreach(var kind in new[]{TacticalItemKind.Fire,TacticalItemKind.PoisonTrap,TacticalItemKind.WireTrap})
            {var def=kind==TacticalItemKind.Fire ? TacticalItemRules.Find("fire_bomb") : kind==TacticalItemKind.PoisonTrap ? TacticalItemRules.Find("poison_trip_mine") : TacticalItemRules.Find("concertina_trip_mine");Data().Objects.Add(new TacticalServerWorld.Object {Id=(uint)(100+(int)kind),Owner=7,Source=released.Source,Definition=def,Phase=kind==TacticalItemKind.Fire ? TacticalWorldPhase.Fire : kind==TacticalItemKind.PoisonTrap ? TacticalWorldPhase.Poison : TacticalWorldPhase.Wire,Scene=scene.GetPhysicsScene(),Position=areaPoint,Until=now+20,Created=now});}
            Tick();var propagated=Data().Statuses[statusVictim];
            Check(propagated.FireSource.ParticipantId==31 && propagated.PoisonSource.ParticipantId==31 && propagated.BleedSource.ParticipantId==31,"Disconnected fire poison and wire retain original captured participant");
            propagated.Bleed=now+30;propagated.BleedSource=longSource;Tick();
            Check(propagated.Wire>now && propagated.Bleed>now+29 && propagated.BleedSource.ParticipantId==70,
                "Wire shortened or stole an existing gunshot wound");
            // Verify the real unified policy against an active match with a reused transport id.
            var arena=new ClashArenaDefinition {Center=new ClashPoint(0,0,0),Target=new ClashPoint(0,0,0),Radius=100,Spawns=new ClashPoint[12],Exits=new[]{new ClashPoint(-70,0,0),new ClashPoint(70,0,0)}};
            var match=new BountyClashMatch(arena,8,0);var original=match.Join("tactical-source-original","Original",0);var opponent=match.Join("tactical-source-opponent","Opponent",0);var replacement=match.Join("tactical-source-replacement","Replacement",0);
            match.CreateTeam(original,1);match.CreateTeam(opponent,1);match.CreateTeam(replacement,1);match.Ready(original,true,0);match.Ready(opponent,true,0);match.Ready(replacement,true,0);match.Tick(11);foreach(var participant in match.Players.Values)match.MarkDeployed(participant.Id);match.Tick(12);
            using(var query=em.CreateEntityQuery(typeof(BountyClashWorld)))em.GetComponentObject<BountyClashWorld>(query.GetSingletonEntity()).Match=match;
            original.NetworkId=7;opponent.NetworkId=8;replacement.NetworkId=9;em.SetComponentData(player,new GhostOwner {NetworkId=7});em.SetComponentData(player,new ClashPlayer {ParticipantId=original.Id,Epoch=8,TeamId=original.TeamId,CanAct=true});
            var captured=BountyClashDamage.Capture(world,player);match.Disconnect(original,12.1);original.NetworkId=0;replacement.NetworkId=7;em.SetComponentData(player,new GhostOwner {NetworkId=0});
            var policyVictim=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(ClashPlayer));var ph=new PredictedPlayerGhost {ClashEnabled=true,ClashLife=ClashLife.Alive,ClashCanAct=true,ClashMaxHealthFraction=1,ClashTeam=opponent.TeamId};RaidHealth.Initialize(ref ph);em.SetComponentData(policyVictim,ph);em.SetComponentData(policyVictim,new ClashPlayer {ParticipantId=opponent.Id,Epoch=8,TeamId=opponent.TeamId,CanAct=true});
            foreach(var kind in new[]{RaidDamageKind.Fire,RaidDamageKind.Poison,RaidDamageKind.Bleed})
                Check(BountyClashDamage.Apply(world,policyVictim,captured,BodyPart.Auto,1,tick,kind)>0 && em.GetComponentData<ClashPlayer>(policyVictim).LastAttacker==original.Id,"Delayed "+kind+" credits original disconnected participant, not reused id");
            Check(BountyClashDamage.Apply(world,policyVictim,captured,BodyPart.LeftArm,25,tick,RaidDamageKind.Bullet)>0 &&
                em.GetComponentData<PredictedPlayerGhost>(policyVictim).PendingBulletWound==0,
                "Bullet wounds must not change bounty-clash damage rules");
            var stale=captured;stale.Epoch=7;float beforeStale=em.GetComponentData<PredictedPlayerGhost>(policyVictim).CurrentHealth;
            Check(BountyClashDamage.Apply(world,policyVictim,stale,BodyPart.Auto,100,tick,RaidDamageKind.Fire)==0 && em.GetComponentData<PredictedPlayerGhost>(policyVictim).CurrentHealth==beforeStale,"Previous epoch source cannot damage new match");
            // Alive disconnected bodies still take field damage; Downed bodies do not burn.
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.ClashEnabled=true;health.ClashLife=ClashLife.Alive;health.ClashCanAct=false;health.ClashMaxHealthFraction=1;health.ClashTeam=original.TeamId;RaidHealth.Initialize(ref health);em.SetComponentData(player,health);
            em.SetComponentData(player,new ClashPlayer {ParticipantId=original.Id,Epoch=8,TeamId=original.TeamId,CanAct=false});
            var disconnectedStatus=new TacticalServerWorld.Status {Fire=now+20,FireSource=captured};Data().Statuses[player]=disconnectedStatus;float disconnectedBefore=health.CurrentHealth;Tick();
            Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth<disconnectedBefore && disconnectedStatus.Fire>now,"Alive disconnected body keeps burning and taking actual damage");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.ClashLife=ClashLife.Downed;em.SetComponentData(player,health);disconnectedBefore=health.CurrentHealth;Tick();
            Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==disconnectedBefore && disconnectedStatus.Fire==0,"Downed body does not burn or repeat death settlement");
            health=em.GetComponentData<PredictedPlayerGhost>(player);health.ClashEnabled=false;em.SetComponentData(player,health);
            em.RemoveComponent<ClashPlayer>(player);em.SetComponentData(player,new GhostOwner {NetworkId=7});em.DestroyEntity(stranger);Clear();
            // User cancelled parallel clients for memory limits. Exercise the exact production
            // owner-acceptance model without creating any second World or network login.
            var ownerModel=new TacticalItemClientState();
            ownerModel.Apply(new TacticalItemOwnerRpc {RaidId=4,RequestId=10,Remaining=3,Feedback=TacticalItemFeedback.Started});
            Check(ownerModel.Owner.Remaining==3 && ownerModel.Owner.Feedback==TacticalItemFeedback.Started,"Owner model accepts pending acknowledgement");
            ownerModel.Apply(new TacticalItemOwnerRpc {RaidId=4,RequestId=10,Remaining=0,Feedback=TacticalItemFeedback.Completed});
            Check(ownerModel.Owner.Remaining==0 && ownerModel.Owner.Feedback==TacticalItemFeedback.Completed,"Same-request completion clears pending");
            Check(!ownerModel.Apply(new TacticalItemOwnerRpc {RaidId=3,RequestId=999,Remaining=2}) && ownerModel.Owner.Remaining==0,"Old raid owner snapshot cannot resurrect pending");
            var secondModel=new TacticalItemClientState();secondModel.Apply(new TacticalItemOwnerRpc {RaidId=99,RequestId=999,Remaining=7});
            Check(secondModel.Owner.RaidId==99 && ownerModel.Owner.RaidId==4,"Owner models remain independent without parallel client Worlds");
            Debug.Log("Tactical authority and physics: "+checks+" checks passed.");
        }
        finally {typeof(MoonRaidMap).GetProperty("Active").SetValue(null,previous);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
