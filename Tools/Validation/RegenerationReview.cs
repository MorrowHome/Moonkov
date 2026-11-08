using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using BodyPart=Unity.MP_FPS.BodyPart;

// Disposable server World and preview scene only. No transport, profile writes, or Play Mode.
public static class RegenerationReview
{
    static int checks;
    static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
    static bool Near(float a,float b)=>Mathf.Abs(a-b)<.015f;
    static PredictedPlayerGhost Healthy(){var h=new PredictedPlayerGhost();RaidHealth.Initialize(ref h);return h;}
    public static string Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode.");
        checks=0;BodyRules();Authority();Presentation();
        string result=$"Regeneration shot: {checks} checks passed. Actual injection RPC, rate, duration, refresh, poison, damage, death, action publishing, owner countdown and assets.";
        Directory.CreateDirectory("LocalData/Regeneration");File.WriteAllText("LocalData/Regeneration/review.txt",result);return result;
    }
    static void BodyRules()
    {
        var h=Healthy();RaidHealth.Set(ref h,BodyPart.Head,20);RaidHealth.Set(ref h,BodyPart.Chest,20);
        float before=h.CurrentHealth;
        Check(Near(RaidHealth.Regenerate(ref h,2),2) && Near(h.CurrentHealth-before,2),"2 HP is one body-wide budget");
        Check(h.HeadHealth==20 && h.ChestHealth==22,"lowest surviving health fraction is restored first");
        h=Healthy();RaidHealth.Set(ref h,BodyPart.Head,34);RaidHealth.Set(ref h,BodyPart.Chest,84);
        Check(Near(RaidHealth.Regenerate(ref h,10),2) && h.CurrentHealth==440,"unused budget spills to another wound then caps");
        h=Healthy();RaidHealth.Set(ref h,BodyPart.LeftArm,0);RaidHealth.Set(ref h,BodyPart.Chest,20);
        Check(RaidHealth.Regenerate(ref h,1000)>0 && h.LeftArmHealth==0 && h.CurrentHealth==380,"destroyed limb stays destroyed");
        h.ClashEnabled=true;h.ClashLife=ClashLife.Alive;h.ClashMaxHealthFraction=.6f;
        RaidHealth.Set(ref h,BodyPart.Chest,10);RaidHealth.Regenerate(ref h,1000);
        Check(Near(h.ChestHealth,51) && h.ClashMaxHealthFraction==.6f && h.LeftArmHealth==0,"Clash recoverable cap stays lost");
        h=Healthy();RaidHealth.Set(ref h,BodyPart.Chest,20);before=h.CurrentHealth;
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-1,0})Check(RaidHealth.Regenerate(ref h,bad)==0 && h.CurrentHealth==before,"invalid healing budget rejected");
        h.TacticalPoisonSeconds=1;Check(RaidHealth.Regenerate(ref h,2)==0 && h.CurrentHealth==before,"poison blocks regeneration");
        h.TacticalPoisonSeconds=0;h.ClashEnabled=true;h.ClashLife=ClashLife.Downed;
        Check(RaidHealth.Regenerate(ref h,2)==0,"downed players cannot regenerate");
        h=Healthy();RaidHealth.Set(ref h,BodyPart.Head,0);Check(RaidHealth.Regenerate(ref h,2)==0 && h.CurrentHealth==0,"dead player cannot revive");
        h=new PredictedPlayerGhost{CurrentHealth=50};Check(RaidHealth.Regenerate(ref h,2)==0,"uninitialized body receives no fabricated health");
    }
    static void Authority()
    {
        var scene=EditorSceneManager.NewPreviewScene();var previous=MoonRaidMap.Active;
        using var world=new World("Regeneration isolated authority",WorldFlags.GameServer);var em=world.EntityManager;
        try
        {
            var mapNode=new GameObject("Temporary regeneration map");mapNode.SetActive(false);SceneManager.MoveGameObjectToScene(mapNode,scene);
            var map=mapNode.AddComponent<MoonRaidMap>();typeof(MoonRaidMap).GetProperty("Active").SetValue(null,map);
            var actor=new GameObject("Injector wearer");SceneManager.MoveGameObjectToScene(actor,scene);
            var link=actor.AddComponent<GhostGameObject>();actor.AddComponent<PlayerGhost>();
            var player=em.CreateEntity(typeof(PredictedPlayerGhost),typeof(LocalTransform),typeof(GhostOwner));
            em.SetComponentData(player,Healthy());em.SetComponentData(player,LocalTransform.Identity);em.SetComponentData(player,new GhostOwner{NetworkId=7});
            em.AddComponentObject(player,new GhostGameObjectLink{LinkedInstance=link});
            var inventory=new RaidInventoryState{Graph=InventoryGraph.Create(false),RaidId=4};
            var connection=em.CreateEntity(typeof(RaidSession),typeof(JoinedClient));
            em.SetComponentData(connection,new RaidSession{RaidId=4,Phase=RaidPhase.Active});em.SetComponentData(connection,new JoinedClient{PlayerEntity=player});em.AddComponentObject(connection,inventory);
            var handle=world.GetOrCreateSystem<ServerGameSystem>();var hands=world.GetOrCreateSystemManaged<RaidHandActionSystem>();
            var clock=em.CreateEntity(typeof(NetworkTime));double now=0;uint tick=10,request=0;
            var snapshot=new TacticalItemOwnerRpc();
            void Step(float dt=.1f)
            {
                now+=dt;world.SetTime(new TimeData(now,dt));em.SetComponentData(clock,new NetworkTime{ServerTick=new NetworkTick(++tick)});
                ref var system=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);ref var state=ref world.Unmanaged.ResolveSystemStateRef(handle);
                var ecb=new EntityCommandBuffer(Allocator.Temp);system.TacticalTestTick(ref state,ecb);ecb.Playback(em);ecb.Dispose();hands.Update();
                using var owners=em.CreateEntityQuery(typeof(TacticalItemOwnerRpc));using var packets=owners.ToComponentDataArray<TacticalItemOwnerRpc>(Allocator.Temp);
                foreach(var packet in packets)if(packet.RaidId==4)snapshot=packet;
                using var outgoing=em.CreateEntityQuery(typeof(SendRpcCommandRequest));em.DestroyEntity(outgoing);
            }
            void Wait(float seconds){double end=now+seconds;while(now<end-.000001)Step((float)Math.Min(.25,end-now));}
            Step();using var query=em.CreateEntityQuery(typeof(TacticalServerWorld));var data=em.GetComponentObject<TacticalServerWorld>(query.GetSingletonEntity());
            string Reset(bool wounded=true)
            {
                inventory.CancelActions();inventory.Graph=InventoryGraph.Create(false);inventory.RequestId=0;data.Uses.Clear();data.Statuses.Clear();data.NextOwnerSnapshot=0;
                var h=Healthy();if(wounded)RaidHealth.Set(ref h,BodyPart.Chest,10);em.SetComponentData(player,h);
                Check(inventory.Graph.AddSupply("regeneration_shot",3,inventory.Graph.Equipped("ChestRig").Id)==InventoryError.None,"prepare injection stock");
                Step();return inventory.Graph.Items.First(i=>i.Code=="regeneration_shot").Id;
            }
            void Command(string id,TacticalItemAction action=TacticalItemAction.Use,int? version=null,uint? replay=null)
            {
                var rpc=em.CreateEntity();em.AddComponentData(rpc,new RaidTacticalItemActionRpc{RaidId=4,ItemId=id,Action=action,RequestId=replay??++request,ExpectedVersion=version??inventory.Graph.Version});
                em.AddComponentData(rpc,new ReceiveRpcCommandRequest{SourceConnection=connection});Step();
            }
            string id=Reset();Command(id,version:inventory.Graph.Version-1);
            Check(!inventory.TacticalPending && inventory.Graph.Find(id).Quantity==3 && inventory.Error==InventoryError.Stale,"stale RPC does not start or consume");
            Command(id);uint started=request;
            Check(inventory.TacticalPending && inventory.Graph.Find(id).Quantity==3,"injection starts without spending");
            var h0=em.GetComponentData<PredictedPlayerGhost>(player);
            Check(h0.HandAction==(byte)HandAction.Inject && h0.HandActionItem.ToString()=="regeneration_shot" && h0.TimedActionActive,"connection-keyed use publishes actor injection and movement state");
            Check(h0.HandActionDuration==3 && h0.ConsumableUseSequence>0,"injection duration and acoustic event");
            Wait(2.75f);Check(inventory.Graph.Find(id).Quantity==3 && (!data.Statuses.TryGetValue(player,out var early) || early.Regeneration==0),"no early buff or item spend");
            Step(.25f);
            Check(!inventory.TacticalPending && inventory.Graph.Find(id).Quantity==2 && Near(em.GetComponentData<PredictedPlayerGhost>(player).ChestHealth,10),"completion consumes one but has no instant heal");
            Check(Math.Abs(data.Statuses[player].Regeneration-now-120)<.001,"exact 120 second duration");
            Wait(1);Check(Near(em.GetComponentData<PredictedPlayerGhost>(player).ChestHealth,12),"actual server restores 2 HP per second");
            Check(snapshot.RegenerationSeconds>118 && snapshot.RegenerationSeconds<=120 && snapshot.Remaining==0,"owner receives authoritative buff countdown");
            Command(id,replay:started);Check(inventory.Graph.Find(id).Quantity==2 && !inventory.TacticalPending,"replayed request cannot spend or refresh");
            Command(id);Wait(3);double refreshed=data.Statuses[player].Regeneration;
            Check(inventory.Graph.Find(id).Quantity==1 && Math.Abs(refreshed-now-120)<.01,"second dose refreshes, never adds duration");
            float before=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;Wait(1);
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth-before,2),"repeat dose does not double healing rate");
            data.Statuses[player].Poison=now+1.1;before=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;Step(.25f);
            Check(Near(before-em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth,1.5f),"poison damage is not offset by regeneration");
            Check(data.Statuses[player].Regeneration==refreshed,"poison does not pause the expiry clock");
            data.Statuses[player].Poison=0;before=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;Wait(1);
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth-before,2),"healing resumes without poison catch-up credit");
            var wounded=em.GetComponentData<PredictedPlayerGhost>(player);RaidHealth.Damage(ref wounded,BodyPart.Chest,3,++tick);em.SetComponentData(player,wounded);Step();
            Check(data.Statuses[player].Regeneration==refreshed,"new damage after injection keeps the effect");
            data.Statuses[player].Regeneration=now+.125;before=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;Step(.25f);
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth-before,.25f),"expiry tick grants only its remaining fraction");
            before=em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth;Wait(.5f);
            Check(em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==before && snapshot.RegenerationSeconds==0 && data.Statuses[player].Regeneration==0,"expired effect clears server work and owner countdown");
            id=Reset();Command(id);Command(id,TacticalItemAction.Cancel);Wait(3);
            Check(inventory.Graph.Find(id).Quantity==3 && data.Statuses[player].Regeneration==0,"cancel keeps item and grants no buff");
            Command(id);wounded=em.GetComponentData<PredictedPlayerGhost>(player);RaidHealth.Damage(ref wounded,BodyPart.Chest,1,++tick);em.SetComponentData(player,wounded);Step();
            Check(inventory.Graph.Find(id).Quantity==3 && !inventory.TacticalPending && data.Statuses[player].Regeneration==0,"attack during injection interrupts atomically");
            id=Reset(false);Command(id);Wait(3);
            Check(inventory.Graph.Find(id).Quantity==2 && em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==440,"full-health preventive injection is permitted");
            Wait(2);wounded=em.GetComponentData<PredictedPlayerGhost>(player);RaidHealth.Set(ref wounded,BodyPart.Chest,10);em.SetComponentData(player,wounded);Wait(1);
            Check(Near(em.GetComponentData<PredictedPlayerGhost>(player).ChestHealth,12),"full-health idle time cannot bank a burst heal");
            wounded=em.GetComponentData<PredictedPlayerGhost>(player);RaidHealth.Set(ref wounded,BodyPart.Head,0);em.SetComponentData(player,wounded);Step();
            Check(data.Statuses[player].Regeneration==0 && em.GetComponentData<PredictedPlayerGhost>(player).CurrentHealth==0,"death removes regeneration");
            wounded=Healthy();RaidHealth.Set(ref wounded,BodyPart.Chest,10);em.SetComponentData(player,wounded);Wait(1);
            Check(data.Statuses[player].Regeneration==0 && em.GetComponentData<PredictedPlayerGhost>(player).ChestHealth==10,"respawn cannot inherit old regeneration");
            id=Reset();Command(id);Wait(3);wounded=em.GetComponentData<PredictedPlayerGhost>(player);wounded.ClashEnabled=true;wounded.ClashLife=ClashLife.Downed;em.SetComponentData(player,wounded);Step();
            Check(data.Statuses[player].Regeneration==0,"Clash down removes regeneration");
            var orphan=em.CreateEntity(typeof(PredictedPlayerGhost));var survivor=Healthy();RaidHealth.Set(ref survivor,BodyPart.Chest,10);em.SetComponentData(orphan,survivor);
            data.Statuses[orphan]=new TacticalServerWorld.Status{Regeneration=now+120,RegenerationTick=now};Step();
            Check(data.Statuses.ContainsKey(orphan) && em.GetComponentData<PredictedPlayerGhost>(orphan).ChestHealth>10,"status lifetime includes regeneration without a sprint component");
            ref var server=ref world.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);ref var ss=ref world.Unmanaged.ResolveSystemStateRef(handle);
            var clear=new EntityCommandBuffer(Allocator.Temp);server.TacticalTestClear(ref ss,clear);clear.Playback(em);clear.Dispose();
            Check(data.Statuses.Count==0 && data.Uses.Count==0,"raid cleanup removes effect and pending use");
        }
        finally{typeof(MoonRaidMap).GetProperty("Active").SetValue(null,previous);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Presentation()
    {
        Check(RaidHandActionSystem.Kind("regeneration_shot",false)==HandAction.Inject,"new injector action classification");
        foreach(string code in new[]{"vitality_shot","stamina_shot","antidote_shot"})Check(RaidHandActionSystem.Kind(code,false)==HandAction.Inject,"existing injector classification "+code);
        var prefab=Resources.Load<GameObject>("Moonkov/Items/Prefabs/regeneration_shot");var icon=Resources.Load<Texture2D>("Moonkov/Items/Icons/regeneration_shot");
        Check(prefab && icon && icon.width==512 && icon.height==512,"native model and transparent inventory icon");
        Check(prefab.GetComponentsInChildren<Renderer>().Length==1 && prefab.GetComponentsInChildren<Collider>().Length==0,"bounded world item renderer");
        var action=Resources.Load<GameObject>("Moonkov/Actions/Props/regeneration_shot");var prop=action?action.GetComponent<RaidActionProp>():null;
        Check(prop && prop.Authored && prop.Grip && prop.Contact && prop.Needle && prop.FindPart("NeedleSheath") && prop.PlungerTravel==.01f,"authored injection anchors, cap and moving plunger retained");
        var tables=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach(var reference in tables.Tables)
        {
            var table=(StringTable)reference.asset;
            foreach(string key in new[]{"Regeneration shot","REGEN {0:0}s","REGEN PAUSED {0:0}s"})Check(!string.IsNullOrEmpty(table.GetEntry(key)?.LocalizedValue),"localized "+table.LocaleIdentifier+" "+key);
        }
        var caption=typeof(RaidHUD).GetMethod("QuickSprintCaption",BindingFlags.Static|BindingFlags.NonPublic);
        string normal=(string)caption.Invoke(null,new object[]{new TacticalItemOwnerRpc{RegenerationSeconds=42}});
        string paused=(string)caption.Invoke(null,new object[]{new TacticalItemOwnerRpc{RegenerationSeconds=42,PoisonSeconds=3}});
        Check(normal.Contains("42") && paused.Contains("42") && normal!=paused,"HUD distinguishes active and poison-paused regeneration");
        Check(caption.Invoke(null,new object[]{new TacticalItemOwnerRpc()})==null,"expired effect leaves no label");
    }
}
