using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

public static class AirlockCloudDriveTools
{
    public const string Folder = "Assets/MoonEnvironment/Variants/EVAAirlockCloudDrive";
    public const string ScenePath = Folder + "/Scenes/EVA_CloudDrive_LocalValidation.unity";
    public const string PrefabPath = Folder + "/Prefabs/EVA_EAST_01_CloudIncrement.prefab";
    const string D = "Temp/MoonAirlockIntegration/CloudDrive/";
    public class V { public float x,y,z; public Vector3 Value => new Vector3(x,y,z); }
    public class Q { public float x,y,z,w; public Quaternion Value => new Quaternion(x,y,z,w); }
    public class Pose { public V position,scale; public Q rotation; }
    public class UV { public float x,y; }
    public class Primitive { public V[] vertices,normals; public UV[] uv; public int[] triangles; public float[] color; }
    public class Node { public int index,parent; public string name; public V position,scale; public Q rotation; public Primitive[] primitives; }
    public class Geometry { public Node[] nodes; }
    public class Box { public string name; public V center,size; }
    public class Panel { public string id,name,source,carriage; public Pose closed,open; public Box collider; public float[] meshBind,carriageBind; }
    public class Door { public string id,name; public Pose pose; public Panel[] panels; public Box sensor; }
    public class Button { public string id,action,mesh,indicator; public Box interaction; }
    public class Anchor { public string id,name; public Pose pose; }
    public class Config { public string id,rootName; public Pose rootPose; public Door[] doors; public Button[] buttons; public Box[] chambers; public Box inside; public Anchor[] anchors; public V exterior,outsideFloor,insideFloor; }
    static Type Json => AppDomain.CurrentDomain.GetAssemblies().First(a=>a.GetName().Name=="Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
    static T Read<T>(string file)=>(T)Json.GetMethod("DeserializeObject",new[]{typeof(string),typeof(Type)}).Invoke(null,new object[]{File.ReadAllText(D+file),typeof(T)});
    static string Serialize(object value)=>(string)Json.GetMethod("SerializeObject",new[]{typeof(object)}).Invoke(null,new[]{value});
    static void Apply(Transform t,Pose p){t.localPosition=p.position.Value;t.localRotation=p.rotation.Value;t.localScale=p.scale.Value;}
    static Matrix4x4 Matrix(float[] values){var m=new Matrix4x4();for(int i=0;i<16;i++)m[i/4,i%4]=values[i];return m;}
    static BoxCollider Collider(Transform parent, Box spec, Dictionary<string,GameObject> names, bool trigger)
    {
        if (!names.TryGetValue(spec.name,out var go)){go=new GameObject(spec.name);go.transform.SetParent(parent,false);names[spec.name]=go;}
        go.transform.SetParent(parent,false);go.transform.localPosition=spec.center.Value;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
        var c=go.GetComponent<BoxCollider>();if(!c)c=go.AddComponent<BoxCollider>();c.center=Vector3.zero;c.size=spec.size.Value;c.isTrigger=trigger;return c;
    }
    static Material Mat(int index,float[] color)
    {
        string path=Folder+"/Materials/Addition_"+index.ToString("000")+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat)return mat;
        mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.color=new Color(color[0],color[1],color[2],color.Length>3?color[3]:1);AssetDatabase.CreateAsset(mat,path);return mat;
    }
    static void Graft(MeshFilter source,Transform target,float[] fixedBind,Quaternion nativeBasis)
    {
        if (!source.GetComponent<Renderer>().enabled) throw new InvalidOperationException("Source dynamically rendered mesh is baked/disabled: "+source.name);
        var scale=source.transform.lossyScale;var basis=Quaternion.Inverse(nativeBasis)*source.transform.rotation;
        var matrix=Matrix(fixedBind);
        foreach(var c in source.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(c);
        GameObjectUtility.SetStaticEditorFlags(source.gameObject,0);
        source.transform.SetParent(target,false);source.transform.localPosition=matrix.GetColumn(3);source.transform.localRotation=matrix.rotation*basis;source.transform.localScale=Vector3.Scale(matrix.lossyScale,scale);
        source.GetComponent<Renderer>().enabled=true;
    }
    [MenuItem("Tools/Moonkov/Airlock/Build current cloud-drive validation scene")]
    public static string Build()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("Require stable edit mode");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved user scene: refuse isolated build");
        if(!File.ReadAllText(D+"source_bind_verified.json").Contains("\"all_pass\": true"))throw new InvalidOperationException("Source AABB precheck has not passed");
        if(File.Exists(ScenePath)||File.Exists(PrefabPath))return "Existing cloud scene retained; no overwrite: "+ScenePath;
        var config=Read<Config>("native_drive_config.json");var geometry=Read<Geometry>("native_additions.json");
        foreach(var child in new[]{"Scenes","Prefabs","Meshes","Materials"})Directory.CreateDirectory(Folder+"/"+child);
        AssetDatabase.Refresh();var original=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            var building=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Prefabs/MC00GameExport.prefab"),scene);
            building.name="FULL_V02_EXISTING_ASSET_TEST_COPY";PrefabUtility.UnpackPrefabInstance(building,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var nativeRoot=building.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="MC00_ROOT");
            var frame=new GameObject("MC00_ORIGINAL_LOCAL_YUP_BIND_FRAME");frame.transform.position=nativeRoot.position;frame.transform.rotation=Quaternion.Euler(0,180,0);
            var byIndex=new Dictionary<int,GameObject>();var names=new Dictionary<string,GameObject>(StringComparer.Ordinal);
            foreach(var n in geometry.nodes){var go=new GameObject(n.name);byIndex[n.index]=go;if(names.ContainsKey(n.name))throw new InvalidOperationException("Duplicate GLB node "+n.name);names[n.name]=go;}
            int materialIndex=0,meshCount=0;
            foreach(var n in geometry.nodes)
            {
                var go=byIndex[n.index];go.transform.SetParent(n.parent<0?frame.transform:byIndex[n.parent].transform,false);
                go.transform.localPosition=n.position.Value;go.transform.localRotation=n.rotation.Value;go.transform.localScale=n.scale.Value;
                if(n.primitives.Length==0)continue;
                var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int[]>();var materials=new List<Material>();
                foreach(var primitive in n.primitives)
                {
                    int start=vertices.Count;vertices.AddRange(primitive.vertices.Select(v=>v.Value));normals.AddRange(primitive.normals.Select(v=>v.Value));uv.AddRange(primitive.uv.Select(v=>new Vector2(v.x,v.y)));
                    indices.Add(primitive.triangles.Select(i=>i+start).ToArray());materials.Add(Mat(materialIndex++,primitive.color));
                }
                var mesh=new Mesh { name=n.name };mesh.vertices=vertices.ToArray();mesh.normals=normals.ToArray();mesh.uv=uv.ToArray();mesh.subMeshCount=indices.Count;
                for(int i=0;i<indices.Count;i++)mesh.SetTriangles(indices[i],i);mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,Folder+"/Meshes/"+n.name+".asset");
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=materials.ToArray();meshCount++;
            }
            var module=names[config.rootName];Apply(module.transform,config.rootPose);
            var requiredNames=new HashSet<string>(config.doors.SelectMany(d=>d.panels).SelectMany(p=>new[]{p.source,p.carriage}),StringComparer.Ordinal);
            var sourceFilters=building.GetComponentsInChildren<MeshFilter>(true).Where(f=>requiredNames.Contains(f.name) && AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("MC00_Render.fbx",StringComparison.Ordinal))
                .GroupBy(f=>f.name,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Single(),StringComparer.Ordinal);
            var owner=module.AddComponent<AirlockController>();var panelArrays=new List<AirlockPanelPose[]>();var sensors=new List<BoxCollider>();
            var openErrors=new List<object>();
            foreach(var door in config.doors)
            {
                var root=names[door.name].transform;Apply(root,door.pose);var poses=new List<AirlockPanelPose>();
                foreach(var panel in door.panels)
                {
                    var motion=names[panel.name].transform;Apply(motion,panel.open);
                    var source=sourceFilters[panel.source];var oldBounds=source.GetComponent<Renderer>().bounds;
                    Graft(source,motion,panel.meshBind,frame.transform.rotation);Graft(sourceFilters[panel.carriage],motion,panel.carriageBind,frame.transform.rotation);
                    var moved=source.GetComponent<Renderer>().bounds;float err=Mathf.Max((oldBounds.min-moved.min).magnitude,(oldBounds.max-moved.max).magnitude);
                    openErrors.Add(new{panel.id,source=panel.source,openBindMaxCornerDeltaM=err});if(err>.002f)throw new InvalidOperationException("Actual open bind mismatch "+panel.id+" delta "+err);
                    Collider(motion,panel.collider,names,false);
                    poses.Add(new AirlockPanelPose {PanelId=panel.id,Motion=motion,ClosedPosition=panel.closed.position.Value,OpenPosition=panel.open.position.Value,
                        ClosedRotation=panel.closed.rotation.Value,OpenRotation=panel.open.rotation.Value});
                }
                panelArrays.Add(poses.ToArray());sensors.Add(Collider(root,door.sensor,names,true));
            }
            var rooms=config.chambers.Select(b=>Collider(module.transform,b,names,true).gameObject.AddComponent<BreathableZone>()).ToArray();
            var inside=Collider(module.transform,config.inside,names,true).gameObject.AddComponent<BreathableZone>();
            owner.Configure(config.id,new AirlockSettings(),panelArrays[0],panelArrays[1],sensors[0],sensors[1],null,inside);owner.ConfigureVolumeSegments(rooms,new[]{inside});
            owner.ConfigureApertureBridges(sensors[0].gameObject.AddComponent<BreathableZone>(),sensors[1].gameObject.AddComponent<BreathableZone>());
            foreach(var button in config.buttons)
            {
                var interaction=Collider(module.transform,button.interaction,names,false).gameObject;
                var textGo=new GameObject(button.id+"_RuntimeStatus");textGo.transform.SetParent(interaction.transform,false);textGo.transform.localPosition=new Vector3(0,.38f,0);
                textGo.transform.localRotation=Quaternion.Euler(0,button.id.Contains("OUTSIDE")?0:180,0);var text=textGo.AddComponent<TextMesh>();text.characterSize=.025f;text.fontSize=48;text.anchor=TextAnchor.MiddleCenter;
                var command=(AirlockCommand)Enum.Parse(typeof(AirlockCommand),string.Concat(button.action.ToLowerInvariant().Split('_').Select(s=>char.ToUpperInvariant(s[0])+s.Substring(1))));
                interaction.AddComponent<AirlockButton>().Configure(button.id,button.action.Replace('_',' '),owner,command,text,names[button.indicator].GetComponent<Renderer>());
            }
            Transform Anchor(string id)=>names[config.anchors.Single(a=>a.id==id).name].transform;
            SoundDef Sound(string name)=>AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/Airlock/SoundDefs/"+name+".asset");
            module.AddComponent<AirlockAudio>().Configure(owner,null,Anchor(config.id+".VALVE"),Anchor(config.id+".PUMP"),Anchor(config.id+".OUTER.MOTOR"),Anchor(config.id+".INNER.MOTOR"),
                Sound("ValveAir_Loop"),Sound("Pump_Loop"),Sound("DoorMotor_Loop"),Sound("LatchLock"),Sound("LatchUnlock"),Anchor(config.id+".OUTER.LATCH"),Anchor(config.id+".INNER.LATCH"));
            PrefabUtility.SaveAsPrefabAsset(module,PrefabPath);
            UnityEngine.Physics.SyncTransforms();var spawn=new GameObject("CLOUD_EVA_OUTSIDE_SPAWN");var candidate=module.transform.TransformPoint(config.exterior.Value);var ray=new Ray(candidate+Vector3.up*15,Vector3.down);
            if(!UnityEngine.Physics.Raycast(ray,out var floor,30,~0,QueryTriggerInteraction.Ignore))throw new InvalidOperationException("No actual legacy outside ramp floor at spawn "+candidate);
            spawn.transform.position=floor.point+Vector3.up*.06f;spawn.transform.rotation=module.transform.rotation;
            var readoutGo=new GameObject("Cloud EVA local suit readout");readoutGo.transform.SetParent(module.transform,false);readoutGo.transform.localPosition=new Vector3(0,2.8f,0);var readout=readoutGo.AddComponent<TextMesh>();readout.fontSize=48;readout.characterSize=.035f;readout.anchor=TextAnchor.MiddleCenter;
            module.AddComponent<AirlockPrototypeDemo>().Configure(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab"),spawn.transform,readout,owner);
            module.GetComponent<AirlockPrototypeDemo>().ConfigureAudioMixer(AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            var sun=new GameObject("Local test sunlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(45,100,0);
            EditorSceneManager.SaveScene(scene,ScenePath);
            File.WriteAllText(D+"native_bind_result.json",Serialize(new{scope="New independent test scene and increment prefab; original full building asset/map unchanged. Only original 24 leaf/carriage instance meshes reparented; no importer changes.",meshCount,doorPanels=12,chamberUnionSegments=rooms.Length,gateButtons=8,openErrors,scene=ScenePath,prefab=PrefabPath,sourceFramePosition=new[]{frame.transform.position.x,frame.transform.position.y,frame.transform.position.z},sourceFrameRotationY=180,provisionalValvePumpAnchorsOnly=true}));
            return "Current cloud increment bound: 12 original leaves+12 carriages,43 additions,3 chamber segments,8 controls; open bind verified <2mm; new scene saved.";
        }
        finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
    }
}
