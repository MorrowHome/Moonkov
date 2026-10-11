using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Explicit adapter for MC00_Game_Export.blend. Author metadata owns material
    /// and collision membership; Unity owns light settings. Creates isolated
    /// assets, never overwrites the current gameplay environment or author blend.
    /// </summary>
    public static class MC00GameExportTools
    {
        public const string Source = "SourceData/Models/MC00GameExport";
        public const string Folder = "Assets/MoonEnvironment/Variants/MC00GameExport";
        public const string PrefabPath = Folder + "/Prefabs/MC00GameExport.prefab";
        public const string EnvironmentPath = Folder + "/Prefabs/Apollo12MC00Candidate.prefab";
        public const string PreviewPath = Folder + "/Scenes/MC00GameExportPreview.unity";
        const string OriginalEnvironment = "Assets/MoonEnvironment/Variants/Apollo12Base/Prefabs/Apollo12Base.prefab";
        const string OriginalPreview = "Assets/MoonEnvironment/Variants/Apollo12Base/Scenes/MoonBaseApollo12.unity";

        [Serializable] public class Manifest
        {
            public string sourceSha256, blender;
            public int renderObjects, renderTriangles, proxyObjects;
            public Geometry[] geometry;
            public MaterialInfo[] materials;
            public LightInfo[] lights;
        }
        [Serializable] public class Geometry
        {
            public string name, sourceType;
            public int triangles, degenerateTriangles;
            public MaterialTriangles[] materialTriangles;
            public bool coveredByProxy, collisionSolid;
            public string[] materials;
            public float[] position, scale;
        }
        [Serializable] public class MaterialTriangles { public string name; public int triangles, degenerate; }
        [Serializable] public class MaterialInfo
        {
            public string name, baseMap, normalMap, maskMap, emissionMap;
            public float[] baseColor, emissionColor;
            public float metallic, roughness, emissionStrength, alpha, transmission;
        }
        [Serializable] public class LightInfo
        {
            public string source_name, source_id, anchor_output_id;
            public float[] position_world_m, forward_world_minus_local_z, up_world_plus_local_y;
            public NativeLight native_reference_only;
        }
        [Serializable] public class NativeLight { public float energy; public float[] color; }

        // Synchronous editor-only scope: menu defaults remain the original candidate.
        // New deliveries get their own source, models, materials, prefabs and scene.
        static string variantSource, variantFolder;
        internal static string CurrentSource => variantSource ?? Source;
        internal static string CurrentFolder => variantFolder ?? Folder;
        internal static string CurrentPrefabPath => CurrentFolder + "/Prefabs/MC00GameExport.prefab";
        internal static string CurrentEnvironmentPath => CurrentFolder + "/Prefabs/Apollo12MC00Candidate.prefab";
        internal static string CurrentPreviewPath => CurrentFolder + "/Scenes/MC00GameExportPreview.unity";
        public static void WithVariant(string source, string folder, Action operation)
        {
            RequireEditMode();
            if (variantSource != null) throw new InvalidOperationException("Nested MC00 authoring is not supported.");
            if (source == Source || folder == Folder || !source.StartsWith("SourceData/Models/", StringComparison.Ordinal) ||
                !folder.StartsWith("Assets/MoonEnvironment/Variants/", StringComparison.Ordinal) ||
                source.Contains("..") || folder.Contains("..")) throw new ArgumentException("Require separate versioned project paths.");
            variantSource = source; variantFolder = folder;
            try { operation(); }
            finally { variantSource = variantFolder = null; }
        }
        static Manifest Read() => JsonUtility.FromJson<Manifest>(File.ReadAllText(CurrentSource + "/unity-manifest.json"));

        [MenuItem("Tools/Moon Environment/MC00 Game Export/1 - Import exported assets")]
        public static void ImportAssets()
        {
            RequireEditMode();
            var manifest = Read();
            Directory.CreateDirectory(CurrentFolder + "/Textures");
            Directory.CreateDirectory(CurrentFolder + "/Materials");
            Directory.CreateDirectory(CurrentFolder + "/Models");
            Directory.CreateDirectory(CurrentFolder + "/Prefabs");
            Directory.CreateDirectory(CurrentFolder + "/Scenes");
            foreach (string source in Directory.GetFiles(CurrentSource + "/Textures"))
                File.Copy(source, CurrentFolder + "/Textures/" + Path.GetFileName(source), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var normalPaths = new HashSet<string>(manifest.materials.Select(m => m.normalMap).Where(s => !string.IsNullOrEmpty(s)));
            var maskPaths = new HashSet<string>(manifest.materials.Select(m => m.maskMap).Where(s => !string.IsNullOrEmpty(s)));
            foreach (string path in Directory.GetFiles(CurrentFolder + "/Textures", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                string relative = "Textures/" + Path.GetFileName(path);
                importer.textureType = normalPaths.Contains(relative) ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normalPaths.Contains(relative) && !maskPaths.Contains(relative) && !relative.Contains("_ORM");
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            foreach (var info in manifest.materials) CreateMaterial(info);
            AssetDatabase.SaveAssets();
            foreach (string name in new[] { "MC00_Render.fbx", "MC00_Collision.fbx" })
                File.Copy(CurrentSource + "/" + name, CurrentFolder + "/Models/" + name, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string name in new[] { "MC00_Render.fbx", "MC00_Collision.fbx" })
            {
                string path = CurrentFolder + "/Models/" + name;
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                ConfigureImporter(importer);
                foreach (var info in manifest.materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), info.name), LoadMaterial(info.name));
                importer.SaveAndReimport();
            }
            Debug.Log("MC00 game export: models, explicit URP materials and packed textures imported. Source blend is untouched.");
        }

        internal static void ConfigureImporter(ModelImporter importer)
        {
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.preserveHierarchy = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.addCollider = false;
            importer.generateSecondaryUV = false;
            importer.swapUVChannels = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            // CPU-readable collision geometry also gives runtime NavMesh the actual
            // architecture instead of unreadable-render-mesh bounds as a fallback.
            importer.isReadable = importer.assetPath.EndsWith("MC00_Collision.fbx", StringComparison.Ordinal);
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        static void CreateMaterial(MaterialInfo info)
        {
            string path = MaterialPath(info.name);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.enableInstancing = true;
            m.SetColor("_BaseColor", string.IsNullOrEmpty(info.baseMap) ? ColorOf(info.baseColor).gamma : Color.white);
            m.SetTexture("_BaseMap", Texture(info.baseMap));
            m.SetFloat("_Metallic", info.metallic);
            m.SetFloat("_Smoothness", 1 - info.roughness);
            m.SetTexture("_BumpMap", Texture(info.normalMap));
            m.SetTexture("_MetallicGlossMap", Texture(info.maskMap));
            m.SetTexture("_OcclusionMap", Texture(info.maskMap));
            if (!string.IsNullOrEmpty(info.normalMap)) m.EnableKeyword("_NORMALMAP");
            if (!string.IsNullOrEmpty(info.maskMap))
            {
                m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_Smoothness", 1);
            }
            var emission = ColorOf(info.emissionColor) * info.emissionStrength;
            m.SetColor("_EmissionColor", emission);
            m.SetTexture("_EmissionMap", Texture(info.emissionMap));
            if (info.emissionStrength > 0) { m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            // URP Lit has no Blender transmission equivalent. Keep windows visibly
            // transparent with an explicit approximation; do not make opaque walls.
            if (info.transmission > .5f || info.alpha < .99f)
            {
                var c = m.GetColor("_BaseColor"); c.a = info.transmission > .5f ? .18f : info.alpha; m.SetColor("_BaseColor", c);
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_SrcBlendAlpha", (int)BlendMode.One); m.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent; m.SetShaderPassEnabled("ShadowCaster", false);
            }
            EditorUtility.SetDirty(m);
        }

        public static object InspectImport()
        {
            var manifest = Read();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(CurrentFolder + "/Models/MC00_Render.fbx");
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            var lookup = filters.ToDictionary(f => f.name);
            int materialMismatches = 0, triangleMismatches = 0;
            var missing = new List<string>();
            foreach (var item in manifest.geometry)
            {
                if (!lookup.TryGetValue(item.name, out var f)) { missing.Add(item.name); continue; }
                int triangles = Triangles(f.sharedMesh);
                if (triangles > item.triangles || triangles < item.triangles - item.degenerateTriangles) triangleMismatches++;
                // Importers may reorder submeshes and omit unused slots. Compare
                // actual per-material geometry, never reassign the Blender slot order.
                // Unity may remove only some zero-area triangles, so accept the
                // closed range bounded by source total and source nondegenerate total.
                var actual = MaterialCounts(f);
                if (item.materialTriangles.Any(x => !actual.TryGetValue(x.name, out int n) || (n > x.triangles || n < x.triangles - x.degenerate)) || actual.Keys.Any(k => !item.materials.Contains(k))) materialMismatches++;
            }
            var t = root.GetComponentsInChildren<Transform>(true).Single(x => x.name == "MC00_ROOT");
            return new { renderers=filters.Length, uniqueMeshes=filters.Select(f => f.sharedMesh).Distinct().Count(),
                triangles=filters.Sum(f => Triangles(f.sharedMesh)), expectedTriangles=manifest.renderTriangles,
                missing, materialMismatches, triangleMismatches,
                rootPosition=t.position.ToString("F4"), rootRotation=t.eulerAngles.ToString("F4"), rootScale=t.lossyScale.ToString("F4"),
                special=filters.Where(f => f.name.Contains("back_pad") || f.name.Contains("fitted_seat_cushion")).Select(f => new {name=f.name, uv0=f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord0),uv1=f.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1),tangents=f.sharedMesh.HasVertexAttribute(VertexAttribute.Tangent)}).ToArray() };
        }

        [MenuItem("Tools/Moon Environment/MC00 Game Export/2 - Create separate map candidate")]
        public static void CreateCandidate()
        {
            RequireEditMode();
            if (File.Exists(CurrentPrefabPath) || File.Exists(CurrentEnvironmentPath) || File.Exists(CurrentPreviewPath))
                throw new InvalidOperationException("Candidate already exists. Preserve it; choose a new version before rebuilding.");
            var manifest = Read();
            var sourceEnv = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalEnvironment);
            var oldMC00 = sourceEnv.GetComponentsInChildren<Transform>(true).Single(t => t.name == "MC00_ROOT");
            var oldLights = oldMC00.GetComponentsInChildren<Light>(true).ToDictionary(l => l.name);
            var assembly = new GameObject("MC00 Game Export");
            try
            {
                var render = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CurrentFolder + "/Models/MC00_Render.fbx"));
                render.transform.SetParent(assembly.transform, false);
                var filters = render.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name);
                if (filters.Count != manifest.renderObjects) throw new InvalidOperationException("Imported render count differs from author manifest.");
                int fallback = 0;
                foreach (var info in manifest.geometry)
                {
                    var f = filters[info.name];
                    var renderer = f.GetComponent<MeshRenderer>();
                    var mats = renderer.sharedMaterials.Select(m => m ? LoadMaterial(m.name) : null).ToArray();
                    if (mats.Any(m => !m) || mats.Length != f.sharedMesh.subMeshCount) throw new InvalidOperationException("Material/submesh count mismatch: " + info.name);
                    renderer.sharedMaterials = mats;
                    var counts = MaterialCounts(f);
                    if (info.materialTriangles.Any(x => !counts.TryGetValue(x.name, out int n) || (n > x.triangles || n < x.triangles - x.degenerate)))
                        throw new InvalidOperationException("Per-material triangle mismatch: " + info.name);
                    if (info.collisionSolid && !info.coveredByProxy)
                    {
                        f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
                        fallback++;
                    }
                }
                var collision = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CurrentFolder + "/Models/MC00_Collision.fbx"));
                collision.transform.SetParent(assembly.transform, false);
                collision.name = "MC00 Collision Proxies";
                foreach (var t in collision.GetComponentsInChildren<Transform>(true))
                    if (t.name == "MC00_ROOT") t.name = "MC00 Collision Origin";
                var proxyFilters = collision.GetComponentsInChildren<MeshFilter>(true);
                if (proxyFilters.Length != manifest.proxyObjects) throw new InvalidOperationException("Proxy count mismatch.");
                foreach (var f in proxyFilters)
                {
                    var mr = f.GetComponent<MeshRenderer>(); mr.enabled = false; mr.shadowCastingMode = ShadowCastingMode.Off;
                    f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
                }
                int retainedLights = 0;
                foreach (var info in manifest.lights)
                {
                    var anchor = render.GetComponentsInChildren<Transform>(true).Single(t => t.name == info.source_name);
                    var go = new GameObject("Runtime Light"); go.transform.SetParent(anchor, false);
                    // Explicit basis conversion from Blender metres to the existing
                    // map's Unity FBX convention. Do not assume Empty.forward is a light axis.
                    go.transform.position = BlenderPoint(info.position_world_m);
                    go.transform.rotation = Quaternion.LookRotation(BlenderPoint(info.forward_world_minus_local_z), BlenderPoint(info.up_world_plus_local_y));
                    var light = go.AddComponent<Light>();
                    if (oldLights.TryGetValue(info.source_name, out var previous))
                    {
                        EditorUtility.CopySerialized(previous, light); retainedLights++;
                        go.name = "Runtime Light";
                    }
                    else
                    {
                        light.type = LightType.Spot; light.intensity = Mathf.Clamp(info.native_reference_only.energy * .02f, 2, 24);
                        light.color = ColorOf(info.native_reference_only.color); light.range = 8;
                        light.spotAngle = 130; light.innerSpotAngle = 80;
                        light.shadows = LightShadows.Soft; light.shadowNearPlane = .05f;
                        light.shadowBias = .03f; light.shadowNormalBias = .15f;
                    }
                    var data = go.AddComponent<UniversalAdditionalLightData>(); data.softShadowQuality = SoftShadowQuality.Low;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
                }
                var reuse = MC00MeshReuseTools.OptimizeSubtree(render);
                var shadows = BuildingShadowTools.Optimize(render);
                PrefabUtility.SaveAsPrefabAsset(assembly, CurrentPrefabPath);
                if (!AssetDatabase.CopyAsset(OriginalEnvironment, CurrentEnvironmentPath)) throw new IOException("Could not copy environment.");
                var environment = PrefabUtility.LoadPrefabContents(CurrentEnvironmentPath);
                try
                {
                    var previous = environment.GetComponentsInChildren<Transform>(true).Single(t => t.name == "MC00_ROOT");
                    var buildings = previous.parent;
                    // Unpack only the old building model instance in this copied
                    // environment, so removing MC00 leaves all other base modules intact.
                    PrefabUtility.UnpackPrefabInstance(buildings.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    UnityEngine.Object.DestroyImmediate(previous.gameObject);
                    var replacement = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CurrentPrefabPath));
                    replacement.transform.SetParent(buildings, false);
                    MoonBaseApollo12Tools.ConfigureLightBudget(buildings.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(environment, CurrentEnvironmentPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(environment); }
                CreatePreview();
                Directory.CreateDirectory("Library/BuildValidation");
                File.WriteAllText(CurrentSource + "/IntegrationReport.json", JsonUtility.ToJson(new IntegrationReport {
                    renderers=filters.Count, triangles=filters.Values.Sum(f=>Triangles(f.sharedMesh)), uniqueBefore=reuse.uniqueBefore,
                    uniqueAfter=reuse.uniqueAfter, proxies=proxyFilters.Length, fallbackColliders=fallback,
                    lights=manifest.lights.Length, lightsWithPriorSettings=retainedLights, casters=shadows.castersAfter,
                    sourceSha256=manifest.sourceSha256 }, true));
                AssetDatabase.SaveAssets();
                Debug.Log("MC00 candidate created: " + CurrentPreviewPath + ". Original gameplay map retained; no runtime/performance acceptance claimed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(assembly); }
        }

        [Serializable] class IntegrationReport
        {
            public int renderers, triangles, uniqueBefore, uniqueAfter, proxies, fallbackColliders, lights, lightsWithPriorSettings, casters;
            public string sourceSha256;
        }

        static void CreatePreview()
        {
            if (!AssetDatabase.CopyAsset(OriginalPreview, CurrentPreviewPath)) throw new IOException("Could not copy preview.");
            var scene = EditorSceneManager.OpenScene(CurrentPreviewPath, OpenSceneMode.Additive);
            try
            {
                var old = scene.GetRootGameObjects().Single(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == OriginalEnvironment);
                var next = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CurrentEnvironmentPath), scene);
                next.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation); next.transform.localScale = old.transform.localScale;
                UnityEngine.Object.DestroyImmediate(old);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        static void RequireEditMode() { if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first."); }
        static int Triangles(Mesh mesh) { int n=0; for(int i=0;i<mesh.subMeshCount;i++) n+=(int)mesh.GetIndexCount(i)/3; return n; }
        static Dictionary<string, int> MaterialCounts(MeshFilter f)
        {
            var counts = new Dictionary<string, int>();
            var mats = f.GetComponent<MeshRenderer>().sharedMaterials;
            for (int i=0; i<f.sharedMesh.subMeshCount; i++)
            {
                string name = i<mats.Length && mats[i] ? mats[i].name : "";
                counts.TryGetValue(name, out int n); counts[name] = n + (int)f.sharedMesh.GetIndexCount(i)/3;
            }
            return counts;
        }
        static Vector3 BlenderPoint(float[] v) => new Vector3(-v[0], v[2], -v[1]);
        static Color ColorOf(float[] v) => new Color(v[0],v[1],v[2],v.Length>3?v[3]:1);
        static string MaterialPath(string name) => CurrentFolder + "/Materials/" + name.Replace('/', '_') + ".mat";
        static Material LoadMaterial(string name) => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(name));
        static Texture2D Texture(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(CurrentFolder + "/" + path);
    }

}
