using System;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class FaceHuggerSetup
{
    [MenuItem("Tools/Moonkov/Aliens/Set up dedicated face parasite")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        const string profilePath = "Assets/AlienCreatures/Profiles/FaceHugger.asset";
        const string prefabPath = "Assets/AlienCreatures/Prefabs/XenoFaceHugger.prefab";
        var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(profilePath);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<AlienCreatureProfile>(); profile.ApplyDefaults(AlienSpecies.FaceHugger);
            profile.name = "FaceHugger"; AssetDatabase.CreateAsset(profile, profilePath);
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (!prefab)
        {
            var go = new GameObject("XenoFaceHugger");
            try
            {
                go.AddComponent<GhostGameObject>().RequireTransformSync = true;
                var ghost = go.AddComponent<GhostAuthoringComponent>(); ghost.HasOwner = true;
                ghost.DefaultGhostMode = GhostMode.Interpolated; ghost.SupportedGhostModes = GhostModeMask.Interpolated;
                ghost.SupportAutoCommandTarget = false;
                var enemy = go.AddComponent<ProceduralAlienEnemy>(); var data = new SerializedObject(enemy);
                data.FindProperty("profile").objectReferenceValue = profile; data.ApplyModifiedPropertiesWithoutUndo();
                go.AddComponent<RifleImpactSurface>().Kind = RifleImpactKind.Body;
                prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally { Object.DestroyImmediate(go); }
        }
        var addresses = AddressableAssetSettingsDefaultObject.Settings;
        if (!addresses) throw new InvalidOperationException("Addressables settings missing.");
        string guid = AssetDatabase.AssetPathToGUID(prefabPath);
        var entry = addresses.CreateOrMoveEntry(guid, addresses.DefaultGroup); entry.address = prefabPath;
        EditorUtility.SetDirty(addresses); EditorUtility.SetDirty(addresses.DefaultGroup);
        AssetDatabase.SaveAssetIfDirty(addresses); AssetDatabase.SaveAssetIfDirty(addresses.DefaultGroup);
        AlienEnemySetup.RegisterResources(new[] { prefab });
        var scene = SceneManager.GetSceneByPath(AlienAmbushSetup.ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(AlienAmbushSetup.ScenePath, OpenSceneMode.Additive);
        Transform frame = null; AlienEncounterSettings settings = null;
        foreach (var root in scene.GetRootGameObjects()) foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name == "MC00_ROOT_Standard_YUp_Frame") frame = transform;
            if (transform.TryGetComponent<AlienEncounterSettings>(out var component)) settings = component;
        }
        if (!frame || !settings) throw new InvalidOperationException("Formal MC00 encounters missing.");
        var serialized = new SerializedObject(settings); var slots = serialized.FindProperty("encounters");
        int found = 0;
        for (int i = 0; i < slots.arraySize; i++) if (slots.GetArrayElementAtIndex(i).FindPropertyRelative("Profile").objectReferenceValue == profile) found++;
        Physics.SyncTransforms();
        for (int i = found; i < 2; i++)
        {
            Vector3 preferred = frame.TransformPoint(new Vector3(-7, .8f, 15 + i * 10));
            if (!FindFloor(preferred, profile.Scale, out var point)) throw new InvalidOperationException("No clear floor for face parasite " + i);
            int index = slots.arraySize; slots.arraySize++;
            var slot = slots.GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("Profile").objectReferenceValue = profile;
            slot.FindPropertyRelative("Prefab").FindPropertyRelative("<GhostPrefab>k__BackingField").FindPropertyRelative("m_AssetGUID").stringValue = guid;
            slot.FindPropertyRelative("Position").vector3Value = point; slot.FindPropertyRelative("Yaw").floatValue = frame.eulerAngles.y;
            slot.FindPropertyRelative("Ambush").boolValue = false; slot.FindPropertyRelative("SurfaceNormal").vector3Value = Vector3.up;
            slot.FindPropertyRelative("CandidatePositions").arraySize = 0;
        }
        serialized.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("Dedicated face parasite registered with two MC00 encounter slots. Leech remains a separate species.");
    }
    private static bool FindFloor(Vector3 preferred, float scale, out Vector3 point)
    {
        for (int ring = 0; ring <= 3; ring++) for (int side = 0; side < (ring == 0 ? 1 : 8); side++)
        {
            Vector3 probe = preferred + Quaternion.Euler(0, side * 45, 0) * Vector3.forward * ring;
            if (RaidSpawnSafety.TryGroundPosition(probe, .23f * scale, .8f * scale, 45, out point, out _)) return true;
        }
        point = default; return false;
    }
}
