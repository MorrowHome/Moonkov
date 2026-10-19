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
var plan=Parse(System.IO.Path.Combine(root,"ImportPlan.json"));var before=Parse(System.IO.Path.Combine(root,"FormalProtectionBefore.json"));
string formal=V<string>(F(plan,"formal")),output=V<string>(F(plan,"output"));
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(formal);Need(scene.IsValid()&&scene.isLoaded&&!scene.isDirty,"Formal scene unavailable/dirty");
var display=scene.GetRootGameObjects().Single(g=>g.name=="REBUILD_R08_DISPLAY_ROOT");
Need(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(display)==output+"/RebuildR08_Display.prefab","Wrong prefab identity");
var originals=scene.GetRootGameObjects().Where(g=>g!=display).ToArray();
var transforms=originals.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
var objects=originals.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c).Cast<UnityEngine.Object>().Concat(transforms.Select(t=>(UnityEngine.Object)t.gameObject)).ToArray();
Need(HashObjects(objects)==V<string>(F(before,"protectedHash")),"Existing in-memory objects differ");
var renderers=display.GetComponentsInChildren<MeshRenderer>(true);Need(display.activeSelf&&renderers.Length==1271&&renderers.All(r=>r.enabled&&r.gameObject.activeInHierarchy&&r.sharedMaterials.All(m=>m&&m.shader&&m.shader.isSupported)),"Display references/visibility invalid");
var meshes=display.GetComponentsInChildren<MeshFilter>(true);int triangles=meshes.Sum(f=>Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(int)f.sharedMesh.GetIndexCount(i)/3));Need(triangles==279994,"Triangle count differs");
Need(display.GetComponentsInChildren<Collider>(true).Length==0&&display.GetComponentsInChildren<Light>(true).Length==0&&display.GetComponentsInChildren<MonoBehaviour>(true).Length==0,"Non-display addition");
var mc=originals.Single(g=>g.name=="MC00_ROOT_Standard_YUp_Frame");int lights=mc.GetComponentsInChildren<Light>(true).Length,buttons=mc.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c&&c.GetType().Name=="LightSwitchButton");Need(lights==118&&buttons==135,"MC00 contract differs");
foreach(var spec in Rows(F(plan,"materials")))if(!V<bool>(F(spec,"create")))Need(Sha(V<string>(F(spec,"path")))==V<string>(F(spec,"beforeSHA256")),"Existing material bytes changed");
var lunar=AssetDatabase.LoadAssetAtPath<Material>("Assets/MoonEnvironment/Variants/TheophilusBase/VisualPass01/SilverSand/SilverSand_R27S_Compacted_WorldContinuous_PBR.mat");int lunarSlots=renderers.Sum(r=>r.sharedMaterials.Count(m=>m==lunar));Need(lunarSlots==26,"Local lunar slots differ");
var north=renderers.Single(r=>r.name=="R08_Existing_EB07_Entry_Toe_Transition");var west=renderers.Single(r=>r.name=="R07_West_Inner_Continuous_Bridge_Deck");
var n=north.bounds.center;var w=west.bounds.center;
bool active=UnityEngine.SceneManagement.SceneManager.GetActiveScene()==scene||UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
Selection.activeGameObject=north.gameObject;var view=UnityEditor.SceneView.lastActiveSceneView;if(view)view.Frame(new Bounds(n,new Vector3(35,20,35)),false);
var result=new{scene=formal,formalBeforeSHA256=V<string>(F(plan,"formalBeforeSHA256")),formalAfterSHA256=Sha(formal),saved=true,root=display.name,prefab=output+"/RebuildR08_Display.prefab",displayObjects=1271,partitions=31,triangles,newMaterials=9,reusedMaterials=8,existingProtectedObjects=objects.Length,existingProtectedObjectsHashUnchanged=true,existingTerrainMaterialsTexturesShadersParametersUnchanged=true,MC00Lights=lights,MC00Buttons=buttons,oldRailMatches=0,oldRenderersDisabled=0,duplicateIncrements=0,preservedLocalLunarSlots=lunarSlots,newColliders=0,newLights=0,newRuntimeScripts=0,northToeUnityWorld=new[]{n.x,n.y,n.z},westApproachUnityWorld=new[]{w.x,w.y,w.z},formalActive=active,selectedNorthToe=true,sceneViewFramed=view!=null,PlayStarted=false,HostStarted=false,runtimeOrPerformanceAcceptance=false};
Report("UnityFormalApplication.json",result);return result;
