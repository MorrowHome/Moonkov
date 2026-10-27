// Reimport only the selected independent LR06 display FBX assets.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LR06ImportRepairedDisplayFBX
{
    public static object Map29() => Import(new[] { 29 });
    public static object Remaining() => Import(new[] { 22, 23, 27, 30, 31 });
    static object Import(int[] parts)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) throw new Exception("Editor busy");
        var results = parts.Select(part =>
        {
            string path = "Assets/MoonEnvironment/Variants/LR06FullPreview/Models/Map_" + part.ToString("D2") + ".fbx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Single();
            if (!importer || !mesh || importer.importTangents != ModelImporterTangents.CalculateMikk ||
                !mesh.HasVertexAttribute(VertexAttribute.TexCoord0) || !mesh.HasVertexAttribute(VertexAttribute.Tangent))
                throw new Exception("Reimported FBX missing UV0 or Mikk tangents: " + path);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId);
            int triangles = Enumerable.Range(0, mesh.subMeshCount).Sum(i => (int)mesh.GetIndexCount(i)/3);
            return new { part, path, guid, localId, vertices = mesh.vertexCount, triangles, submeshes = mesh.subMeshCount,
                uvFormat = mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord0).ToString(), tangentMode = importer.importTangents.ToString() };
        }).ToArray();
        return new { status = "display_fbx_reimported", parts = results };
    }
}
