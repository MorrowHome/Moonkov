using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.MP_FPS.Moon.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GL125SurfaceCleanup
{
    private const string Formal = SourceWorldSceneInstall.ScenePath;

    [Serializable] private sealed class Chunk
    {
        public string name;
        public int vertices;
        public Slot[] slots;
    }

    [Serializable] private sealed class Slot
    {
        public int slot, count;
        public int[] remove;
        public string before, after;
    }

    [Serializable] private sealed class Manifest
    {
        public int version, removed, mixed;
        public string worldHash;
        public Chunk[] chunks;
    }

    public static string Install(string manifestPath)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Unity Editor in Edit Mode.");
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
        string revision = File.ReadAllText(GL125WorldAssets.Root + "/ImportSource.txt").Trim();
        if (manifest.version != 1 || manifest.worldHash != revision || manifest.removed <= 0 ||
            manifest.chunks.Sum(c => c.slots.Sum(slot => slot.remove.Length)) != manifest.removed)
            throw new InvalidDataException("GL125 surface cleanup does not match the installed map.");

        var scene = SceneManager.GetSceneByPath(Formal);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Formal, OpenSceneMode.Additive);
        try
        {
            var world = scene.GetRootGameObjects().Single(root => root.name == "GL125 World");
            var targets = world.GetComponentsInChildren<MeshFilter>(true)
                .ToLookup(filter => filter.name);
            var colliders = world.GetComponentsInChildren<MeshCollider>(true).Select(c => c.sharedMesh).ToHashSet();
            var plans = new List<(Mesh Mesh, int Slot, int[] Indices)>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            int removed = 0;
            foreach (var chunk in manifest.chunks)
            {
                var mesh = targets[chunk.name].Single().sharedMesh;
                string path = AssetDatabase.GetAssetPath(mesh);
                if (!mesh || !mesh.isReadable || mesh.vertexCount != chunk.vertices || colliders.Contains(mesh) ||
                    !path.StartsWith(GL125WorldAssets.Root + "/Meshes/", StringComparison.Ordinal))
                    throw new InvalidDataException("Unexpected renderer mesh " + chunk.name);
                foreach (var row in chunk.slots)
                {
                    var indices = mesh.GetTriangles(row.slot);
                    string fingerprint = Fingerprint(indices);
                    if (fingerprint == row.after) continue;
                    if (indices.Length != row.count * 3 ||
                        fingerprint != row.before ||
                        row.remove.Distinct().Count() != row.remove.Length ||
                        row.remove.Any(face => face < 0 || face >= row.count))
                        throw new InvalidDataException("Triangle layout differs for " + chunk.name);
                    var duplicates = row.remove.ToHashSet();
                    var result = new int[indices.Length - duplicates.Count * 3];
                    int destination = 0;
                    for (int face = 0; face < row.count; face++)
                    {
                        if (duplicates.Contains(face)) continue;
                        Array.Copy(indices, face * 3, result, destination, 3);
                        destination += 3;
                    }
                    if (Fingerprint(result) != row.after)
                        throw new InvalidDataException("Unexpected cleanup output for " + chunk.name);
                    plans.Add((mesh, row.slot, result));
                    paths.Add(path);
                    removed += duplicates.Count;
                }
            }

            if (plans.Count == 0)
                return $"Verified {manifest.removed} removed faces across {manifest.chunks.Length} saved render chunks.";
            string directory = Path.GetDirectoryName(manifestPath);
            string backup = Path.Combine(directory, "MeshesBeforeOverlapCleanup");
            Directory.CreateDirectory(backup);
            foreach (var path in paths)
            {
                string copy = Path.Combine(backup, Path.GetFileName(path));
                if (!File.Exists(copy)) File.Copy(path, copy);
            }
            foreach (var plan in plans)
            {
                plan.Mesh.SetTriangles(plan.Indices, plan.Slot, false);
                EditorUtility.SetDirty(plan.Mesh);
            }
            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            File.WriteAllLines(Path.Combine(directory, "SurfaceCleanupPaths.txt"), paths.OrderBy(path => path, StringComparer.Ordinal));
            return $"Removed {removed} coincident triangles from {paths.Count} mesh bundles. " +
                $"The stage includes {manifest.mixed} reviewed same-sided overlaps of different finishes.";
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static string Fingerprint(int[] indices)
    {
        var bytes = new byte[indices.Length * sizeof(int)];
        Buffer.BlockCopy(indices, 0, bytes, 0, bytes.Length);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
