var root=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../SourceData/Models/RebuildR08"));
void Need(bool ok,string message) { if(!ok) throw new Exception(message); }
string Sha(string path) { using(var h=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(System.IO.File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant(); }
string Scenes() { var a=new List<string>(); for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++){var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);a.Add(s.path+":"+s.isDirty+":"+s.isLoaded);}return string.Join("\n",a); }
var jsonType=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
object F(object obj,string key) { return obj.GetType().GetProperty("Item",new[]{typeof(string)}).GetValue(obj,new object[]{key}); }
T V<T>(object obj) { return (T)obj.GetType().GetMethod("ToObject",new[]{typeof(Type)}).Invoke(obj,new object[]{typeof(T)}); }
object[] Rows(object obj) { return ((System.Collections.IEnumerable)obj).Cast<object>().ToArray(); }
void Report(string name,object value) { var text=(string)jsonType.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});System.IO.File.WriteAllText(System.IO.Path.Combine(root,name),text); }
void Ensure(string path) { if(AssetDatabase.IsValidFolder(path))return; Need(!System.IO.Directory.Exists(path),"Existing unregistered folder: "+path);int cut=path.LastIndexOf('/');Ensure(path.Substring(0,cut));AssetDatabase.CreateFolder(path.Substring(0,cut),path.Substring(cut+1)); }

try {

Need(Application.dataPath.Replace((char)92,(char)47)=="E:/UnityProjects/FPS_Template/Assets","Wrong project");
Need(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!Lightmapping.isRunning,"Editor occupied");
var plan=jsonType.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{System.IO.File.ReadAllText(System.IO.Path.Combine(root,"ImportPlan.json")),typeof(object)});
string output=V<string>(F(plan,"output")),formal=V<string>(F(plan,"formal"));
Need(Sha(formal)==V<string>(F(plan,"formalBeforeSHA256")),"Formal scene changed");
Need(!System.IO.File.Exists(output+"/RebuildR08_Display.prefab"),"Prefab already exists; inspect");
Need(Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).All(i=>!UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty),"Unsaved user scene");
string beforeScenes=Scenes();var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var sourceMaterials=new Dictionary<string,Material>();foreach(var spec in Rows(F(plan,"materials"))){var mat=AssetDatabase.LoadAssetAtPath<Material>(V<string>(F(spec,"path")));Need(mat&&mat.shader&&mat.shader.isSupported,"Missing material");sourceMaterials.Add(V<string>(F(spec,"source")),mat);}
var meshes=new Dictionary<string,Mesh>();int totalTriangles=0;foreach(var e in Rows(F(plan,"objects"))){string key=V<string>(F(e,"key"));var m=AssetDatabase.LoadAssetAtPath<Mesh>(output+"/Meshes/"+key+".asset");Need(m&&m.vertexCount==V<int>(F(e,"vertices"))&&m.subMeshCount==Rows(F(e,"materials")).Length,"Reloaded mesh invalid: "+key);Need(m.uv.Length==m.vertexCount&&m.uv2.Length==0&&m.tangents.Length==m.vertexCount,"Reloaded UV/tangent contract");meshes.Add(key,m);totalTriangles+=V<int>(F(e,"triangles"));}
Need(meshes.Count==1271&&totalTriangles==279994,"Native scope differs");
var scratch=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
GameObject display=null;double maxDrift=0;
try {
 
 display=new GameObject("REBUILD_R08_DISPLAY_ROOT");if(display.scene!=scratch)UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(display,scratch);Need(display.scene==scratch,"Wrong temporary scene");var groups=new Dictionary<string,Transform>();
 foreach(var entry in Rows(F(plan,"objects"))){string partition=V<string>(F(entry,"partition"));if(!groups.ContainsKey(partition)){var group=new GameObject(partition);group.transform.SetParent(display.transform,false);groups.Add(partition,group.transform);}var go=new GameObject(V<string>(F(entry,"name")));go.transform.SetParent(groups[partition],false);var p=V<float[]>(F(entry,"position"));var q=V<float[]>(F(entry,"rotation"));var s=V<float[]>(F(entry,"scale"));go.transform.localPosition=new Vector3(p[0],p[1],p[2]);go.transform.localRotation=new Quaternion(q[0],q[1],q[2],q[3]);go.transform.localScale=new Vector3(s[0],s[1],s[2]);var mesh=meshes[V<string>(F(entry,"key"))];go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=Rows(F(entry,"materials")).Select(v=>sourceMaterials[V<string>(v)]).ToArray();renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;renderer.receiveShadows=true;
 var b=V<float[][]>(F(entry,"worldBounds"));var min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);var max=new Vector3(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);foreach(var v in mesh.vertices){var w=go.transform.TransformPoint(v);min=Vector3.Min(min,w);max=Vector3.Max(max,w);}for(int i=0;i<3;i++){maxDrift=Math.Max(maxDrift,Math.Abs(min[i]-b[0][i]));maxDrift=Math.Max(maxDrift,Math.Abs(max[i]-b[1][i]));}Need(maxDrift<0.002,"Unity world placement differs: "+go.name);
 }
 Need(display.GetComponentsInChildren<MeshRenderer>(true).Length==1271&&groups.Count==31,"Prefab display scope differs");
 Need(display.GetComponentsInChildren<Collider>(true).Length==0&&display.GetComponentsInChildren<Light>(true).Length==0&&display.GetComponentsInChildren<MonoBehaviour>(true).Length==0,"Non-display component in prefab");
 var prefab=PrefabUtility.SaveAsPrefabAsset(display,output+"/RebuildR08_Display.prefab",out bool ok);Need(ok&&prefab,"Prefab save failed");
} finally { if(display)UnityEngine.Object.DestroyImmediate(display);UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scratch,true); }
foreach(var spec in Rows(F(plan,"materials")))if(!V<bool>(F(spec,"create")))Need(Sha(V<string>(F(spec,"path")))==V<string>(F(spec,"beforeSHA256")),"Existing material was modified");
Need(Sha(formal)==V<string>(F(plan,"formalBeforeSHA256"))&&Scenes()==beforeScenes,"Independent import changed existing scene state");
var result=new{meshAssets=1271,partitions=31,triangles=totalTriangles,newMaterials=9,reusedMaterials=8,preservedLocalLunarSlots=26,cloudLunarMaterialsCreated=0,newTextures=0,newColliders=0,newLights=0,newRuntimeScripts=0,unityWorldBoundsMaxDriftMetres=maxDrift,existingScenesUnchanged=true,existingMaterialsUnchanged=true,sceneBound=false,prefab=output+"/RebuildR08_Display.prefab"};
Report("UnityIndependentImport.json",result);return result;

} catch(Exception ex) { Report("StageResumeFailure.json",new{error=ex.ToString()});return new{failed=true,error=ex.ToString()}; }
