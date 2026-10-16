using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;
using Unity.MP_FPS.Moon;

public static class AirlockApollo12Tools
{
    public const string SourceScene = "Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/Apollo12GameScene.unity";
    public const string Folder = "Assets/MoonEnvironment/Variants/Apollo12EVA";
    public const string ScenePath = Folder + "/Scenes/Apollo12_EVA_Walkthrough.unity";

    [MenuItem("Tools/Moonkov/Airlock/Create Apollo12 EVA walkthrough copy")]
    public static string Build()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stable edit mode required");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved user scene; refuse map copy build");
        if (File.Exists(ScenePath)) return "Existing EVA walkthrough retained: " + ScenePath;
        Directory.CreateDirectory(Folder + "/Scenes"); AssetDatabase.Refresh();
        if (!File.Exists(ScenePath) && !AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new InvalidOperationException("Could not copy Apollo12 scene");
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var native = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "MC00_ROOT");
            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Prefabs/MC00GameExport.prefab");
            var sourceNative = sourcePrefab.GetComponentsInChildren<Transform>(true).Single(t => t.name == "MC00_ROOT");
            var mapFromSource = native.localToWorldMatrix * sourceNative.worldToLocalMatrix;
            int lightsBefore = roots.Sum(r => r.GetComponentsInChildren<Light>(true).Length);
            int lightButtonsBefore = roots.Sum(r => r.GetComponentsInChildren<LightSwitchButton>(true).Length);
            var increment = AssetDatabase.LoadAssetAtPath<GameObject>(AirlockCloudDriveTools.PrefabPath);
            var sourceNames = increment.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("MC00_Render.fbx", StringComparison.Ordinal))
                .Select(f => f.name).ToArray();
            if (sourceNames.Length != 24 || sourceNames.Distinct().Count() != 24) throw new InvalidOperationException("Expected exactly 12 legacy leaves and 12 carriages");
            var sourceTransforms = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            foreach (var name in sourceNames)
                sourceTransforms.Single(f => f.name == name && AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("MC00_Render.fbx", StringComparison.Ordinal)).gameObject.SetActive(false);

            var frame = new GameObject("EVA_SINGLE_MODULE_ORIGINAL_LOCAL_YUP_FRAME");
            SceneManager.MoveGameObjectToScene(frame, scene);
            var frameMatrix = mapFromSource * Matrix4x4.TRS(sourceNative.position, Quaternion.Euler(0, 180, 0), Vector3.one);
            frame.transform.SetPositionAndRotation(frameMatrix.GetColumn(3), frameMatrix.rotation);
            frame.transform.localScale = frameMatrix.lossyScale;
            var module = (GameObject)PrefabUtility.InstantiatePrefab(increment, frame.transform);
            var owner = module.GetComponent<AirlockController>();
            if (!owner || !owner.Settings.IsValid) throw new InvalidOperationException("Invalid EVA prefab configuration");
            var serializedOwner = new SerializedObject(owner);
            if (serializedOwner.FindProperty("m_OuterPanels").arraySize != 6 || serializedOwner.FindProperty("m_InnerPanels").arraySize != 6 ||
                !serializedOwner.FindProperty("m_OuterBridge").objectReferenceValue || !serializedOwner.FindProperty("m_InnerBridge").objectReferenceValue)
                throw new InvalidOperationException("EVA prefab door/bridge references incomplete");

            // Retain the copied roots for rollback, but prevent network resource/spawn entry points
            // from competing with the local walkthrough character. The source map stays untouched.
            var disabledRoots = new[] { "GhostSpawnerManager", "LeaderboardSpawner", "GameResourcesSubScene", "Apollo12 Spawn Points SubScene" };
            foreach (var r in roots) if (disabledRoots.Contains(r.name)) r.SetActive(false);
            UnityEngine.Physics.SyncTransforms();
            var spawn = new GameObject("EVA_ActualPlayer_OutsideSpawn"); SceneManager.MoveGameObjectToScene(spawn, scene);
            var candidate = module.transform.TransformPoint(new Vector3(0, 0, -6.24f));
            if (!UnityEngine.Physics.Raycast(candidate + Vector3.up * 15, Vector3.down, out var floor, 30, ~0, QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("No native map floor at EVA spawn");
            spawn.transform.SetPositionAndRotation(floor.point + Vector3.up * .06f, module.transform.rotation);
            var status = new GameObject("EVA_LocalSuit_Readout"); status.transform.SetParent(module.transform, false);
            status.transform.localPosition = new Vector3(0, 2.8f, 0);
            var text = status.AddComponent<TextMesh>(); text.characterSize = .035f; text.fontSize = 48; text.anchor = TextAnchor.MiddleCenter;
            var demo = module.AddComponent<AirlockPrototypeDemo>();
            demo.Configure(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"), spawn.transform, text, owner);
            var structuralFrame = native;
            while (structuralFrame.parent && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(structuralFrame.gameObject) !=
                "Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Prefabs/MC00GameExport.prefab") structuralFrame = structuralFrame.parent;
            owner.GetComponent<AirlockAudio>().ConfigureStructure(structuralFrame);
            demo.ConfigureAudioMixer(AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
            var demoFields = new SerializedObject(demo);
            demoFields.FindProperty("m_InteractionPanelSettings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/UI Toolkit/PanelSettings.asset");
            demoFields.ApplyModifiedPropertiesWithoutUndo();
            foreach (var button in module.GetComponentsInChildren<AirlockButton>(true))
            {
                var label = button.GetComponentInChildren<TextMesh>();
                label.transform.localRotation = Quaternion.Euler(0, button.ButtonId.Contains(".OUTSIDE.") ? 0 : button.ButtonId.Contains(".INSIDE.") ? 180 : 90, 0);
                label.characterSize = .004f;
            }

            var afterRoots = scene.GetRootGameObjects();
            int lightsAfter = afterRoots.Sum(r => r.GetComponentsInChildren<Light>(true).Length);
            int lightButtonsAfter = afterRoots.Sum(r => r.GetComponentsInChildren<LightSwitchButton>(true).Length);
            if (lightsAfter != lightsBefore || lightButtonsAfter != lightButtonsBefore) throw new InvalidOperationException("Original light controls changed");
            if (afterRoots.Sum(r => r.GetComponentsInChildren<AirlockController>(true).Length) != 1 ||
                afterRoots.Sum(r => r.GetComponentsInChildren<AirlockButton>(true).Length) != 8) throw new InvalidOperationException("Single EVA / eight controls invariant failed");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Temp/MoonAirlockIntegration/DOT00_map_build.json", JsonUtility.ToJson(new BuildRecord {
                scene = ScenePath, source = SourceScene, moduleWorldPosition = module.transform.position,
                spawn = spawn.transform.position, lights = lightsAfter, lightButtons = lightButtonsAfter,
                replacedLegacyObjects = sourceNames, disabledNetworkRoots = disabledRoots,
                hardwareStatus = "Hardware increment binds separately through AirlockHardwareTools; door baseline and audio anchors retained"
            }, true));
            return "Apollo12 independent EVA walkthrough saved; original lights/buttons retained (" + lightsAfter + "/" + lightButtonsAfter + "); 24 repeated legacy objects inactive; one EVA with eight buttons: " + ScenePath;
        }
        finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
    }
    [Serializable] class BuildRecord
    {
        public string scene, source, hardwareStatus;
        public Vector3 moduleWorldPosition, spawn;
        public int lights, lightButtons;
        public string[] replacedLegacyObjects, disabledNetworkRoots;
    }
    [MenuItem("Tools/Moonkov/Airlock/Open Apollo12 EVA walkthrough")]
    public static void Open()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first");
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
    }
}
