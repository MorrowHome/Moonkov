using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
public static class ExportSurvivalRigGeometry
{
 static string N(float f)=>f.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
 static string V(Vector3 v)=>"["+N(v.x)+","+N(v.y)+","+N(v.z)+"]";
 static string Q(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
 public static object Run(){
 if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode.");
 var rigs=new List<string>();var props=new List<string>();
 foreach(string rig in RaidActionAnimationSetup.Characters){var scene=EditorSceneManager.NewPreviewScene();
 try{
 var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"),scene);
 foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
 var a=root.GetComponentInChildren<Animator>(true);for(var n=a.transform;n;n=n.parent)n.gameObject.SetActive(true);a.runtimeAnimatorController=null;a.Rebind();a.Update(0);a.enabled=false;
 var body=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s=>s.sharedMesh.vertexCount).First();var mesh=new Mesh();body.BakeMesh(mesh);
 try{
 var vertices=mesh.vertices;var weights=body.sharedMesh.boneWeights;var palette=body.bones;var lower=a.GetBoneTransform(HumanBodyBones.LeftUpperArm);var hand=a.GetBoneTransform(HumanBodyBones.LeftHand);
 string Dominant(int i){var w=weights[i];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};return palette[ids[Array.IndexOf(ws,ws.Max())]].name;}
 var groups=Enumerable.Range(0,vertices.Length).Select(Dominant).ToArray();var hierarchy=a.GetComponentsInChildren<Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
 bool Forearm(int i)=>hierarchy.TryGetValue(groups[i],out var t)&&t.IsChildOf(lower)&&!t.IsChildOf(hand);
 var triangles=new List<string>();var used=new HashSet<int>();
 for(int s=0;s<body.sharedMesh.subMeshCount;s++){var ids=body.sharedMesh.GetTriangles(s);for(int t=0;t<ids.Length;t+=3){int x=ids[t],y=ids[t+1],z=ids[t+2];if(!Forearm(x)||!Forearm(y)||!Forearm(z))continue;used.Add(x);used.Add(y);used.Add(z);triangles.Add("{\"ids\":["+x+","+y+","+z+"],\"material\":"+Q(body.sharedMaterials[s].name)+"}");}}
 var data=new List<string>();foreach(int i in used){var w=weights[i];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] ws={w.weight0,w.weight1,w.weight2,w.weight3};var influences=new List<string>();for(int j=0;j<4;j++)if(ws[j]>0)influences.Add("{\"bone\":"+Q(palette[ids[j]].name)+",\"weight\":"+N(ws[j])+"}");data.Add("{\"id\":"+i+",\"p\":"+V(body.transform.position+body.transform.rotation*vertices[i])+",\"weights\":["+string.Join(",",influences)+"]}");}
 // Measure actual palm tissue in each bone's anatomical frame.
 var pads=new List<string>();foreach(string side in new[]{"L","R"}){var h=hierarchy["手首."+side];var f=(hierarchy["中指１."+side].position-h.position).normalized;var across=(hierarchy["人指１."+side].position-hierarchy["小指１."+side].position).normalized;var normal=Vector3.Cross(across,f)*(side=="R"?-1:1);var samples=new List<float>();for(int i=0;i<vertices.Length;i++)if(groups[i]==h.name){var d=body.transform.position+body.transform.rotation*vertices[i]-h.position;if(Vector3.Dot(d,f)>.012f&&Vector3.Dot(d,f)<.055f)samples.Add(Vector3.Dot(d,normal));}samples.Sort();float pad=samples.Count>0?samples[Mathf.Min(samples.Count-1,Mathf.FloorToInt(samples.Count*.97f))]:.014f;pads.Add(Q(side)+":"+N(Mathf.Clamp(pad,.005f,.035f)));}
 rigs.Add("{\"rig\":"+Q(rig)+",\"palmPad\":{"+string.Join(",",pads)+"},\"vertices\":["+string.Join(",",data)+"],\"triangles\":["+string.Join(",",triangles)+"]}");
 }finally{UnityEngine.Object.DestroyImmediate(mesh);}
 if(props.Count==0)foreach(var def in InventoryCatalog.Definitions.Values){string code=def.Code;if(ConsumableCatalog.Get(code)==null&&ConsumableCatalog.MedicalRestore(code)<=0&&!code.EndsWith("_shot"))continue;var holder=new GameObject("Measure prop");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder,scene);var variant=Resources.Load<GameObject>("Moonkov/Actions/Props/"+code);var model=variant?UnityEngine.Object.Instantiate(variant,holder.transform):PhysicalItemVisuals.Create(code,holder.transform,true);if(!model){UnityEngine.Object.DestroyImmediate(holder);continue;}var rs=model.GetComponentsInChildren<Renderer>(true);var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);var anchor=model.GetComponent<RaidActionProp>();Vector3 grip=anchor?anchor.Grip.position:bounds.center,contact=anchor?anchor.Contact.position:bounds.center+Vector3.up*(bounds.extents.y-.005f);props.Add("{\"code\":"+Q(code)+",\"action\":"+Q(RaidHandActionSystem.Kind(code,ConsumableCatalog.Get(code)!=null).ToString())+",\"grip\":"+V(grip)+",\"contact\":"+V(contact)+",\"center\":"+V(bounds.center)+",\"size\":"+V(bounds.size)+"}");UnityEngine.Object.DestroyImmediate(holder);}
 }finally{EditorSceneManager.ClosePreviewScene(scene);}}
 string path="SourceData/Animations/Survival/RigGeometry.json";File.WriteAllText(path,"{\"rigs\":["+string.Join(",",rigs)+"],\"props\":["+string.Join(",",props)+"]}");return new{path,rigs=rigs.Count,props=props.Count};}
}
