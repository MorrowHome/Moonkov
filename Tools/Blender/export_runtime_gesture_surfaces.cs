// Run with Unity CLI eval_file while WeaponGestureLab is playing.
// Captures the actual deformed full player mesh for offline intersection checks.
var lab=UnityEngine.Object.FindAnyObjectByType<Unity.MP_FPS.WeaponGestureLab>();
var root=UnityEngine.Object.FindAnyObjectByType<UnityEngine.UIElements.UIDocument>().rootVisualElement;
T Q<T>(string name) where T:UnityEngine.UIElements.VisualElement => UnityEngine.UIElements.UQueryExtensions.Q<T>(root,name);
var renderer=lab.Halo.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true).First(x=>x.name=="FirstPersonGestureArms (Runtime)");
var original=UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/DollSinger/Art/Model/LuoTianYi_DollSinger.fbx").OfType<UnityEngine.Mesh>().OrderByDescending(x=>x.vertexCount).First();
string Group(string n){var side=n.EndsWith(".L")?"L":n.EndsWith(".R")?"R":"";if(side=="")return "body";if(new[]{"人指","中指","薬指","小指","親指"}.Any(d=>n.StartsWith(d)))return n.Substring(0,2)+"."+side;if(n.StartsWith("手首"))return "palm."+side;if(n.StartsWith("ひじ")||n.StartsWith("手捩"))return "forearm."+side;if(n.StartsWith("腕")||n.StartsWith("肩"))return "upper."+side;return "body";}
var labels=original.boneWeights.Select(w=>new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}.OrderByDescending(x=>x.Item2).First().Item1).Select(i=>Group(renderer.bones[i].name)).ToArray();
var folder=System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"Temp/GestureRuntimeSurfaces");System.IO.Directory.CreateDirectory(folder);
var bake=(UnityEngine.TextAsset)new UnityEditor.SerializedObject(lab.Halo).FindProperty("gestureBake").objectReferenceValue;
var revision=System.Text.Json.JsonDocument.Parse(bake.text).RootElement.GetProperty("revision").GetString();
var diskRevision=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(UnityEditor.AssetDatabase.GetAssetPath(bake))).RootElement.GetProperty("revision").GetString();
if(revision!=diskRevision)throw new System.InvalidOperationException("Unity bake is stale. Stop Play and force-import the JSON before capturing.");
foreach(var old in System.IO.Directory.GetFiles(folder,"*.json")) System.IO.File.Delete(old);
System.IO.File.Delete(folder+"/complete.txt");
System.IO.File.WriteAllText(folder+"/topology.json",System.Text.Json.JsonSerializer.Serialize(new{labels,triangles=original.triangles}));
var mesh=new UnityEngine.Mesh();
void Save(string name){
 renderer.BakeMesh(mesh,true);var matrix=renderer.transform.localToWorldMatrix;
 var vertices=mesh.vertices.Select(v=>matrix.MultiplyPoint3x4(v)).Select(v=>new[]{v.x,v.y,v.z}).ToArray();
 System.IO.File.WriteAllText(folder+"/"+name+".json",System.Text.Json.JsonSerializer.Serialize(new{vertices}));
}
float ReloadProgress(int w)=>w==0?lab.Halo.rifleVisual.ReloadProgress:w==1?lab.Halo.revolverVisual.ReloadProgress:lab.Halo.shotgunVisual.ReloadProgress;
System.Collections.IEnumerator Record(){
 lab.SetPaused(false);Q<UnityEngine.UIElements.Toggle>("aim").value=true;Q<UnityEngine.UIElements.Toggle>("firstPerson").value=true;Q<UnityEngine.UIElements.Slider>("speed").value=1;
 Q<UnityEngine.UIElements.Slider>("pitch").lowValue=-80;Q<UnityEngine.UIElements.Slider>("pitch").highValue=80;
 for(int w=0;w<3;w++) foreach(float pitch in new[]{-80f,-65f,-40f,0f,15f,30f,45f,65f,70f,75f,80f}){
  lab.SelectWeapon(w);Q<UnityEngine.UIElements.Slider>("pitch").value=pitch;
  yield return new UnityEngine.WaitForSeconds(.45f);yield return new UnityEngine.WaitForEndOfFrame();
  Save(w+"-"+pitch+"-idle");
  if(pitch==-80 || pitch==80){
   UnityEngine.ScreenCapture.CaptureScreenshot(folder+"/"+w+"-"+pitch+".png");
   lab.Fire();yield return new UnityEngine.WaitForSeconds(new[]{.054f,.118f,.12f}[w]);yield return new UnityEngine.WaitForEndOfFrame();
   Save(w+"-"+pitch+"-fire");yield return new UnityEngine.WaitForSeconds(.65f);
   lab.Reload();
   while(lab.Reloading && ReloadProgress(w)<.5f)yield return new UnityEngine.WaitForEndOfFrame();
   yield return new UnityEngine.WaitForEndOfFrame();Save(w+"-"+pitch+"-reload");
   while(lab.Reloading)yield return new UnityEngine.WaitForEndOfFrame();
  }
 }
 UnityEngine.Object.Destroy(mesh);Q<UnityEngine.UIElements.Slider>("pitch").value=0;lab.SelectWeapon(0);
 System.IO.File.WriteAllText(folder+"/complete.txt",System.Text.Json.JsonSerializer.Serialize(new{revision,samples=45,scope="Actual production mesh; includes animated torso/head and cloth. 3 weapons x 11 pitch samples, plus fire/reload at +/-80."}));
}
lab.StartCoroutine(Record());return new{original.vertexCount,rendered=renderer.sharedMesh.vertexCount};
