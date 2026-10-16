using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

public static class AirlockPrototypeTools
{
    public const string Folder = "Assets/MoonEnvironment/Variants/EVAAirlockPrototype";
    public const string ScenePath = Folder + "/Scenes/EVA_Airlock_LocalValidation.unity";
    public const string PrefabPath = Folder + "/Prefabs/EVA_EAST_01_Placeholder.prefab";
    private static Material s_Wall, s_Door, s_Button;
    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool trigger = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
        go.GetComponent<BoxCollider>().isTrigger = trigger; if (trigger) go.GetComponent<Renderer>().enabled = false; return go;
    }
    private static Material MakeMaterial(string name, Color color)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name, color = color }; AssetDatabase.CreateAsset(m, path); return m;
    }
    private static BreathableZone Volume(string name, Transform parent, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        var box = go.AddComponent<BoxCollider>(); box.size = size; box.isTrigger = true; return go.AddComponent<BreathableZone>();
    }
    private static TextMesh Text(string name, Transform parent, Vector3 position, string content)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(0, 180, 0); var t = go.AddComponent<TextMesh>();
        t.text = content; t.characterSize = .035f; t.fontSize = 48; t.anchor = TextAnchor.MiddleCenter; t.color = Color.white; return t;
    }
    private static AirlockPanelPose[] Door(Transform parent, string side, float z)
    {
        var root = new GameObject("EVA_EAST_01_" + side + "DoorRoot"); root.transform.SetParent(parent, false); root.transform.localPosition = new Vector3(0, 1.6f, z);
        var panels = new AirlockPanelPose[6];
        for (int i = 0; i < 6; i++)
        {
            var motion = new GameObject("PanelMotion_P" + i.ToString("00")); motion.transform.SetParent(root.transform, false);
            float x = -1.3f + (i + .5f) * (2.6f / 6);
            var closed = new Vector3(x, 0, 0); motion.transform.localPosition = closed;
            Box("PLACEHOLDER_PANEL", motion.transform, Vector3.zero, new Vector3(2.6f / 6, 3.2f, .10f), s_Door);
            panels[i] = new AirlockPanelPose { PanelId = "EVA_EAST_01." + side.ToUpperInvariant() + ".P" + i.ToString("00"), Motion = motion.transform,
                ClosedPosition = closed, OpenPosition = closed + Vector3.right * (i < 3 ? -2 : 2), ClosedRotation = Quaternion.identity, OpenRotation = Quaternion.identity };
        }
        return panels;
    }
    private static BoxCollider Sensor(Transform parent, string name, float z)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(0, 1.6f, z);
        var c = go.AddComponent<BoxCollider>(); c.size = new Vector3(2.65f, 3.2f, .7f); c.isTrigger = true; return c;
    }
    private static void Button(Transform parent, AirlockController owner, string id, string label, AirlockCommand command, Vector3 position, float yaw)
    {
        var go = Box(id, parent, position, new Vector3(.26f,.26f,.12f), s_Button); go.transform.localRotation = Quaternion.Euler(0,yaw,0);
        var text = Text("Status", go.transform, new Vector3(0,1.3f,-.7f), label); text.transform.localScale = new Vector3(3,3,3);
        go.AddComponent<AirlockButton>().Configure("EVA_EAST_01." + id, label, owner, command, text, go.GetComponent<Renderer>());
    }
    [MenuItem("Tools/Moonkov/Airlock/Create replaceable EVA validation module")]
    public static string Build()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Require stable edit mode");
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scene before building preview");
        if (File.Exists(ScenePath) || File.Exists(PrefabPath)) return "Existing prototype retained; no overwrite: " + ScenePath;
        foreach (var child in new[]{"Scenes","Prefabs","Materials"}) Directory.CreateDirectory(Folder + "/" + child);
        AssetDatabase.Refresh(); var original = SceneManager.GetActiveScene(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            s_Wall = MakeMaterial("PlaceholderWall",new Color(.23f,.30f,.34f)); s_Door = MakeMaterial("PlaceholderDoor",new Color(.67f,.53f,.2f)); s_Button = MakeMaterial("PlaceholderControl",new Color(.24f,.48f,.55f));
            var root = new GameObject("EVA_EAST_01_REPLACEABLE_PLACEHOLDER"); var owner = root.AddComponent<AirlockController>();
            Box("Metal floor",root.transform,new Vector3(0,-.1f,4),new Vector3(4,.2f,14),s_Wall);
            Box("Left chamber wall",root.transform,new Vector3(-2,1.65f,2.1f),new Vector3(.15f,3.3f,4.2f),s_Wall);
            Box("Right chamber wall",root.transform,new Vector3(2,1.65f,2.1f),new Vector3(.15f,3.3f,4.2f),s_Wall);
            foreach(float z in new[]{0f,4.2f})
            {
                Box("Fixed door jamb L",root.transform,new Vector3(-1.7f,1.6f,z),new Vector3(.8f,3.2f,.25f),s_Wall);
                Box("Fixed door jamb R",root.transform,new Vector3(1.7f,1.6f,z),new Vector3(.8f,3.2f,.25f),s_Wall);
                Box("Fixed door header",root.transform,new Vector3(0,3.3f,z),new Vector3(4,.2f,.25f),s_Wall);
            }
            var chamber = Volume("Chamber volume segment 0",root.transform,new Vector3(0,1.6f,2.1f),new Vector3(3.8f,3.2f,4.4f));
            var inside = Volume("Pressurized inside volume",root.transform,new Vector3(0,1.6f,7.55f),new Vector3(3.8f,3.2f,6.7f));
            owner.Configure("EVA_EAST_01",new AirlockSettings(),Door(root.transform,"Outer",0),Door(root.transform,"Inner",4.2f),
                Sensor(root.transform,"Fixed outer aperture sensor",0),Sensor(root.transform,"Fixed inner aperture sensor",4.2f),chamber,inside);
            Button(root.transform,owner,"OUTSIDE.OPEN_OUTER","OPEN OUTER",AirlockCommand.OpenOuter,new Vector3(-1.65f,1.1f,-.45f),0);
            Button(root.transform,owner,"CHAMBER.CLOSE_OUTER","CLOSE OUTER",AirlockCommand.CloseOuter,new Vector3(-1.65f,1.1f,.65f),180);
            Button(root.transform,owner,"CHAMBER.EQUALIZE_INSIDE","PRESSURIZE",AirlockCommand.EqualizeInside,new Vector3(-1.65f,1.1f,1.5f),90);
            Button(root.transform,owner,"CHAMBER.OPEN_INNER","OPEN INNER",AirlockCommand.OpenInner,new Vector3(-1.65f,1.1f,3.55f),0);
            Button(root.transform,owner,"INSIDE.OPEN_INNER","OPEN INNER",AirlockCommand.OpenInner,new Vector3(-1.65f,1.1f,4.65f),180);
            Button(root.transform,owner,"CHAMBER.CLOSE_INNER","CLOSE INNER",AirlockCommand.CloseInner,new Vector3(1.65f,1.1f,3.55f),0);
            Button(root.transform,owner,"CHAMBER.EQUALIZE_OUTSIDE","DEPRESSURIZE",AirlockCommand.EqualizeOutside,new Vector3(1.65f,1.1f,2.6f),270);
            Button(root.transform,owner,"CHAMBER.OPEN_OUTER","OPEN OUTER",AirlockCommand.OpenOuter,new Vector3(1.65f,1.1f,.65f),180);
            var audio = root.AddComponent<AirlockAudio>();
            var anchors = new Transform[4]; for(int i=0;i<4;i++){var a=new GameObject(new[]{"Valve anchor","Pump anchor","Outer motor anchor","Inner motor anchor"}[i]);a.transform.SetParent(root.transform,false);a.transform.localPosition=new Vector3(1.8f,2,new[]{2f,3f,0f,4.2f}[i]);anchors[i]=a.transform;}
            audio.Configure(owner,null,anchors[0],anchors[1],anchors[2],anchors[3]);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            var spawn = new GameObject("Outside player spawn");spawn.transform.position=new Vector3(0,.06f,-2.5f);
            var readout = Text("Local suit status",root.transform,new Vector3(0,2.65f,3.85f),"REPLACEABLE EVA / VALIDATION");
            var demo = root.AddComponent<AirlockPrototypeDemo>();demo.Configure(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab"),spawn.transform,readout,owner);
            Box("Outside regolith test ground",null,new Vector3(0,-.15f,-5),new Vector3(15,.2f,8),s_Wall);
            var sun=new GameObject("Preview sunlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(40,130,0);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;RenderSettings.skybox=null;
            EditorSceneManager.SaveScene(scene,ScenePath);
            return "New independent placeholder prefab and validation scene created; formal map unchanged: " + ScenePath;
        }
        finally { EditorSceneManager.CloseScene(scene,true); SceneManager.SetActiveScene(original); }
    }
}
