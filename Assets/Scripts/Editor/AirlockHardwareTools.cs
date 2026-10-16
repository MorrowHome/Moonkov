using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

// Imports only the separately delivered static hardware; never changes door/room contracts.
public static class AirlockHardwareTools
{
    public const string Folder = AirlockApollo12Tools.Folder + "/Hardware";
    public const string PrefabPath = Folder + "/EVA_AirHardware.prefab";
    [Serializable] public class Primitive
    { public AirlockCloudDriveTools.V[] vertices, normals; public AirlockCloudDriveTools.UV[] uv; public int[] triangles; public int material; }
    [Serializable] public class Node
    { public int index, parent; public string name; public AirlockCloudDriveTools.V position, scale; public AirlockCloudDriveTools.Q rotation; public Primitive[] primitives; }
    [Serializable] public class MaterialSpec
    { public string name, baseColorTexture; public float[] color; public float metallic, roughness; }
    [Serializable] public class Geometry { public Node[] nodes; public MaterialSpec[] materials; }
    [Serializable] public class Spec { public MeshSpec[] meshes; }
    [Serializable] public class MeshSpec { public string mesh; public float[][] bounds_core_local_Blender_XYZ_m; }
    [Serializable] class BindingRecord
    { public string prefab; public int meshCount, reusedMaterials; public float maxModuleBoundsDeltaM; public bool audioAnchorPosesUnchanged; }
    static T Read<T>(string path)
    {
        var json = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        return (T)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null, new object[] { File.ReadAllText(path), typeof(T) });
    }
    public static string Bind()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stable edit mode required");
        for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved user scene");
        if (File.Exists(PrefabPath)) return "Existing hardware retained: " + PrefabPath;
        var geometry = Read<Geometry>("Temp/DOT00_HardwareHandoff/native_hardware.json");
        var spec = Read<Spec>("Temp/DOT00_HardwareHandoff/unpacked/EVA_Air_Hardware_Increment.json");
        if (geometry.nodes.Count(n => n.primitives.Length > 0) != 3) throw new InvalidOperationException("Hardware package must contain three mesh nodes");
        Directory.CreateDirectory(Folder + "/Meshes"); Directory.CreateDirectory(Folder + "/Materials"); AssetDatabase.Refresh();
        var materials = new Material[geometry.materials.Length]; int reused = 0;
        var existing = AssetDatabase.FindAssets("t:Material", new[] { "Assets/MoonEnvironment" }).Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>).Where(m => m).ToArray();
        for (int i = 0; i < materials.Length; i++)
        {
            var m = geometry.materials[i];
            materials[i] = existing.FirstOrDefault(e => e.name == m.name);
            if (materials[i]) { reused++; continue; }
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = m.name };
            material.SetColor("_BaseColor", new Color(m.color[0], m.color[1], m.color[2], m.color[3]));
            material.SetFloat("_Metallic", m.metallic); material.SetFloat("_Smoothness", 1 - m.roughness);
            if (!string.IsNullOrEmpty(m.baseColorTexture))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(m.baseColorTexture);
                if (!importer) throw new InvalidOperationException("Missing hardware texture " + m.baseColorTexture);
                importer.sRGBTexture = true; importer.SaveAndReimport();
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(m.baseColorTexture));
            }
            material.SetColor("_EmissionColor", Color.black); material.DisableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(material, Folder + "/Materials/Hardware_" + i.ToString("00") + ".mat"); materials[i] = material;
        }
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(AirlockApollo12Tools.ScenePath, OpenSceneMode.Additive);
        try
        {
            var owner = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AirlockController>(true)).Single();
            var anchors = owner.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_AudioAnchor", StringComparison.Ordinal))
                .ToDictionary(t => t.name, t => t.localToWorldMatrix);
            if (owner.transform.Find("EVA_EAST_01_AirHardwareRoot")) throw new InvalidOperationException("Hardware already installed");
            var nodes = geometry.nodes.ToDictionary(n => n.index, n => new GameObject(n.name));
            foreach (var node in geometry.nodes)
            {
                var go = nodes[node.index];
                go.transform.SetParent(node.parent < 0 ? owner.transform : nodes[node.parent].transform, false);
                go.transform.localPosition = node.position.Value; go.transform.localRotation = node.rotation.Value; go.transform.localScale = node.scale.Value;
                if (node.primitives.Length == 0) continue;
                var mesh = new UnityEngine.Mesh { name = node.name }; var vertices = new List<Vector3>(); var normals = new List<Vector3>();
                var uv = new List<Vector2>(); var triangles = new List<int[]>();
                foreach (var primitive in node.primitives)
                {
                    int start = vertices.Count; vertices.AddRange(primitive.vertices.Select(v => v.Value)); normals.AddRange(primitive.normals.Select(v => v.Value));
                    uv.AddRange(primitive.uv.Select(v => new Vector2(v.x, v.y))); triangles.Add(primitive.triangles.Select(v => v + start).ToArray());
                }
                mesh.vertices = vertices.ToArray(); mesh.normals = normals.ToArray(); mesh.uv = uv.ToArray(); mesh.subMeshCount = triangles.Count;
                for (int i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, Folder + "/Meshes/" + node.name + ".asset");
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = node.primitives.Select(p => materials[p.material]).ToArray();
                // Static display hardware stays behind the clear passage; no new physics owner.
            }
            var root = nodes[geometry.nodes.Single(n => n.parent < 0).index];
            root.transform.localPosition = new Vector3(1.89f, 0, -1.69f); root.transform.localRotation = Quaternion.identity; root.transform.localScale = Vector3.one;
            float maxError = 0;
            foreach (var item in spec.meshes)
            {
                var filter = root.GetComponentsInChildren<MeshFilter>().Single(f => f.name == item.mesh);
                var matrix = owner.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var points = filter.sharedMesh.vertices.Select(v => matrix.MultiplyPoint3x4(v)).ToArray();
                var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max); var expected = item.bounds_core_local_Blender_XYZ_m;
                var expectedMin = new Vector3(expected[0][0] - 20, expected[0][2] - .6f, expected[0][1] + 28.76f);
                var expectedMax = new Vector3(expected[1][0] - 20, expected[1][2] - .6f, expected[1][1] + 28.76f);
                maxError = Mathf.Max(maxError, Vector3.Distance(min, expectedMin), Vector3.Distance(max, expectedMax));
            }
            if (maxError > .002f) throw new InvalidOperationException("Hardware module-local bounds mismatch " + maxError);
            bool unchanged = anchors.All(a => owner.GetComponentsInChildren<Transform>(true).Single(t => t.name == a.Key).localToWorldMatrix == a.Value);
            if (!unchanged) throw new InvalidOperationException("Hardware import changed audio anchor pose");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText("Temp/MoonAirlockIntegration/DOT00_hardware_bind.json", JsonUtility.ToJson(new BindingRecord {
                prefab = PrefabPath, meshCount = 3, reusedMaterials = reused, maxModuleBoundsDeltaM = maxError, audioAnchorPosesUnchanged = unchanged }, true));
            return "Three static hardware meshes bound in map copy; existing material reuse " + reused + "; bounds error " + maxError + "m; audio anchors unchanged";
        }
        finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
    }
}
