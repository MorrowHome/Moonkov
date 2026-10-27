using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LR06VerifyFullPreviewScene
{
    [Serializable] public class SunRow { public float[] direction_unity, color_linear; public float unity_intensity; public bool soft_shadows; }
    [Serializable] public class PointRow { public string name; public float[] position_unity, source_color_linear; public float intensity, range_metres; public bool shadows; }
    [Serializable] public class Plan { public SunRow source_sun; public PointRow[] point_lights; }
    static bool MatchesColor(Color actual, float[] linear)
    {
        var expected = new Color(linear[0], linear[1], linear[2]).gamma;
        return Mathf.Abs(actual.r - expected.r) < .001f && Mathf.Abs(actual.g - expected.g) < .001f && Mathf.Abs(actual.b - expected.b) < .001f;
    }
    public static object Run()
    {
        const string path = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new Exception("Editor busy");
        var original = SceneManager.GetActiveScene();
        string originalPath = original.path;
        bool originalDirty = original.isDirty;
        var scene = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new Exception("Saved preview scene did not reopen");
            var roots = scene.GetRootGameObjects();
            var filters = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true)).Where(f => f.sharedMesh).ToArray();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            var terrains = roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).ToArray();
            var colliders = roots.SelectMany(r => r.GetComponentsInChildren<Collider>(true)).ToArray();
            var lights = roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToArray();
            var cameras = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).ToArray();
            int triangles = filters.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount)
                .Sum(i => (int)(f.sharedMesh.GetIndexCount(i) / 3)));
            var missing = renderers.Where(r => !r.sharedMaterial || r.sharedMaterials.Any(m => !m || !m.shader)).Select(r => r.name).ToArray();
            var points = lights.Where(l => l.type == LightType.Point).ToDictionary(l => l.name, StringComparer.Ordinal);
            var sun = lights.SingleOrDefault(l => l.type == LightType.Directional);
            if (filters.Length != 65 || renderers.Length != 65 || terrains.Length != 2 || colliders.Length != 0 ||
                lights.Length != 32 || points.Count != 31 || points.Values.Any(l => l.shadows != LightShadows.None) ||
                !sun || sun.shadows != LightShadows.Soft || cameras.Length != 1 || missing.Length != 0)
                throw new Exception("Saved preview has missing or unexpected scene references");
            var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var plan = (Plan)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
                new object[] { System.IO.File.ReadAllText("SourceData/Models/LR06_20261027/DisplayLightCalibrationPlan.json"), typeof(Plan) });
            if (plan == null || plan.point_lights.Length != 31 || plan.point_lights.Any(row =>
                !points.ContainsKey(row.name) || Vector3.Distance(points[row.name].transform.position,
                    new Vector3(row.position_unity[0], row.position_unity[1], row.position_unity[2])) > .001f ||
                !Mathf.Approximately(points[row.name].intensity, row.intensity) || !Mathf.Approximately(points[row.name].range, row.range_metres) ||
                !MatchesColor(points[row.name].color, row.source_color_linear)))
                throw new Exception("Saved point lights differ from calibration plan");
            var direction = plan.source_sun.direction_unity;
            if (Vector3.Distance(sun.transform.forward, new Vector3(direction[0], direction[1], direction[2]).normalized) > .001f ||
                !MatchesColor(sun.color, plan.source_sun.color_linear) || !Mathf.Approximately(sun.intensity, plan.source_sun.unity_intensity))
                throw new Exception("Saved sun direction differs from source calibration");
            var report = new { status = "saved_scene_reopened_in_preview", scene = path, rootCount = roots.Length,
                meshFilters = filters.Length, meshRenderers = renderers.Length, triangles,
                terrainAssets = terrains.Select(t => AssetDatabase.GetAssetPath(t.terrainData)).ToArray(),
                colliders = colliders.Length, lights = lights.Length, warmPointProxies = points.Count,
                pointShadowLights = points.Values.Count(l => l.shadows != LightShadows.None),
                directionalShadowLights = lights.Count(l => l.type == LightType.Directional && l.shadows != LightShadows.None),
                cameras = cameras.Length, missingRenderers = missing, sceneDirty = scene.isDirty,
                originalScene = originalPath, originalSceneDirty = originalDirty };
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06PBR/UnityLightingReopen.json", serialized);
            return report;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (original.path != originalPath || original.isDirty != originalDirty) throw new Exception("Original scene changed during reopen check");
        }
    }
}
