using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LR06VerifyFullPreviewScene
{
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
            if (filters.Length != 65 || renderers.Length != 65 || terrains.Length != 2 || colliders.Length != 0 ||
                lights.Length != 32 || lights.Any(l => l.shadows != LightShadows.None) || cameras.Length != 1 || missing.Length != 0)
                throw new Exception("Saved preview has missing or unexpected scene references");
            var report = new { status = "saved_scene_reopened_in_preview", scene = path, rootCount = roots.Length,
                meshFilters = filters.Length, meshRenderers = renderers.Length, triangles,
                terrainAssets = terrains.Select(t => AssetDatabase.GetAssetPath(t.terrainData)).ToArray(),
                colliders = colliders.Length, lights = lights.Length, shadowLights = lights.Count(l => l.shadows != LightShadows.None),
                cameras = cameras.Length, missingRenderers = missing, sceneDirty = scene.isDirty,
                originalScene = originalPath, originalSceneDirty = originalDirty };
            var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06FullPreview/UnitySceneReopen.json", serialized);
            return report;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (original.path != originalPath || original.isDirty != originalDirty) throw new Exception("Original scene changed during reopen check");
        }
    }
}
