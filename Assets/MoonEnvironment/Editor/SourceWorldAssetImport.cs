using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon.Editor
{
    [Serializable] public sealed class SourceTextureJob
    { public string source, asset, wrap, sha256; public bool srgb; public int maxSize; }
    [Serializable] public sealed class SourceTextureBinding { public string property, asset; }
    [Serializable] public sealed class SourceMaterialJob
    { public int id; public string name, shader, asset; public SourceTextureBinding[] textures; public bool transparent; }
    [Serializable] public sealed class SourceAtlasJob
    { public int id, size; public string sourceIrradiance, assetIrradiance, sourceStatistics, assetStatistics; }
    [Serializable] public sealed class SourceAssetManifest
    { public string sourceSha256; public SourceTextureJob[] textures; public SourceMaterialJob[] materials; public SourceAtlasJob[] atlases; public int vertices, triangles, chunkCount; }
    [Serializable] public sealed class SourceChunk
    { public int id, atlas, material, vertices, indices, bytes; public string file; public Vector3 position; }
    [Serializable] public sealed class SourceGeometryManifest
    { public string sourceSha256; public int triangles, vertices, atlasCount; public SourceChunk[] chunks; public int[] materialIds; }
    [Serializable] public sealed class SourceReflectionPlan
    { public string sourceSha256, lightingRevision; public Vector3 towardSun; public Color sunColor; public float sunEnergy, sunAngle; public SourceReflectionJob[] probes; }
    [Serializable] public sealed class SourceReflectionJob
    { public int id, importance; public Vector3 position, boxMin, boxMax; public bool sunVisible; public string label, file, sourceSha256, lightingRevision; }
    [Serializable] internal sealed class SourceImportState { public int phase, index; public bool running; public string error; }

    /// <summary>Bounded Editor imports with a durable checkpoint, without changing active scenes.</summary>
    [InitializeOnLoad]
    public static class SourceWorldAssetImport
    {
        public const string Work = "LocalData/SourceFidelity20261028/World/";
        public const string Root = "Assets/MoonEnvironment/Variants/SourceFidelity/World/";
        public const string DataPath = Root + "Lighting/SourceWorldLighting.asset";
        public const string ReflectionPath = Root + "Reflections/SourceWorldReflections.asset";
        private const string StatePath = Work + "AssetImport.state.json";
        private static SourceAssetManifest manifest;
        private static SourceGeometryManifest geometry;
        private static SourceImportState state;
        public static SourceAssetManifest Manifest => manifest ?? (manifest = JsonUtility.FromJson<SourceAssetManifest>(File.ReadAllText(Work + "WorldAssetManifest.json")));
        public static SourceGeometryManifest Geometry => geometry ?? (geometry = JsonUtility.FromJson<SourceGeometryManifest>(File.ReadAllText(Work + "WorldGeometry.json")));
        static SourceWorldAssetImport()
        {
            if (File.Exists(StatePath)) { state = JsonUtility.FromJson<SourceImportState>(File.ReadAllText(StatePath)); if (state.running) EditorApplication.update += Tick; }
        }
        [MenuItem("Tools/Moon Environment/Source World/Import complete source world assets")]
        public static void Start()
        {
            manifest = null; geometry = null;
            state = File.Exists(StatePath) ? JsonUtility.FromJson<SourceImportState>(File.ReadAllText(StatePath)) : new SourceImportState();
            state.running = true; state.error = null; Save();
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }
        public static void Stop()
        {
            if (state != null) { state.running = false; Save(); } EditorApplication.update -= Tick;
        }
        private static void Save()
        { Directory.CreateDirectory(Work); File.WriteAllText(StatePath, JsonUtility.ToJson(state, true)); }
        private static void Advance(int count)
        { state.index++; if (state.index >= count) { state.phase++; state.index = 0; } Save(); }
        private static void Tick()
        {
            if (state == null || !state.running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                switch (state.phase)
                {
                    case 0:
                        ImportTexture(Manifest.textures[state.index]); Advance(Manifest.textures.Length); break;
                    case 1:
                        ImportMaterial(Manifest.materials[state.index]); Advance(Manifest.materials.Length); break;
                    case 2:
                        // The corrected, original-coordinate bake is a hard prerequisite.
                        if (!File.Exists(Work + "BakedWorld.json") || !File.ReadAllText(Work + "BakedWorld.json").Contains("\"version\": 2")) return;
                        ImportAtlas(Manifest.atlases[state.index]); Advance(Manifest.atlases.Length); break;
                    case 3:
                        if (state.index == 0) PrepareStaticLightingData();
                        ImportMeshAtlas(state.index); Advance(Manifest.atlases.Length); break;
                    case 4:
                        var plan = JsonUtility.FromJson<SourceReflectionPlan>(File.ReadAllText(Work + "WorldReflectionPlan.json"));
                        var reflectionFile = plan.probes[state.index].file;
                        if (!File.Exists(reflectionFile) || !File.Exists(Path.ChangeExtension(reflectionFile, ".json"))) return;
                        var captureText = File.ReadAllText(Path.ChangeExtension(reflectionFile, ".json"));
                        if (string.IsNullOrWhiteSpace(captureText) || !captureText.TrimEnd().EndsWith("}")) return;
                        var captured = JsonUtility.FromJson<SourceReflectionJob>(captureText);
                        if (!CaptureMatches(plan, plan.probes[state.index], captured)) return;
                        ImportReflection(state.index); Advance(plan.probes.Length); break;
                    case 5:
                        BuildLightingData(); state.phase = 6; state.index = 0; state.running = false; Save();
                        EditorApplication.update -= Tick; Debug.Log("Full source world assets imported. Formal scene installation and validation remain."); break;
                    default: state.running = false; Save(); EditorApplication.update -= Tick; break;
                }
            }
            catch (Exception exception)
            {
                state.error = exception.ToString(); state.running = false; Save(); EditorApplication.update -= Tick;
                Debug.LogError("Source world import stopped at phase " + state.phase + " item " + state.index + ": " + exception);
            }
        }
        private static void Ensure(string asset)
        {
            var folder = Path.GetDirectoryName(asset);
            if (!Directory.Exists(folder)) { Directory.CreateDirectory(folder); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
        }
        private static string Stamp(string source) => Manifest.sourceSha256 + ":v2:" + new FileInfo(source).Length + ":" + File.GetLastWriteTimeUtc(source).Ticks;
        private static void Copy(string source, string asset, string template = null, int maxSize = 0)
        {
            Ensure(asset);
            if (!File.Exists(asset) || new FileInfo(asset).Length != new FileInfo(source).Length ||
                File.GetLastWriteTimeUtc(asset) != File.GetLastWriteTimeUtc(source))
            {
                // Seed the local importer before its first import. Otherwise a large
                // HDR atlas is compressed with defaults and then compressed again.
                string meta = asset + ".meta";
                if (template != null && (File.Exists(meta) || File.Exists(template + ".meta")))
                {
                    bool existing = File.Exists(meta);
                    string settings = File.ReadAllText(existing ? meta : template + ".meta");
                    if (!existing) settings = Regex.Replace(settings, @"(?m)^guid: .*", "guid: " + Guid.NewGuid().ToString("N"));
                    if (maxSize > 0) settings = Regex.Replace(settings, @"(?m)^(\s*maxTextureSize:) \d+", "$1 " + maxSize);
                    settings = Regex.Replace(settings, @"(?m)^(  userData:).*", "$1 " + Stamp(source));
                    File.WriteAllText(meta, settings);
                }
                File.Copy(source, asset, true); File.SetLastWriteTimeUtc(asset, File.GetLastWriteTimeUtc(source));
                AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
            }
        }
        private static void ImportTexture(SourceTextureJob job)
        {
            Copy(job.source, job.asset);
            var importer = (TextureImporter)AssetImporter.GetAtPath(job.asset); string stamp = Stamp(job.source) + ":" + job.wrap + ":" + job.srgb;
            if (importer.userData == stamp) return;
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = job.srgb;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Clamp(job.maxSize, 32, 8192)); importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true; importer.streamingMipmaps = false; importer.anisoLevel = 16; importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = job.wrap == "REPEAT" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.alphaIsTransparency = false; importer.isReadable = false; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.userData = stamp; importer.SaveAndReimport();
        }
        private static void ImportMaterial(SourceMaterialJob job)
        {
            Ensure(job.asset); var material = AssetDatabase.LoadAssetAtPath<Material>(job.asset); var shader = Shader.Find(job.shader);
            if (!shader) throw new InvalidOperationException("Missing source shader: " + job.shader);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, job.asset); }
            material.shader = shader; material.name = job.name;
            foreach (var binding in job.textures)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(binding.asset);
                if (!texture) throw new InvalidOperationException("Missing source texture: " + binding.asset);
                material.SetTexture(binding.property, texture);
            }
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        }
        private static void ImportAtlas(SourceAtlasJob job)
        {
            Copy(job.sourceIrradiance, job.assetIrradiance, Root + "Lighting/Irradiance_000.exr", job.size); var importer = (TextureImporter)AssetImporter.GetAtPath(job.assetIrradiance);
            if (importer.userData != Stamp(job.sourceIrradiance))
            {
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.mipmapEnabled = true;
                importer.maxTextureSize = job.size; importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = job.size, format = TextureImporterFormat.BC6H, compressionQuality = 100 });
                importer.userData = Stamp(job.sourceIrradiance); importer.SaveAndReimport();
            }
            Copy(job.sourceStatistics, job.assetStatistics, Root + "Lighting/SurfaceStatistics_000.png", job.size); importer = (TextureImporter)AssetImporter.GetAtPath(job.assetStatistics);
            if (importer.userData != Stamp(job.sourceStatistics))
            {
                importer.sRGBTexture = false; importer.mipmapEnabled = false; importer.maxTextureSize = job.size;
                importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.userData = Stamp(job.sourceStatistics); importer.SaveAndReimport();
            }
        }
        private static bool reflectionConversionValidated;
        public static bool CaptureMatches(SourceReflectionPlan plan, SourceReflectionJob expected, SourceReflectionJob captured)
        {
            return captured != null && !string.IsNullOrEmpty(plan.lightingRevision) &&
                captured.sourceSha256 == plan.sourceSha256 && captured.lightingRevision == plan.lightingRevision &&
                captured.id == expected.id && (captured.position - expected.position).sqrMagnitude < .00000001f;
        }
        private static void ImportReflection(int index)
        {
            if (!reflectionConversionValidated) { SourceWorldCubeBuilder.ValidateConversion(); reflectionConversionValidated = true; }
            var plan = JsonUtility.FromJson<SourceReflectionPlan>(File.ReadAllText(Work + "WorldReflectionPlan.json")); var job = plan.probes[index];
            if (string.IsNullOrEmpty(plan.lightingRevision) || plan.sourceSha256 != Manifest.sourceSha256)
                throw new InvalidDataException("Reflection lighting revision/source is missing or stale.");
            if (!File.Exists(job.file)) throw new InvalidOperationException("Missing completed source reflection: " + job.file);
            Ensure(ReflectionPath); var data = AssetDatabase.LoadAssetAtPath<SourceWorldReflectionData>(ReflectionPath);
            if (!data)
            {
                data = ScriptableObject.CreateInstance<SourceWorldReflectionData>(); AssetDatabase.CreateAsset(data, ReflectionPath);
            }
            if (data.sourceSha256 != plan.sourceSha256 || data.lightingRevision != plan.lightingRevision ||
                data.cubemaps == null || data.probes == null || data.cubemaps.Length != plan.probes.Length || data.probes.Length != plan.probes.Length)
            {
                data.sourceSha256 = plan.sourceSha256; data.lightingRevision = plan.lightingRevision;
                data.cubemaps = new Cubemap[plan.probes.Length]; data.probes = new SourceWorldReflectionData.Probe[plan.probes.Length];
            }
            if (data.cubemaps[index] && data.probes[index] != null && (data.probes[index].position - job.position).sqrMagnitude < .000001f)
            {
                if (data.probes[index].boxMin != job.boxMin || data.probes[index].boxMax != job.boxMax)
                { data.probes[index].boxMin = job.boxMin; data.probes[index].boxMax = job.boxMax; EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); }
                return;
            }
            string probePath = Root + "Reflections/Probe_" + index.ToString("000") + ".asset";
            var stored = AssetDatabase.LoadAssetAtPath<SourceWorldReflectionData>(probePath);
            if (stored && stored.sourceSha256 == plan.sourceSha256 && stored.lightingRevision == plan.lightingRevision && stored.cubemaps != null && stored.cubemaps.Length == 1 && stored.cubemaps[0] && stored.probes != null && stored.probes.Length == 1 && stored.probes[0] != null && (stored.probes[0].position - job.position).sqrMagnitude < .000001f)
            { data.cubemaps[index] = stored.cubemaps[0]; data.probes[index] = stored.probes[0]; data.probes[index].boxMin = job.boxMin; data.probes[index].boxMax = job.boxMax; EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); return; }
            string input = Root + "Reflections/Input_" + index.ToString("000") + ".exr"; Copy(job.file, input, Root + "Reflections/Input_000.exr", 2048);
            var importer = (TextureImporter)AssetImporter.GetAtPath(input);
            if (importer.userData != Stamp(job.file))
            {
                importer.sRGBTexture = false; importer.mipmapEnabled = false; importer.maxTextureSize = 2048;
                importer.wrapModeU = TextureWrapMode.Repeat; importer.wrapModeV = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.userData = Stamp(job.file); importer.SaveAndReimport();
            }
            var panorama = AssetDatabase.LoadAssetAtPath<Texture2D>(input);
            SourceWorldCubeBuilder.Project(panorama, plan.towardSun, plan.sunAngle, out var full, out var masked);
            var cube = SourceWorldCubeBuilder.Build(panorama); cube.name = "SourceReflection_" + index.ToString("000");
            if (!stored) { stored = ScriptableObject.CreateInstance<SourceWorldReflectionData>(); AssetDatabase.CreateAsset(stored, probePath); }
            var previousCubes = stored.cubemaps;
            stored.sourceSha256 = plan.sourceSha256; stored.lightingRevision = plan.lightingRevision; AssetDatabase.AddObjectToAsset(cube, stored);
            data.cubemaps[index] = cube; data.probes[index] = new SourceWorldReflectionData.Probe
            { position = job.position, boxMin = job.boxMin, boxMax = job.boxMax, importance = job.importance, sunVisible = job.sunVisible,
                sphericalHarmonics = SourceWorldCubeBuilder.Pack(full), characterSphericalHarmonics = SourceWorldCubeBuilder.Pack(masked) };
            stored.cubemaps = new[] { cube }; stored.probes = new[] { data.probes[index] };
            if (previousCubes != null) foreach (var previousCube in previousCubes)
                if (previousCube && AssetDatabase.GetAssetPath(previousCube) == probePath) UnityEngine.Object.DestroyImmediate(previousCube, true);
            EditorUtility.SetDirty(stored); AssetDatabase.SaveAssetIfDirty(stored);
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); Resources.UnloadAsset(panorama);
        }
        public static SourceWorldReflectionData.Probe Nearest(Vector3 p, SourceWorldReflectionData data)
        {
            SourceWorldReflectionData.Probe best = data.probes[0]; float score = float.PositiveInfinity;
            foreach (var probe in data.probes)
            {
                if (probe == null) continue;
                bool inside = p.x >= probe.boxMin.x && p.y >= probe.boxMin.y && p.z >= probe.boxMin.z && p.x <= probe.boxMax.x && p.y <= probe.boxMax.y && p.z <= probe.boxMax.z;
                float candidate = (p - probe.position).sqrMagnitude + (inside ? 0 : 10000000) - probe.importance * 10000;
                if (candidate < score) { score = candidate; best = probe; }
            }
            return best;
        }
        private static void ImportMeshAtlas(int atlas)
        {
            string path = Root + "Meshes/Atlas_" + atlas.ToString("000") + ".asset"; Ensure(path);
            var bundle = AssetDatabase.LoadAssetAtPath<SourceWorldMeshBundle>(path);
            if (!bundle) { bundle = ScriptableObject.CreateInstance<SourceWorldMeshBundle>(); AssetDatabase.CreateAsset(bundle, path); }
            var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToDictionary(m => m.name);
            var meshes = new List<Mesh>();
            foreach (var chunk in Geometry.chunks.Where(c => c.atlas == atlas))
            {
                string name = "SourceMesh_" + chunk.id.ToString("00000");
                if (existing.TryGetValue(name, out var ready)) { meshes.Add(ready); continue; }
                byte[] bytes = File.ReadAllBytes(chunk.file);
                if (bytes.Length != 12 + chunk.vertices * 136 + chunk.indices * 4 || bytes[0] != 'S' || bytes[1] != 'M') throw new InvalidDataException(chunk.file);
                var vertices = new float[chunk.vertices * 34]; var indices = new uint[chunk.indices];
                Buffer.BlockCopy(bytes, 12, vertices, 0, chunk.vertices * 136); Buffer.BlockCopy(bytes, 12 + chunk.vertices * 136, indices, 0, chunk.indices * 4);
                var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity); var maximum = -minimum;
                for (int v = 0; v < chunk.vertices; v++)
                {
                    int offset = v * 34; var local = new Vector3(vertices[offset], vertices[offset + 1], vertices[offset + 2]); minimum = Vector3.Min(minimum, local); maximum = Vector3.Max(maximum, local);

                }
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertexBufferParams(chunk.vertices,
                    new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 4),
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 4));
                mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length); mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32); mesh.SetIndexBufferData(indices, 0, 0, indices.Length);
                mesh.subMeshCount = 1; mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length, MeshTopology.Triangles)); mesh.bounds = new Bounds((minimum + maximum) * .5f, maximum - minimum);
                AssetDatabase.AddObjectToAsset(mesh, bundle); meshes.Add(mesh);
            }
            bundle.meshes = meshes.ToArray(); EditorUtility.SetDirty(bundle); AssetDatabase.SaveAssetIfDirty(bundle);
        }
        public static SourceWorldLightingData PrepareStaticLightingData()
        {
            Ensure(DataPath); var data = AssetDatabase.LoadAssetAtPath<SourceWorldLightingData>(DataPath);
            if (!data) { data = ScriptableObject.CreateInstance<SourceWorldLightingData>(); AssetDatabase.CreateAsset(data, DataPath); }
            data.sourceSha256 = Manifest.sourceSha256; data.irradiance = Manifest.atlases.Select(a => AssetDatabase.LoadAssetAtPath<Texture2D>(a.assetIrradiance)).ToArray();
            data.displayTransform = AssetDatabase.LoadAssetAtPath<Material>("Assets/MoonEnvironment/Variants/SourceFidelity/Materials/SourceAgX.mat");
            var plan = JsonUtility.FromJson<SourceReflectionPlan>(File.ReadAllText(Work + "WorldReflectionPlan.json")); data.towardSun = plan.towardSun; data.linearSunColor = plan.sunColor; data.sourceSunEnergy = plan.sunEnergy;
            string statisticsRevision = Manifest.sourceSha256 + ":BC5-v1:" + string.Join(";", Manifest.atlases.Select(a =>
                a.id + ":" + AssetDatabase.GetAssetDependencyHash(a.assetStatistics).ToString()));
            if (!data.surfaceStatistics || data.statisticsRevision != statisticsRevision)
            { BuildStatistics(data); data.statisticsRevision = statisticsRevision; }
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); return data;
        }
        private static void BuildLightingData()
        {
            var data = PrepareStaticLightingData();
            data.reflections = AssetDatabase.LoadAssetAtPath<SourceWorldReflectionData>(ReflectionPath);
            var plan = JsonUtility.FromJson<SourceReflectionPlan>(File.ReadAllText(Work + "WorldReflectionPlan.json"));
            if (!data.reflections || string.IsNullOrEmpty(plan.lightingRevision) || data.reflections.lightingRevision != plan.lightingRevision ||
                data.reflections.sourceSha256 != Manifest.sourceSha256 || data.reflections.cubemaps == null || data.reflections.probes == null ||
                data.reflections.cubemaps.Length != plan.probes.Length || data.reflections.probes.Length != plan.probes.Length ||
                data.reflections.cubemaps.Any(c => !c) || data.reflections.probes.Any(p => p == null))
                throw new InvalidOperationException("The complete current source reflection field is required.");
            data.fallbackReflection = data.reflections.cubemaps[0];
            string characterRevision = "character-v1:" + data.reflections.lightingRevision + ":" + AssetDatabase.GetAssetDependencyHash(ReflectionPath);
            if (!data.characterProbes || data.characterProbeRevision != characterRevision)
            { BuildCharacterProbes(data); data.characterProbeRevision = characterRevision; }
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);
        }
        private static void BuildStatistics(SourceWorldLightingData data)
        {
            var array = new Texture2DArray(4096, 4096, Manifest.atlases.Length, TextureFormat.BC5, false, true)
            { name = "Source AO and measured path variance", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var previous = RenderTexture.active; bool previousSRGB = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                foreach (var atlas in Manifest.atlases)
                {
                    var source = AssetDatabase.LoadAssetAtPath<Texture2D>(atlas.assetStatistics);
                    var target = RenderTexture.GetTemporary(4096, 4096, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    var pixels = new Texture2D(4096, 4096, TextureFormat.RGBA32, false, true);
                    try
                    {
                        Graphics.Blit(source, target); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 4096, 4096), 0, 0); pixels.Apply(false, false);
                        EditorUtility.CompressTexture(pixels, TextureFormat.BC5, TextureCompressionQuality.Best); pixels.Apply(false, false);
                        array.SetPixelData(pixels.GetRawTextureData<byte>(), 0, atlas.id);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.ReleaseTemporary(target); Resources.UnloadAsset(source); }
                }
                array.Apply(false, false); AssetDatabase.AddObjectToAsset(array, data);
                var old = data.surfaceStatistics; data.surfaceStatistics = array;
                if (old && AssetDatabase.GetAssetPath(old) == DataPath) UnityEngine.Object.DestroyImmediate(old, true);
            }
            catch { UnityEngine.Object.DestroyImmediate(array); throw; }
            finally { RenderTexture.active = previous; GL.sRGBWrite = previousSRGB; }
        }
        private static void BuildCharacterProbes(SourceWorldLightingData data)
        {
            // Loading every BuildingBakedLightingData also loads many obsolete lightmap states.
            var template = AssetDatabase.LoadAssetAtPath<LightProbes>("Assets/MoonEnvironment/Variants/MC00BakedBuilding/States/Baseline/Probes.asset");
            if (!template)
                template = AssetDatabase.FindAssets("t:LightProbes").Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<LightProbes>).Where(p => p).OrderByDescending(p => p.countSelf).FirstOrDefault();
            if (!template) throw new InvalidOperationException("A native probe structure is required for the source character field.");
            var copy = UnityEngine.Object.Instantiate(template); copy.name = "Source character irradiance";
            var positions = new List<Vector3>(); var unique = new HashSet<Vector3Int>();
            void Add(Vector3 p) { var key = new Vector3Int(Mathf.RoundToInt(p.x * 10), Mathf.RoundToInt(p.y * 10), Mathf.RoundToInt(p.z * 10)); if (unique.Add(key)) positions.Add(p); }
            foreach (var probe in data.reflections.probes.OrderByDescending(p => p.importance))
            {
                Add(probe.position);
                if (probe.importance == 0) continue;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    Add(new Vector3(Mathf.Lerp(probe.boxMin.x, probe.boxMax.x, x == 0 ? .2f : .8f), Mathf.Lerp(probe.boxMin.y, probe.boxMax.y, y == 0 ? .2f : .8f), Mathf.Lerp(probe.boxMin.z, probe.boxMax.z, z == 0 ? .2f : .8f)));
            }
            foreach (var p in template.GetPositionsSelf()) Add(p);
            int count = copy.countSelf;
            if (positions.Count > count) positions = positions.Take(count).ToList();
            for (int i = 0; positions.Count < count; i++) Add(new Vector3(-5000 - i * 2, 3000 + i % 7, -5000));
            if (!copy.SetPositionsSelf(positions.ToArray(), true)) throw new InvalidOperationException("Could not assign source probe positions.");
            var coefficients = new SphericalHarmonicsL2[count];
            for (int i = 0; i < count; i++) coefficients[i] = SourceWorldCubeBuilder.Unpack(Nearest(positions[i], data.reflections).characterSphericalHarmonics);
            copy.bakedProbes = coefficients;
            var previous = LightmapSettings.lightProbes;
            try { LightmapSettings.lightProbes = copy; LightProbes.Tetrahedralize(); }
            finally { LightmapSettings.lightProbes = previous; }
            AssetDatabase.AddObjectToAsset(copy, data);
            var old = data.characterProbes; data.characterProbes = copy;
            if (old && AssetDatabase.GetAssetPath(old) == DataPath)
            { if (LightmapSettings.lightProbes == old) LightmapSettings.lightProbes = copy; UnityEngine.Object.DestroyImmediate(old, true); }
        }
    }
}
