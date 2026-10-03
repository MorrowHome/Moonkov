using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS.Client;

public static class MoonkovMoonDisplayBuilder
{
    private const string Folder = "Assets/UI Toolkit/GameUI/3D";
    private const int SamplesU = 1024, SamplesV = 512, SegmentsU = 256, SegmentsV = 128;
    private struct SourceVertex { public Vector3 Position, Normal; public Vector4 Tangent; public Vector2 UV; }

    [MenuItem("Tools/Moonkov/Rebuild Interactive Moon")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Rebuild the display moon in Edit mode.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FBX/Moon/Moon_NASA_LRO_23k_Topo_Unity.fbx");
        var sourceMesh = source.GetComponentInChildren<MeshFilter>().sharedMesh;
        int columns = SamplesU + 1, rows = SamplesV + 1;
        var samples = new SourceVertex[columns * rows];
        var errors = new float[samples.Length];
        for (int i = 0; i < errors.Length; i++) errors[i] = float.PositiveInfinity;
        using (var array = UnityEngine.Mesh.AcquireReadOnlyMeshData(sourceMesh))
        {
            var data = array[0];
            if (data.GetVertexBufferStride(0) != 48) throw new InvalidOperationException("Source vertex layout changed; preserve the existing display assets.");
            var vertices = data.GetVertexData<SourceVertex>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                float u = Mathf.Clamp01(vertex.UV.x) * SamplesU, v = Mathf.Clamp01(vertex.UV.y) * SamplesV;
                int x = Mathf.RoundToInt(u), y = Mathf.RoundToInt(v), index = y * columns + x;
                float distance = (u - x) * (u - x) + (v - y) * (v - y);
                if (distance < errors[index]) { errors[index] = distance; samples[index] = vertex; }
            }
        }
        // Only polar cells can lack a source vertex; propagate the nearest adjacent sample.
        for (int pass = 0; pass < 8; pass++)
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                int i = y * columns + x;
                if (!float.IsPositiveInfinity(errors[i])) continue;
                foreach (int adjacent in new[] { y * columns + (x + 1) % columns, y * columns + (x + columns - 1) % columns, Mathf.Max(0, y - 1) * columns + x, Mathf.Min(rows - 1, y + 1) * columns + x })
                    if (!float.IsPositiveInfinity(errors[adjacent])) { samples[i] = samples[adjacent]; errors[i] = 1; break; }
            }
        for (int i = 0; i < samples.Length; i++)
            if (float.IsPositiveInfinity(errors[i])) throw new InvalidOperationException("Source spherical UV coverage is incomplete.");

        var positions = new Vector3[(SegmentsU + 1) * (SegmentsV + 1)];
        var normals = new Vector3[positions.Length];
        var tangents = new Vector4[positions.Length];
        var uvs = new Vector2[positions.Length];
        float radius = Mathf.Max(sourceMesh.bounds.extents.x, sourceMesh.bounds.extents.y, sourceMesh.bounds.extents.z);
        for (int y = 0; y <= SegmentsV; y++) for (int x = 0; x <= SegmentsU; x++)
        {
            int i = y * (SegmentsU + 1) + x;
            float u = (float)x / SegmentsU, v = (float)y / SegmentsV;
            Basis(u, v, out var radial, out var tangent, out _);
            float height = samples[(y * (SamplesV / SegmentsV)) * columns + x * (SamplesU / SegmentsU)].Position.magnitude / radius;
            positions[i] = radial * height; normals[i] = radial;
            tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, -1);
            uvs[i] = new Vector2(u, v);
        }
        for (int y = 0; y <= SegmentsV; y++) positions[y * (SegmentsU + 1) + SegmentsU] = positions[y * (SegmentsU + 1)];
        for (int x = 1; x <= SegmentsU; x++) { positions[x] = positions[0]; positions[SegmentsV * (SegmentsU + 1) + x] = positions[SegmentsV * (SegmentsU + 1)]; }
        var triangles = new int[SegmentsU * SegmentsV * 6];
        int offset = 0;
        for (int y = 0; y < SegmentsV; y++) for (int x = 0; x < SegmentsU; x++)
        {
            int a = y * (SegmentsU + 1) + x, b = a + 1, c = a + SegmentsU + 1, d = c + 1;
            triangles[offset++] = a; triangles[offset++] = b; triangles[offset++] = c;
            triangles[offset++] = b; triangles[offset++] = d; triangles[offset++] = c;
        }
        // Increasing U,V is inward in the source's Z-up UV convention.
        for (int i = 0; i < triangles.Length; i += 3) { int temp = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = temp; }
        var mesh = new UnityEngine.Mesh { name = "NASA moon / menu LOD" };
        mesh.vertices = positions; mesh.normals = normals; mesh.tangents = tangents; mesh.uv = uvs; mesh.triangles = triangles; mesh.RecalculateBounds();
        Directory.CreateDirectory(Folder);
        SaveAsset(mesh, Folder + "/MoonDisplayMesh.asset");
        var normalPixels = new Color32[SamplesU * SamplesV];
        for (int y = 0; y < SamplesV; y++) for (int x = 0; x < SamplesU; x++)
        {
            Basis((float)x / SamplesU, (float)y / SamplesV, out var radial, out var tangent, out var bitangent);
            var normal = samples[y * columns + x].Normal.normalized;
            var local = new Vector3(Vector3.Dot(normal, tangent), Vector3.Dot(normal, bitangent), Vector3.Dot(normal, radial)).normalized;
            normalPixels[y * SamplesU + x] = new Color(local.x * .5f + .5f, local.y * .5f + .5f, local.z * .5f + .5f, 1);
        }
        var image = new Texture2D(SamplesU, SamplesV, TextureFormat.RGB24, false, true);
        try { image.SetPixels32(normalPixels); image.Apply(); File.WriteAllBytes(Folder + "/MoonDisplayNormals.png", image.EncodeToPNG()); }
        finally { UnityEngine.Object.DestroyImmediate(image); }
        AssetDatabase.ImportAsset(Folder + "/MoonDisplayNormals.png", ImportAssetOptions.ForceSynchronousImport);
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(Folder + "/MoonDisplayNormals.png");
        normalImporter.textureType = TextureImporterType.NormalMap; normalImporter.sRGBTexture = false; normalImporter.wrapModeU = TextureWrapMode.Repeat; normalImporter.wrapModeV = TextureWrapMode.Clamp; normalImporter.SaveAndReimport();
        var material = new Material(Shader.Find("Moonkov/Lunar Display")) { name = "Lunar display / direct sunlight" };
        material.SetTexture("_BaseMap", source.GetComponentInChildren<Renderer>().sharedMaterial.mainTexture);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/MoonDisplayNormals.png"));
        SaveAsset(material, Folder + "/MoonDisplayMaterial.mat");
        var settings = ScriptableObject.CreateInstance<MoonDisplaySettings>();
        settings.Mesh = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(Folder + "/MoonDisplayMesh.asset");
        settings.Material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/MoonDisplayMaterial.mat");
        settings.SourceOrientation = source.transform.localRotation;
        SaveAsset(settings, "Assets/Resources/Moonkov/MoonDisplaySettings.asset");
        AssetDatabase.SaveAssets();
        MoonkovBackgroundBaker.BakeStarfield();
        Debug.Log("Built interactive NASA moon: " + positions.Length + " vertices / " + triangles.Length / 3 + " triangles; source normals retained in a 1024x512 normal map.");
    }

    private static void Basis(float u, float v, out Vector3 radial, out Vector3 tangent, out Vector3 bitangent)
    {
        float longitude = u * Mathf.PI * 2, latitude = (v - .5f) * Mathf.PI;
        float c = Mathf.Cos(longitude), s = Mathf.Sin(longitude), cl = Mathf.Cos(latitude), sl = Mathf.Sin(latitude);
        radial = new Vector3(c * cl, -s * cl, sl); tangent = new Vector3(-s, -c, 0); bitangent = new Vector3(-c * sl, s * sl, cl);
    }
    private static void SaveAsset(UnityEngine.Object asset, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (existing != null) { EditorUtility.CopySerialized(asset, existing); UnityEngine.Object.DestroyImmediate(asset); }
        else AssetDatabase.CreateAsset(asset, path);
    }
}
