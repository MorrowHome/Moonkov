var root=@"E:\Code\Codex\2026-10-19\task\R09_R18";
void Need(bool value,string message){if(!value)throw new Exception(message);}
string Sha(string p){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(System.IO.File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
var jsonType=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
object F(object o,string key)=>o.GetType().GetProperty("Item",new[]{typeof(string)}).GetValue(o,new object[]{key});
T V<T>(object o)=>(T)o.GetType().GetMethod("ToObject",new[]{typeof(Type)}).Invoke(o,new object[]{typeof(T)});
object[] Rows(object o)=>((System.Collections.IEnumerable)o).Cast<object>().ToArray();
object Parse(string path)=>jsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{System.IO.File.ReadAllText(path),typeof(object)});
void Report(string name,object value)=>System.IO.File.WriteAllText(System.IO.Path.Combine(root,name),(string)jsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value}));
string HashObjects(UnityEngine.Object[] objects){using(var h=System.Security.Cryptography.SHA256.Create()){foreach(var o in objects){var bytes=System.Text.Encoding.UTF8.GetBytes(o.GetEntityId()+":"+EditorJsonUtility.ToJson(o)+"\n");h.TransformBlock(bytes,0,bytes.Length,bytes,0);}h.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(h.Hash).Replace("-","");}}
Need(Application.dataPath.Replace('\\','/')=="E:/UnityProjects/FPS_Template/Assets","Wrong project");
Need(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!Lightmapping.isRunning&&!BuildPipeline.isBuildingPlayer,"Editor occupied");
var plan=Parse(System.IO.Path.Combine(root,"ImportPlan.json"));var before=Parse(System.IO.Path.Combine(root,"FormalBefore.json"));
Need(System.IO.File.Exists(System.IO.Path.Combine(root,"UnityIndependentImport.json")),"Independent import missing");
string formal=V<string>(F(plan,"formal")),output=V<string>(F(plan,"output")),prefabPath=output+"/RebuildR09R18_Display.prefab";
Need(Sha(formal)==V<string>(F(plan,"formalBeforeSHA256")),"Formal scene changed since plan");
Need(Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).All(i=>!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty),"Unsaved user scene");
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(formal);Need(scene.IsValid()&&scene.isLoaded,"Formal scene unavailable");
var originals=scene.GetRootGameObjects();Need(!originals.Any(g=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g)==prefabPath||g.name=="REBUILD_R09_R18_DISPLAY_ROOT"),"Already bound; inspect before retry");
var transforms=originals.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
var names=new HashSet<string>(Rows(F(plan,"objects")).Select(r=>V<string>(F(r,"name"))));Need(!transforms.Any(t=>names.Contains(t.name)),"Existing increment source identity; inspect duplicates");
var objects=originals.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c).Cast<UnityEngine.Object>().Concat(transforms.Select(t=>(UnityEngine.Object)t.gameObject)).ToArray();
var oldHash=HashObjects(objects);Need(oldHash==V<string>(F(before,"protectedHash")),"Original scene changed in memory since baseline");
var maps=LightmapSettings.lightmaps.Select(l=>new[]{l.lightmapColor,l.lightmapDir,l.shadowMask}).ToArray();var probes=LightmapSettings.lightProbes;
var mc=originals.Single(g=>g.name=="MC00_ROOT_Standard_YUp_Frame");Need(mc.GetComponentsInChildren<Light>(true).Length==118&&mc.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c&&c.GetType().Name=="LightSwitchButton")==135,"Light/button contract differs");
var terrain=originals.SelectMany(g=>g.GetComponentsInChildren<Terrain>(true)).Select(t=>new{name=t.name,data=AssetDatabase.GetAssetPath(t.terrainData),material=AssetDatabase.GetAssetPath(t.materialTemplate)}).ToArray();
var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);Need(prefab,"Prefab missing");
GameObject added=null;
try{
 added=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);added.name="REBUILD_R09_R18_DISPLAY_ROOT";added.SetActive(true);
 var rs=added.GetComponentsInChildren<MeshRenderer>(true);Need(rs.Length==1629&&rs.All(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMaterials.All(m=>m&&m.shader&&m.shader.isSupported)),"Missing renderer/material");
 Need(added.GetComponentsInChildren<Collider>(true).Length==0&&added.GetComponentsInChildren<Light>(true).Length==0&&added.GetComponentsInChildren<MonoBehaviour>(true).Length==0,"Display-only scope exceeded");
 Need(HashObjects(objects)==oldHash,"Original objects changed");
 Need(LightmapSettings.lightProbes==probes&&LightmapSettings.lightmaps.Length==maps.Length&&LightmapSettings.lightmaps.Select((l,i)=>new[]{l.lightmapColor,l.lightmapDir,l.shadowMask}.SequenceEqual(maps[i])).All(x=>x),"Lightmaps/probes changed");
 foreach(var spec in Rows(F(plan,"materials")))Need(Sha(V<string>(F(spec,"path")))==V<string>(F(spec,"beforeSHA256")),"Existing material changed");
 Need(Sha(formal)==V<string>(F(plan,"formalBeforeSHA256")),"Concurrent formal file change");
 Need(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene),"Scene save failed");
 Need(!scene.isDirty&&HashObjects(objects)==oldHash,"Original objects changed on save");
 int triangles=added.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3);Need(triangles==121395,"Triangle total differs");
 Report("UnityFormalApplication.json",new{scene=formal,saved=true,prefab=prefabPath,root=added.name,formalBeforeSHA256=V<string>(F(plan,"formalBeforeSHA256")),formalAfterUnitySaveSHA256=Sha(formal),displayObjects=1629,partitions=33,triangles,protectedObjects=objects.Length,protectedHash=oldHash,protectedObjectsUnchanged=true,oldCollidersUnchanged=originals.Sum(g=>g.GetComponentsInChildren<Collider>(true).Length),oldR08CollidersUnchanged=originals.Single(g=>g.name=="REBUILD_R08_DISPLAY_ROOT").GetComponentsInChildren<Collider>(true).Length,lights=118,buttons=135,terrain,lightmapsProbesUnchanged=true,oldRenderersDisabled=0,newColliders=0,newLights=0,newRuntimeScripts=0,newMaterials=0,newTextures=0,R16OldRodCorrectionApplied=false,R19Applied=false,R20Applied=false,PlayStarted=false,HostStarted=false,runtimeOrPerformanceAcceptance=false});
 return new{saved=true,displayObjects=rs.Length,partitions=33,triangles,originalObjectsUnchanged=true};
}catch(Exception ex){Report("BindFailure.json",new{error=ex.ToString(),addedRootExists=added!=null,sceneDirty=scene.isDirty,sceneSHA256=Sha(formal)});throw;}
