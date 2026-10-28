using System;
using System.Linq;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class SurvivalActionChecks {
 static int count;
 static void Check(bool b,string message){count++;if(!b)throw new InvalidOperationException(message);}
 public static object Run(){
 count=0;
 var anatomy=new System.Collections.Generic.List<object>();
 foreach(var code in new[]{"ration","energy_gel","protein_bar","alien_raw_meat"})Check(RaidHandActionSystem.Kind(code,true)==HandAction.Eat,"Food classification: "+code);
 foreach(var code in new[]{"water","electrolyte"})Check(RaidHandActionSystem.Kind(code,true)==HandAction.Drink,"Drink classification");
 foreach(var code in new[]{"oxygen_canister","oxygen_reserve"})Check(RaidHandActionSystem.Kind(code,true)==HandAction.Treat,"Oxygen is not a drink");
 foreach(var code in new[]{"medkit","vitality_shot","stamina_shot","antidote_shot"})Check(RaidHandActionSystem.Kind(code,false)==HandAction.Inject,"Injector classification");
 var sequence=new PredictedPlayerGhost(); RaidHandActionSystem.Publish(ref sequence,HandAction.Eat,"ration",3,3);uint first=sequence.HandActionSequence;
 RaidHandActionSystem.Publish(ref sequence,HandAction.Eat,"ration",2.9f,3);Check(sequence.HandActionSequence==first,"Refresh restarted action");
 RaidHandActionSystem.Publish(ref sequence,HandAction.Eat,"ration",2.9f,3);Check(sequence.HandActionSequence==first,"Duplicate restarted action");
 RaidHandActionSystem.Publish(ref sequence,HandAction.None,null,0,0);Check(sequence.HandAction==0&&sequence.HandActionItem.IsEmpty,"Stop retained prop");
 RaidHandActionSystem.Publish(ref sequence,HandAction.Eat,"ration",3,3);Check(sequence.HandActionSequence==first+1,"Restart did not create fresh action");
 var variants=AssetDatabase.FindAssets("t:Prefab",new[]{RaidActionAnimationSetup.Root+"Props"});Check(variants.Length==11,"Action variant set incomplete");
 foreach(var guid in variants){
 var variant=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));var anchors=variant.GetComponent<RaidActionProp>();
 Check(anchors&&anchors.Grip&&anchors.Contact,variant.name+" missing anchors");
 foreach(var filter in variant.GetComponentsInChildren<MeshFilter>(true)){
 var mesh=filter.sharedMesh;Check(mesh&&mesh.vertexCount>0&&mesh.bounds.size.magnitude<.5f,variant.name+" mesh units or geometry invalid");
 Check(mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),variant.name+" nonfinite mesh");
 }
 Check(variant.GetComponentsInChildren<MeshRenderer>(true).All(r=>r.sharedMaterials.All(m=>m)),variant.name+" missing material");
 }
 foreach(var supply in new[]{"ration","water"})using(var world=new World("Isolated survival action authority")){
 var em=world.EntityManager; var graph=InventoryGraph.Create(false);graph.AddSupply(supply,2,graph.Equipped("Backpack").Id);
 var inv=new RaidInventoryState{Graph=graph,RaidId=4};var health=new PredictedPlayerGhost();RaidHealth.Initialize(ref health);health.Energy=40;health.Hydration=40;
 var player=em.CreateEntity(typeof(PredictedPlayerGhost));em.SetComponentData(player,health);var connection=em.CreateEntity(typeof(JoinedClient));
 em.SetComponentData(connection,new JoinedClient{PlayerEntity=player});em.AddComponentObject(connection,inv);
 string id=graph.Items.First(i=>i.Code==supply).Id;
 Check(inv.BeginConsumable(health,id,graph.Version,float3.zero,10)==InventoryError.None,"Start real supply");
 world.SetTime(new TimeData(10,0));var system=world.GetOrCreateSystemManaged<RaidHandActionSystem>();system.Update();
 var shown=em.GetComponentData<PredictedPlayerGhost>(player);Check(shown.HandAction==(byte)(supply=="ration"?HandAction.Eat:HandAction.Drink),"Server published wrong action");
 Check(shown.HandActionItem.ToString()==supply&&graph.Count(supply,true)==2,"Animation consumed real item");
 world.SetTime(new TimeData(10.25,0));system.Update();Check(em.GetComponentData<PredictedPlayerGhost>(player).HandActionSequence==shown.HandActionSequence,"Server tick restarted motion");
 inv.CancelActions();system.Update();Check(em.GetComponentData<PredictedPlayerGhost>(player).HandAction==0&&graph.Count(supply,true)==2,"Cancel did not stop without consumption");
 inv.BeginConsumable(health,id,graph.Version,float3.zero,20);world.SetTime(new TimeData(20,0));system.Update();
 Check(em.GetComponentData<PredictedPlayerGhost>(player).HandAction!=0,"Second use missing");
 health.CurrentHealth=0;em.SetComponentData(player,health);system.Update();Check(em.GetComponentData<PredictedPlayerGhost>(player).HandAction==0,"Death retained action");
 }
 foreach(string rig in RaidActionAnimationSetup.Characters){
 var scene=EditorSceneManager.NewPreviewScene();RaidActionPresentation presentation=null;
 try{
 var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"),scene);
 foreach(var mb in root.GetComponentsInChildren<MonoBehaviour>(true))mb.enabled=false;
 var a=root.GetComponentInChildren<Animator>(true);for(var p=a.transform;p;p=p.parent)p.gameObject.SetActive(true);
 a.runtimeAnimatorController=null;a.Rebind();a.Update(0);a.enabled=false;
 var bake=new DollSingerActionAnimation(a,AssetDatabase.LoadAssetAtPath<TextAsset>(RaidActionAnimationSetup.Root+rig+".json"));
 foreach(var kind in new[]{HandAction.Search,HandAction.Eat,HandAction.Drink,HandAction.Treat,HandAction.Inject})Check(bake.Has(kind),rig+" missing motion");
 var foot=a.GetBoneTransform(HumanBodyBones.RightFoot);var oldFoot=foot.position;var head=a.GetBoneTransform(HumanBodyBones.Head);var oldHead=head.position;
 bake.Sample(HandAction.Drink,0,1,"water");var hand=a.GetBoneTransform(HumanBodyBones.RightHand);var before=hand.position;bake.RestoreBeforeAnimation();bake.Sample(HandAction.Drink,.5f,1,"water");
 Check(Vector3.Distance(before,hand.position)>.08f,rig+" authored motion on wrong hand");
 Check(Vector3.Distance(oldFoot,foot.position)<.0001f&&Vector3.Distance(oldHead,head.position)<.0001f,rig+" action changed locomotion or eye");
 var middle=a.GetBoneTransform(HumanBodyBones.RightMiddleProximal);var elbow=a.GetBoneTransform(HumanBodyBones.RightLowerArm);
 float sourceBend=Vector3.Angle(hand.position-elbow.position,middle.position-hand.position);
 var handLocal=hand.localRotation;var elbowLocal=elbow.localRotation;
 bake.RestoreBeforeAnimation();presentation=a.gameObject.AddComponent<RaidActionPresentation>();presentation.Configure(a,null,null,rig);
 presentation.Present(new PredictedPlayerGhost{CurrentHealth=100,HandAction=(byte)HandAction.Drink,HandActionSequence=1,HandActionItem="water",HandActionDuration=2,HandActionRemaining=2},true,true,false);
 presentation.SamplePreview(.5f);var prop=a.GetComponentInChildren<RaidActionProp>();Check(prop&&prop.Contact,"Opened prop anchor missing");
 var contact=new RaidActionContact(a);float error=Vector3.Distance(contact.Mouth,prop.Contact.position);Check(error<.025f,rig+" mouth contact error "+error);
 float runtimeBend=Vector3.Angle(hand.position-elbow.position,middle.position-hand.position);
 Check(sourceBend<72&&Mathf.Abs(sourceBend-runtimeBend)<.12f,rig+" runtime bent the authored wrist");
 Check(Quaternion.Angle(handLocal,hand.localRotation)<.12f&&Quaternion.Angle(elbowLocal,elbow.localRotation)<.12f,rig+" runtime replaced authored hand/elbow rotations");
 anatomy.Add(new{rig,sourceBend,runtimeBend});
 Check(prop.Cap.gameObject.activeSelf&&Vector3.Distance(prop.Cap.position,prop.transform.position)>.05f,"Removed bottle cap was not held by the left hand");
 presentation.Present(new PredictedPlayerGhost{CurrentHealth=100,HandAction=(byte)HandAction.Inject,HandActionSequence=2,HandActionItem="medkit",HandActionDuration=3,HandActionRemaining=3},true,true,false);
 presentation.SamplePreview(.55f);prop=a.GetComponentsInChildren<RaidActionProp>().First(p=>p.name.Contains("medkit"));
 Check(prop.Needle&&prop.Needle.activeSelf,"Injector needle missing");
 Check(presentation.ContactError<.025f,rig+" needle misses actual left forearm "+presentation.ContactError);
 Check(prop.Plunger.localPosition.y<-.001f,"Plunger was not pressed");
 presentation.Present(new PredictedPlayerGhost{CurrentHealth=100,HandAction=(byte)HandAction.Eat,HandActionSequence=3,HandActionItem="alien_raw_meat",HandActionDuration=3,HandActionRemaining=3},true,true,false);
 presentation.SamplePreview(.5f);Check(presentation.ContactError<.025f,rig+" raw food misses mouth");
 presentation.Present(default,false,false,false);Check(!presentation.OwnsHands,"Inactive actor retained hands");
 }finally{if(presentation)presentation.DisposePresentation();EditorSceneManager.ClosePreviewScene(scene);}
 }
 return new{checks=count,result="PASS server authority, duplicates, cancellation, death, real rigs, movement isolation, opening and mouth contact",anatomy};
 }
}
