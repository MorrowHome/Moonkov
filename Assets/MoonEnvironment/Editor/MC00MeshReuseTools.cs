using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Exact mesh reuse. The original builder wrapper targets MC00; editor menus
    /// can process any selected hierarchy. The FBX exports many
    /// identical meshes under separate names, preventing GPU instancing. Only
    /// MeshFilter references change: transforms, renderers, materials, colliders
    /// and detachable object hierarchy remain authored and independently editable.
    /// </summary>
    public static class MC00MeshReuseTools
    {
        [Serializable]
        public sealed class Result
        {
            public int meshFilters, uniqueBefore, uniqueAfter, referencesReused;
        }

        public static Result OptimizeHierarchy(GameObject buildings)
        {
            var roots = buildings.GetComponentsInChildren<Transform>(true).Where(t => t.name == "MC00_ROOT").ToArray();
            if (roots.Length != 1) throw new InvalidOperationException("Expected exactly one MC00_ROOT in the candidate.");
            return OptimizeSubtree(roots[0].gameObject);
        }

        public static Result OptimizeSubtree(GameObject root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<MeshRenderer>()).ToArray();
            var meshes = filters.Select(f => f.sharedMesh).Distinct().ToArray();
            var replacements = new Dictionary<Mesh, Mesh>();
            var canonical = new Dictionary<string, Mesh>();
            using (var data = MeshUtility.AcquireReadOnlyMeshData(meshes))
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    var mesh = meshes[i];
                    // Animated mesh payloads are outside this static-geometry pass.
                    if (mesh.blendShapeCount != 0 || mesh.bindposes.Length != 0) continue;
                    string signature = Fingerprint(mesh, data[i]);
                    if (canonical.TryGetValue(signature, out var shared)) replacements.Add(mesh, shared);
                    else canonical.Add(signature, mesh);
                }
            }
            var result = new Result { meshFilters = filters.Length, uniqueBefore = meshes.Length };
            foreach (var filter in filters)
            {
                if (!replacements.TryGetValue(filter.sharedMesh, out var mesh)) continue;
                Undo.RecordObject(filter, "Reuse identical building mesh");
                filter.sharedMesh = mesh;
                PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
                result.referencesReused++;
            }
            result.uniqueAfter = filters.Select(f => f.sharedMesh).Distinct().Count();
            return result;
        }

        private static string Fingerprint(Mesh mesh, Mesh.MeshData data)
        {
            using (var hash = SHA256.Create())
            {
                // Include layout and culling metadata, not only positions. Different
                // normals/UVs/tangents/topologies/bounds must never share a mesh.
                var header = new StringBuilder().Append(mesh.vertexCount).Append('|').Append(mesh.indexFormat).Append('|').Append(mesh.subMeshCount);
                AppendBounds(header, mesh.bounds);
                foreach (var a in mesh.GetVertexAttributes())
                    header.Append('|').Append(a.attribute).Append(':').Append(a.format).Append(':').Append(a.dimension).Append(':').Append(a.stream);
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var sub = data.GetSubMesh(s);
                    header.Append('|').Append(sub.indexStart).Append(':').Append(sub.indexCount).Append(':').Append(sub.baseVertex)
                        .Append(':').Append(sub.topology).Append(':').Append(sub.firstVertex).Append(':').Append(sub.vertexCount);
                    AppendBounds(header, sub.bounds);
                }
                byte[] bytes = Encoding.UTF8.GetBytes(header.ToString());
                hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
                for (int s = 0; s < data.vertexBufferCount; s++)
                {
                    bytes = data.GetVertexData<byte>(s).ToArray();
                    // Prefix lengths so different stream boundaries cannot collide.
                    byte[] length = BitConverter.GetBytes(bytes.Length);
                    hash.TransformBlock(length, 0, length.Length, null, 0);
                    hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
                bytes = data.GetIndexData<byte>().ToArray();
                hash.TransformFinalBlock(bytes, 0, bytes.Length);
                return Convert.ToBase64String(hash.Hash);
            }
        }

        private static void AppendBounds(StringBuilder text, Bounds bounds)
        {
            for (int i = 0; i < 3; i++) text.Append('|').Append(bounds.center[i].ToString("R", CultureInfo.InvariantCulture));
            for (int i = 0; i < 3; i++) text.Append('|').Append(bounds.extents[i].ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
