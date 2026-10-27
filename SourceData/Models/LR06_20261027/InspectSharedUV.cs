// Measure UV coverage and density for the seven shared materials on imported meshes.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LR06InspectSharedUV
{
    [Serializable] public class MaterialRow { public string name; public int index; }
    [Serializable] public class Plan { public MaterialRow[] materials; }
    sealed class Stats { public string name; public int triangles, badUvTriangles; public double area, badUvArea, uvArea; }
    public static object Run()
    {
        const string folder = "Assets/MoonEnvironment/Variants/LR06FullPreview";
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new Exception("Editor busy");
        var original = SceneManager.GetActiveScene(); string originalPath = original.path; bool originalDirty = original.isDirty;
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var plan = (Plan)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { System.IO.File.ReadAllText("SourceData/Models/LR06_20261027/SharedPBRPlan.json"), typeof(Plan) });
        var stats = plan.materials.ToDictionary(row => AssetDatabase.LoadAssetAtPath<Material>(folder + "/Materials/LR06_" + row.index.ToString("D3") + ".mat"), row => new Stats { name = row.name });
        var scene = EditorSceneManager.OpenPreviewScene(folder + "/Scenes/LR06_Full_Display.unity");
        try
        {
            var perRenderer = new List<object>();
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)))
            {
                var slots = renderer.sharedMaterials;
                if (!slots.Any(material => material && stats.ContainsKey(material))) continue;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var vertices = mesh.vertices; var uv = mesh.uv;
                var transform = renderer.transform.localToWorldMatrix;
                for (int slot = 0; slot < slots.Length && slot < mesh.subMeshCount; slot++)
                {
                    if (!slots[slot] || !stats.TryGetValue(slots[slot], out var s)) continue;
                    var indices = mesh.GetTriangles(slot);
                    double localArea = 0, localBadArea = 0;
                    int localBadCount = 0;
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        int a = indices[i], b = indices[i+1], c = indices[i+2];
                        var ab = transform.MultiplyVector(vertices[b] - vertices[a]);
                        var ac = transform.MultiplyVector(vertices[c] - vertices[a]);
                        double area = Vector3.Cross(ab, ac).magnitude * .5;
                        double uvArea = uv.Length == vertices.Length ? Math.Abs((uv[b].x-uv[a].x)*(uv[c].y-uv[a].y) -
                            (uv[b].y-uv[a].y)*(uv[c].x-uv[a].x)) * .5 : 0;
                        s.triangles++; s.area += area; s.uvArea += uvArea;
                        localArea += area;
                        if (uvArea < 1e-12) { s.badUvTriangles++; s.badUvArea += area; localBadCount++; localBadArea += area; }
                    }
                    perRenderer.Add(new { renderer = renderer.name, material = s.name, triangles = indices.Length/3, area = localArea,
                        badUvTriangles = localBadCount, badUvArea = localBadArea, badUvFraction = localArea > 0 ? localBadArea/localArea : 0 });
                }
            }
            var rows = stats.Values.Select(s => new { s.name, s.triangles, surfaceAreaM2 = s.area, s.badUvTriangles,
                degenerateUvAreaM2 = s.badUvArea, degenerateUvAreaFraction = s.area > 0 ? s.badUvArea / s.area : 0,
                rmsTexelsPerMetreAt1024 = s.area > 0 ? 1024 * Math.Sqrt(s.uvArea / s.area) : 0 }).ToArray();
            var report = new { status = "uv_coverage_measured", materials = rows, perRenderer, note = "Area-weighted density; 1024-pixel texture; no claim of per-object density." };
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06PBR/UnityUVReport.json", serialized);
            return new { report.status, report.materials, rendererMaterialPairs = perRenderer.Count };
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (original.path != originalPath || original.isDirty != originalDirty) throw new Exception("Original scene changed");
        }
    }
}
