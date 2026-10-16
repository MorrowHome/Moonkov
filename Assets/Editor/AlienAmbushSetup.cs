using System;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>A small, physical surface graph in the current formal map. The baked
/// samples describe available surfaces, not scripted enemy routes.</summary>
public static class AlienAmbushSetup
{
    public const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
    public const string NavigationName = "MC00 Spider Ambush Navigation";

    [MenuItem("Tools/Moonkov/Aliens/Set up MC00 spider ambush")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before setup.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Transform frame = null; AlienEncounterSettings settings = null; AlienSurfaceNavigation navigation = null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "MC00_ROOT_Standard_YUp_Frame") frame = t;
                if (t.TryGetComponent<AlienEncounterSettings>(out var encounters)) settings = encounters;
                if (t.name == NavigationName) navigation = t.GetComponent<AlienSurfaceNavigation>();
            }
        if (!frame || !settings) throw new InvalidOperationException("Formal MC00 frame or encounters missing.");
        var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Spider.asset");
        string guid = AssetDatabase.AssetPathToGUID("Assets/AlienCreatures/Prefabs/XenoSpider.prefab");
        if (!profile || string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Registered spider assets missing.");
        if (!navigation)
        {
            var go = new GameObject(NavigationName);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create spider navigation");
            navigation = go.AddComponent<AlienSurfaceNavigation>();
        }
        var localBounds = new Bounds(new Vector3(-6.5f, 3.35f, 22), new Vector3(19, 6.2f, 24));
        var worldBounds = new Bounds(frame.TransformPoint(localBounds.center), Vector3.zero);
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            worldBounds.Encapsulate(frame.TransformPoint(localBounds.center + Vector3.Scale(localBounds.extents, new Vector3(x, y, z))));
        Physics.SyncTransforms();
        Undo.RecordObject(navigation, "Bake MC00 spider surfaces");
        if (!navigation.Bake(worldBounds, 1.25f, profile.Scale)) throw new InvalidOperationException(navigation.LastBakeReport);

        var data = new SerializedObject(settings);
        var slots = data.FindProperty("encounters");
        for (int encounter = 0; encounter < 2; encounter++)
        {
            Vector3 preferred = frame.TransformPoint(new Vector3(-5, 5.2f, 16 + encounter * 10));
            if (!Physics.Raycast(preferred, Vector3.up, out var ceiling, 1.7f, LayerMask.GetMask("Default", "Ground"),
                    QueryTriggerInteraction.Ignore) || ceiling.normal.y > -.75f)
                throw new InvalidOperationException("No inner ceiling at MC00 encounter " + encounter);
            Vector3 body = ceiling.point + ceiling.normal * SurfaceMotor.Height * profile.Scale;
            if (Physics.CheckSphere(body, SurfaceMotor.BodyRadius * profile.Scale, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Blocked ceiling perch " + encounter);
            int index = -1;
            for (int i = 0; i < slots.arraySize; i++)
            {
                var candidate = slots.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("Ambush").boolValue &&
                    Vector3.Distance(candidate.FindPropertyRelative("Position").vector3Value, ceiling.point) < 2) { index = i; break; }
            }
            if (index < 0) { index = slots.arraySize; slots.arraySize++; }
            var slot = slots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("Profile").objectReferenceValue = profile;
            slot.FindPropertyRelative("Prefab").FindPropertyRelative("<GhostPrefab>k__BackingField").FindPropertyRelative("m_AssetGUID").stringValue = guid;
            slot.FindPropertyRelative("Position").vector3Value = ceiling.point;
            slot.FindPropertyRelative("SurfaceNormal").vector3Value = ceiling.normal;
            slot.FindPropertyRelative("Yaw").floatValue = frame.eulerAngles.y;
            slot.FindPropertyRelative("Ambush").boolValue = true;
            slot.FindPropertyRelative("CandidatePositions").arraySize = 0;
        }
        data.FindProperty("maximumAliens").intValue = Mathf.Max(data.FindProperty("maximumAliens").intValue, 10);
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(navigation);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("MC00 G02/G03: two one-life spider ambushes. " + navigation.LastBakeReport);
    }
}
