using System.IO;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class WeaponGestureLabSetup
{
    public const string ScenePath = "Assets/DollSinger/Scenes/WeaponGestureLab.unity";
    private const string Folder = "Assets/Scripts/Debug/WeaponGestureLab/";

    [MenuItem("Tools/Moonkov/Open Weapon Gesture Lab")]
    public static void Open()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Stop Play mode before opening the gesture lab.");
        if (!File.Exists(ScenePath)) Create();
        EditorSceneManager.OpenScene(ScenePath);
    }

    public static void Create()
    {
        if (File.Exists(ScenePath)) throw new System.InvalidOperationException("Gesture lab already exists; open it instead of overwriting it.");
        AssetDatabase.Refresh();
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.65f, .68f, .72f);
        var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "GestureLab Floor" };
        floorMaterial.SetColor("_BaseColor", new Color(.24f, .29f, .32f));
        AssetDatabase.CreateAsset(floorMaterial, "Assets/DollSinger/Demo/GestureLabFloor.mat");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "GestureLab Ground";
        floor.transform.position = new Vector3(0, -.04f, 0);
        floor.transform.localScale = new Vector3(8, .08f, 8);
        floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backdrop.name = "GestureLab Backdrop";
        backdrop.transform.position = new Vector3(0, 1.5f, 4);
        backdrop.transform.localScale = new Vector3(8, 3, .08f);
        backdrop.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        Light("Key", Quaternion.Euler(35, -35, 0), 1.4f, new Color(1, .94f, .86f));
        Light("Fill", Quaternion.Euler(20, 125, 0), .7f, new Color(.80f, .9f, 1));
        Camera[] observers = new Camera[3];
        string[] names = { "Front", "Right Side", "Back" };
        for (int i = 0; i < 3; i++)
        {
            var camera = new GameObject("GestureLab " + names[i]).AddComponent<Camera>();
            camera.fieldOfView = 45; camera.nearClipPlane = .02f; camera.farClipPlane = 20;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .16f, .19f);
            camera.cullingMask = ~LayerMask.GetMask("FirstPersonOverlay");
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            Vector3 target = Vector3.up * 1.15f;
            camera.transform.position = target + Quaternion.Euler(0, i * 90, 0) * Vector3.forward * 1.25f + Vector3.up * .1f;
            camera.transform.LookAt(target);
            observers[i] = camera;
        }
        var output = new GameObject("GestureLab Screen Background").AddComponent<Camera>();
        output.cullingMask = 0; output.clearFlags = CameraClearFlags.SolidColor;
        output.backgroundColor = new Color(.06f, .09f, .12f); output.depth = -20;
        output.gameObject.AddComponent<AudioListener>();
        var panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset"));
        panel.name = "GestureLabPanel"; panel.referenceResolution = new Vector2Int(1600, 1000); panel.match = .5f;
        AssetDatabase.CreateAsset(panel, "Assets/DollSinger/Demo/GestureLabPanel.asset");
        var root = new GameObject("Weapon Gesture Lab");
        var document = root.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Folder + "WeaponGestureLab.uxml");
        document.sortingOrder = 100;
        var lab = root.AddComponent<WeaponGestureLab>();
        lab.Configure(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"),
            new[] { Weapon("Halo"), Weapon("Revolver"), Weapon("Shotgun"), Weapon("Sniper") }, document, observers);
        EditorUtility.SetDirty(lab);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        SceneManager.SetActiveScene(original);
        EditorSceneManager.CloseScene(scene, true);
    }

    private static WeaponData Weapon(string name) => AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/DollSinger/" + name + "Weapon.asset");
    private static void Light(string name, Quaternion rotation, float intensity, Color color)
    {
        var light = new GameObject("GestureLab " + name).AddComponent<Light>();
        light.type = LightType.Directional; light.transform.rotation = rotation; light.intensity = intensity;
        light.color = color; light.shadows = LightShadows.None;
    }
}
