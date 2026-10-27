// Assemble only a new LR06 display scene and prefab. Never save the formal map.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

public static class LR06AssembleFullPreview
{
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static object Field(object value, string name) => value.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(value, new object[] { name });
    static T Read<T>(object value) => (T)value.GetType().GetMethod("ToObject", new[] { typeof(Type) }).Invoke(value, new object[] { typeof(T) });
    static object[] Rows(object value) => ((IEnumerable)value).Cast<object>().ToArray();
    static readonly string Folder = "Assets/MoonEnvironment/Variants/LR06FullPreview";
    static readonly string FormalScene = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
    static string Serialize(Type json, object value) => (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { value });

    sealed class TerrainRecipe
    {
        public string name;
        public TerrainData data;
        public Material material;
        public Vector3 position, scale;
        public Quaternion rotation;
        public float heightmapPixelError, basemapDistance, treeDistance, detailDistance;
        public bool drawInstanced, drawTreesAndFoliage;
    }

    static Bounds WorldBounds(Transform transform, Bounds local)
    {
        var c = local.center; var e = local.extents;
        var points = new Vector3[8]; int k = 0;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            points[k++] = c + Vector3.Scale(e, new Vector3(x, y, z));
        return GeometryUtility.CalculateBounds(points, transform.localToWorldMatrix);
    }

    public static object Run()
    {
        Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer,
            "Unity Editor must be idle for the display assembly");
        Need(System.IO.Directory.Exists(Folder), "Copy the verified preview payload into Assets first");
        var oldScene = SceneManager.GetActiveScene();
        string oldScenePath = oldScene.path;
        bool oldSceneDirty = oldScene.isDirty;
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var plan = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { System.IO.File.ReadAllText(Folder + "/PreviewPlan.json"), typeof(object) });
        var parts = Rows(Field(plan, "parts"));
        var materialRows = Rows(Field(plan, "materials"));
        var lightRows = Rows(Field(plan, "preview_light_proxies"));
        Need(parts.Length == 65 && materialRows.Length == 449 && lightRows.Length <= 32, "Unexpected preview plan counts");
        string scenePath = Folder + "/Scenes/LR06_Full_Display.unity";
        string prefabPath = Folder + "/LR06_Full_Display.prefab";
        Need(!System.IO.File.Exists(scenePath) && !System.IO.File.Exists(prefabPath), "Preview scene or prefab already exists; inspect before retrying");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        Need(shader, "URP Lit shader is unavailable");

        // Reuse only existing display TerrainData and materials. Do not copy their colliders or scripts.
        var terrainRecipes = new List<TerrainRecipe>();
        var terrainPreview = EditorSceneManager.OpenPreviewScene(FormalScene);
        try
        {
            Need(terrainPreview.IsValid() && terrainPreview.isLoaded, "Formal scene preview did not open");
            foreach (var t in terrainPreview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Terrain>(true)))
            {
                if (!t || !t.enabled || !t.gameObject.activeInHierarchy || !t.drawHeightmap || !t.terrainData) continue;
                terrainRecipes.Add(new TerrainRecipe { name = t.name, data = t.terrainData, material = t.materialTemplate,
                    position = t.transform.position, rotation = t.transform.rotation, scale = t.transform.lossyScale,
                    heightmapPixelError = t.heightmapPixelError, basemapDistance = t.basemapDistance,
                    treeDistance = t.treeDistance, detailDistance = t.detailObjectDistance,
                    drawInstanced = t.drawInstanced, drawTreesAndFoliage = t.drawTreesAndFoliage });
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(terrainPreview); }
        Need(terrainRecipes.Count > 0, "No enabled display terrain found; do not substitute a different DEM");
        Need(oldScene.path == oldScenePath && oldScene.isDirty == oldSceneDirty, "Existing scene changed during terrain inspection");

        // The material recipes explicitly identify procedural approximations.
        var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        int textureMaterials = 0, emissiveMaterials = 0;
        for (int i = 0; i < materialRows.Length; i++)
        {
            var row = materialRows[i];
            string name = Read<string>(Field(row, "name"));
            string path = Folder + "/Materials/LR06_" + i.ToString("D3") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader) { name = name, enableInstancing = true };
                var c = Read<float[]>(Field(row, "base_color"));
                material.SetColor("_BaseColor", new Color(c[0], c[1], c[2], c[3]).gamma);
                material.SetFloat("_Metallic", Mathf.Clamp01(Read<float>(Field(row, "metallic"))));
                material.SetFloat("_Smoothness", 1 - Mathf.Clamp01(Read<float>(Field(row, "roughness"))));
                var texture = Field(row, "base_texture");
                if (texture != null && !string.IsNullOrEmpty(texture.ToString()))
                {
                    string hash = Read<string>(Field(texture, "sha256"));
                    var image = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Textures/" + hash + ".png");
                    Need(image, "Missing direct source texture " + hash);
                    material.SetTexture("_BaseMap", image);
                }
                float strength = Read<float>(Field(row, "emission_strength"));
                if (strength > 0)
                {
                    var emission = Read<float[]>(Field(row, "emission_color"));
                    material.SetColor("_EmissionColor", new Color(emission[0], emission[1], emission[2], emission[3]) * strength);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                    material.EnableKeyword("_EMISSION");
                }
                AssetDatabase.CreateAsset(material, path);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            Need(!materials.ContainsKey(name), "Duplicate source material name: " + name);
            materials.Add(name, material);
            if (material.GetTexture("_BaseMap")) textureMaterials++;
            if (material.IsKeywordEnabled("_EMISSION")) emissiveMaterials++;
        }
        var fallback = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Materials/LR06_Unspecified.mat");
        if (!fallback)
        {
            fallback = new Material(shader) { name = "LR06_UnspecifiedSourceMaterial" };
            fallback.SetColor("_BaseColor", new Color(.42f, .42f, .42f));
            AssetDatabase.CreateAsset(fallback, Folder + "/Materials/LR06_Unspecified.mat");
            AssetDatabase.SaveAssetIfDirty(fallback);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture renderTarget = null;
        Texture2D pixels = null;
        var priorActive = RenderTexture.active;
        try
        {
            Need(scene.IsValid() && scene.isLoaded, "Could not create independent display scene");
            if (SceneManager.GetActiveScene() != scene)
                Need(SceneManager.SetActiveScene(scene), "Could not activate independent display scene");
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.18f, .18f, .20f);
            RenderSettings.fog = false;
            var root = new GameObject("LR06_FULL_DISPLAY_APPROXIMATE_MATERIALS");
            SceneManager.MoveGameObjectToScene(root, scene);
            var groupObjects = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            var failures = new List<string>();
            var checks = new List<object>();
            var triangleDifferences = new List<object>();
            int totalTriangles = 0, totalRenderers = 0, fallbackSlots = 0, unmatchedNamedSlots = 0;
            float worstOutside = 0;
            foreach (var row in parts)
            {
                string sourcePath = Read<string>(Field(row, "path"));
                string group = Read<string>(Field(row, "group"));
                string modelPath = Folder + "/Models/" + System.IO.Path.GetFileName(sourcePath);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                Need(model, "Missing imported model: " + modelPath);
                if (!groupObjects.TryGetValue(group, out var parent))
                {
                    parent = new GameObject(group); parent.transform.SetParent(root.transform, false);
                    groupObjects.Add(group, parent);
                }
                var instance = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
                Need(instance, "Could not instantiate " + modelPath);
                instance.transform.SetParent(parent.transform, false);
                instance.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var filters = instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray();
                Need(filters.Length > 0, "Imported part has no meshes: " + modelPath);
                var expectedSlots = Read<string[]>(Field(row, "materials"));
                int triangles = 0;
                bool hasBounds = false;
                var actualBounds = new Bounds();
                foreach (var filter in filters)
                {
                    var mesh = filter.sharedMesh;
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        Need(mesh.GetTopology(sub) == MeshTopology.Triangles, "Non-triangle mesh in " + modelPath);
                        triangles += (int)(mesh.GetIndexCount(sub) / 3);
                    }
                    var b = WorldBounds(filter.transform, mesh.bounds);
                    if (!hasBounds) { actualBounds = b; hasBounds = true; } else actualBounds.Encapsulate(b);
                    var renderer = filter.GetComponent<MeshRenderer>();
                    Need(renderer, "Imported mesh has no renderer: " + filter.name);
                    var old = renderer.sharedMaterials;
                    var replacement = new Material[old.Length];
                    for (int i = 0; i < old.Length; i++)
                    {
                        string name = old[i] ? old[i].name : null;
                        if (name != null && materials.TryGetValue(name, out var match)) replacement[i] = match;
                        else if (expectedSlots.Length == old.Length && expectedSlots[i] != null && materials.TryGetValue(expectedSlots[i], out match))
                            replacement[i] = match;
                        else
                        {
                            replacement[i] = fallback; fallbackSlots++;
                            if (name != null && name != "Default-Material")
                            {
                                unmatchedNamedSlots++;
                                if (failures.Count < 100) failures.Add(modelPath + ": " + name);
                            }
                        }
                    }
                    renderer.sharedMaterials = replacement;
                    totalRenderers++;
                }
                int expectedTriangles = Read<int>(Field(row, "triangles"));
                Need(triangles > 0, "Imported part contains no triangles: " + modelPath);
                if (triangles != expectedTriangles) triangleDifferences.Add(new { modelPath, expectedTriangles, actualTriangles = triangles });
                var sourceBounds = Read<float[][]>(Field(row, "source_world_bounds"));
                var sourceMin = new Vector3(sourceBounds[0][0], sourceBounds[0][2], sourceBounds[0][1]);
                var sourceMax = new Vector3(sourceBounds[1][0], sourceBounds[1][2], sourceBounds[1][1]);
                float outside = Mathf.Max(0, sourceMin.x - actualBounds.min.x, sourceMin.y - actualBounds.min.y, sourceMin.z - actualBounds.min.z,
                    actualBounds.max.x - sourceMax.x, actualBounds.max.y - sourceMax.y, actualBounds.max.z - sourceMax.z);
                worstOutside = Mathf.Max(worstOutside, outside);
                Need(outside < .05f, "Imported bounds leave the source envelope in " + modelPath + " by " + outside + " m");
                totalTriangles += triangles;
                checks.Add(new { modelPath, group, triangles, expectedTriangles, boundsOutsideSourceMetres = outside });
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool prefabSaved);
            Need(prefabSaved, "Could not save independent display prefab");

            var terrainRoot = new GameObject("Existing_DEM_Display_References");
            SceneManager.MoveGameObjectToScene(terrainRoot, scene);
            foreach (var item in terrainRecipes)
            {
                var go = new GameObject(item.name + " (display reference)");
                go.transform.SetParent(terrainRoot.transform, false);
                go.transform.SetPositionAndRotation(item.position, item.rotation);
                go.transform.localScale = item.scale;
                var terrain = go.AddComponent<Terrain>();
                terrain.terrainData = item.data; terrain.materialTemplate = item.material;
                terrain.heightmapPixelError = item.heightmapPixelError; terrain.basemapDistance = item.basemapDistance;
                terrain.treeDistance = item.treeDistance; terrain.detailObjectDistance = item.detailDistance;
                terrain.drawInstanced = item.drawInstanced; terrain.drawTreesAndFoliage = item.drawTreesAndFoliage;
            }
            var lighting = new GameObject("LR06_Illustrative_Lighting_31_Proxies_No_Shadows");
            SceneManager.MoveGameObjectToScene(lighting, scene);
            var sun = new GameObject("Neutral overview sun"); sun.transform.SetParent(lighting.transform, false);
            var directional = sun.AddComponent<Light>(); directional.type = LightType.Directional;
            directional.intensity = .8f; directional.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(48, -32, 0);
            foreach (var row in lightRows)
            {
                var p = Read<float[]>(Field(row, "source_position_blender"));
                var color = Read<float[]>(Field(row, "color"));
                var go = new GameObject(Read<string>(Field(row, "name"))); go.transform.SetParent(lighting.transform, false);
                go.transform.position = new Vector3(p[0], p[2], p[1]);
                var light = go.AddComponent<Light>(); light.type = LightType.Point;
                light.color = new Color(color[0], color[1], color[2]);
                light.intensity = Read<float>(Field(row, "unity_intensity_illustrative"));
                light.range = Read<float>(Field(row, "unity_range_metres")); light.shadows = LightShadows.None;
            }
            var cameraObject = new GameObject("LR06_Overview_Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .043f, .055f);
            camera.fieldOfView = 55; camera.nearClipPlane = .1f; camera.farClipPlane = 5000;
            camera.transform.position = new Vector3(1250, 950, 1400);
            camera.transform.LookAt(new Vector3(2000, 220, 2180));
            Need(EditorSceneManager.SaveScene(scene, scenePath), "Could not save independent display scene");

            string output = "Library/BuildValidation/LR06FullPreview";
            System.IO.Directory.CreateDirectory(output);
            renderTarget = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            Need(renderTarget.Create(), "Could not create overview target");
            pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            var captures = new List<string>();
            void Capture(string name, Vector3 position, Vector3 look)
            {
                camera.transform.position = position; camera.transform.LookAt(look);
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = renderTarget });
                RenderTexture.active = renderTarget;
                pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply();
                string path = output + "/" + name + ".png";
                System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG()); captures.Add(path);
            }
            Capture("overview", new Vector3(1250, 950, 1400), new Vector3(2000, 220, 2180));
            Capture("north_corridor", new Vector3(1998, 253.3f, 2470), new Vector3(1998, 254.3f, 2461));
            Capture("central_structure", new Vector3(1790, 420, 1830), new Vector3(2000, 275, 2110));
            var report = new { scope = "independent full-layout Unity display; material and light approximations are explicit",
                scene = scenePath, prefab = prefabPath, parts = parts.Length, instances = Read<int>(Field(plan, "visible_instances")),
                triangles = totalTriangles, expectedTriangles = Read<int>(Field(plan, "triangles")), triangleDifferences,
                renderers = totalRenderers, materials = materials.Count,
                directTextureMaterials = textureMaterials, emissiveMaterials, fallbackSlots, unmatchedNamedSlots, unmatchedNames = failures,
                terrainDisplayReferences = terrainRecipes.Select(t => AssetDatabase.GetAssetPath(t.data)).ToArray(),
                sourceLightsRecorded = Read<int>(Field(plan, "source_light_count")), warmProxies = lightRows.Length,
                proxyShadows = false, maxBoundsOutsideSourceMetres = worstOutside, checks, captures,
                noGameplayOrCollisions = true, formalSceneChanged = false, lightingFinal = false };
            System.IO.File.WriteAllText(output + "/UnityFullPreview.json", Serialize(json, report));
            return new { report.scope, report.scene, report.prefab, report.parts, report.instances, report.triangles,
                report.expectedTriangles, report.triangleDifferences, report.renderers, report.materials, report.directTextureMaterials, report.emissiveMaterials, report.fallbackSlots, report.unmatchedNamedSlots,
                report.terrainDisplayReferences, report.sourceLightsRecorded, report.warmProxies, report.maxBoundsOutsideSourceMetres, report.captures };
        }
        finally
        {
            RenderTexture.active = priorActive;
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            if (renderTarget) { renderTarget.Release(); UnityEngine.Object.DestroyImmediate(renderTarget); }
            SceneManager.SetActiveScene(oldScene);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            Need(oldScene.path == oldScenePath && oldScene.isDirty == oldSceneDirty, "Original active scene changed during assembly");
        }
    }
}
