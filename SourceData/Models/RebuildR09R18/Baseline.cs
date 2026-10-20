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
var formal="Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(formal);Need(scene.IsValid()&&scene.isLoaded&&!scene.isDirty,"Formal scene not clean/loaded");
var roots=scene.GetRootGameObjects();Need(!roots.Any(g=>g.name=="REBUILD_R09_R18_DISPLAY_ROOT"),"Already integrated");
var transforms=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
var objects=roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c).Cast<UnityEngine.Object>().Concat(transforms.Select(t=>(UnityEngine.Object)t.gameObject)).ToArray();
var r08=roots.Single(g=>g.name=="REBUILD_R08_DISPLAY_ROOT");var mc=roots.Single(g=>g.name=="MC00_ROOT_Standard_YUp_Frame");
var targets=transforms.Where(t=>t.name=="B_R06_SOUTHWEST_RIM_EB01_01").ToArray();
string PathOf(Transform t)=>t.parent?PathOf(t.parent)+"/"+t.name:t.name;
var targetInfo=targets.Select(t=>new{path=PathOf(t),id=GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString(),position=new[]{t.position.x,t.position.y,t.position.z},renderers=t.GetComponentsInChildren<MeshRenderer>(true).Select(r=>new{path=PathOf(r.transform),enabled=r.enabled,mesh=r.GetComponent<MeshFilter>()?AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh):null,meshName=r.GetComponent<MeshFilter>()?r.GetComponent<MeshFilter>().sharedMesh.name:null,submeshes=r.GetComponent<MeshFilter>()?r.GetComponent<MeshFilter>().sharedMesh.subMeshCount:0,materials=r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray(),boundsMin=new[]{r.bounds.min.x,r.bounds.min.y,r.bounds.min.z},boundsMax=new[]{r.bounds.max.x,r.bounds.max.y,r.bounds.max.z}}).ToArray()}).ToArray();
var seamMatches=transforms.Where(t=>t.name.StartsWith("B_SRC_EB01_RoofStandingSeam")).Select(t=>new{path=PathOf(t),renderers=t.GetComponentsInChildren<MeshRenderer>(true).Length}).ToArray();
Report("FormalBefore.json",new{formal,formalSHA256=Sha(formal),protectedObjects=objects.Length,protectedHash=HashObjects(objects),roots=roots.Length,colliders=roots.Sum(g=>g.GetComponentsInChildren<Collider>(true).Length),lights=mc.GetComponentsInChildren<Light>(true).Length,buttons=mc.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c&&c.GetType().Name=="LightSwitchButton"),r08Renderers=r08.GetComponentsInChildren<MeshRenderer>(true).Length,r08Colliders=r08.GetComponentsInChildren<Collider>(true).Length,targetMatches=targets.Length,targetInfo,seamSourceNameMatches=seamMatches,dirty=scene.isDirty});
return new{protectedObjects=objects.Length,targets=targets.Length,seamSourceNames=seamMatches.Length,r08Colliders=r08.GetComponentsInChildren<Collider>(true).Length};
