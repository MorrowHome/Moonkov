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

public static class GL125CraterInstall
{
    private const string Formal = SourceWorldSceneInstall.ScenePath;
    private const string Geology = GL125WorldAssets.Root + "/Geology";

    [Serializable] private sealed class Layout
    {
        public string worldHash;
        public float[] faciesRect;
        public Crater[] craters;
    }

    [Serializable] private sealed class Crater
    {
        public float x, z, radius, freshness;
        public Vector2 Center => new Vector2(x, z);

        // The asymmetric bowl, raised rim and soft ejecta edge follow MoonSurfaceDetail.
        public float Height(Vector2 delta)
        {
            float radial = delta.magnitude / radius;
            if (radial >= 1.8f) return 0;
            float angle = Mathf.Atan2(delta.y, delta.x);
            float skew = 1 + .07f * Mathf.Sin(angle * 3 + x) + .035f * Mathf.Sin(angle * 7);
            float r = radial / skew;
            float basin = Mathf.Max(0, 1 - r * r);
            float rimDistance = (r - 1) / Mathf.Lerp(.3f, .14f, freshness);
            float rim = Mathf.Exp(-rimDistance * rimDistance) * Mathf.Lerp(.24f, .48f, freshness);
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.4f, 1.8f, Mathf.Max(r, radial)));
            return (-basin * basin + rim) * fade * radius * Mathf.Lerp(.085f, .113f, freshness);
        }
    }

    private sealed class MeshEdit
    {
        public Mesh Mesh;
        public MeshCollider Collider;
        public Vector3[] OriginalPositions, OriginalNormals, Positions, Normals;
    }

    public static string Install(string prepared, string stage)
    {
        var scene = SceneManager.GetSceneByPath(Formal);
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            BuildPipeline.isBuildingPlayer || scene.isDirty)
            throw new InvalidOperationException("Use an idle Unity Editor with a clean formal scene.");

        var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(Path.Combine(stage, "CraterLayout.json")));
        var revision = File.ReadAllText(GL125WorldAssets.Root + "/ImportSource.txt").Trim();
        using (var worldFile = File.OpenRead(Path.Combine(prepared, "World.json")))
        using (var sha = SHA256.Create())
        {
            var hash = BitConverter.ToString(sha.ComputeHash(worldFile)).Replace("-", "").ToLowerInvariant();
            if (layout.worldHash != revision || hash != revision || layout.faciesRect.Length != 4 ||
                layout.craters.Length == 0 || layout.craters.Any(c => c.radius < 3.8f || c.radius > 22 || c.freshness < 0 || c.freshness > 1))
                throw new InvalidDataException("Crater inputs do not match the GL125 map revision.");
        }

        var ground = AssetDatabase.LoadAssetAtPath<Material>(GL125WorldAssets.Root + "/Materials/Material_0002.mat");
        if (!ground || ground.shader.name != "MoonEnvironment/Regolith")
            throw new InvalidOperationException("Restore the GL125 Regolith material first.");
        string faciesSource = Path.Combine(stage, "ImpactFacies.png");
        string maskSource = Path.Combine(stage, "GroundExclusion.bytes");
        if (!File.Exists(faciesSource) || new FileInfo(maskSource).Length != 2048 * 2048 / 8)
            throw new InvalidDataException("Incomplete GL125 geology inputs.");

        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(Formal, OpenSceneMode.Additive);
        var edits = new List<MeshEdit>();
        bool applied = false;
        try
        {
            var world = scene.GetRootGameObjects().Single(root => root.name == "GL125 World");
            var terrain = world.transform.Find("Static geometry").GetComponentsInChildren<MeshFilter>()
                .Where(f => f.name.StartsWith("DEM_", StringComparison.Ordinal)).ToArray();
            if (terrain.Length != 256) throw new InvalidDataException("Expected the complete GL125 DEM.");
            var colliders = terrain.Select(f => f.GetComponent<MeshCollider>()).ToHashSet();
            if (colliders.Contains(null)) throw new InvalidDataException("A GL125 terrain collider is missing.");
            int layers = LayerMask.GetMask("Default", "Ground");
            RaycastHit Ground(Vector2 point)
            {
                if (!Physics.Raycast(new Vector3(point.x, 1200, point.y), Vector3.down, out var hit,
                        1300, layers, QueryTriggerInteraction.Ignore) || !colliders.Contains(hit.collider as MeshCollider))
                    throw new InvalidDataException($"Crater sample is not open DEM at {point:F2}.");
                return hit;
            }

            Physics.SyncTransforms();
            var centersBefore = layout.craters.Select(c => Ground(c.Center).point.y).ToArray();
            int sculpted = 0;
            foreach (var filter in terrain)
            {
                var bounds = filter.GetComponent<MeshRenderer>().bounds;
                var nearby = layout.craters.Where(c =>
                    Mathf.Abs(c.x - bounds.center.x) <= bounds.extents.x + c.radius * 1.8f &&
                    Mathf.Abs(c.z - bounds.center.z) <= bounds.extents.z + c.radius * 1.8f).ToArray();
                if (nearby.Length == 0) continue;
                var collider = filter.GetComponent<MeshCollider>();
                if (!collider || collider.sharedMesh != filter.sharedMesh)
                    throw new InvalidDataException("Terrain renderer and collision must use the same mesh.");
                var edit = Prepare(prepared, filter, collider, nearby, ref sculpted);
                if (edit != null) edits.Add(edit);
            }
            if (edits.Count == 0 && !ground.GetTexture("_ImpactGeology"))
                throw new InvalidDataException("No GL125 terrain vertices received crater geometry.");

            var paths = edits.Select(e => AssetDatabase.GetAssetPath(e.Mesh)).Distinct().OrderBy(p => p).ToArray();
            string backup = Path.Combine(stage, "MeshBundlesBeforeCraters");
            Directory.CreateDirectory(backup);
            foreach (string path in paths)
            {
                string copy = Path.Combine(backup, Path.GetFileName(path));
                if (!File.Exists(copy)) File.Copy(path, copy);
            }

            applied = true;
            foreach (var edit in edits) SetGeometry(edit, edit.Positions, edit.Normals);
            Physics.SyncTransforms();

            float maximumSlope = 0;
            float minDepth = float.MaxValue, maxDepth = 0;
            for (int i = 0; i < layout.craters.Length; i++)
            {
                var crater = layout.craters[i];
                var center = Ground(crater.Center);
                if (edits.Count > 0)
                {
                    float depth = centersBefore[i] - center.point.y;
                    if (depth < .16f || depth > crater.radius * .14f)
                        throw new InvalidDataException($"Unexpected crater depth {depth:F2} at {crater.Center:F2}.");
                    minDepth = Mathf.Min(minDepth, depth);
                    maxDepth = Mathf.Max(maxDepth, depth);
                }
                for (int j = 0; j < 8; j++)
                {
                    float angle = j * Mathf.PI / 4;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    foreach (float ring in new[] { .68f, .92f, 1.19f })
                    {
                        var hit = Ground(crater.Center + direction * (crater.radius * ring));
                        float slope = Vector3.Angle(hit.normal, Vector3.up);
                        if (slope > 39f)
                            throw new InvalidDataException($"Crater exceeds the slope limit at {hit.point:F2} ({slope:F1} degrees).");
                        maximumSlope = Mathf.Max(maximumSlope, slope);
                    }
                }
            }

            GL125WorldAssets.EnsureFolder(Geology);
            CopyInput(stage, "CraterLayout.json");
            CopyInput(stage, "GroundExclusion.bytes");
            CopyInput(stage, "ImpactFacies.png");
            string texturePath = Geology + "/ImpactFacies.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            if (importer.sRGBTexture || !importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (!texture || texture.width != 1024 || texture.height != 1024)
                throw new InvalidDataException("GL125 crater facies are missing.");

            foreach (var edit in edits) EditorUtility.SetDirty(edit.Mesh);
            foreach (var path in paths)
            {
                var bundle = AssetDatabase.LoadMainAssetAtPath(path);
                EditorUtility.SetDirty(bundle);
                AssetDatabase.SaveAssetIfDirty(bundle);
            }
            ground.SetTexture("_ImpactGeology", texture);
            var rect = layout.faciesRect;
            ground.SetVector("_LocalTerrainRect", new Vector4(rect[0], rect[1], rect[2], rect[3]));
            ground.SetFloat("_ImpactReady", 1);
            EditorUtility.SetDirty(ground);
            AssetDatabase.SaveAssetIfDirty(ground);
            applied = false;
            if (paths.Length > 0) File.WriteAllLines(Path.Combine(stage, "CraterMeshPaths.txt"), paths);
            string depths = edits.Count > 0 ? $", center depth {minDepth:F2}–{maxDepth:F2} m" : "";
            return $"{layout.craters.Length} crater centers and {layout.craters.Length * 24} rim samples checked" +
                $"{depths}, maximum sampled slope {maximumSlope:F1} degrees. Updated {edits.Count} terrain chunks, {sculpted} vertices.";
        }
        finally
        {
            if (applied)
                foreach (var edit in edits) SetGeometry(edit, edit.OriginalPositions, edit.OriginalNormals);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static MeshEdit Prepare(string prepared, MeshFilter filter, MeshCollider collider, Crater[] craters, ref int sculpted)
    {
        var mesh = filter.sharedMesh;
        var positions = mesh.vertices;
        var normals = mesh.normals;
        var output = (Vector3[])positions.Clone();
        var outputNormals = (Vector3[])normals.Clone();
        using var input = new BinaryReader(File.OpenRead(Path.Combine(prepared, "Meshes", filter.name + ".bytes")));
        if (input.ReadUInt32() != 0x314d4c47 || input.ReadInt32() != positions.Length || input.ReadInt32() != 10 ||
            input.ReadInt32() != mesh.subMeshCount || normals.Length != positions.Length)
            throw new InvalidDataException("Unexpected DEM mesh layout " + filter.name);

        Vector3 ReadVector()
        {
            float x = input.ReadSingle(), z = input.ReadSingle(), y = input.ReadSingle();
            return new Vector3(x, y, z);
        }
        bool changed = false;
        var offset = filter.transform.position;
        for (int i = 0; i < positions.Length; i++)
        {
            var baseline = ReadVector();
            var normal = ReadVector();
            input.BaseStream.Position += 4 * sizeof(float);
            Vector2 point = new Vector2(baseline.x + offset.x, baseline.z + offset.z);
            float height = 0, gradientX = 0, gradientZ = 0;
            foreach (var crater in craters)
            {
                var delta = point - crater.Center;
                if (delta.sqrMagnitude >= crater.radius * crater.radius * 3.24f) continue;
                height += crater.Height(delta);
                const float step = .08f;
                gradientX += (crater.Height(delta + Vector2.right * step) - crater.Height(delta - Vector2.right * step)) / (2 * step);
                gradientZ += (crater.Height(delta + Vector2.up * step) - crater.Height(delta - Vector2.up * step)) / (2 * step);
            }
            var result = baseline + Vector3.up * height;
            if ((positions[i] - baseline).sqrMagnitude > 1e-7f && (positions[i] - result).sqrMagnitude > 1e-7f)
                throw new InvalidDataException("DEM has unrecognized local edits " + filter.name);
            if ((positions[i] - result).sqrMagnitude <= 1e-10f) continue;
            output[i] = result;
            outputNormals[i] = new Vector3(normal.x - normal.y * gradientX, normal.y, normal.z - normal.y * gradientZ).normalized;
            sculpted++;
            changed = true;
        }
        return changed ? new MeshEdit
        {
            Mesh = mesh, Collider = collider,
            OriginalPositions = positions, OriginalNormals = normals, Positions = output, Normals = outputNormals
        } : null;
    }

    private static void SetGeometry(MeshEdit edit, Vector3[] positions, Vector3[] normals)
    {
        edit.Mesh.vertices = positions;
        edit.Mesh.normals = normals;
        edit.Mesh.RecalculateBounds();
        edit.Collider.sharedMesh = null;
        edit.Collider.sharedMesh = edit.Mesh;
    }

    private static void CopyInput(string stage, string name)
    {
        string target = Geology + "/" + name;
        File.Copy(Path.Combine(stage, name), target, true);
        AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
    }
}
