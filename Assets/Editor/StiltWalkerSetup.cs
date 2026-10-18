using System;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class StiltWalkerSetup
{
    [MenuItem("CONTEXT/ProceduralAlienEnemy/Edit creature profile")]
    private static void SelectProfile(MenuCommand command)
    {
        var profile = new SerializedObject(command.context).FindProperty("profile").objectReferenceValue;
        if (profile) { Selection.activeObject = profile; EditorGUIUtility.PingObject(profile); }
    }

    [MenuItem("Tools/Moonkov/Aliens/Set up upright stilt walkers")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var scene = SceneManager.GetSceneByPath(AlienAmbushSetup.ScenePath);
        if (scene.isLoaded && scene.isDirty) throw new InvalidOperationException("Save formal scene edits before setup.");
        var addresses = AddressableAssetSettingsDefaultObject.Settings;
        if (!addresses) throw new InvalidOperationException("Addressables settings missing.");
        var prefabs = new GameObject[2];
        for (int i = 0; i < 2; i++)
        {
            string name = i == 0 ? "StiltWalker" : "StiltWatcher";
            string profilePath = "Assets/AlienCreatures/Profiles/" + name + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(profilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<AlienCreatureProfile>(); profile.ApplyDefaults(AlienSpecies.StiltWalker);
                if (i == 1)
                {
                    profile.StiltHeight = 4.2f; profile.StiltThickness = .065f; profile.StiltStepSeconds = .7f;
                    profile.Speed = 2.2f; profile.Health = 280; profile.AttackReach = 3.6f; profile.Windup = 1.2f; profile.Recovery = 1.6f;
                }
                profile.name = name; AssetDatabase.CreateAsset(profile, profilePath);
            }
            string prefabPath = "Assets/AlienCreatures/Prefabs/Xeno" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                var go = new GameObject("Xeno" + name);
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
            prefabs[i] = prefab;
            var entry = addresses.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(prefabPath), addresses.DefaultGroup); entry.address = prefabPath;
        }
        EditorUtility.SetDirty(addresses); EditorUtility.SetDirty(addresses.DefaultGroup);
        AssetDatabase.SaveAssetIfDirty(addresses); AssetDatabase.SaveAssetIfDirty(addresses.DefaultGroup);
        AlienEnemySetup.RegisterResources(prefabs);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(AlienAmbushSetup.ScenePath, OpenSceneMode.Additive);
        Transform frame = null; AlienEncounterSettings settings = null;
        foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "MC00_ROOT_Standard_YUp_Frame") frame = t;
            if (t.TryGetComponent<AlienEncounterSettings>(out var component)) settings = component;
        }
        if (!frame || !settings) throw new InvalidOperationException("Formal MC00 encounter frame missing.");
        var serialized = new SerializedObject(settings); var slots = serialized.FindProperty("encounters");
        Physics.SyncTransforms();
        for (int variant = 0; variant < 2; variant++)
        {
            string name = variant == 0 ? "StiltWalker" : "StiltWatcher";
            var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
            string guid = AssetDatabase.AssetPathToGUID("Assets/AlienCreatures/Prefabs/Xeno" + name + ".prefab");
            int found = 0;
            for (int i = 0; i < slots.arraySize; i++) if (slots.GetArrayElementAtIndex(i).FindPropertyRelative("Profile").objectReferenceValue == profile) found++;
            int total = variant == 0 ? 2 : 1;
            for (int i = found; i < total; i++)
            {
                Vector3 preferred = frame.TransformPoint(new Vector3(-4, .8f, variant == 0 ? 13 + i * 12 : 31));
                if (!FindFloor(preferred, profile, out var floor)) throw new InvalidOperationException("No full-height clearance for " + name);
                int index = slots.arraySize; slots.arraySize++;
                var slot = slots.GetArrayElementAtIndex(index);
                slot.FindPropertyRelative("Profile").objectReferenceValue = profile;
                slot.FindPropertyRelative("Prefab").FindPropertyRelative("<GhostPrefab>k__BackingField").FindPropertyRelative("m_AssetGUID").stringValue = guid;
                slot.FindPropertyRelative("Position").vector3Value = floor; slot.FindPropertyRelative("Yaw").floatValue = frame.eulerAngles.y;
                slot.FindPropertyRelative("Ambush").boolValue = false; slot.FindPropertyRelative("SurfaceNormal").vector3Value = Vector3.up;
                slot.FindPropertyRelative("CandidatePositions").arraySize = 0;
            }
        }
        serialized.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Debug.Log("Two upright prefabs registered; three full-height MC00 spawn anchors saved.");
    }
    private static bool FindFloor(Vector3 preferred, AlienCreatureProfile profile, out Vector3 floor)
    {
        for (int ring = 0; ring <= 4; ring++) for (int side = 0; side < (ring == 0 ? 1 : 8); side++)
        {
            Vector3 point = preferred + Quaternion.Euler(0, side * 45, 0) * Vector3.forward * ring * 1.5f;
            if (RaidSpawnSafety.TryGroundPosition(point, profile.UprightRadius(profile.Scale), profile.UprightHeight(profile.Scale), 30,
                out floor, out _)) return true;
        }
        floor = default; return false;
    }
}
