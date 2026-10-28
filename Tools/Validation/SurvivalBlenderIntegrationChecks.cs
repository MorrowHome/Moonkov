using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
public static class SurvivalBlenderIntegrationChecks
{
 static int checks;static void Check(bool b,string s){checks++;if(!b)throw new InvalidOperationException(s);}
 static float ForearmDistance(Animator animator,Vector3 point,bool upperArm)
 {
  var lower=animator.GetBoneTransform(upperArm?HumanBodyBones.LeftUpperArm:HumanBodyBones.LeftLowerArm);var hand=animator.GetBoneTransform(HumanBodyBones.LeftHand);
  var renderer=animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r=>r.sharedMesh.vertexCount).First();
  var palette=renderer.bones;var weights=renderer.sharedMesh.boneWeights;var included=palette.Select(b=>b&&b.IsChildOf(lower)&&!b.IsChildOf(hand)).ToArray();
  bool Forearm(int i){var w=weights[i];return (included[w.boneIndex0]?w.weight0:0)+(included[w.boneIndex1]?w.weight1:0)+(included[w.boneIndex2]?w.weight2:0)+(included[w.boneIndex3]?w.weight3:0)>.5f;}
  Vector3 Segment(Vector3 a,Vector3 b)=>a+Vector3.ClampMagnitude(b-a,Mathf.Clamp01(Vector3.Dot(point-a,b-a)/Mathf.Max(1e-12f,(b-a).sqrMagnitude))*(b-a).magnitude);
  var mesh=new Mesh();float best=float.PositiveInfinity;
  try{
   renderer.BakeMesh(mesh);var vertices=mesh.vertices;
   Vector3 P(int i)=>renderer.transform.position+renderer.transform.rotation*vertices[i];
   foreach(var ids in Enumerable.Range(0,renderer.sharedMesh.subMeshCount).Select(renderer.sharedMesh.GetTriangles))for(int i=0;i<ids.Length;i+=3){
    if(!Forearm(ids[i])||!Forearm(ids[i+1])||!Forearm(ids[i+2]))continue;
    var a=P(ids[i]);var b=P(ids[i+1]);var c=P(ids[i+2]);var ab=b-a;var ac=c-a;var normal=Vector3.Cross(ab,ac).normalized;var projected=point-normal*Vector3.Dot(point-a,normal);var ap=projected-a;
    float aa=Vector3.Dot(ab,ab),bb=Vector3.Dot(ab,ac),cc=Vector3.Dot(ac,ac),den=aa*cc-bb*bb;
    if(den<1e-14f)continue;float u=(cc*Vector3.Dot(ap,ab)-bb*Vector3.Dot(ap,ac))/den,v=(aa*Vector3.Dot(ap,ac)-bb*Vector3.Dot(ap,ab))/den;
    float distance=u>=0&&v>=0&&u+v<=1?Vector3.Distance(point,projected):Mathf.Min(Vector3.Distance(point,Segment(a,b)),Vector3.Distance(point,Segment(b,c)),Vector3.Distance(point,Segment(c,a)));
    best=Mathf.Min(best,distance);
   }
   return best;
  }finally{UnityEngine.Object.DestroyImmediate(mesh);}
 }
 public static object Run(){checks=0;Check(!EditorApplication.isPlaying,"Edit mode required");var samples=new List<object>();
 foreach(string rig in RaidActionAnimationSetup.Characters){var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;RaidActionPresentation action=null;
 try{
 root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"),scene);foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
 var a=root.GetComponentInChildren<Animator>(true);for(var n=a.transform;n;n=n.parent)n.gameObject.SetActive(true);a.runtimeAnimatorController=null;a.Rebind();a.Update(0);a.enabled=false;
 var data=JsonUtility.FromJson<DollSingerActionAnimation.Library>(Resources.Load<TextAsset>("Moonkov/Actions/"+rig).text);Check(data.anatomicalContactVersion==2,rig+" is using prototype data");var hierarchy=a.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
 action=a.gameObject.AddComponent<RaidActionPresentation>();action.Configure(a,null,null,rig);uint sequence=1;
 foreach(var code in InventoryCatalog.Definitions.Keys.Where(c=>ConsumableCatalog.Get(c)!=null||ConsumableCatalog.MedicalRestore(c)>0||c.EndsWith("_shot")).Concat(new[]{""})){
 var kind=code==""?HandAction.Search:RaidHandActionSystem.Kind(code,ConsumableCatalog.Get(code)!=null);var motion=data.motions.FirstOrDefault(m=>m.action==kind.ToString()&&(code==""||m.items!=null&&m.items.Contains(code)))??data.motions.First(m=>m.action==kind.ToString());
 Check(kind==HandAction.Treat||kind==HandAction.Search||motion.root?.positions?.Length==motion.frames*3,rig+" missing authored prop trajectory "+code);
 var state=new PredictedPlayerGhost{CurrentHealth=100,HandAction=(byte)kind,HandActionSequence=sequence++,HandActionItem=code,HandActionDuration=3,HandActionRemaining=3};action.Present(state,false,false,false);
 foreach(float phase in new[]{.05f,.20f,.50f,.77f,.95f}){
 action.SamplePreview(phase);Check(action.OwnsHands,rig+" lost hand ownership "+code);
 foreach(string name in new[]{"ひじ.R","手捩.R","手首.R","人指１.R","親指１.R"}){
 int index=Array.IndexOf(data.bones,name);float f=action.SampledPhase*(motion.frames-1);int x=Mathf.FloorToInt(f),y=Mathf.Min(x+1,motion.frames-1);Quaternion Q(int frame){int i=(frame*data.bones.Length+index)*4;return new Quaternion(motion.rotations[i],motion.rotations[i+1],motion.rotations[i+2],motion.rotations[i+3]);}
 Check(Quaternion.Angle(hierarchy[name].localRotation,Quaternion.Slerp(Q(x),Q(y),f-x))<.15f,rig+" runtime altered saved joint "+name+" "+code);
 }
 if(phase==.50f&&(kind==HandAction.Eat||kind==HandAction.Drink||kind==HandAction.Inject)){Check(action.ContactError<.04f,rig+" contact miss "+code+" "+action.ContactError);samples.Add(new{rig,code,error=action.ContactError});}
 if(phase==.50f&&code=="medkit"){var model=a.GetComponentsInChildren<RaidActionProp>().First(p=>p.name.Contains("medkit")&&p.gameObject.activeInHierarchy);float surfaceError=ForearmDistance(a,model.Contact.position,rig=="HatsuneMiku");Check(surfaceError<.006f,rig+" needle misses actual deformed arm: "+surfaceError);samples.Add(new{rig,code,surfaceError});}
 }
 action.Present(default,false,false,false);Check(!action.OwnsHands,rig+" cancellation retained hand ownership");
 }
 }finally{if(action)action.DisposePresentation();if(root)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}}
 foreach(string code in new[]{"ration","water","medkit","vitality_shot","stamina_shot","antidote_shot"}){var p=Resources.Load<GameObject>("Moonkov/Actions/Props/"+code).GetComponent<RaidActionProp>();Check(p.Authored,code+" retained old prop");Check(p.Parts!=null&&p.Parts.Length>1,code+" missing independent pieces");if(code.Contains("shot")||code=="medkit")Check(p.PlungerTravel==.01f&&p.FindPart("NeedleSheath"),code+" incorrect plunger or cap");}
 var audio=MoonkovAudio.Library;foreach(var bank in new[]{audio.EatBite,audio.DrinkSip,audio.BottleOpen,audio.PacketOpen,audio.InjectorCap,audio.InjectorPress}){Check(bank&&bank.Clips.Count>0,"Missing recorded action foley");Check(bank.Clips.All(c=>c.channels==1),"Action foley must be mono");}
 var result=new{checks,result="PASS saved bone playback, item trajectories, contacts, complete catalogue, cancellation, real meshes and recorded clips",samples};Directory.CreateDirectory("LocalData/Actions/Integration");File.WriteAllText("LocalData/Actions/Integration/EditChecks.txt","PASS "+checks);return result;}
}
