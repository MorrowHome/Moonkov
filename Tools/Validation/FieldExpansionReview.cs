using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using BodyPart=Unity.MP_FPS.BodyPart;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;

// Isolated authority fixtures. Never starts transport, a raid, or a player profile.
public static class FieldExpansionReview
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
    static PredictedPlayerGhost Healthy(){var h=new PredictedPlayerGhost();RaidHealth.Initialize(ref h);return h;}
    static RaidInventoryState Inventory(string code,out string id)
    {
        var graph=InventoryGraph.Create(false);
        if(graph.Equipped("Helmet")==null)graph.Items.Add(new InventoryItem{Id=Guid.NewGuid().ToString("D"),Code="helmet",Parent="equipment",Region="Helmet"});
        Check(graph.AddSupply(code,2,graph.Equipped("ChestRig").Id)==InventoryError.None,"prepare "+code);
        id=graph.Items.Find(i=>i.Code==code).Id;return new RaidInventoryState{Graph=graph,RaidId=4};
    }
    public static string Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode.");
        checks=0;Medical();Supplies();Oxygen();Combat();Assets();
        string result=$"Field expansion: {checks} checks passed; actual timed transactions, grenade RPCs, server clouds, flash visibility, passive gear and native assets.";
        Directory.CreateDirectory("LocalData/FieldExpansion");File.WriteAllText("LocalData/FieldExpansion/review.txt",result);return result;
    }
    static void Medical()
    {
        foreach(string code in new[]{"hemostatic_bandage","rescue_kit"})
        {
            var inventory=Inventory(code,out var id);var health=Healthy();RaidHealth.Set(ref health,BodyPart.Chest,10);health.TacticalBleeding=true;
            Check(inventory.BeginMedical(health,id,inventory.Graph.Version,BodyPart.Chest,default,10)==InventoryError.None,"start "+code);
            double end=10+ConsumableCatalog.MedicalSeconds(code);
            inventory.RefreshMedical(ref health,true,default,end-.01);
            Check(health.ChestHealth==10 && inventory.Graph.Find(id).Quantity==2,"no early use "+code);
            inventory.RefreshMedical(ref health,true,new float3(3,0,0),end);
            Check(inventory.Error==InventoryError.None && inventory.Graph.Find(id).Quantity==1 && health.ChestHealth==Mathf.Min(RaidHealth.Maximum(health,BodyPart.Chest),10+ConsumableCatalog.MedicalRestore(code)),"exact deadline and heal cap "+code);
            Check(health.TacticalBleeding!=ConsumableCatalog.StopsBleeding(code),"only bandage stops bleed "+code);
            inventory.RefreshMedical(ref health,true,default,end+1);Check(inventory.Graph.Find(id).Quantity==1,"no replay spend "+code);
            health=Healthy();RaidHealth.Set(ref health,BodyPart.Chest,10);
            Check(inventory.BeginMedical(health,id,inventory.Graph.Version,BodyPart.Chest,default,20)==InventoryError.None,"restart "+code);
            health.NonPhysiologicalDamageSequence++;
            inventory.RefreshMedical(ref health,true,default,21);
            Check(inventory.Error==InventoryError.Inaccessible && inventory.Graph.Find(id).Quantity==1 && health.ChestHealth==10,"attack cancels atomically "+code);
            Check(inventory.BeginMedical(health,id,inventory.Graph.Version-1,BodyPart.Chest,default,30)==InventoryError.Stale,"stale medical "+code);
        }
        var actor=Inventory("hemostatic_bandage",out var item);var full=Healthy();full.TacticalBleeding=true;
        Check(actor.BeginMedical(full,item,actor.Graph.Version,BodyPart.Auto,default,0)==InventoryError.None,"full-health bleeding can be bandaged");
        actor.RefreshMedical(ref full,true,default,1.5);
        Check(!full.TacticalBleeding && full.CurrentHealth==440 && actor.Graph.Find(item).Quantity==1,"bandage only consumes once at full health");
        Check(actor.BeginMedical(full,item,actor.Graph.Version,BodyPart.Auto,default,2)==InventoryError.Invalid,"healthy non-bleeding rejects waste");
        full.TacticalPoisonSeconds=2;full.TacticalBleeding=true;
        Check(actor.BeginMedical(full,item,actor.Graph.Version,BodyPart.Auto,default,3)==InventoryError.Inaccessible,"poison retains medical lockout");
    }
    static void Supplies()
    {
        foreach(string code in new[]{"hydration_gel","compressed_ration","emergency_oxygen"})
        {
            var inventory=Inventory(code,out var id);var health=Healthy();EnergyShieldEquipment.Synchronize(ref health,inventory.Graph);
            health.Energy=10;health.Hydration=40;health.Satiety=0;health.Oxygen=50;
            Check(inventory.BeginConsumable(health,id,inventory.Graph.Version,default,10)==InventoryError.None,"start supply "+code);
            double end=10+ConsumableCatalog.UseSeconds(code);
            inventory.RefreshMedical(ref health,true,default,end-.001);
            Check(inventory.Graph.Find(id).Quantity==2 && health.Oxygen==50 && health.Energy==10,"supply no early credit "+code);
            inventory.RefreshMedical(ref health,true,default,end);
            var definition=ConsumableCatalog.Get(code);
            Check(inventory.Error==InventoryError.None && inventory.Graph.Find(id).Quantity==1 &&
                health.Energy==10+definition.Energy && health.Hydration==40+definition.Hydration && health.Oxygen==50+definition.Oxygen,"supply actual resource accounting "+code);
            inventory.RefreshMedical(ref health,true,default,end+2);Check(inventory.Graph.Find(id).Quantity==1,"supply replay "+code);
            Check(inventory.BeginConsumable(health,id,inventory.Graph.Version,default,30)==InventoryError.None,"supply restart "+code);
            inventory.CancelActions();inventory.RefreshMedical(ref health,true,default,40);
            Check(inventory.Graph.Find(id).Quantity==1,"manual cancel keeps supply "+code);
        }
        var oxygen=Inventory("emergency_oxygen",out var flask);var bare=Healthy();bare.ProtectionInitialized=true;bare.HeadDomeEquipped=false;
        Check(oxygen.BeginConsumable(bare,flask,oxygen.Graph.Version,default,0)==InventoryError.Invalid,"no collar means no oxygen refill");
        bare.HeadDomeEquipped=true;bare.Oxygen=95;
        Check(oxygen.BeginConsumable(bare,flask,oxygen.Graph.Version,default,0)==InventoryError.None,"partial oxygen reserve accepts refill");
        oxygen.RefreshMedical(ref bare,true,default,1);
        Check(bare.Oxygen==100 && oxygen.Graph.Find(flask).Quantity==1,"oxygen overfill clamps once");
    }
    static void Oxygen()
    {
        var graph=InventoryGraph.Create(false);var plain=Healthy();EnergyShieldEquipment.Synchronize(ref plain,graph);plain.Oxygen=100;
        var fitted=plain;var vacuum=new AtmosphereSample(null,0,0,false);
        graph.Items.Add(new InventoryItem{Id="recycler",Code="oxygen_recycler",Parent="equipment",Region="Special1"});
        RaidOxygen.Tick(ref plain,vacuum,OxygenParameters.Default,10,1);
        RaidOxygen.Tick(ref fitted,vacuum,OxygenParameters.Default,10,1,equipmentDrainMultiplier:SpecialEquipmentRules.OxygenDrainMultiplier(graph));
        Check(Mathf.Abs((100-fitted.Oxygen)/(100-plain.Oxygen)-.75f)<.001f,"recycler changes actual server oxygen consumption");
        Check(Mathf.Abs(fitted.OxygenDrainPerSecond/plain.OxygenDrainPerSecond-.75f)<.001f,"HUD drain rate stays truthful");
        var noSuit=Healthy();noSuit.Oxygen=100;
        RaidOxygen.Tick(ref noSuit,vacuum,OxygenParameters.Default,10,1,sealedReserve:false,equipmentDrainMultiplier:.75f);
        Check(noSuit.Oxygen==0 && noSuit.HypoxiaSeconds>0,"recycler cannot replace the collar");
        var breathable=Healthy();breathable.Oxygen=50;
        RaidOxygen.Tick(ref breathable,new AtmosphereSample(null,101.325f,.21f,true),OxygenParameters.Default,1,1,equipmentDrainMultiplier:.75f);
        Check(breathable.OxygenDrainPerSecond==0,"recycler never drains breathable air");
    }
    static void Combat()
    {
        var scene=EditorSceneManager.NewPreviewScene();var previous=MoonRaidMap.Active;
        using var world=new World("Field expansion isolated authority",WorldFlags.GameServer);var em=world.EntityManager;
        try
        {
            var mapNode=new GameObject("Temporary field map");mapNode.SetActive(false);SceneManager.MoveGameObjectToScene(mapNode,scene);
            var map=mapNode.AddComponent<MoonRaidMap>();typeof(MoonRaidMap).GetProperty("Active").SetValue(null,map);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(40,.2f,40);
            Entity Spawn(string name,Vector3 position,float yaw,int owner)
            {
                var node=new GameObject(name);SceneManager.MoveGameObjectToScene(node,scene);node.transform.position=position;
                var link=node.AddComponent<GhostGameObject>();node.AddComponent<PlayerGhost>();
                var entity=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(LocalTransform),typeof(GhostOwner));
                var health=Healthy();health.ControllerState.YawDegrees=yaw;em.SetComponentData(entity,health);
                em.SetComponentData(entity,LocalTransform.FromPosition(position));em.SetComponentData(entity,new GhostOwner{NetworkId=owner});
                em.AddComponentObject(entity,new GhostGameObjectLink{LinkedInstance=link});return entity;
            }
            var owner=Spawn("Thrower",Vector3.zero,0,7);var enemy=Spawn("Target",new Vector3(0,0,6),180,-1);
            var inventory=new RaidInventoryState{Graph=InventoryGraph.Create(false),RaidId=4};
            var connection=em.CreateEntity(typeof(RaidSession),typeof(JoinedClient));em.SetComponentData(connection,new RaidSession{RaidId=4,Phase=RaidPhase.Active});
            em.SetComponentData(connection,new JoinedClient{PlayerEntity=owner});em.AddComponentObject(connection,inventory);
            var targetInventory=new RaidInventoryState{Graph=InventoryGraph.Create(false),RaidId=4};
            var targetConnection=em.CreateEntity(typeof(JoinedClient));em.SetComponentData(targetConnection,new JoinedClient{PlayerEntity=enemy});em.AddComponentObject(targetConnection,targetInventory);
            var handle=world.GetOrCreateSystem<ServerGameSystem>();var grenades=world.GetOrCreateSystemManaged<EnergyGrenadeServerSystem>();
            var glare=world.GetOrCreateSystemManaged<HaloLightGlareSystem>();em.CreateEntity(typeof(NetworkStreamInGame));
            var clock=em.CreateEntity(typeof(NetworkTime));double now=0;uint tick=10,request=0;
            void Step(float dt=.1f)
            {
                now+=dt;world.SetTime(new TimeData(now,dt));em.SetComponentData(clock,new NetworkTime{ServerTick=new NetworkTick(++tick)});Physics.SyncTransforms();
                ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
                var ecb=new EntityCommandBuffer(Allocator.Temp);system.TacticalTestTick(ref state,ecb);ecb.Playback(em);ecb.Dispose();
                system.TacticalTestRefreshMedical(ref state,connection);
                grenades.Update();glare.Update();using var outgoing=em.CreateEntityQuery(typeof(SendRpcCommandRequest));em.DestroyEntity(outgoing);
            }
            void Wait(float seconds){double end=now+seconds;while(now<end)Step();}
            Step();using var query=em.CreateEntityQuery(typeof(TacticalServerWorld));var data=em.GetComponentObject<TacticalServerWorld>(query.GetSingletonEntity());
            string Add(string code){Check(inventory.Graph.AddSupply(code,2,inventory.Graph.Equipped("ChestRig").Id)==InventoryError.None,"add throwable "+code);return inventory.Graph.Items.Find(i=>i.Code==code).Id;}
            void Command(string id,GrenadeCommand action,int? version=null)
            {
                var rpc=em.CreateEntity();em.AddComponentData(rpc,new RaidGrenadeThrowRpc{RaidId=4,ItemId=id,Command=action,RequestId=++request,
                    ExpectedVersion=version??inventory.Graph.Version,HoldId=em.GetComponentData<PredictedPlayerGhost>(owner).GrenadeHoldId});
                em.AddComponentData(rpc,new ReceiveRpcCommandRequest{SourceConnection=connection});Step();
            }
            TacticalServerWorld.Object Throw(string code)
            {
                string id=Add(code);Command(id,GrenadeCommand.Equip,inventory.Graph.Version-1);
                Check(inventory.Graph.Find(id).Quantity==2,"stale throw cannot spend "+code);
                Command(id,GrenadeCommand.Equip);Wait(.4f);Command(id,GrenadeCommand.Prime);
                Check(inventory.Graph.Find(id).Quantity==1,"priming reserves exactly one "+code);
                Wait(.6f);Command(id,GrenadeCommand.Release);Wait(.4f);
                var obj=data.Objects.LastOrDefault(o=>o.Definition.Code==code && o.Phase==TacticalWorldPhase.Flying);
                Check(obj!=null,"actual RPC releases "+code);return obj;
            }
            Check(inventory.Graph.AddSupply("hemostatic_bandage",2,inventory.Graph.Equipped("ChestRig").Id)==InventoryError.None,"bandage under wound fixture");
            var dressing=inventory.Graph.Items.Find(i=>i.Code=="hemostatic_bandage");
            var bleeding=Healthy();RaidHealth.Set(ref bleeding,BodyPart.Chest,50);bleeding.TacticalBleeding=true;em.SetComponentData(owner,bleeding);
            data.Statuses[owner]=new TacticalServerWorld.Status{Bleed=now+30};
            Check(inventory.BeginMedical(bleeding,dressing.Id,inventory.Graph.Version,BodyPart.Chest,default,now)==InventoryError.None,"begin bandage under actual wound");
            Wait(1.6f);
            Check(inventory.Error==InventoryError.None && dressing.Quantity==1 && data.Statuses[owner].Bleed==0 && !em.GetComponentData<PredictedPlayerGhost>(owner).TacticalBleeding,"own bleed ticks permit bandage and authoritative status clears");
            float stopped=em.GetComponentData<PredictedPlayerGhost>(owner).CurrentHealth;Wait(.5f);
            Check(em.GetComponentData<PredictedPlayerGhost>(owner).CurrentHealth==stopped,"wound does not return on next tick");
            bleeding=em.GetComponentData<PredictedPlayerGhost>(owner);bleeding.TacticalBleeding=true;data.Statuses[owner].Bleed=now+30;em.SetComponentData(owner,bleeding);
            Check(inventory.BeginMedical(bleeding,dressing.Id,inventory.Graph.Version,BodyPart.Chest,default,now)==InventoryError.None,"second bandage begins");
            BountyClashDamage.Apply(world,owner,10,BodyPart.Chest,5,tick,RaidDamageKind.Bullet);Step();
            Check(inventory.Error==InventoryError.Inaccessible && dressing.Quantity==1 && data.Statuses[owner].Bleed>now,"real external bullet still interrupts bandage");
            data.Statuses[owner].Bleed=0;em.SetComponentData(owner,Healthy());Step();
            var flash=Throw("flash_grenade");flash.Position=new Vector3(0,1.6f,3);flash.Resting=true;flash.Until=now+.01;Step();
            Check(flash.Phase==TacticalWorldPhase.Explosion && data.Statuses.TryGetValue(enemy,out var flashStatus) && flashStatus.FlashUntil>now,
                "flash detonation publishes timed exposure: phase="+flash.Phase+" victims="+data.Victims.Count+" statuses="+data.Statuses.Count+
                " yaw="+em.GetComponentData<PredictedPlayerGhost>(enemy).ControllerState.YawDegrees+" clear="+
                EnergyGrenadePhysics.ClearSight(scene.GetPhysicsScene(),flash.Position,new Vector3(0,1.6f,6),null)+" pos="+flash.Position+" now="+now);
            float full=em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare;
            Check(full>.8f && em.GetComponentData<PredictedPlayerGhost>(enemy).CurrentHealth==440,"flash dazzles without blast damage");
            var stateEnemy=em.GetComponentData<PredictedPlayerGhost>(enemy);stateEnemy.ControllerState.YawDegrees=0;em.SetComponentData(enemy,stateEnemy);Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare>0,"turning after detonation does not erase afterimage");
            targetInventory.Graph.Items.Add(new InventoryItem{Id="filter",Code="glare_filter",Parent="equipment",Region="Special1"});Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare<full*.4f,"equipped filter reduces actual replicated glare");
            targetInventory.Graph.Items.RemoveAll(i=>i.Id=="filter");Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare>full*.6f,"unequip removes filter benefit");
            Wait(4.1f);Check(em.GetComponentData<PredictedPlayerGhost>(enemy).HaloLightGlare==0,"flash expires completely");
            flash.Position=new Vector3(0,1.6f,3);
            data.Statuses.Clear();ServerGameSystem.TacticalFlash(em,data,flash,now);
            Check(!data.Statuses.ContainsKey(enemy),"looking away at detonation avoids flash");
            stateEnemy.ControllerState.YawDegrees=180;em.SetComponentData(enemy,stateEnemy);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(wall,scene);
            wall.transform.position=new Vector3(0,1.5f,4.5f);wall.transform.localScale=new Vector3(5,3,.2f);Physics.SyncTransforms();
            data.Statuses.Clear();ServerGameSystem.TacticalFlash(em,data,flash,now);
            Check(!data.Statuses.ContainsKey(enemy),"wall blocks flash");wall.SetActive(false);Physics.SyncTransforms();
            var smoke=new TacticalServerWorld.Object{Definition=TacticalItemRules.Find("smoke_grenade"),Phase=TacticalWorldPhase.Smoke,Scene=scene.GetPhysicsScene(),Position=new Vector3(0,0,4),Until=now+15};
            data.Objects.Add(smoke);data.Statuses.Clear();ServerGameSystem.TacticalFlash(em,data,flash,now);
            Check(!data.Statuses.ContainsKey(enemy),"smoke blocks flash");data.Objects.Remove(smoke);
            em.AddComponentData(enemy,new OnlineRaidMember{Team=3});flash.Source=new ClashDamageSource{OnlineHuman=true,OnlineTeam=3,OnlineActor=owner};
            data.Statuses.Clear();ServerGameSystem.TacticalFlash(em,data,flash,now);
            Check(!data.Statuses.ContainsKey(enemy),"online teammate protection applies to flash");em.RemoveComponent<OnlineRaidMember>(enemy);flash.Source=default;
            var poison=Throw("poison_bomb");poison.Position=new Vector3(0,.05f,5);poison.Resting=true;poison.Until=now+.01;Step();
            Check(poison.Phase==TacticalWorldPhase.Poison && Math.Abs(poison.Until-now-15)<.01,"poison detonation creates correct cloud lifetime");
            Check(em.GetComponentData<PredictedPlayerGhost>(enemy).TacticalPoisonSeconds>0 && em.GetComponentData<PredictedPlayerGhost>(enemy).CurrentHealth<440,"poison cloud actually damages and blocks healing");
            data.Statuses[enemy].Antidote=now+60;data.Statuses[enemy].Poison=0;Step();
            Check(em.GetComponentData<PredictedPlayerGhost>(enemy).TacticalPoisonSeconds==0,"existing antidote protects against new gas");
            data.Objects.Add(new TacticalServerWorld.Object{Definition=TacticalItemRules.Find("choke_bomb"),Phase=TacticalWorldPhase.Choke,Scene=scene.GetPhysicsScene(),Position=poison.Position,Until=now+20});Step();
            Check(!data.Objects.Contains(poison),"existing choke removes poison bomb cloud");
        }
        finally{typeof(MoonRaidMap).GetProperty("Active").SetValue(null,previous);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Assets()
    {
        string[] codes={"oxygen_recycler","glare_filter","survey_multitool","hemostatic_bandage","rescue_kit","hydration_gel","compressed_ration","emergency_oxygen","flash_grenade","poison_bomb"};
        var tables=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach(string code in codes)
        {
            var prefab=Resources.Load<GameObject>("Moonkov/Items/Prefabs/"+code);var icon=Resources.Load<Texture2D>("Moonkov/Items/Icons/"+code);
            Check(prefab && icon && icon.width==512 && icon.height==512,"native model/icon "+code);
            Check(prefab.GetComponentsInChildren<MeshRenderer>().Length==1 && prefab.GetComponentsInChildren<Collider>().Length==0,"one renderer without colliders "+code);
            var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh && mesh.vertexCount>0 && mesh.bounds.size.magnitude<1 && prefab.GetComponent<Renderer>().sharedMaterials.All(m=>m && m.shader.name=="Universal Render Pipeline/Lit"),"bounded URP geometry "+code);
            foreach(var reference in tables.Tables)
            {
                var table=(StringTable)reference.asset;var entry=table.GetEntry(InventoryCatalog.Get(code).Name);
                Check(entry!=null && !string.IsNullOrEmpty(entry.LocalizedValue),"localized name "+table.LocaleIdentifier+" "+code);
            }
        }
    }
}
