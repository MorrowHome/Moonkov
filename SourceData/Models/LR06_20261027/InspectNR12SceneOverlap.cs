// Inspect the saved formal map in a temporary preview scene. No scene is saved.
void Need(bool ok, string reason) { if (!ok) throw new Exception(reason); }
Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer,
    "Editor must be idle before opening the inspection preview");
string scenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
string root = System.IO.Path.GetFullPath("SourceData/Models/LR06_20261027");
var jsonType = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
object Field(object o, string k) => o.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(o, new object[] { k });
T Read<T>(object o) => (T)o.GetType().GetMethod("ToObject", new[] { typeof(Type) }).Invoke(o, new object[] { typeof(T) });
object[] Rows(object o) => ((System.Collections.IEnumerable)o).Cast<object>().ToArray();
var plan = jsonType.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
    new object[] { System.IO.File.ReadAllText(System.IO.Path.Combine(root, "NR12WallBatchPilot/ImportPlan.json")), typeof(object) });
var targets = Rows(Field(plan, "materials")).Select(row => {
    var bb = Read<float[][]>(Field(row, "source_world_bounds"));
    var min = new Vector3(bb[0][0], bb[0][2], bb[0][1]);
    var max = new Vector3(bb[1][0], bb[1][2], bb[1][1]);
    var b = new Bounds(); b.SetMinMax(min, max); b.Expand(.02f);
    return (name: Read<string>(Field(row, "object")), bounds: b);
}).ToArray();
Need(targets.Length == 6, "Expected six wall bounds");
// The floor can sit just below the wall bounds, so inspect its wider approach
// separately. This remains an AABB search, not a walkability conclusion.
targets = targets.Concat(new[] { (name: "NR12_CORRIDOR_FLOOR_CONTEXT",
    bounds: new Bounds(new Vector3(1998.2f, 250.5f, 2470.2f), new Vector3(10, 6, 24))) }).ToArray();
var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
string originalScene = active.path;
bool originalDirty = active.isDirty;
var originalLightmaps = LightmapSettings.lightmaps;
var originalProbes = LightmapSettings.lightProbes;
var originalLightmapsMode = LightmapSettings.lightmapsMode;
var originalSky = RenderSettings.skybox;
string skyJson = originalSky ? EditorJsonUtility.ToJson(originalSky) : null;
bool skyDirty = originalSky && EditorUtility.IsDirty(originalSky);
var preview = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene(scenePath);
try
{
    Need(preview.IsValid() && preview.isLoaded, "Could not open saved scene as preview");
    string PathOf(Transform t)
    {
        var names = new List<string>();
        while (t) { names.Add(t.name); t = t.parent; }
        names.Reverse(); return string.Join("/", names);
    }
    Bounds WorldBounds(Transform t, Bounds local)
    {
        var c = local.center; var e = local.extents;
        var points = new Vector3[8]; int k = 0;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            points[k++] = c + Vector3.Scale(e, new Vector3(x, y, z));
        return GeometryUtility.CalculateBounds(points, t.localToWorldMatrix);
    }
    var roots = preview.GetRootGameObjects();
    var renderers = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
    var colliders = roots.SelectMany(r => r.GetComponentsInChildren<Collider>(true)).ToArray();
    var overlap = new List<object>();
    var overlapSummary = new List<object>();
    foreach (var target in targets)
    {
        var matches = new List<object>();
        int enabledMatches = 0;
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) continue;
            var bounds = WorldBounds(renderer.transform, filter.sharedMesh.bounds);
            if (!bounds.Intersects(target.bounds)) continue;
            if (renderer.enabled && renderer.gameObject.activeInHierarchy) enabledMatches++;
            matches.Add(new { name = renderer.name, hierarchy = PathOf(renderer.transform),
                rendererEnabled = renderer.enabled, active = renderer.gameObject.activeInHierarchy,
                mesh = AssetDatabase.GetAssetPath(filter.sharedMesh), lightmapIndex = renderer.lightmapIndex,
                boundsMin = new[] { bounds.min.x, bounds.min.y, bounds.min.z },
                boundsMax = new[] { bounds.max.x, bounds.max.y, bounds.max.z } });
        }
        var colliderMatches = colliders.Where(c => c.bounds.Intersects(target.bounds)).ToArray();
        var colliderDetails = colliderMatches.Select(c => {
            var terrain = c as TerrainCollider;
            var terrainData = terrain ? terrain.terrainData : null;
            var terrainSamples = new List<object>();
            var meshSamples = new List<object>();
            if (terrainData)
            {
                float[] xs = { target.bounds.min.x, target.bounds.center.x, target.bounds.max.x };
                float[] zs = { target.bounds.min.z, target.bounds.center.z, target.bounds.max.z };
                foreach (float x in xs) foreach (float z in zs)
                {
                    var local = terrain.transform.InverseTransformPoint(new Vector3(x, target.bounds.min.y, z));
                    float u = local.x / terrainData.size.x, v = local.z / terrainData.size.z;
                    if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                    var ground = terrain.transform.TransformPoint(new Vector3(local.x, terrainData.GetInterpolatedHeight(u, v), local.z));
                    int holesResolution = terrainData.holesResolution;
                    bool hole = holesResolution > 0 && terrainData.IsHole(
                        Mathf.Clamp((int)(u * holesResolution), 0, holesResolution - 1),
                        Mathf.Clamp((int)(v * holesResolution), 0, holesResolution - 1));
                    terrainSamples.Add(new { x, z, groundY = ground.y, wallBottomY = target.bounds.min.y + .01f,
                        groundAboveWallBottom = ground.y - (target.bounds.min.y + .01f), hole });
                }
            }
            if (c is MeshCollider && target.name == "NR12_CORRIDOR_FLOOR_CONTEXT")
            {
                float[] xs = { 1996.0f, 1998.2f, 2000.4f };
                float[] zs = { 2461.0f, 2470.0f, 2479.0f };
                foreach (float x in xs) foreach (float z in zs)
                {
                    bool hit = c.Raycast(new Ray(new Vector3(x, 265, z), Vector3.down), out RaycastHit hitInfo, 60);
                    meshSamples.Add(new { x, z, hit, surfaceY = hit ? (float?)hitInfo.point.y : null,
                        normalY = hit ? (float?)hitInfo.normal.y : null });
                }
            }
            return new { name = c.name, hierarchy = PathOf(c.transform), type = c.GetType().Name,
                colliderEnabled = c.enabled, active = c.gameObject.activeInHierarchy,
                terrainData = terrainData ? AssetDatabase.GetAssetPath(terrainData) : null,
                terrainSamples, meshSamples };
        }).ToArray();
        overlap.Add(new { target = target.name, candidates = matches.Count, colliderBoundsIntersections = colliderMatches.Length,
            candidatesList = matches, colliderDetails });
        overlapSummary.Add(new { target = target.name, candidates = matches.Count, enabledMatches,
            colliderBoundsIntersections = colliderMatches.Length, colliderTypes = colliderMatches.Select(c => c.GetType().Name).ToArray() });
    }
    var report = new { scope = "read-only preview of saved formal scene; AABB intersections are candidates, not proof of duplicate geometry",
        originalScene, originalSceneDirty = originalDirty, formalScene = scenePath,
        previewSceneDirty = preview.isDirty, rootCount = roots.Length, meshRendererCount = renderers.Length,
        colliderCount = colliders.Length, roots = roots.Select(r => r.name).ToArray(), overlap };
    string serialized = (string)jsonType.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
    System.IO.File.WriteAllText(System.IO.Path.Combine(root, "NR12WallBatchPilot/ExistingSceneOverlap.json"), serialized);
    return new { report.scope, report.originalScene, report.originalSceneDirty, report.previewSceneDirty,
        report.rootCount, report.meshRendererCount, report.colliderCount, overlapSummary,
        reportPath = "SourceData/Models/LR06_20261027/NR12WallBatchPilot/ExistingSceneOverlap.json" };
}
finally
{
    UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
    if (LightmapSettings.lightProbes != originalProbes) LightmapSettings.lightProbes = originalProbes;
    var currentMaps = LightmapSettings.lightmaps;
    if (currentMaps.Length != originalLightmaps.Length || currentMaps.Where((m, i) => i < originalLightmaps.Length &&
        (m.lightmapColor != originalLightmaps[i].lightmapColor || m.lightmapDir != originalLightmaps[i].lightmapDir ||
         m.shadowMask != originalLightmaps[i].shadowMask)).Any())
        LightmapSettings.lightmaps = originalLightmaps;
    if (LightmapSettings.lightmapsMode != originalLightmapsMode) LightmapSettings.lightmapsMode = originalLightmapsMode;
    if (RenderSettings.skybox != originalSky) RenderSettings.skybox = originalSky;
    if (originalSky && EditorJsonUtility.ToJson(originalSky) != skyJson)
    {
        EditorJsonUtility.FromJsonOverwrite(skyJson, originalSky);
        if (!skyDirty) EditorUtility.ClearDirty(originalSky);
    }
    Need(active.path == originalScene && active.isDirty == originalDirty, "Original active scene changed during inspection");
}
