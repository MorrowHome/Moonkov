using System;
using System.Collections.Generic;
using System.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Editor-only source for the two existing runtime scene configurations.
// Rebind anchors and publish again when replacing or moving map buildings.
[CreateAssetMenu(menuName = "Moonkov/Raid layout")]
public sealed class RaidLayoutAuthoring : ScriptableObject
{
    [Serializable]
    public sealed class Point
    {
        public string Id;
        [Tooltip("Scene hierarchy path. Empty uses world coordinates. A missing named anchor is an error.")]
        public string AnchorPath;
        public Vector3 LocalPosition;
        public float LocalYaw;
        [Tooltip("For indoor spawns: the walkable way out, in the same anchor space.")]
        public Vector3[] AccessRoute = Array.Empty<Vector3>();
    }

    public SceneAsset MapScene, SpawnScene;
    [Min(1)] public float RaidMinutes = 20;
    public Point[] IndoorSpawns = Array.Empty<Point>();
    public Point[] Extractions = Array.Empty<Point>();

    public struct Resolved
    {
        public string Id;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3[] Route;
    }

    public Resolved Resolve(Point point, Scene scene)
    {
        Transform anchor = null;
        if (!string.IsNullOrEmpty(point.AnchorPath))
        {
            var parts = point.AnchorPath.Split('/');
            anchor = scene.GetRootGameObjects().SingleOrDefault(r => r.name == parts[0])?.transform;
            for (int i = 1; anchor && i < parts.Length; i++) anchor = anchor.Find(parts[i]);
            if (!anchor) throw new InvalidOperationException(point.Id + ": missing map anchor " + point.AnchorPath);
            Require(Vector3.Dot(anchor.up, Vector3.up) > .999f, point.Id + ": bind to an upright placement root, not a model axis-conversion root.");
        }
        Vector3 World(Vector3 p) => anchor ? anchor.TransformPoint(p) : p;
        var rotation = (anchor ? anchor.rotation : Quaternion.identity) * Quaternion.Euler(0, point.LocalYaw, 0);
        var forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        return new Resolved { Id = point.Id, Position = World(point.LocalPosition),
            Rotation = Quaternion.LookRotation(forward.normalized, Vector3.up),
            Route = (point.AccessRoute ?? Array.Empty<Vector3>()).Select(World).ToArray() };
    }

    public string ValidateGeometry()
    {
        var map = LoadedMap();
        ValidateData();
        Physics.SyncTransforms();
        var spawns = IndoorSpawns.Select(p => Resolve(p, map.gameObject.scene)).ToArray();
        var exits = Extractions.Select(p => Resolve(p, map.gameObject.scene)).ToArray();
        var visibleMeshes = map.gameObject.scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<MeshFilter>())
            .Where(f => f.sharedMesh && f.TryGetComponent<Renderer>(out var r) && r.enabled && !r.forceRenderingOff).ToArray();
        foreach (var point in spawns)
        {
            Require(Standing(point.Position), point.Id + ": floor or standing clearance is invalid.");
            Require(Enclosed(point.Position), point.Id + ": spawn needs a roof and enclosing walls.");
            Require(VisibleRoof(point.Position, visibleMeshes), point.Id + ": retained collision is not enough; the roof must also be visible.");
            Require(point.Route.Length >= 2, point.Id + ": an indoor spawn needs a checked way out.");
            var previous = point.Position;
            foreach (var step in point.Route)
            {
                Require(WalkSegment(previous, step), point.Id + ": access route is obstructed or unsupported near " + step);
                previous = step;
            }
            Require(exits.All(e => Horizontal(e.Position, point.Position) >= 100), point.Id + ": extraction is less than 100 m from spawn.");
        }
        for (int i = 0; i < spawns.Length; i++)
            for (int j = i + 1; j < spawns.Length; j++)
                Require(Horizontal(spawns[i].Position, spawns[j].Position) >= 12, "Spawn points overlap the same small starting area.");
        for (int i = 0; i < exits.Length; i++)
        {
            var point = exits[i];
            Require(Standing(point.Position), point.Id + ": extraction lacks floor or standing clearance.");
            // Check the actual extraction radius, not just its centre.
            for (int j = 0; j < 8; j++)
            {
                var probe = point.Position + Quaternion.Euler(0, j * 45, 0) * Vector3.forward * map.ExtractionRadius;
                Require(Physics.Raycast(probe + Vector3.up * 2, Vector3.down, out var support, 4.5f, Mask, QueryTriggerInteraction.Ignore) &&
                    support.normal.y >= .7f && Mathf.Abs(support.point.y - point.Position.y) < 2.5f,
                    point.Id + ": extraction radius crosses a cliff or another floor.");
            }
            for (int j = i + 1; j < exits.Length; j++)
                Require(Horizontal(point.Position, exits[j].Position) >= 150, "Extraction points need separate approach routes.");
        }
        return $"PASS: {spawns.Length} indoor spawns with supported escape routes; {exits.Length} separated exits; {RaidMinutes:0.#} minute raid.";
    }

    public string CheckPublished()
    {
        var map = LoadedMap();
        ValidateData();
        Require(Mathf.Abs(map.RaidDuration - RaidMinutes * 60) < .01f, "Map raid duration is stale. Publish the layout.");
        Require(map.ExtractionCount == Extractions.Length, "Map extraction count is stale. Publish the layout.");
        for (int i = 0; i < Extractions.Length; i++)
            Require(map.TryExtractionPosition(i, out var p) && Vector3.Distance(p, Resolve(Extractions[i], map.gameObject.scene).Position) < .01f,
                Extractions[i].Id + ": published extraction position is stale.");
        var scene = SpawnSceneForCheck(out bool opened);
        try
        {
            var points = Spawns(scene);
            Require(points.Length == IndoorSpawns.Length, "Spawn scene count does not match the layout.");
            foreach (var source in IndoorSpawns)
            {
                var published = points.SingleOrDefault(p => p.name == source.Id);
                var target = Resolve(source, map.gameObject.scene);
                Require(published && Vector3.Distance(published.transform.position, target.Position) < .01f &&
                    Quaternion.Angle(published.transform.rotation, target.Rotation) < .1f, source.Id + ": published spawn is stale.");
            }
        }
        finally { if (opened && !scene.isDirty) EditorSceneManager.CloseScene(scene, true); }
        return "PASS: saved runtime map and spawn scene match the authored layout.";
    }

    public string Publish()
    {
        var map = LoadedMap();
        Require(!map.gameObject.scene.isDirty, "Save unrelated map changes before publishing the layout.");
        var result = ValidateGeometry();
        var scene = SpawnSceneForCheck(out bool opened);
        try
        {
            Require(!scene.isDirty, "Save unrelated spawn scene changes first.");
            var targets = Spawns(scene).OrderBy(p => p.name, StringComparer.Ordinal).ToArray();
            Require(targets.Length == IndoorSpawns.Length, "Publishing moves existing spawns; it does not add or delete them.");
            var assigned = new HashSet<SpawnPointAuthoring>();
            var pairs = new List<(SpawnPointAuthoring target, Resolved point)>();
            foreach (var source in IndoorSpawns)
            {
                var target = targets.FirstOrDefault(p => p.name == source.Id && !assigned.Contains(p));
                if (!target) target = targets.First(p => !assigned.Contains(p) && !IndoorSpawns.Any(s => s.Id == p.name));
                assigned.Add(target); pairs.Add((target, Resolve(source, map.gameObject.scene)));
            }
            Undo.RecordObject(map, "Publish raid layout");
            var fields = new SerializedObject(map);
            fields.FindProperty("m_RaidDuration").floatValue = RaidMinutes * 60;
            fields.FindProperty("m_ExtractionPosition").vector3Value = Resolve(Extractions[0], map.gameObject.scene).Position;
            var extra = fields.FindProperty("m_AdditionalExtractionPositions");
            extra.arraySize = Extractions.Length - 1;
            for (int i = 1; i < Extractions.Length; i++)
                extra.GetArrayElementAtIndex(i - 1).vector3Value = Resolve(Extractions[i], map.gameObject.scene).Position;
            fields.ApplyModifiedProperties();
            foreach (var pair in pairs)
            {
                Undo.RecordObjects(new UnityEngine.Object[] { pair.target.gameObject, pair.target.transform }, "Move indoor raid spawn");
                pair.target.name = pair.point.Id;
                pair.target.transform.SetPositionAndRotation(pair.point.Position, pair.point.Rotation);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save the spawn scene.");
            Require(EditorSceneManager.SaveScene(map.gameObject.scene), "Could not save the raid map.");
        }
        finally { if (opened && !scene.isDirty) EditorSceneManager.CloseScene(scene, true); }
        return result + " " + CheckPublished();
    }

    void ValidateData()
    {
        Require(MapScene && SpawnScene && MapScene != SpawnScene, "Assign the map and its separate spawn subscene.");
        Require(float.IsFinite(RaidMinutes) && RaidMinutes >= 1 && RaidMinutes <= 60, "Raid duration must be 1–60 minutes.");
        Require(IndoorSpawns != null && IndoorSpawns.Length > 0 && Extractions != null &&
            Extractions.Length > 0 && Extractions.Length <= RaidExtractionRules.MaxExits, "Invalid spawn or extraction count.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in IndoorSpawns.Concat(Extractions))
            Require(point != null && !string.IsNullOrWhiteSpace(point.Id) && ids.Add(point.Id) &&
                Finite(point.LocalPosition) && float.IsFinite(point.LocalYaw) &&
                (point.AccessRoute == null || point.AccessRoute.All(Finite)), "Layout points need unique IDs and finite coordinates.");
    }
    MoonRaidMap LoadedMap()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Stable Edit Mode required.");
        Require(MapScene, "Assign the map scene.");
        var scene = SceneManager.GetSceneByPath(AssetDatabase.GetAssetPath(MapScene));
        Require(scene.IsValid() && scene.isLoaded, "Open the layout's map scene before checking or publishing.");
        return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MoonRaidMap>(true)).Single();
    }
    Scene SpawnSceneForCheck(out bool opened)
    {
        var path = AssetDatabase.GetAssetPath(SpawnScene);
        var scene = SceneManager.GetSceneByPath(path);
        opened = !scene.IsValid() || !scene.isLoaded;
        return opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : scene;
    }
    static SpawnPointAuthoring[] Spawns(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<SpawnPointAuthoring>(true)).ToArray();
    static int Mask => LayerMask.GetMask("Default", "Ground");
    static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
    static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    static bool Floor(Vector3 p, float range, out Vector3 floor)
    {
        bool found = Physics.Raycast(p + Vector3.up * .5f, Vector3.down, out var hit, range + .5f, Mask, QueryTriggerInteraction.Ignore) && hit.normal.y >= .7f;
        floor = found ? hit.point : default; return found;
    }
    static bool Standing(Vector3 p) => Floor(p, .3f, out var floor) && Mathf.Abs(p.y - floor.y) <= .2f &&
        !Physics.CheckCapsule(floor + Vector3.up * .41f, floor + Vector3.up * 1.51f, .35f, Mask, QueryTriggerInteraction.Ignore);
    static bool Enclosed(Vector3 p)
    {
        foreach (var offset in new[] { Vector3.zero, Vector3.right * .4f, Vector3.left * .4f, Vector3.forward * .4f, Vector3.back * .4f })
            if (!Physics.Raycast(p + Vector3.up * 1.8f + offset, Vector3.up, 12, Mask, QueryTriggerInteraction.Ignore)) return false;
        int walls = 0;
        for (int i = 0; i < 8; i++)
            if (Physics.Raycast(p + Vector3.up * 1.2f, Quaternion.Euler(0, i * 45, 0) * Vector3.forward, 35, Mask, QueryTriggerInteraction.Ignore)) walls++;
        return walls >= 5;
    }
    static bool VisibleRoof(Vector3 p, MeshFilter[] meshes)
    {
        // Unity's Editor picking query can read non-readable imported meshes without
        // creating extra physics colliders or changing their import settings.
        var intersect = typeof(HandleUtility).GetMethod("IntersectRayMesh",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Require(intersect != null, "This Unity version needs an updated Editor mesh-picking adapter.");
        var ray = new Ray(p + Vector3.up * 1.8f, Vector3.up);
        foreach (var mesh in meshes)
        {
            if (!mesh.GetComponent<Renderer>().bounds.IntersectRay(ray, out float distance) || distance > 12) continue;
            var args = new object[] { ray, mesh.sharedMesh, mesh.transform.localToWorldMatrix, new RaycastHit() };
            if ((bool)intersect.Invoke(null, args) && ((RaycastHit)args[3]).distance < 12) return true;
        }
        return false;
    }
    // Permit the controller's 25 cm step envelope at ramp seams; never relax spawn clearance.
    static bool RouteClear(Vector3 floor) => !Physics.CheckCapsule(floor + Vector3.up * .53f,
        floor + Vector3.up * 1.52f, .28f, Mask, QueryTriggerInteraction.Ignore);
    static bool WalkSegment(Vector3 from, Vector3 to)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .3f));
        if (steps > 1000) return false;
        Vector3 previous = from;
        for (int i = 0; i <= steps; i++)
        {
            var expected = Vector3.Lerp(from, to, i / (float)steps);
            if (!Floor(expected, .7f, out var floor) || Mathf.Abs(floor.y - expected.y) > .65f || !RouteClear(floor)) return false;
            if (i > 0 && (Mathf.Abs(floor.y - previous.y) > .25f ||
                Physics.Linecast(previous + Vector3.up, floor + Vector3.up, Mask, QueryTriggerInteraction.Ignore))) return false;
            previous = floor;
        }
        return true;
    }
    static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}

[CustomEditor(typeof(RaidLayoutAuthoring))]
public sealed class RaidLayoutAuthoringInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
        {
            var layout = (RaidLayoutAuthoring)target;
            if (GUILayout.Button("Check placement and access")) Debug.Log(layout.ValidateGeometry());
            if (GUILayout.Button("Publish to map and spawn scenes")) Debug.Log(layout.Publish());
            if (GUILayout.Button("Check published scene data")) Debug.Log(layout.CheckPublished());
        }
    }
}
