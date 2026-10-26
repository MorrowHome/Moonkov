using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Scene handles and bounded placement repair; never modifies a running raid.</summary>
[CustomEditor(typeof(MoonRaidMap))]
public sealed class RaidSpawnAuthoring : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Loot positions are shared by client/server. Profiles and yaw use the same array index. " +
            "Empty caches refill only after the cooldown, with no nearby player or viewer. Entry points are for PMCs, not patrol goals.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Check cache support and access")) Debug.Log(Check((MoonRaidMap)target));
            if (GUILayout.Button("Repair cache grounding (within 12 m)")) Debug.Log(Repair((MoonRaidMap)target));
        }
    }

    private void OnSceneGUI()
    {
        var map = (MoonRaidMap)target;
        var positions = serializedObject.FindProperty("m_LootPositions");
        serializedObject.Update();
        for (int i = 0; i < positions.arraySize; i++)
        {
            var property = positions.GetArrayElementAtIndex(i);
            Vector3 position = property.vector3Value;
            Handles.color = ProfileColor(map.GetLootProfile(i));
            Handles.Label(position + Vector3.up, $"Cache {i + 1:00} / {map.GetLootProfile(i)}");
            Handles.DrawWireCube(position, new Vector3(1.16f, .7f, .86f));
            if (EditorApplication.isPlayingOrWillChangePlaymode) continue;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck()) property.vector3Value = moved;
        }
        Handles.color = new Color(1, .35f, .15f);
        foreach (var point in map.EnemySpawnPositions)
        {
            Handles.DrawWireDisc(point, Vector3.up, 2);
            Handles.Label(point + Vector3.up * 2, "PMC entry");
        }
        serializedObject.ApplyModifiedProperties();
    }

    private static Color ProfileColor(RaidLootProfile profile) => profile switch
    {
        RaidLootProfile.Medical => new Color(1, .35f, .4f),
        RaidLootProfile.Industrial => new Color(1, .75f, .2f),
        RaidLootProfile.Equipment => new Color(.5f, .6f, 1),
        _ => new Color(.35f, 1, .65f)
    };

    public static string Check(MoonRaidMap map)
    {
        UnityEngine.Physics.SyncTransforms();
        var invalid = new List<int>();
        for (int i = 0; i < map.LootPositions.Length; i++)
            if (!RaidSpawnSafety.TryPlaceCache(map.LootPositions[i] - map.GetLootGroundNormal(i) * .35f, map.GetLootYaw(i), out var ground) ||
                Vector3.Distance(ground, map.LootPositions[i]) > .05f) invalid.Add(i + 1);
        return $"Caches: {map.LootPositions.Length - invalid.Count}/{map.LootPositions.Length} supported, clear and accessible. " +
            (invalid.Count == 0 ? "" : "Need repair: " + string.Join(", ", invalid));
    }

    public static string Repair(MoonRaidMap map)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before authoring placements.");
        UnityEngine.Physics.SyncTransforms();
        var resolved = new Vector3[map.LootPositions.Length];
        var normals = new Vector3[resolved.Length];
        for (int i = 0; i < resolved.Length; i++)
        {
            bool found = false;
            for (int attempt = 0; attempt < 73; attempt++)
            {
                float radius = attempt == 0 ? 0 : 2 * (1 + (attempt - 1) / 12);
                float angle = (attempt - 1) * 30 * Mathf.Deg2Rad;
                Vector3 candidate = map.LootPositions[i] - map.GetLootGroundNormal(i) * .35f +
                    new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (!RaidSpawnSafety.TryPlaceCache(candidate, map.GetLootYaw(i), out var point, out var normal)) continue;
                bool overlap = false;
                for (int previous = 0; previous < i; previous++)
                    if (Vector3.Distance(point, resolved[previous]) < 3) { overlap = true; break; }
                if (overlap) continue;
                resolved[i] = point;
                normals[i] = normal;
                found = true;
                break;
            }
            // Prepare all points before mutating, so a failed repair cannot partially move the route.
            if (!found) throw new InvalidOperationException($"No safe cache placement within 12 m of cache {i + 1}. Move this anchor manually.");
        }
        var fields = new SerializedObject(map);
        var positions = fields.FindProperty("m_LootPositions");
        var groundNormals = fields.FindProperty("m_LootGroundNormals");
        groundNormals.arraySize = resolved.Length;
        for (int i = 0; i < resolved.Length; i++) positions.GetArrayElementAtIndex(i).vector3Value = resolved[i];
        for (int i = 0; i < resolved.Length; i++) groundNormals.GetArrayElementAtIndex(i).vector3Value = normals[i];
        fields.ApplyModifiedProperties();
        return $"Grounded {resolved.Length} caches with supported feet and standing access.";
    }

    public static string ConfigureTheophilus(MoonRaidMap map)
    {
        const string formal = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        if (EditorApplication.isPlayingOrWillChangePlaymode || map.gameObject.scene.path != formal)
            throw new InvalidOperationException("Open the formal Theophilus scene in Edit Mode.");
        Repair(map);
        var entries = new List<Vector3>();
        foreach (int index in new[] { 2, 4, 7, 9, 11 })
        {
            if (index >= map.LootPositions.Length) continue;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = (attempt * 30 + index * 41) * Mathf.Deg2Rad;
                float radius = attempt < 12 ? 12 : 20;
                Vector3 candidate = map.LootPositions[index] + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (!RaidSpawnSafety.TryGroundPosition(candidate, .35f, 1.8f, 40, out var floor, out _)) continue;
                if (Vector3.Distance(floor, map.NearestExtraction(floor)) < 35) continue;
                entries.Add(floor + Vector3.up * .05f);
                break;
            }
        }
        if (entries.Count < 3) throw new InvalidOperationException("Not enough safe PMC entry anchors on the route.");
        var fields = new SerializedObject(map);
        fields.FindProperty("m_LootRespawnSeconds").floatValue = 300;
        fields.FindProperty("m_LootRefillPlayerRadius").floatValue = 18;
        fields.FindProperty("m_LootRefillLimit").intValue = 2;
        var profiles = fields.FindProperty("m_LootProfiles");
        profiles.arraySize = map.LootPositions.Length;
        // Survival supplies at entry; medical and equipment at buildings; industrial caches deeper in the route.
        var routeProfiles = new[] { 0, 0, 2, 3, 1, 3, 0, 1, 2, 3, 0, 2 };
        for (int i = 0; i < profiles.arraySize; i++) profiles.GetArrayElementAtIndex(i).enumValueIndex = routeProfiles[i % routeProfiles.Length];
        var yaw = fields.FindProperty("m_LootYaw");
        yaw.arraySize = map.LootPositions.Length;
        var spawns = fields.FindProperty("m_EnemySpawnPositions");
        spawns.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++) spawns.GetArrayElementAtIndex(i).vector3Value = entries[i];
        fields.ApplyModifiedProperties();
        var aliens = map.GetComponent<AlienEncounterSettings>();
        if (aliens != null)
        {
            var alienFields = new SerializedObject(aliens);
            alienFields.FindProperty("minimumPlayerSpawnDistance").floatValue = 25;
            alienFields.FindProperty("visibilityDistance").floatValue = 75;
            alienFields.ApplyModifiedProperties();
        }
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        return $"Configured {map.LootPositions.Length} themed caches and {entries.Count} independent PMC entries; refill 300 s / 18 m / twice.";
    }
}
