using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MC00DrawerLootAuthoring
{
    private const string FormalScene = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";

    private static string PathFor(Transform target)
    {
        string path = target.name;
        for (var parent = target.parent; parent; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }

    private static bool IsBody(Transform target)
    {
        string name = target.name.ToLowerInvariant();
        return target.gameObject.activeInHierarchy && (name.Contains("drawer") && name.Contains("travel_root") ||
            name.Contains(" drawer_") && !name.Contains("runner") && !name.Contains("suspension"));
    }

    private static bool TryInteractionPoint(Transform target, out Vector3 point, out Vector3 access)
    {
        point = default;
        access = default;
        var renderers = target.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return false;
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        int mask = LayerMask.GetMask("Default", "Ground");
        for (int ring = 0; ring < 4; ring++)
            for (int side = 0; side < 16; side++)
            {
                float angle = side * Mathf.PI / 8;
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + .55f + ring * .3f;
                var candidate = bounds.center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (!UnityEngine.Physics.Raycast(candidate + Vector3.up, Vector3.down, out var floor, 5,
                        mask, QueryTriggerInteraction.Ignore) ||
                    !RaidSpawnSafety.TryGroundPosition(floor.point, .3f, 1.8f, 40, out var standing, out _)) continue;
                Vector3 eye = standing + Vector3.up * 1.4f;
                // The centre can lie behind a desktop or tool insert. Aim at the
                // near face of each drawer piece, still requiring a hit on that drawer.
                foreach (var renderer in renderers)
                    for (int aim = 0; aim < 2; aim++)
                    {
                        var local = renderer.localBounds;
                        var near = renderer.transform.TransformPoint(local.ClosestPoint(renderer.transform.InverseTransformPoint(eye)));
                        near.y = renderer.bounds.center.y;
                        Vector3 goal = aim == 0 ? near : renderer.bounds.center;
                        Vector3 direction = goal - eye;
                        if (!UnityEngine.Physics.Raycast(eye, direction.normalized, out var face, direction.magnitude + .15f,
                                mask, QueryTriggerInteraction.Ignore) ||
                            !(face.transform == target || face.transform.IsChildOf(target))) continue;
                        point = face.point - direction.normalized * .04f;
                        if (Vector3.Distance(standing, point) <= RaidRules.PickupRange) { access = standing; return true; }
                    }
            }
        return false;
    }

    [MenuItem("Tools/Moonkov/Configure MC00 Drawer Loot")]
    public static void Configure()
    {
        var map = UnityEngine.Object.FindAnyObjectByType<MoonRaidMap>();
        if (Application.isPlaying || !map || map.gameObject.scene.path != FormalScene)
            throw new InvalidOperationException("Open the formal Theophilus scene in Edit Mode.");
        UnityEngine.Physics.SyncTransforms();
        var bodies = UnityEngine.Object.FindObjectsByType<Transform>()
            .Where(t => t.gameObject.scene == map.gameObject.scene && IsBody(t)).OrderBy(PathFor).ToArray();
        var entries = new List<RaidFurnitureLoot.Drawer>();
        var skipped = new List<string>();
        foreach (var body in bodies)
        {
            if (!TryInteractionPoint(body, out var point, out _)) { skipped.Add(body.name); continue; }
            string name = body.name.ToLowerInvariant();
            var profile = name.Contains("specimen") ? RaidLootProfile.Medical :
                name.Contains("crew prep") || name.Contains("briefing") ? RaidLootProfile.Supplies : RaidLootProfile.Industrial;
            var room = Regex.Match(PathFor(body), "ORG_ROOM_([A-Z0-9]+)_");
            string area = room.Success ? room.Groups[1].Value : "MC00";
            string kind = profile == RaidLootProfile.Medical ? "医疗抽屉" : profile == RaidLootProfile.Supplies ? "生活物资抽屉" : "工具资料抽屉";
            entries.Add(new RaidFurnitureLoot.Drawer { Target = body, LocalPoint = body.InverseTransformPoint(point),
                Label = $"{area} · {kind} {entries.Count + 1:00}", Profile = profile });
        }
        if (entries.Count == 0) throw new InvalidOperationException("No reachable drawer bodies found.");
        var furniture = map.GetComponent<RaidFurnitureLoot>();
        if (!furniture) furniture = Undo.AddComponent<RaidFurnitureLoot>(map.gameObject);
        var fields = new SerializedObject(furniture);
        var drawers = fields.FindProperty("m_Drawers");
        drawers.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            var item = drawers.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("Target").objectReferenceValue = entries[i].Target;
            item.FindPropertyRelative("LocalPoint").vector3Value = entries[i].LocalPoint;
            item.FindPropertyRelative("Label").stringValue = entries[i].Label;
            item.FindPropertyRelative("Profile").enumValueIndex = (int)entries[i].Profile;
        }
        fields.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        EditorSceneManager.SaveScene(map.gameObject.scene);
        Debug.Log($"MC00 drawer loot: {entries.Count}/{bodies.Length} reachable containers. Skipped: {string.Join(", ", skipped)}");
    }

    [MenuItem("Tools/Moonkov/Check MC00 Drawer Loot")]
    public static void Check()
    {
        var map = UnityEngine.Object.FindAnyObjectByType<MoonRaidMap>();
        var furniture = map ? map.GetComponent<RaidFurnitureLoot>() : null;
        if (Application.isPlaying || !furniture || furniture.Drawers.Length == 0) throw new Exception("Open a configured map in Edit Mode.");
        var caches = new RaidLootContainers();
        caches.Initialize(map.LootPositions.Length, map.GetLootProfile, map);
        furniture.Initialize(caches);
        int count = caches.Containers.Count;
        var first = caches.Containers[RaidFurnitureLoot.FirstId];
        furniture.Initialize(caches);
        if (count != map.LootPositions.Length + furniture.Drawers.Length || count != caches.Containers.Count ||
            !ReferenceEquals(first, caches.Containers[RaidFurnitureLoot.FirstId]) || caches.RefillStates.Count != map.LootPositions.Length)
            throw new Exception("Drawer IDs collide, contents reroll, or drawers enter the refill loop.");
        for (int i = 0; i < furniture.Drawers.Length; i++)
            if (caches.Containers[RaidFurnitureLoot.FirstId + i].Validate() != InventoryError.None)
                throw new Exception("Invalid drawer inventory.");
        var random = new System.Random(7613);
        for (int i = 0; i < 48; i++)
        {
            var graph = RaidLootTable.CreateDrawer((RaidLootProfile)(i % 4), random);
            if (graph.Validate() != InventoryError.None || graph.Items.Count(item => item.Parent == LootInventoryExchange.Root) > 2)
                throw new Exception("Drawer loot exceeds its small-container budget.");
        }
        var inventory = new RaidInventoryState();
        inventory.BeginDissection(RaidFurnitureLoot.FirstId, 0, default, 0, 2);
        inventory.RefreshDissection(caches, true, new Unity.Mathematics.float3(2, 0, 0), 0, 3);
        if (inventory.OpenedLootId >= 0 || inventory.SearchedLoot.Count != 0) throw new Exception("Moving failed to interrupt drawer search.");
        inventory.BeginDissection(RaidFurnitureLoot.FirstId, 4, default, 0, 2);
        inventory.RefreshDissection(caches, true, default, 0, 7);
        if (inventory.OpenedLootId != RaidFurnitureLoot.FirstId) throw new Exception("Drawer search did not reveal its existing shared inventory.");
        GameObject cameraObject = null, wall = null;
        try
        {
            var drawer = furniture.Drawers[0];
            if (!TryInteractionPoint(drawer.Target, out _, out var actor)) throw new Exception("Drawer access point lost support.");
            cameraObject = new GameObject("Temporary drawer selection check") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = actor + Vector3.up * 1.4f;
            camera.transform.forward = drawer.Position - camera.transform.position;
            if (furniture.Select(camera, actor, out _) != RaidFurnitureLoot.FirstId ||
                furniture.Select(camera, actor + Vector3.up * 100, out _) != -1)
                throw new Exception("Drawer focus selection or range enforcement failed.");
            wall = new GameObject("Temporary drawer occlusion check") { hideFlags = HideFlags.HideAndDontSave };
            wall.transform.position = Vector3.Lerp(camera.transform.position, drawer.Position, .5f);
            wall.transform.rotation = Quaternion.LookRotation(drawer.Position - camera.transform.position);
            wall.AddComponent<BoxCollider>().size = new Vector3(2, 2, .08f);
            UnityEngine.Physics.SyncTransforms();
            if (furniture.Select(camera, actor, out _) >= 0) throw new Exception("Drawer can be selected through an obstruction.");
        }
        finally
        {
            if (wall) UnityEngine.Object.DestroyImmediate(wall);
            if (cameraObject) UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Physics.SyncTransforms();
        }
        Debug.Log($"Drawer checks passed: {furniture.Drawers.Length} independent containers, no reroll/refill, small loot budgets, search completion and movement interruption.");
    }
}
