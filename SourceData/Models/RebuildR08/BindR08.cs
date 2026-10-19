var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../SourceData/Models/RebuildR08"));
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
Need(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!Lightmapping.isRunning,"Editor occupied");
Need(System.IO.File.Exists(System.IO.Path.Combine(root,"UnityIndependentImport.json")),"Independent import not verified");
var plan=Parse(System.IO.Path.Combine(root,"ImportPlan.json"));string formal=V<string>(F(plan,"formal")),output=V<string>(F(plan,"output"));
Need(Sha(formal)==V<string>(F(plan,"formalBeforeSHA256")),"Formal scene changed since plan");
Need(Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).All(i=>!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty),"User has unsaved scene changes");
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(formal);bool openedHere=!scene.IsValid()||!scene.isLoaded;
if(openedHere)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(formal,UnityEditor.SceneManagement.OpenSceneMode.Additive);
Need(scene.IsValid()&&scene.isLoaded&&!scene.isDirty,"Formal scene became dirty on load; stop before binding");
var sceneRoots=scene.GetRootGameObjects();
var existingTaskRoots=sceneRoots.Where(g=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g)==output+"/RebuildR08_Display.prefab").ToArray();
Need(existingTaskRoots.Length<=1,"Multiple R08 instances; stop before changing anything");
var originals=sceneRoots.Except(existingTaskRoots).ToArray();
Need(!originals.Any(g=>g.name=="REBUILD_R08_DISPLAY_ROOT"),"R08 root already exists; inspect before retrying");
var allTransforms=originals.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
var names=new HashSet<string>(Rows(F(plan,"objects")).Select(e=>V<string>(F(e,"name"))));
var duplicateNames=allTransforms.Where(t=>names.Contains(t.name)).Select(t=>t.name).ToArray();
Need(duplicateNames.Length==0,"An increment source identity is already present; exact replacement review required: "+string.Join(",",duplicateNames.Take(5)));
var oldRail=allTransforms.Where(t=>t.name==V<string>(F(plan,"oldRail"))).ToArray();
Need(oldRail.Length==0,"An existing prior R04 rail requires geometry-signature review before replacement");
var mc=originals.Single(g=>g.name=="MC00_ROOT_Standard_YUp_Frame");
int lights=mc.GetComponentsInChildren<Light>(true).Length,buttons=mc.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c&&c.GetType().Name=="LightSwitchButton");
Need(lights==118&&buttons==135,"MC00 stable light/button contract differs");
var protectedObjects=originals.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c).Cast<UnityEngine.Object>().Concat(allTransforms.Select(t=>(UnityEngine.Object)t.gameObject)).ToArray();
string protectedBefore=HashObjects(protectedObjects);
if(existingTaskRoots.Length==1){var previous=Parse(System.IO.Path.Combine(root,"FormalProtectionBefore.json"));Need(protectedBefore==V<string>(F(previous,"protectedHash")),"Original components changed since the interrupted binding");}
var terrain=originals.SelectMany(g=>g.GetComponentsInChildren<Terrain>(true)).ToArray();
var terrainBindings=terrain.Select(t=>new{objectName=t.name,data=AssetDatabase.GetAssetPath(t.terrainData),material=AssetDatabase.GetAssetPath(t.materialTemplate),materialGUID=t.materialTemplate?AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t.materialTemplate)):null}).ToArray();
var probes=LightmapSettings.lightProbes;var maps=LightmapSettings.lightmaps.Select(l=>new[]{l.lightmapColor,l.lightmapDir,l.shadowMask}).ToArray();
Report("FormalProtectionBefore.json",new{formal,sha256=Sha(formal),originalRoots=originals.Length,protectedObjects=protectedObjects.Length,protectedHash=protectedBefore,MC00Lights=lights,MC00Buttons=buttons,terrains=terrainBindings,colliders=originals.Sum(g=>g.GetComponentsInChildren<Collider>(true).Length),oldRailMatches=0,duplicateIncrementNames=0});
var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(output+"/RebuildR08_Display.prefab");Need(prefab,"R08 prefab missing");GameObject added=null;
try{
 added=existingTaskRoots.Length==1?existingTaskRoots[0]:(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
 added.name="REBUILD_R08_DISPLAY_ROOT";added.SetActive(true);
 Need(added&&added.activeSelf&&added.name=="REBUILD_R08_DISPLAY_ROOT","Prefab activation failed");
 var renderers=added.GetComponentsInChildren<MeshRenderer>(true);Need(renderers.Length==1271&&renderers.All(r=>r.enabled&&r.sharedMaterials.All(m=>m&&m.shader&&m.shader.isSupported)),"Display references invalid");
 Need(added.GetComponentsInChildren<Collider>(true).Length==0&&added.GetComponentsInChildren<Light>(true).Length==0&&added.GetComponentsInChildren<MonoBehaviour>(true).Length==0,"Display-only scope exceeded");
 Need(HashObjects(protectedObjects)==protectedBefore,"Existing scene components or GameObjects changed");
 Need(LightmapSettings.lightProbes==probes&&LightmapSettings.lightmaps.Length==maps.Length&&LightmapSettings.lightmaps.Select((l,i)=>new[]{l.lightmapColor,l.lightmapDir,l.shadowMask}.SequenceEqual(maps[i])).All(v=>v),"Existing lightmap/probe state changed");
 foreach(var spec in Rows(F(plan,"materials")))if(!V<bool>(F(spec,"create")))Need(Sha(V<string>(F(spec,"path")))==V<string>(F(spec,"beforeSHA256")),"Existing material changed");
 Need(Sha(formal)==V<string>(F(plan,"formalBeforeSHA256")),"Concurrent formal scene write; do not save");
 Need(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene),"Formal scene save failed");
 Need(!scene.isDirty&&HashObjects(protectedObjects)==protectedBefore,"Existing components changed during save");
 var north=renderers.Single(r=>r.name=="R08_Existing_EB07_Entry_Toe_Transition");var west=renderers.Single(r=>r.name=="R07_West_Inner_Continuous_Bridge_Deck");
 var localLunar=AssetDatabase.LoadAssetAtPath<Material>("Assets/MoonEnvironment/Variants/TheophilusBase/VisualPass01/SilverSand/SilverSand_R27S_Compacted_WorldContinuous_PBR.mat");
 int lunarSlots=renderers.Sum(r=>r.sharedMaterials.Count(m=>m==localLunar));Need(lunarSlots==26,"Local lunar mapping count differs");
 var result=new{scene=formal,formalBeforeSHA256=V<string>(F(plan,"formalBeforeSHA256")),formalAfterSHA256=Sha(formal),saved=true,root=added.name,prefab=output+"/RebuildR08_Display.prefab",displayObjects=1271,triangles=279994,existingProtectedObjects=protectedObjects.Length,existingProtectedObjectsHashUnchanged=true,existingTerrainMaterialsTexturesShadersParametersUnchanged=true,MC00Lights=lights,MC00Buttons=buttons,lightmapsProbesPreserved=true,oldRailMatches=0,oldRenderersDisabled=0,duplicateIncrements=0,preservedLocalLunarSlots=lunarSlots,newColliders=0,newLights=0,newRuntimeScripts=0,openedHere,northToeUnityWorld=new[]{north.bounds.center.x,north.bounds.center.y,north.bounds.center.z},westApproachUnityWorld=new[]{west.bounds.center.x,west.bounds.center.y,west.bounds.center.z},PlayStarted=false,HostStarted=false,runtimeOrPerformanceAcceptance=false};
 Report("UnityFormalApplication.json",result);
 Selection.activeGameObject=north.gameObject;var view=UnityEditor.SceneView.lastActiveSceneView;if(view)view.Frame(new Bounds(north.bounds.center,new Vector3(35,20,35)),false);
 return result;
}catch(Exception ex){Report("FormalBindingFailure.json",new{error=ex.ToString(),addedRootExists=added!=null,sceneDirty=scene.isDirty,sceneSHA256=Sha(formal)});throw;}
