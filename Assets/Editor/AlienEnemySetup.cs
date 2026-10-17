using System;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Idempotent formal-game integration through Unity serialization APIs.
/// Preserves existing profile edits, PMC settings and previous experiment scenes.</summary>
public static class AlienEnemySetup
{
    public const string Root = "Assets/AlienCreatures";
    public const string GameScene = "Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/Apollo12GameScene.unity";
    private const string ResourcesScene = "Assets/Scenes/GameResourcesSubScene.unity";
    [MenuItem("Tools/Moonkov/Aliens/Set up eight alien enemies")]
    public static void SetUp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits before alien integration.");
        Folder(Root); Folder(Root + "/Profiles"); Folder(Root + "/Prefabs");
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (!settings) throw new InvalidOperationException("Addressables settings are missing.");
        var prefabs = new GameObject[8]; var profiles = new AlienCreatureProfile[8];
        for (int i = 0; i < 8; i++)
        {
            var species = (AlienSpecies)i;
            string profilePath = Root + "/Profiles/" + species + ".asset";
            var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(profilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<AlienCreatureProfile>(); profile.name = species.ToString();
                profile.ApplyDefaults(species); AssetDatabase.CreateAsset(profile, profilePath);
            }
            string prefabPath = Root + "/Prefabs/Xeno" + species + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                var go = new GameObject("Xeno" + species);
                try
                {
                    var ghost = go.AddComponent<GhostGameObject>(); ghost.RequireTransformSync = true;
                    var network = go.AddComponent<GhostAuthoringComponent>();
                    network.HasOwner = true; network.DefaultGhostMode = GhostMode.Interpolated;
                    network.SupportedGhostModes = GhostModeMask.Interpolated; network.SupportAutoCommandTarget = false;
                    var enemy = go.AddComponent<ProceduralAlienEnemy>(); var serialized = new SerializedObject(enemy);
                    serialized.FindProperty("profile").objectReferenceValue = profile; serialized.ApplyModifiedPropertiesWithoutUndo();
                    go.AddComponent<RifleImpactSurface>().Kind = RifleImpactKind.Body;
                    prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                }
                finally { Object.DestroyImmediate(go); }
            }
            profiles[i] = profile; prefabs[i] = prefab;
            var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(prefabPath), settings.DefaultGroup);
            entry.address = prefabPath;
        }
        RegisterResources(prefabs);
        var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        var map = Object.FindAnyObjectByType<MoonRaidMap>();
        if (!map) throw new InvalidOperationException("Formal map has no MoonRaidMap.");
        var encounters = map.GetComponent<AlienEncounterSettings>();
        bool created = !encounters;
        if (created) encounters = map.gameObject.AddComponent<AlienEncounterSettings>();
        bool missingProfiles = false;
        if (!created) for (int i = 0; i < encounters.Count; i++) missingProfiles |= !encounters[i].Profile;
        if (created || missingProfiles)
        {
            var data = new SerializedObject(encounters); var slots = data.FindProperty("encounters"); slots.arraySize = 8;
            Physics.SyncTransforms();
            for (int i = 0; i < slots.arraySize; i++)
            {
                var slot = slots.GetArrayElementAtIndex(i);
                // Single-scene opening may unload formerly unused assets. Reacquire
                // persistent authoring references after that scene transition.
                var profile = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(Root + "/Profiles/" + (AlienSpecies)i + ".asset");
                slot.FindPropertyRelative("Profile").objectReferenceValue = profile;
                var reference = slot.FindPropertyRelative("Prefab");
                reference.FindPropertyRelative("<GhostPrefab>k__BackingField").FindPropertyRelative("m_AssetGUID").stringValue = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefabs[i]));
                Vector3 preferred = new Vector3(1136.35f, 31.32f, 1318.03f) + new Vector3((i % 4 - 1.5f) * 4, 0, (i / 4) * 7);
                if (created)
                {
                    Vector3 spawn = FindFloor(preferred, map.LootPositions, profile.Scale);
                    slot.FindPropertyRelative("Position").vector3Value = spawn;
                    slot.FindPropertyRelative("Yaw").floatValue = -90;
                }
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets(); Selection.activeGameObject = encounters.gameObject;
        Debug.Log("Eight aliens registered in the formal raid. Profiles expose Scale; encounters select counts and spawn points.");
    }
    private static Vector3 FindFloor(Vector3 preferred, Vector3[] fallback, float scale)
    {
        int mask = LayerMask.GetMask("Default", "Ground");
        for (int radius = 0; radius <= 12; radius += 2) for (int side = 0; side < (radius == 0 ? 1 : 8); side++)
        {
            Vector3 point = preferred + Quaternion.Euler(0, side * 45, 0) * Vector3.forward * radius;
            if (!Physics.Raycast(point + Vector3.up * 2, Vector3.down, out var floor, 6, mask, QueryTriggerInteraction.Ignore) || floor.normal.y < .75f) continue;
            Vector3 centre = floor.point + floor.normal * Unity.MP_FPS.SpiderLab.SurfaceMotor.Height * scale;
            if (Physics.CheckSphere(centre, Unity.MP_FPS.SpiderLab.SurfaceMotor.BodyRadius * scale, mask, QueryTriggerInteraction.Ignore)) continue;
            return floor.point;
        }
        foreach (var point in fallback)
            if (Physics.Raycast(point + Vector3.up * 3, Vector3.down, out var hit, 8, mask, QueryTriggerInteraction.Ignore)) return hit.point;
        throw new InvalidOperationException("No physical floor for alien spawn " + preferred);
    }
    internal static void RegisterResources(GameObject[] prefabs)
    {
        var scene = SceneManager.GetSceneByPath(ResourcesScene); bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ResourcesScene, OpenSceneMode.Additive);
        try
        {
            foreach (var root in scene.GetRootGameObjects()) foreach (var authoring in root.GetComponentsInChildren<GhostPrefabsAuthoring>(true))
            {
                var serialized = new SerializedObject(authoring); var list = serialized.FindProperty("<GhostPrefabs>k__BackingField");
                foreach (var prefab in prefabs)
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)); bool found = false;
                    for (int i = 0; i < list.arraySize; i++) found |= list.GetArrayElementAtIndex(i).FindPropertyRelative("m_AssetGUID").stringValue == guid;
                    if (found) continue;
                    int index = list.arraySize; list.InsertArrayElementAtIndex(index);
                    var element = list.GetArrayElementAtIndex(index); element.FindPropertyRelative("m_AssetGUID").stringValue = guid;
                    element.FindPropertyRelative("m_SubObjectName").stringValue = ""; element.FindPropertyRelative("m_SubObjectType").stringValue = "";
                    element.FindPropertyRelative("m_SubObjectGUID").stringValue = "";
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
