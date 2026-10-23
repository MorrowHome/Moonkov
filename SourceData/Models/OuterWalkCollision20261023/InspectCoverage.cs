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
Need(Application.dataPath.Replace('\\','/')=="E:/UnityProjects/FPS_Template/Assets","Wrong project");
var setup=UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
var dirty=setup.Where(x=>UnityEngine.SceneManagement.SceneManager.GetSceneByPath(x.path).isDirty).Select(x=>x.path).ToArray();
if(dirty.Length>0){Report("CoverageBlocked.json",new{reason="Existing unsaved scenes; left unchanged",dirty});return new{blocked=true,dirty};}
string formal="Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(formal);
if(!scene.IsValid()||!scene.isLoaded)scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(formal,UnityEditor.SceneManagement.OpenSceneMode.Single);
Need(scene.isLoaded&&!scene.isDirty,"Formal scene not clean");
var roots=scene.GetRootGameObjects();var filters=roots.SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f&&f.sharedMesh).ToArray();
var colliders=roots.SelectMany(g=>g.GetComponentsInChildren<Collider>(true)).Where(c=>c&&c.enabled&&c.gameObject.activeInHierarchy&&!c.isTrigger).ToArray();
var plan=Parse(System.IO.Path.Combine(root,"CollisionPreparation.json"));var rows=Rows(F(plan,"rows"));var output=new List<object>();
Physics.SyncTransforms();
foreach(var row in rows){
 string name=V<string>(F(row,"name"));
 if(System.Text.RegularExpressions.Regex.IsMatch(name,"Shoe|Bearing|Raker|Cladding|Fascia|Cover|Canopy|Skirt|Bracket|Haunch",System.Text.RegularExpressions.RegexOptions.IgnoreCase))continue;
 string path=V<string>(F(row,"mesh"));Need(Sha(path)==V<string>(F(row,"meshSHA256")),"Mesh drift: "+path);
 var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);Need(mesh&&mesh.isReadable,"Unreadable source mesh");
 var matches=filters.Where(f=>f.sharedMesh==mesh&&f.gameObject.activeInHierarchy&&f.TryGetComponent<MeshRenderer>(out var rr)&&rr.enabled).ToArray();Need(matches.Length==1,"Mesh scene ownership not unique: "+path);
 var filter=matches[0];var matrix=filter.transform.localToWorldMatrix;var expected=V<float[][]>(F(row,"worldBoundsManifest"));var actual=GeometryUtility.CalculateBounds(mesh.vertices,matrix);
 float boundsError=Mathf.Max(Vector3.Distance(actual.min,new Vector3(expected[0][0],expected[0][1],expected[0][2])),Vector3.Distance(actual.max,new Vector3(expected[1][0],expected[1][1],expected[1][2])));Need(boundsError<.004f,"World placement drift: "+name);
 var verts=mesh.vertices;var ids=mesh.triangles;var samples=new List<(Vector3 p,float area)>();
 for(int i=0;i<ids.Length;i+=3){var a=matrix.MultiplyPoint3x4(verts[ids[i]]);var b=matrix.MultiplyPoint3x4(verts[ids[i+1]]);var c=matrix.MultiplyPoint3x4(verts[ids[i+2]]);var cross=Vector3.Cross(b-a,c-a);float area=cross.magnitude*.5f;if(area>.025f&&cross.normalized.y>.6f)samples.Add(((a+b+c)/3,area));}
 var chosen=new List<Vector3>();foreach(var item in samples.OrderByDescending(s=>s.area)){if(chosen.Any(p=>Vector3.Distance(p,item.p)<.35f))continue;chosen.Add(item.p);if(chosen.Count>=8)break;}
 if(chosen.Count==0)continue;
 var probes=new List<object>();int covered=0;foreach(var point in chosen){var near=colliders.Where(c=>{var b=c.bounds;b.Expand(.16f);return b.Contains(point);}).ToArray();var hits=Physics.RaycastAll(point+Vector3.up*.15f,Vector3.down,.32f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore).Where(h=>h.collider&&h.collider.gameObject.scene==scene&&Mathf.Abs(h.point.y-point.y)<=.08f&&h.normal.y>.5f).OrderBy(h=>Mathf.Abs(h.point.y-point.y)).ToArray();if(hits.Length>0)covered++;probes.Add(new{point=new[]{point.x,point.y,point.z},covered=hits.Length>0,hit=hits.Length>0?hits[0].collider.name:null,hitId=hits.Length>0?GlobalObjectId.GetGlobalObjectIdSlow(hits[0].collider).ToString():null,broadphaseCandidates=near.Select(c=>new{name=c.name,type=c.GetType().Name,id=GlobalObjectId.GetGlobalObjectIdSlow(c).ToString()}).ToArray()});}
 output.Add(new{name,key=V<string>(F(row,"key")),mesh=path,meshSHA256=Sha(path),sourcePrefab=V<string>(F(row,"sourcePrefab")),sourceVersion=V<string>(F(row,"sourceVersion")),meshFilterId=GlobalObjectId.GetGlobalObjectIdSlow(filter).ToString(),worldMatrix=Enumerable.Range(0,16).Select(i=>matrix[i]).ToArray(),localBoundsCenter=new[]{mesh.bounds.center.x,mesh.bounds.center.y,mesh.bounds.center.z},localBoundsSize=new[]{mesh.bounds.size.x,mesh.bounds.size.y,mesh.bounds.size.z},triangles=ids.Length/3,sourceLayer=filter.gameObject.layer,samples=chosen.Count,covered,missing=chosen.Count-covered,boundsError,probes});
}
var mc=roots.Single(g=>g.name=="MC00_ROOT_Standard_YUp_Frame");int lights=mc.GetComponentsInChildren<Light>(true).Length,buttons=mc.GetComponentsInChildren<MonoBehaviour>(true).Count(c=>c&&c.GetType().Name=="LightSwitchButton");Need(lights==118&&buttons==135,"Stable IDs/count baseline drift");
var clocks=roots.SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>(true)).Where(c=>c&&c.GetType().Name=="ExpeditionClockPresentation").ToArray();Need(clocks.Length>0,"Clock presentation missing");
var imports=new List<object>();foreach(var path in AssetDatabase.GetAllAssetPaths().Where(p=>p.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase)&&p.Contains("DisplayIncrements"))){var importer=AssetImporter.GetAtPath(path) as ModelImporter;if(importer)imports.Add(new{path,generateColliders=importer.addCollider});}
Report("LiveCoverage.json",new{formal,sceneSHA256=Sha(formal),sceneDirty=scene.isDirty,priorSetup=setup.Select(x=>x.path).ToArray(),colliderCount=colliders.Length,lights,buttons,clocks=clocks.Length,playerLayer3vsDefault=!Physics.GetIgnoreLayerCollision(3,0),playerLayer6vsDefault=!Physics.GetIgnoreLayerCollision(6,0),imports,surfaces=output,query="Static Editor Physics.RaycastAll at up to8 actual upward triangle centroids; no simulation or Play"});
return new{surfaces=output.Count,sceneDirty=scene.isDirty,lights,buttons};
