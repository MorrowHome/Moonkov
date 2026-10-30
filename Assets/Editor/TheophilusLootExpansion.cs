using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TheophilusLootExpansion
{
    private const string FormalScene = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
    private const int SupplementalCount = 32;
    private const string RootName = "Theophilus Route Supplies";

    private struct Placement
    {
        public Vector3 Position, Normal;
        public float Yaw;
        public RaidLootProfile Profile;
    }

    private static MoonRaidMap Map()
    {
        var map = UnityEngine.Object.FindAnyObjectByType<MoonRaidMap>();
        if (EditorApplication.isPlayingOrWillChangePlaymode || !map || map.gameObject.scene.path != FormalScene)
            throw new InvalidOperationException("Open the formal Theophilus scene outside Play.");
        return map;
    }

    private static List<Placement> Plan(MoonRaidMap map)
    {
        if (map.LootPositions.Length != 61) throw new InvalidOperationException("Expected the original 61-cache map.");
        var anchors = map.LootPositions.Where(p => Vector3.Distance(p, map.ExtractionPosition) < 850)
            .OrderBy(p => Vector3.Distance(p, map.ExtractionPosition)).Take(12).ToArray();
        if (anchors.Length < 3) throw new InvalidOperationException("Missing entry-route anchors.");
        var result = new List<Placement>();
        UnityEngine.Physics.SyncTransforms();
        for (int i = 0; i < 3 + SupplementalCount; i++)
        {
            bool found = false;
            for (int attempt = 0; attempt < 96; attempt++)
            {
                float angle = (attempt % 16 * 22.5f + i / anchors.Length * 11) * Mathf.Deg2Rad;
                float radius = 15 + attempt / 16 * 10;
                Vector3 candidate = anchors[i % anchors.Length] + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (!RaidSpawnSafety.TryPlaceCache(candidate, angle * Mathf.Rad2Deg, out var point, out var normal) ||
                    Vector3.Distance(point, map.ExtractionPosition) < 25 ||
                    map.LootPositions.Any(p => Vector3.Distance(p, point) < 12) ||
                    result.Any(p => Vector3.Distance(p.Position, point) < 10)) continue;
                var profile = i < 3 ? (i == 1 ? RaidLootProfile.Medical : RaidLootProfile.Supplies) :
                    (RaidLootProfile)((i - 3) % 4);
                result.Add(new Placement { Position = point, Normal = normal, Yaw = angle * Mathf.Rad2Deg, Profile = profile });
                found = true; break;
            }
            if (!found) throw new InvalidOperationException("No safe cache placement for route slot " + i + "; no changes applied.");
        }
        return result;
    }

    [MenuItem("Tools/Moonkov/Expand Theophilus Loot")]
    public static string Apply()
    {
        var map = Map();
        var furniture = map.GetComponent<RaidFurnitureLoot>();
        int existing = furniture.Drawers.Count(d => d != null && d.SupplyCache);
        if (existing == SupplementalCount && map.LootPositions.Length == 64) return "Expansion already present; unchanged.";
        if (existing != 0 || map.transform.Find(RootName)) throw new InvalidOperationException("Partial or conflicting expansion; leave untouched.");
        if (map.gameObject.scene.isDirty) throw new InvalidOperationException("Save unrelated scene changes before authoring loot.");
        var plan = Plan(map);
        Undo.RecordObjects(new UnityEngine.Object[] { map, furniture }, "Expand Theophilus loot");
        var mapFields = new SerializedObject(map);
        var positions = mapFields.FindProperty("m_LootPositions"); positions.arraySize = 64;
        var profiles = mapFields.FindProperty("m_LootProfiles"); profiles.arraySize = 64;
        var yaws = mapFields.FindProperty("m_LootYaw"); yaws.arraySize = 64;
        var normals = mapFields.FindProperty("m_LootGroundNormals"); normals.arraySize = 64;
        for (int i = 0; i < 3; i++)
        {
            int index = 61 + i;
            positions.GetArrayElementAtIndex(index).vector3Value = plan[i].Position;
            profiles.GetArrayElementAtIndex(index).enumValueIndex = (int)plan[i].Profile;
            yaws.GetArrayElementAtIndex(index).floatValue = plan[i].Yaw;
            normals.GetArrayElementAtIndex(index).vector3Value = plan[i].Normal;
        }
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Add route supply anchors");
        root.transform.SetParent(map.transform, false);
        var fields = new SerializedObject(furniture);
        var drawers = fields.FindProperty("m_Drawers");
        int first = drawers.arraySize; drawers.arraySize += SupplementalCount;
        for (int i = 0; i < SupplementalCount; i++)
        {
            var placement = plan[3 + i];
            var anchor = new GameObject("Route cache " + (65 + i).ToString("00"));
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.position = placement.Position;
            anchor.transform.rotation = Quaternion.FromToRotation(Vector3.up, placement.Normal) * Quaternion.Euler(0, placement.Yaw, 0);
            var drawer = drawers.GetArrayElementAtIndex(first + i);
            drawer.FindPropertyRelative("Target").objectReferenceValue = anchor.transform;
            drawer.FindPropertyRelative("LocalPoint").vector3Value = Vector3.zero;
            drawer.FindPropertyRelative("Label").stringValue = "补给箱 " + (65 + i).ToString("00");
            drawer.FindPropertyRelative("Profile").enumValueIndex = (int)placement.Profile;
            drawer.FindPropertyRelative("SupplyCache").boolValue = true;
        }
        mapFields.ApplyModifiedProperties(); fields.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        if (!EditorSceneManager.SaveScene(map.gameObject.scene)) throw new InvalidOperationException("Failed to save loot expansion.");
        return "Added 35 loot points: 64 ordinary caches + 32 route caches + " + first + " existing drawers.";
    }

    private static bool SupportedCache(Vector3 position, Quaternion rotation)
    {
        int mask = LayerMask.GetMask("Default", "Ground");
        Vector3 normal = rotation * Vector3.up;
        Vector3 floor = position - normal * .35f;
        // A short probe stays on the authored floor in multi-storey structures.
        if (!UnityEngine.Physics.Raycast(floor + Vector3.up * .3f, Vector3.down, out var ground, .6f,
                mask, QueryTriggerInteraction.Ignore) || Vector3.Distance(ground.point, floor) > .05f ||
            Vector3.Angle(ground.normal, Vector3.up) > 18) return false;
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = floor + rotation * new Vector3(x * .51f, 0, z * .37f);
                if (!UnityEngine.Physics.Raycast(corner + Vector3.up * .3f, Vector3.down, out var support, .6f,
                        mask, QueryTriggerInteraction.Ignore) || Mathf.Abs(Vector3.Dot(support.point - corner, normal)) > .12f) return false;
            }
        if (UnityEngine.Physics.CheckBox(position + normal * .035f, new Vector3(.58f, .31f, .43f), rotation,
                mask, QueryTriggerInteraction.Ignore)) return false;
        for (int side = -1; side <= 1; side += 2)
            if (RaidSpawnSafety.TryGroundPosition(position + rotation * new Vector3(0, 0, side * 1.25f), .35f, 1.8f, 40,
                    out var actor, out _) && Mathf.Abs(actor.y - floor.y) < .6f) return true;
        return false;
    }

    public static string Check()
    {
        var map = Map();
        var furniture = map.GetComponent<RaidFurnitureLoot>();
        int checks = 0;
        void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        Require(map.LootPositions.Length == 64 && furniture.Drawers.Count(d => d.SupplyCache) == SupplementalCount,
            "Saved expansion count mismatch.");
        var caches = new RaidLootContainers();
        caches.Initialize(map.LootPositions.Length, map.GetLootProfile, map);
        furniture.Initialize(caches);
        Require(caches.Containers.Count == 64 + furniture.Drawers.Length && caches.RefillStates.Count == 64,
            "Supplemental IDs collided or entered ordinary-cache refills.");
        var retained = furniture.Drawers.Select((d, i) => caches.Containers[RaidFurnitureLoot.FirstId + i]).ToArray();
        furniture.Initialize(caches);
        Require(retained.Where((graph, i) => !ReferenceEquals(graph, caches.Containers[RaidFurnitureLoot.FirstId + i])).Count() == 0,
            "Scene containers rerolled during initialization.");
        UnityEngine.Physics.SyncTransforms();
        for (int i = 61; i < 64; i++)
            Require(SupportedCache(map.LootPositions[i], Quaternion.FromToRotation(Vector3.up, map.GetLootGroundNormal(i)) *
                Quaternion.Euler(0, map.GetLootYaw(i), 0)),
                "New ordinary cache lacks support or access: " + i);
        var preview = EditorSceneManager.NewPreviewScene();
        var cameraObject = new GameObject("Route cache selection check") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, preview);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        try
        {
            for (int i = 0; i < furniture.Drawers.Length; i++)
            {
                var drawer = furniture.Drawers[i];
                if (!drawer.SupplyCache) continue;
                int id = RaidFurnitureLoot.FirstId + i;
                var inventory = caches.Containers[id];
                Require(inventory.Validate() == InventoryError.None && RaidLootRefillState.HasContents(inventory),
                    "Supplemental cache has invalid or empty full-cache inventory: " + id);
                // Active is a Play-mode singleton; editor checks use the loaded scene instance.
                Require(drawer.Target && drawer.SupplyCache && drawer.Label.StartsWith("补给箱 ", StringComparison.Ordinal) &&
                    Vector3.Distance((Vector3)caches.FurnitureCaches[id], drawer.Position) < .001f,
                    "Supplemental position or interaction type was lost: " + id);
                Require(SupportedCache(drawer.Position, drawer.Target.rotation), "Supplemental cache lacks support or access: " + id);
                bool selected = false;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!RaidSpawnSafety.TryGroundPosition(drawer.Position + drawer.Target.rotation * new Vector3(0, 0, side * 1.25f),
                        .35f, 1.8f, 40, out var actor, out _) || Mathf.Abs(actor.y - (drawer.Position - drawer.Target.up * .35f).y) >= .6f) continue;
                    camera.transform.position = actor + Vector3.up * 1.4f;
                    camera.transform.forward = drawer.Position - camera.transform.position;
                    if (furniture.Select(camera, actor, out _) == id) { selected = true; break; }
                }
                Require(selected, "Supplemental cache cannot be selected from a standing access point: " + id);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
        return "PASS " + checks + " saved-map loot checks: 96 supply caches + " +
            (furniture.Drawers.Length - SupplementalCount) + " drawers; support, access, selection, unique IDs and shared full-cache inventories.";
    }
}
