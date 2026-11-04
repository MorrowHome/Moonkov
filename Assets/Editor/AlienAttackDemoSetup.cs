using System;
using System.Collections.Generic;
using System.IO;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class AlienAttackDemoSetup
{
    public const string ScenePath = "Assets/Scenes/AlienAttackDemo.unity";
    private const string Data = "Assets/Data/AlienAttackDemo";

    [MenuItem("Tools/Moonkov/Aliens/Open attack demonstration")]
    public static void Open()
    {
        RequireIdle();
        if (!File.Exists(ScenePath)) Create();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    public static void Create()
    {
        RequireIdle();
        if (File.Exists(ScenePath)) throw new InvalidOperationException("Demo already exists; open it to preserve scene edits.");
        if (!AssetDatabase.IsValidFolder(Data)) AssetDatabase.CreateFolder("Assets/Data", "AlienAttackDemo");
        Material Material(string name, Color color, bool unlit = false)
        {
            var material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            if (!unlit) { material.SetFloat("_Smoothness", .22f); material.SetFloat("_Metallic", 0); }
            AssetDatabase.CreateAsset(material, Data + "/" + name + ".mat"); return material;
        }
        var floorMaterial = Material("DemoGround", new Color(.12f, .145f, .17f));
        var gridMaterial = Material("DemoGrid", new Color(.23f, .28f, .31f), true);
        var targetMaterial = Material("DemoTarget", new Color(.94f, .75f, .40f));
        var ceilingMaterial = Material("DemoCeiling", new Color(.22f, .26f, .29f));
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            GameObject Box(string name, Vector3 position, Vector3 scale, Material material)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name;
                obj.transform.position = position; obj.transform.localScale = scale;
                obj.GetComponent<Renderer>().sharedMaterial = material; return obj;
            }
            Box("Ground", new Vector3(0, -.2f, 8), new Vector3(64, .4f, 64), floorMaterial);
            var ceiling = Box("Ceiling spider perch", new Vector3(0, 3.9f, -.5f), new Vector3(4.8f, .2f, 3.5f), ceilingMaterial);
            ceiling.SetActive(false);
            var vertices = new List<Vector3>(); var indices = new List<int>();
            for (int i = -30; i <= 30; i += 2)
            {
                vertices.Add(new Vector3(i, .012f, -24)); vertices.Add(new Vector3(i, .012f, 40));
                vertices.Add(new Vector3(-30, .012f, i + 8)); vertices.Add(new Vector3(30, .012f, i + 8));
            }
            for (int i = 0; i < vertices.Count; i++) indices.Add(i);
            var mesh = new Mesh { name = "Demo metre grid" }; mesh.SetVertices(vertices); mesh.SetIndices(indices, MeshTopology.Lines, 0); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Data + "/DemoGrid.asset");
            var grid = new GameObject("2 metre floor grid"); grid.AddComponent<MeshFilter>().sharedMesh = mesh;
            grid.AddComponent<MeshRenderer>().sharedMaterial = gridMaterial;
            RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.5f, .53f, .58f);
            var light = new GameObject("Key light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.5f; light.color = new Color(1, .96f, .9f); light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(42, -35, 0);
            var camera = new GameObject("Attack demonstration camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>(); camera.fieldOfView = 48; camera.nearClipPlane = .08f; camera.farClipPlane = 100;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .105f);
            camera.transform.position = new Vector3(10, 7, 16); camera.transform.LookAt(new Vector3(0, 1.1f, 2));
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            var background = new GameObject("Demo screen background").AddComponent<Camera>();
            background.cullingMask = 0; background.depth = -20; background.clearFlags = CameraClearFlags.SolidColor;
            background.backgroundColor = new Color(.067f, .067f, .075f);
            var panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset"));
            panel.name = "AlienAttackDemoPanel"; panel.referenceResolution = new Vector2Int(1440, 900); panel.match = .5f;
            AssetDatabase.CreateAsset(panel, Data + "/DemoPanel.asset");
            var root = new GameObject("Monster attack demonstration");
            var doc = root.AddComponent<UIDocument>(); doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Scripts/Debug/AlienAttackDemo/AlienAttackDemo.uxml");
            var names = new[] { "SpiderBoss", "Spider", "Snake", "Caterpillar", "Centipede", "Leech", "Slime", "FaceHugger", "Tentacle", "Roots", "StiltWalker", "StiltWatcher" };
            var profiles = new AlienCreatureProfile[names.Length];
            for (int i = 0; i < names.Length; i++) profiles[i] = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + names[i] + ".asset");
            root.AddComponent<AlienAttackDemo>().Configure(profiles, camera, doc,
                AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"), targetMaterial, ceiling);
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        }
        finally { SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true); }
        Debug.Log("ALIEN_ATTACK_DEMO_CREATED: 12 profiles, native attacks, no raid or transport.");
    }

    private static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before opening the demonstration.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
    }
}
