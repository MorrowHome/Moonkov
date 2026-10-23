string root=@"E:\Code\Codex\2026-10-19\task\OuterCollision_20261023";
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
string formal="Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(formal);Need(scene.isLoaded&&!scene.isDirty,"Formal scene must be clean");
var coverage=Parse(System.IO.Path.Combine(root,"LiveCoverage.json"));Need(Sha(formal)==V<string>(F(coverage,"sceneSHA256")),"Scene changed since coverage query");
Need(V<bool>(F(coverage,"playerLayer3vsDefault"))&&V<bool>(F(coverage,"playerLayer6vsDefault")),"Player layer collision disabled");
var selected=Rows(F(coverage,"surfaces")).Where(x=>V<int>(F(x,"covered"))==0&&V<int>(F(x,"missing"))>0).ToArray();Need(selected.Length==108,"Coverage selection changed");
var offline=Parse(System.IO.Path.Combine(root,"CollisionPreparation.json"));var boxKeys=new HashSet<string>(V<string[]>(F(offline,"draftKeys")));var offlineRows=Rows(F(offline,"rows"));
string folder="Assets/MoonEnvironment/Variants/TheophilusBase/CollisionIncrements/OuterWalkSurfaces20261023";string prefabPath=folder+"/OuterWalkSurfaces_StaticCollision.prefab";
Need(!System.IO.File.Exists(prefabPath)&&!AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),"Collision prefab already exists; inspect outcome first");
var settingsPaths=new[]{"Assets/Settings/UniversalRenderPipelineGlobalSettings.asset","ProjectSettings/ProjectSettings.asset"};var settingsHashes=settingsPaths.Select(Sha).ToArray();
var roots=scene.GetRootGameObjects();Need(!roots.Any(g=>g.name=="OUTER_WALK_SURFACES_COLLISION_20261023"),"Collision root already present");
var oldComponents=roots.SelectMany(g=>g.GetComponentsInChildren<Component>(true)).Where(c=>c&&(c is Collider||c is Renderer||c is MeshFilter||c is Light||c.GetType().Name=="LightSwitchButton"||c.GetType().Name=="ExpeditionClockPresentation")).Cast<UnityEngine.Object>().ToArray();string oldHash=HashObjects(oldComponents);
var mc=roots.Single(g=>g.name=="MC00_ROOT_Standard_YUp_Frame");Need(mc.GetComponentsInChildren<Light>(true).Length==118&&mc.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c&&c.GetType().Name=="LightSwitchButton")==135,"MC00 mapping baseline drift");
var sourceFilters=roots.SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f&&f.sharedMesh).ToArray();
var staging=new GameObject("OUTER_WALK_SURFACES_COLLISION_20261023");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(staging,scene);
var applied=new List<object>();int boxes=0,meshColliders=0,totalMeshTris=0;bool saved=false;
try{
 foreach(var row in selected){
  string key=V<string>(F(row,"key")),path=V<string>(F(row,"mesh")),name=V<string>(F(row,"name"));Need(Sha(path)==V<string>(F(row,"meshSHA256")),"Source mesh drift");
  var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);Need(mesh&&mesh.isReadable&&mesh.triangles.Length/3==V<int>(F(row,"triangles")),"Source topology drift");
  var matches=sourceFilters.Where(f=>f.sharedMesh==mesh&&GlobalObjectId.GetGlobalObjectIdSlow(f).ToString()==V<string>(F(row,"meshFilterId"))).ToArray();Need(matches.Length==1,"Source ownership drift: "+name);var source=matches[0];
  var expected=V<float[]>(F(row,"worldMatrix"));var matrix=source.transform.localToWorldMatrix;for(int i=0;i<16;i++)Need(Mathf.Abs(matrix[i]-expected[i])<.0001f,"World matrix drift");
  var go=new GameObject(key+"_"+name);go.layer=0;go.transform.SetParent(staging.transform,false);go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);go.transform.localScale=source.transform.lossyScale;
  var reconstructed=go.transform.localToWorldMatrix;for(int i=0;i<16;i++)Need(Mathf.Abs(reconstructed[i]-matrix[i])<.0003f,"Sheared transform needs another proxy strategy");
  Collider col;if(boxKeys.Contains(key)){
   var proof=offlineRows.Single(x=>V<string>(F(x,"key"))==key);var shape=F(proof,"simpleBox");var center=V<float[]>(F(shape,"center"));var size=V<float[]>(F(shape,"size"));
   var box=go.AddComponent<BoxCollider>();box.center=new Vector3(center[0],center[1],center[2]);box.size=new Vector3(size[0],size[1],size[2]);col=box;boxes++;
  }else{
   Need(mesh.triangles.Length/3<=3000,"Per-object static mesh budget exceeded");var proxy=go.AddComponent<MeshCollider>();proxy.sharedMesh=mesh;proxy.convex=false;proxy.cookingOptions=MeshColliderCookingOptions.CookForFasterSimulation|MeshColliderCookingOptions.EnableMeshCleaning|MeshColliderCookingOptions.WeldColocatedVertices|MeshColliderCookingOptions.UseFastMidphase;col=proxy;meshColliders++;totalMeshTris+=mesh.triangles.Length/3;
  }
  col.enabled=true;col.isTrigger=false;go.isStatic=true;
  applied.Add(new{name,key,mesh=path,meshSHA256=Sha(path),type=col.GetType().Name,triangles=mesh.triangles.Length/3});
 }
 Need(totalMeshTris<20000,"Total collision triangle budget exceeded");Need(HashObjects(oldComponents)==oldHash,"Protected existing components changed before save");
 Physics.SyncTransforms();var misses=new List<object>();int checkedSamples=0;foreach(var row in selected)foreach(var probe in Rows(F(row,"probes"))){var a=V<float[]>(F(probe,"point"));var point=new Vector3(a[0],a[1],a[2]);var hits=Physics.RaycastAll(point+Vector3.up*.15f,Vector3.down,.32f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Where(h=>h.collider&&h.collider.transform.IsChildOf(staging.transform)&&Mathf.Abs(h.point.y-point.y)<=.08f&&h.normal.y>.5f).ToArray();checkedSamples++;if(hits.Length==0)misses.Add(new{name=V<string>(F(row,"name")),point=a});}
 Report("BeforeSaveVerification.json",new{selected=selected.Length,boxes,meshColliders,totalMeshTris,checkedSamples,misses,existingComponentHashBefore=oldHash,existingComponentHashAfter=HashObjects(oldComponents)});Need(misses.Count==0,"Generated proxies missed candidate samples; no save");
 foreach(var dir in new[]{"Assets/MoonEnvironment/Variants/TheophilusBase/CollisionIncrements",folder}){if(!AssetDatabase.IsValidFolder(dir)){string parent=System.IO.Path.GetDirectoryName(dir).Replace('\\','/');AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(dir));}}
 var prefab=PrefabUtility.SaveAsPrefabAssetAndConnect(staging,prefabPath,InteractionMode.AutomatedAction);Need(prefab,"Prefab save failed");
 Need(HashObjects(oldComponents)==oldHash,"Protected components changed");for(int i=0;i<settingsPaths.Length;i++)Need(Sha(settingsPaths[i])==settingsHashes[i],"User settings changed");
 UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);Need(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene),"Scoped formal scene save failed");saved=true;
 Report("Application.json",new{applied=true,formal,sceneBeforeSHA256=V<string>(F(coverage,"sceneSHA256")),sceneAfterSHA256=Sha(formal),prefab=prefabPath,sourceSurfaceCount=selected.Length,boxes,meshColliders,totalMeshTris,checkedSamples,misses=0,existingProtectedComponents=oldComponents.Length,existingComponentHashBefore=oldHash,existingComponentHashAfter=HashObjects(oldComponents),rootId=GlobalObjectId.GetGlobalObjectIdSlow(staging).ToString(),sceneDirty=scene.isDirty,settingsHashes,objects=applied,skippedAlreadyCovered=4,partialCoverageSurfacesDeferred=4,PlayStarted=false,HostStarted=false,oldCollisionCommitMerged=false});
 return new{applied=true,surfaces=selected.Length,boxes,meshColliders,totalMeshTris,checkedSamples,sceneDirty=scene.isDirty};
}catch{if(!saved&&staging)UnityEngine.Object.DestroyImmediate(staging);throw;}
