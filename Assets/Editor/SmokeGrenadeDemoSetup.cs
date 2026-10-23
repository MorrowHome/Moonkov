using System.IO;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class SmokeGrenadeDemoSetup
{
    public const string ScenePath = "Assets/Scenes/SmokeGrenadeTest.unity";
    [MenuItem("Tools/Moonkov/Smoke/Open Effect Test Scene")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(ScenePath)) Build();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
    public static void Build()
    {
        if (File.Exists(ScenePath)) throw new System.InvalidOperationException("Test scene already exists; open it to preserve edits.");
        const string folder = "Assets/Data/SmokeGrenadeTest";
        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        Material Material(string name, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name; material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .2f);
            AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat"); return material;
        }
        var floor = Material("Floor", new Color(.18f,.22f,.26f));
        var wall = Material("Wall", new Color(.35f,.4f,.46f));
        var target = Material("Target", new Color(.95f,.31f,.055f));
        var lines = Material("RangeMarkings", new Color(.7f,.83f,.88f));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        GameObject Box(string name, Vector3 position, Vector3 scale, Material material, PrimitiveType type = PrimitiveType.Cube)
        {
            var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.position = position; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material; return obj;
        }
        TextMesh Text(string name, string text, Vector3 position, float size)
        {
            var obj = new GameObject(name); obj.transform.position = position;
            var label = obj.AddComponent<TextMesh>(); label.text = text; label.fontSize = 64; label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = new Color(.85f,.94f,1);
            return label;
        }
        Box("Floor 30m", new Vector3(0,-.15f,0), new Vector3(30,.3f,30), floor);
        Box("Backdrop", new Vector3(0,2,10), new Vector3(26,4,.3f), wall);
        Box("Foreground occluder", new Vector3(-3,1.25f,-3), new Vector3(1.4f,2.5f,.4f), wall);
        Box("Side wall", new Vector3(6,1.5f,2), new Vector3(.3f,3,6), wall);
        for (int i = -2; i <= 2; i++)
        {
            Box("Target " + (i+3), new Vector3(i*2,1,7), new Vector3(.65f,1,.65f), target, PrimitiveType.Capsule);
            Text("Target label " + i, "TARGET " + (i+3), new Vector3(i*2,2.4f,7), .06f);
        }
        for (int i = -10; i <= 10; i += 2)
        {
            Box("Grid X " + i, new Vector3(i,.006f,0), new Vector3(.025f,.01f,22), lines);
            Box("Grid Z " + i, new Vector3(0,.006f,i), new Vector3(22,.01f,.025f), lines);
        }
        var ring = new GameObject("5m smoke radius").AddComponent<LineRenderer>();
        ring.sharedMaterial = lines; ring.widthMultiplier = .045f; ring.loop = true; ring.positionCount = 96;
        for (int i=0;i<96;i++) { float a=i*Mathf.PI*2/96;ring.SetPosition(i,new Vector3(Mathf.Cos(a)*5,.025f,Mathf.Sin(a)*5)); }
        Text("Instructions", "SMOKE EFFECT TEST\n1 OVERVIEW   2 EYE LEVEL   3 INSIDE   4 SIDE\nR RESTART   G THROW   C CLEAR   L LOOP\nHOLD RMB + WASD / QE TO MOVE", new Vector3(0,4.2f,9.7f), .095f);
        var status = Text("Cycle status", "SMOKE LAB / PRESS PLAY", new Vector3(0,2.9f,-5.5f), .08f);
        var light = new GameObject("Test sunlight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
        light.transform.rotation = Quaternion.Euler(45,-30,0);light.shadows = LightShadows.Soft;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.45f,.5f,.58f);
        var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.gameObject.AddComponent<AudioListener>(); camera.nearClipPlane = .05f; camera.farClipPlane = 150;
        camera.fieldOfView = 55; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f,.075f,.1f);
        var data = camera.GetUniversalAdditionalCameraData(); data.requiresDepthTexture = true; data.renderPostProcessing = false;
        var demo = new GameObject("Smoke effect test controller").AddComponent<SmokeGrenadeDemo>();
        demo.Configure(camera,status); demo.SetView(0);
        EditorSceneManager.SaveScene(scene,ScenePath); AssetDatabase.SaveAssets();
        Selection.activeGameObject = demo.gameObject;
        if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(new Vector3(0,1.4f,0),camera.transform.rotation,18);
    }
}
