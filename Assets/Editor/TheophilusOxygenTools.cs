using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

/// <summary>Scene-owned air volumes from MC00's occupied plates, without presentation padding.</summary>
public static class TheophilusOxygenTools
{
    public const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
    private const string RootName = "MC00 Pressurized Interior";

    [MenuItem("Tools/Moonkov/Oxygen/Apply Theophilus interior air")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stable Edit Mode required.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded || scene.isDirty)
            throw new InvalidOperationException("Open and save the formal Theophilus map first.");
        if (scene.GetRootGameObjects().Any(g => g.name == RootName))
            throw new InvalidOperationException("Interior air already authored; validate or edit the existing volumes.");
        var model = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
            .Single(t => t.name == "MC00_ROOT");
        var plates = model.GetComponentsInChildren<MeshFilter>(true);
        Vector2[] Footprint(string wing) => Contour(plates.Single(f =>
            f.name.StartsWith("MC_" + wing + " PRIMARY OCCUPIED PLATE L0_", StringComparison.Ordinal)), model);
        // Extract all geometry before changing the scene. The curved wings must retain their outlines.
        var east = Footprint("E"); var north = Footprint("N");
        var west = Footprint("W"); var hub = Footprint("H");
        int group = Undo.GetCurrentGroup();
        GameObject root = null;
        try
        {
            root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Author MC00 interior air");
            root.transform.SetPositionAndRotation(model.position, model.rotation);
            root.transform.localScale = model.lossyScale;
            // The east ground floor contains the EVA chamber. Leave its full footprint to
            // the dynamic chamber volumes, including the walls, rather than relying on priority.
            Zone(root.transform, "E lower west", Clip(east, p => -24 - p.x), .6f, 6.6f);
            Zone(root.transform, "E lower east", Clip(east, p => p.x + 16), .6f, 6.6f);
            Zone(root.transform, "E lower inside", Clip(east, p => p.y + 24.7f), .6f, 6.6f);
            Zone(root.transform, "E upper", east, 6.6f, 12.28f);
            Zone(root.transform, "N rooms and stairs", north, .6f, 18.28f);
            Zone(root.transform, "W rooms and stairs", west, .6f, 18.28f);
            Zone(root.transform, "Transfer hub", hub, .6f, 18.28f);
            Validate();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save interior air.");
            Undo.CollapseUndoOperations(group);
            Debug.Log("Theophilus interior air saved: seven footprint volumes; chamber and outdoors remain separate.");
        }
        catch
        {
            if (root) UnityEngine.Object.DestroyImmediate(root);
            throw;
        }
    }

    private static void Zone(Transform parent, string name, Vector2[] polygon, float bottom, float top)
    {
        if (polygon.Length < 3) throw new InvalidOperationException("Empty interior footprint: " + name);
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        var bounds = new Bounds(new Vector3(polygon[0].x, polygon[0].y, bottom), Vector3.zero);
        foreach (var p in polygon)
        {
            bounds.Encapsulate(new Vector3(p.x, p.y, bottom));
            bounds.Encapsulate(new Vector3(p.x, p.y, top));
        }
        var box = go.AddComponent<BoxCollider>(); box.isTrigger = true;
        box.center = bounds.center; box.size = bounds.size;
        var zone = go.AddComponent<BreathableZone>();
        // Match the existing inner-door airspace so pressure-aware audio stays connected.
        zone.Configure("EVA_EAST_01.INSIDE", 101.325f, .21f, 10);
        zone.AllowsSuitRefill = false;
        zone.ConfigureFootprint(polygon);
    }

    // Read only the upper plate surface; shared vertex positions join bevel-split mesh vertices.
    private static Vector2[] Contour(MeshFilter filter, Transform model)
    {
        var vertices = filter.sharedMesh.vertices.Select(p => model.InverseTransformPoint(filter.transform.TransformPoint(p))).ToArray();
        float top = vertices.Max(p => p.z);
        var points = new Dictionary<Vector2Int, Vector2>();
        var counts = new Dictionary<(Vector2Int, Vector2Int), int>();
        Vector2Int Key(Vector3 p)
        {
            var key = new Vector2Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.y * 1000));
            points[key] = new Vector2(p.x, p.y); return key;
        }
        var indices = filter.sharedMesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            var triangle = new[] { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
            if (triangle.Any(p => Mathf.Abs(p.z - top) > .005f)) continue;
            for (int e = 0; e < 3; e++)
            {
                var a = Key(triangle[e]); var b = Key(triangle[(e + 1) % 3]);
                if (a == b) continue;
                bool order = a.x < b.x || a.x == b.x && a.y < b.y;
                var edge = order ? (a, b) : (b, a);
                counts[edge] = counts.TryGetValue(edge, out int count) ? count + 1 : 1;
            }
        }
        var boundary = counts.Where(p => p.Value == 1).Select(p => p.Key).ToList();
        if (boundary.Count < 3) throw new InvalidOperationException("Missing plate boundary: " + filter.name);
        var start = boundary[0].Item1; var current = start;
        var polygon = new List<Vector2>();
        do
        {
            polygon.Add(points[current]);
            int index = boundary.FindIndex(e => e.Item1 == current || e.Item2 == current);
            if (index < 0) throw new InvalidOperationException("Open plate boundary: " + filter.name);
            var edge = boundary[index]; boundary.RemoveAt(index);
            current = edge.Item1 == current ? edge.Item2 : edge.Item1;
        } while (current != start);
        if (boundary.Count != 0) throw new InvalidOperationException("Multiple plate contours require explicit authoring: " + filter.name);
        return polygon.ToArray();
    }

    private static Vector2[] Clip(Vector2[] source, Func<Vector2, float> distance)
    {
        var output = new List<Vector2>();
        for (int i = 0; i < source.Length; i++)
        {
            var a = source[i]; var b = source[(i + 1) % source.Length];
            float da = distance(a), db = distance(b);
            if (da >= 0) output.Add(a);
            if ((da >= 0) != (db >= 0)) output.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        return output.ToArray();
    }

    [MenuItem("Tools/Moonkov/Oxygen/Check Theophilus interior air")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        var root = scene.GetRootGameObjects().Single(g => g.name == RootName);
        var zones = root.GetComponentsInChildren<BreathableZone>();
        if (zones.Length != 7) throw new InvalidOperationException("Expected seven authored air volumes.");
        int checks = 0;
        void Check(Vector3 local, bool breathable)
        {
            var world = root.transform.TransformPoint(local);
            BreathableZone.TrySample(world, out var atmosphere);
            if (atmosphere.Breathable != breathable)
                throw new InvalidOperationException("Interior air mismatch at model " + local);
            var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state); state.Oxygen = 40;
            RaidOxygen.Tick(ref state, atmosphere, OxygenParameters.Default, 10, 1);
            if (breathable ? state.Oxygen < 40 || state.OxygenDrainPerSecond != 0 : state.Oxygen >= 40)
                throw new InvalidOperationException("Reserve mismatch at model " + local);
            checks++;
        }
        // Both east floors; three north/west/hub floors; connected stairwells and inner-door route.
        foreach (float height in new[] { 2f, 8f, 14f })
        {
            foreach (var xy in new[] { new Vector2(20, -18), new Vector2(13, 20), new Vector2(0, 2), new Vector2(-14, 21), new Vector2(24, -4) })
                Check(new Vector3(xy.x, xy.y, height), true);
            if (height < 12) Check(new Vector3(-28, -15, height), true);
        }
        foreach (float y in new[] { -23.4f, -23f, -22f, -21f }) Check(new Vector3(-20, y, 2), true);
        // Courtyard, curved hull corners, roof, ground, and all three exterior approaches.
        foreach (var p in new[] { new Vector3(0,-15,2), new Vector3(-35,-28,2), new Vector3(36,-28,2),
            new Vector3(13,20,20), new Vector3(13,20,-1), new Vector3(-20,-36,2),
            new Vector3(0,38,2), new Vector3(43,-10,2), new Vector3(-28,-15,14) }) Check(p, false);
        // Static room footprints must never grant air inside the chamber, even if dynamic zones are disabled.
        var dynamic = UnityEngine.Object.FindObjectsByType<BreathableZone>()
            .Where(z => z.gameObject.scene == scene && z.AirspaceId == "EVA_EAST_01.CHAMBER").ToArray();
        var enabled = dynamic.Select(z => z.enabled).ToArray();
        try
        {
            foreach (var zone in dynamic) zone.enabled = false;
            foreach (float y in new[] { -32f, -29.5f, -27f, -25f }) Check(new Vector3(-20, y, 2), false);
        }
        finally { for (int i = 0; i < dynamic.Length; i++) dynamic[i].enabled = enabled[i]; }
        Debug.Log("Theophilus interior oxygen passed: " + checks + " spatial samples and ten-second reserve checks.");
    }
}
