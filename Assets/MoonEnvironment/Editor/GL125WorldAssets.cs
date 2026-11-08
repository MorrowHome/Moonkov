using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Imports the source-hash-bound Blender payload once, as native Unity assets.</summary>
    public sealed class GL125WorldAssets
    {
        public const string Root = "Assets/MoonEnvironment/Variants/GL125";
        public const string MainSun = "HM02_N21_REFERENCE_NEUTRAL_SIDEFRONT_SUN";
        private const string SourceHash = "730d2651933762900f231ede202018e83bc1f347e1212d2c38bab7136afd4d73";

        [Serializable] public sealed class Atlas
        {
            public int id, size, triangles;
        }

        [Serializable] public sealed class Chunk
        {
            public string name, file, sha256;
            public int atlas, vertices, triangles;
            public float[] origin;
            public int[] materials;
            public bool collider;
        }

        [Serializable] public sealed class TextureSpec
        {
            public string file, space;
        }

        [Serializable] public sealed class MaterialSpec
        {
            public string name;
            public float[] @base, emission;
            public float metallic, roughness, alpha;
            public bool transparent, doubleSided;
            public TextureSpec baseMap, normalMap;
        }

        [Serializable] public sealed class LightSpec
        {
            public string name, type;
            public float energy, probeRange;
            public float[] color, position, forward, up;
            public bool shadow;
        }

        [Serializable] private sealed class Manifest
        {
            public int version;
            public string sourceHash;
            public Atlas[] atlases;
            public Chunk[] chunks;
            public MaterialSpec[] materials;
            public LightSpec[] lights;
        }

        [Serializable] private sealed class BakeReceipt
        {
            public string worldHash;
            public int version, id, size, samples, coveredPixels, supersample;
        }

        [Serializable] private sealed class CollisionManifest
        {
            public int version;
            public string worldHash;
            public Chunk[] chunks;
        }

        private readonly Manifest manifest;
        private readonly string prepared, evaluated, worldHash;
        private Chunk[] staticCollision;
        private string staticCollisionId;
        private readonly Dictionary<string, Mesh> meshes = new();
        private readonly Dictionary<int, Material> materials = new();
        public IEnumerable<Chunk> Chunks => manifest.chunks.Where(chunk => chunk.atlas >= 0);
        public IReadOnlyList<Chunk> StaticCollision => staticCollision ??= ReadStaticCollision();
        public IReadOnlyList<LightSpec> Lights => manifest.lights;
        public int AtlasCount => manifest.atlases.Length;
        public IEnumerable<int> InteriorAtlasIndices => manifest.chunks
            .Where(chunk => chunk.atlas >= 0 && chunk.name.StartsWith("MC00_", StringComparison.Ordinal))
            .Select(chunk => chunk.atlas).Distinct();
        public Mesh MeshFor(Chunk chunk) => meshes[chunk.name];
        public Material[] MaterialsFor(Chunk chunk) => chunk.materials.Select(id => materials[id]).ToArray();

        public Mesh ReadCollider(Chunk chunk)
        {
            if (!chunk.collider) throw new ArgumentException("Chunk has no collision.", nameof(chunk));
            return ReadMesh(chunk, true);
        }

        public GL125WorldAssets(string prepared, string evaluated)
        {
            this.prepared = Path.GetFullPath(prepared);
            this.evaluated = Path.GetFullPath(evaluated);
            string path = Path.Combine(this.prepared, "World.json");
            using (var stream = File.OpenRead(path)) worldHash = ContentHash(stream);
            manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (manifest == null || manifest.version != 1 || manifest.sourceHash != SourceHash ||
                manifest.chunks.Length == 0 || manifest.atlases.Length == 0 ||
                manifest.atlases.Where((atlas, i) => atlas.id != i).Any() ||
                manifest.chunks.Any(c => c.atlas < -1 || c.atlas >= AtlasCount) ||
                manifest.chunks.Select(c => c.name).Distinct().Count() != manifest.chunks.Length)
                throw new InvalidDataException("GL125 export manifest does not match the current map contract.");
        }

        private Chunk[] ReadStaticCollision()
        {
            string path = Path.Combine(prepared, "StaticCollision", "Collision.json");
            var data = JsonUtility.FromJson<CollisionManifest>(File.ReadAllText(path));
            if (data == null || data.version != 1 || data.worldHash != worldHash || data.chunks == null ||
                data.chunks.Length == 0 || data.chunks.Any(c => c.atlas != -1 || !c.collider || c.sha256?.Length != 64 ||
                    c.vertices < 3 || c.triangles < 1 || c.origin?.Length != 3) ||
                data.chunks.Select(c => c.name).Distinct().Count() != data.chunks.Length)
                throw new InvalidDataException("GL125 static collision does not match the current render manifest.");
            using (var stream = File.OpenRead(path)) staticCollisionId = ContentHash(stream).Substring(0, 12);
            return data.chunks;
        }

        private static string ContentHash(Stream stream)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public Texture2D[] ImportLightmaps(IEnumerable<int> atlasIndices, IEnumerable<int> supersampledIndices = null)
        {
            var selected = atlasIndices.Distinct().OrderBy(i => i).ToArray();
            var supersampled = supersampledIndices?.ToHashSet();
            var inputs = new Dictionary<int, string>(selected.Length);
            foreach (int index in selected)
            {
                var atlas = manifest.atlases[index];
                bool highQuality = supersampled?.Contains(index) == true;
                string folder = Path.Combine(prepared, highQuality ? "LightmapsHQ" : "Lightmaps");
                string from = Path.Combine(folder, $"Irradiance_{index:000}.exr");
                var receipt = JsonUtility.FromJson<BakeReceipt>(File.ReadAllText(Path.ChangeExtension(from, ".json")));
                if (receipt == null || receipt.version != 2 || receipt.worldHash != worldHash || receipt.id != index ||
                    receipt.size != atlas.size || receipt.coveredPixels <= 0 || receipt.samples < 16 || !File.Exists(from) ||
                    (highQuality && receipt.supersample < 2))
                    throw new InvalidDataException($"GL125 atlas {index} needs a completed Cycles bake.");
                inputs.Add(index, from);
            }
            EnsureImportRoot();
            EnsureFolder(Root + "/Lightmaps");
            return selected.Select(index =>
            {
                string name = $"Irradiance_{index:000}.exr";
                return ImportTexture(inputs[index], Root + "/Lightmaps/" + name,
                    TextureImporterType.Lightmap, false, manifest.atlases[index].size);
            }).ToArray();
        }

        public void ImportChunks(IEnumerable<Chunk> selection)
        {
            var chunks = selection.ToArray();
            EnsureImportRoot();
            EnsureFolder(Root + "/Meshes");
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Textures");
            foreach (int id in chunks.SelectMany(c => c.materials).Distinct())
                if (!materials.ContainsKey(id)) materials.Add(id, ImportMaterial(id));
            foreach (var group in chunks.GroupBy(c => c.atlas))
            {
                var batch = group.OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
                if (group.Key >= 0) ImportBundle("Atlas_" + group.Key.ToString("000"), batch);
                else for (int start = 0; start < batch.Length; start += 32)
                    ImportBundle($"Structure_{staticCollisionId}_{start / 32:000}", batch.Skip(start).Take(32).ToArray());
            }
        }

        private void EnsureImportRoot()
        {
            EnsureFolder(Root);
            string marker = Root + "/ImportSource.txt";
            if (!File.Exists(marker))
            {
                File.WriteAllText(marker, worldHash + "\n");
                AssetDatabase.ImportAsset(marker, ImportAssetOptions.ForceSynchronousImport);
            }
            if (File.ReadAllText(marker).Trim() != worldHash)
                throw new InvalidOperationException("Existing GL125 assets belong to a different geometry payload.");
        }

        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new InvalidOperationException($"Invalid asset path {folder}.");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private Material ImportMaterial(int id)
        {
            string path = Root + $"/Materials/Material_{id:0000}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            var row = manifest.materials[id];
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("URP Lit is required for GL125.");
            var material = new Material(shader) { name = row.name };
            var baseMap = ImportSurface(row.baseMap, false);
            var normal = ImportSurface(row.normalMap, true);
            Color color = baseMap ? Color.white : ColorFromLinear(row.@base);
            color.a = row.transparent ? Mathf.Clamp(row.alpha, 0.08f, 0.95f) : 1;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", Mathf.Clamp01(row.metallic));
            material.SetFloat("_Smoothness", Mathf.Clamp01(1 - row.roughness));
            material.SetFloat("_Cull", row.doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            if (baseMap) material.SetTexture("_BaseMap", baseMap);
            if (normal)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            Color emission = ColorFromLinear(row.emission);
            if (emission.maxColorComponent > 0)
            {
                material.SetColor("_EmissionColor", emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                material.EnableKeyword("_EMISSION");
            }
            if (row.transparent)
            {
                material.SetFloat("_Surface", 1);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static Color ColorFromLinear(float[] values) => new Color(values[0], values[1], values[2]).gamma;
        public static Vector3 ToUnity(float[] xyz) => new Vector3(xyz[0], xyz[2], xyz[1]);

        private Texture2D ImportSurface(TextureSpec spec, bool normal)
        {
            if (spec == null || string.IsNullOrEmpty(spec.file)) return null;
            string file = (normal ? "Normal_" : spec.space == "sRGB" ? "Color_" : "Data_") + spec.file;
            return ImportTexture(Path.Combine(evaluated, "Textures", spec.file), Root + "/Textures/" + file,
                normal ? TextureImporterType.NormalMap : TextureImporterType.Default, !normal && spec.space == "sRGB", 2048);
        }

        private static Texture2D ImportTexture(string from, string to, TextureImporterType type, bool srgb, int size)
        {
            bool lightmap = type == TextureImporterType.Lightmap;
            if (!File.Exists(to) || lightmap && !SameContents(from, to))
            {
                File.Copy(from, to, true);
                AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = AssetImporter.GetAtPath(to) as TextureImporter;
            if (!importer) throw new IOException($"Unity cannot import {to}.");
            if (importer.textureType != type || importer.sRGBTexture != srgb || importer.maxTextureSize != size ||
                importer.mipmapEnabled == lightmap || importer.isReadable)
            {
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.maxTextureSize = size;
                importer.mipmapEnabled = !lightmap;
                importer.isReadable = false;
                importer.wrapMode = lightmap ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(to) ?? throw new IOException($"Texture is unavailable after import: {to}.");
        }

        private static bool SameContents(string from, string to)
        {
            using var source = File.OpenRead(from);
            using var imported = File.OpenRead(to);
            if (source.Length != imported.Length) return false;
            using var sha = SHA256.Create();
            return sha.ComputeHash(source).SequenceEqual(sha.ComputeHash(imported));
        }

        private void ImportBundle(string name, Chunk[] chunks)
        {
            string path = Root + "/Meshes/" + name + ".asset";
            var bundle = AssetDatabase.LoadAssetAtPath<SourceWorldMeshBundle>(path);
            if (!bundle)
            {
                var packed = chunks.Select(ReadMesh).ToArray();
                bundle = ScriptableObject.CreateInstance<SourceWorldMeshBundle>();
                bundle.meshes = packed;
                AssetDatabase.CreateAsset(bundle, path);
                foreach (var mesh in packed) AssetDatabase.AddObjectToAsset(mesh, bundle);
                EditorUtility.SetDirty(bundle);
                AssetDatabase.SaveAssetIfDirty(bundle);
            }
            if (bundle.meshes.Length != chunks.Length || chunks.Where((c, i) => !bundle.meshes[i] ||
                bundle.meshes[i].name != c.name || bundle.meshes[i].vertexCount != c.vertices).Any())
                throw new InvalidDataException($"Incomplete GL125 mesh bundle {path}.");
            foreach (var mesh in bundle.meshes) meshes[mesh.name] = mesh;
        }

        private Mesh ReadMesh(Chunk chunk) => ReadMesh(chunk, false);

        private Mesh ReadMesh(Chunk chunk, bool collisionOnly)
        {
            if (chunk.file != chunk.name + ".bytes" || Path.GetFileName(chunk.file) != chunk.file)
                throw new InvalidDataException($"Invalid GL125 mesh file name {chunk.file}.");
            bool visual = chunk.atlas >= 0;
            string folder = visual ? prepared : Path.Combine(prepared, "StaticCollision");
            using var stream = File.OpenRead(Path.Combine(folder, "Meshes", chunk.file));
            if (!visual && ContentHash(stream) != chunk.sha256)
                throw new InvalidDataException($"GL125 static collision mesh hash changed: {chunk.name}.");
            stream.Position = 0;
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x314d4c47 || reader.ReadInt32() != chunk.vertices)
                throw new InvalidDataException($"Invalid GL125 mesh header {chunk.name}.");
            int stride = reader.ReadInt32(), submeshCount = reader.ReadInt32();
            if (stride != (visual ? 10 : 3) || submeshCount != (visual ? chunk.materials.Length : 1))
                throw new InvalidDataException($"Invalid GL125 mesh layout {chunk.name}.");

            var vertices = new Vector3[chunk.vertices];
            bool shading = visual && !collisionOnly;
            var normals = shading ? new Vector3[chunk.vertices] : null;
            var uv = shading ? new Vector2[chunk.vertices] : null;
            var uv2 = shading ? new Vector2[chunk.vertices] : null;
            Vector3 Vector()
            {
                float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
                return new Vector3(x, z, y);
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector();
                if (!visual) continue;
                if (!shading)
                {
                    for (int component = 0; component < 7; component++) reader.ReadSingle();
                    continue;
                }
                normals[i] = Vector();
                uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                uv2[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }
            var mesh = new Mesh { name = chunk.name, indexFormat = chunk.vertices > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                mesh.vertices = vertices;
                mesh.subMeshCount = submeshCount;
                int total = 0;
                for (int i = 0; i < submeshCount; i++)
                {
                    if (reader.ReadInt32() != (visual ? chunk.materials[i] : -1)) throw new InvalidDataException($"Material order changed: {chunk.name}.");
                    int count = reader.ReadInt32();
                    if (count <= 0 || count % 3 != 0 || count > chunk.triangles * 3 - total)
                        throw new InvalidDataException($"Invalid triangle indices: {chunk.name}.");
                    var indices = new int[count];
                    for (int n = 0; n < count; n += 3)
                    {
                        indices[n] = reader.ReadInt32();
                        indices[n + 2] = reader.ReadInt32();
                        indices[n + 1] = reader.ReadInt32();
                    }
                    mesh.SetTriangles(indices, i, false);
                    total += count;
                }
                if (total != chunk.triangles * 3 || stream.Position != stream.Length)
                    throw new InvalidDataException($"GL125 mesh size changed: {chunk.name}.");
                if (shading)
                {
                    mesh.normals = normals;
                    mesh.uv = uv;
                    mesh.uv2 = uv2;
                    if (chunk.materials.Any(id => materials[id].IsKeywordEnabled("_NORMALMAP"))) mesh.RecalculateTangents();
                }
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }
    }
}
