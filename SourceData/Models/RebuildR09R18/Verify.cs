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
Need(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!BuildPipeline.isBuildingPlayer,"Editor occupied");
var plan=Parse(System.IO.Path.Combine(root,"ImportPlan.json"));var before=Parse(System.IO.Path.Combine(root,"FormalBefore.json"));var app=Parse(System.IO.Path.Combine(root,"UnityFormalApplication.json"));
string formal=V<string>(F(plan,"formal")),output=V<string>(F(plan,"output"));var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(formal);Need(scene.IsValid()&&scene.isLoaded&&!scene.isDirty,"Formal scene unavailable/dirty");
var display=scene.GetRootGameObjects().Single(g=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g)==output+"/RebuildR09R18_Display.prefab");
var originals=scene.GetRootGameObjects().Where(g=>g!=display).ToArray();
var objects=originals.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c).Cast<UnityEngine.Object>().Concat(originals.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Select(t=>(UnityEngine.Object)t.gameObject)).ToArray();
Need(HashObjects(objects)==V<string>(F(before,"protectedHash")),"Original objects changed");
var filters=display.GetComponentsInChildren<MeshFilter>(true);var renderers=display.GetComponentsInChildren<MeshRenderer>(true);Need(filters.Length==1629&&renderers.Length==1629,"Scope differs");
var bindings=Rows(F(plan,"materials")).ToDictionary(x=>V<string>(F(x,"source")),x=>V<string>(F(x,"path")));
foreach(var entry in Rows(F(plan,"objects"))){var key=V<string>(F(entry,"key"));var f=filters.Single(x=>AssetDatabase.GetAssetPath(x.sharedMesh)==output+"/Meshes/"+key+".asset");var r=f.GetComponent<MeshRenderer>();Need(r&&r.enabled&&r.gameObject.activeInHierarchy,"Invisible renderer");var expected=Rows(F(entry,"materials")).Select(x=>bindings[V<string>(x)]).ToArray();Need(r.sharedMaterials.Select(AssetDatabase.GetAssetPath).SequenceEqual(expected),"Wrong material mapping");Need(f.sharedMesh.uv.Length==f.sharedMesh.vertexCount&&f.sharedMesh.uv2.Length==0,"UV channels changed");Need(f.sharedMesh.tangents.All(t=>!float.IsNaN(t.x)&&!float.IsInfinity(t.x)&&!float.IsNaN(t.y)&&!float.IsInfinity(t.y)&&!float.IsNaN(t.z)&&!float.IsInfinity(t.z)),"Nonfinite tangents");}
Need(renderers.All(r=>r.sharedMaterials.All(m=>m&&m.shader&&m.shader.isSupported)),"Material shader unsupported");
Need(filters.Sum(f=>f.sharedMesh.triangles.Length/3)==121395,"Triangle total differs");
Need(display.GetComponentsInChildren<Collider>(true).Length==0&&display.GetComponentsInChildren<Light>(true).Length==0&&display.GetComponentsInChildren<MonoBehaviour>(true).Length==0,"Non-display component");
foreach(var spec in Rows(F(plan,"materials")))Need(Sha(V<string>(F(spec,"path")))==V<string>(F(spec,"beforeSHA256")),"Material file changed");
Report("UnityStaticVerification.json",new{savedSceneClean=true,existingObjects=objects.Length,existingObjectHashUnchanged=true,meshes=1629,renderers=1629,partitions=display.transform.childCount,triangles=121395,allMaterialBindingsCorrect=true,allShaderReferencesSupported=true,UV0Preserved=true,UV1Reserved=true,finiteGeneratedTangents=true,newColliders=0,newLights=0,newScripts=0,oldColliderCount=originals.Sum(g=>g.GetComponentsInChildren<Collider>(true).Length),R16OldSeamOverrideApplied=false,R19Applied=false,R20Applied=false,PlayStarted=false,HostStarted=false,runtimeOrPerformanceAccepted=false});
return new{verified=true,meshes=filters.Length,partitions=display.transform.childCount};
