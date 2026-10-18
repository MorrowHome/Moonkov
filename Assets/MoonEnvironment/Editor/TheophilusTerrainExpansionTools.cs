using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Author the eight neighbouring 25 cm tiles without resampling the authored centre.</summary>
    public static class TheophilusTerrainExpansionTools
    {
        private const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        private const string AssetRoot = "Assets/MoonEnvironment/Variants/TheophilusBase/Terrain/Expanded";
        private const string RootName = "Expanded Lunar Surface / 1536m";
        private const string SourceRoot = "Assets/MoonEnvironment/Variants/TheophilusBase/Terrain/R21/";
        private const int Resolution = 2049;
        private const float TileSize = 512;
        private static readonly Vector2 CentreOrigin = new(1536, 1920);
        private static readonly Vector2 Min = CentreOrigin - Vector2.one * TileSize;
        private static readonly Vector2 Max = CentreOrigin + Vector2.one * TileSize * 2;

        private sealed class Field
        {
            public readonly float[,] Far, Near;
            public readonly float[,,] FarAlpha, NearAlpha;
            public readonly TerrainData FarData, NearData;
            public readonly List<Vector3> Craters = new();

            public Field()
            {
                FarData = AssetDatabase.LoadAssetAtPath<TerrainData>(SourceRoot + "R21_FarDisplay.asset");
                NearData = AssetDatabase.LoadAssetAtPath<TerrainData>(SourceRoot + "R21_NearDisplay.asset");
                Far = FarData.GetHeights(0, 0, FarData.heightmapResolution, FarData.heightmapResolution);
                Near = NearData.GetHeights(0, 0, Resolution, Resolution);
                FarAlpha = FarData.GetAlphamaps(0, 0, FarData.alphamapWidth, FarData.alphamapHeight);
                NearAlpha = NearData.GetAlphamaps(0, 0, NearData.alphamapWidth, NearData.alphamapHeight);
                var random = new System.Random(1807319);
                for (int i = 0; i < 1900; i++)
                {
                    float radius = i < 48 ? Range(random, 9, 24) : Range(random, 1, 6);
                    var p = new Vector2(Range(random, Min.x + 48, Max.x - 48), Range(random, Min.y + 48, Max.y - 48));
                    // Do not disturb the centre edge, its grading, or its shaft cutout.
                    if (DistanceToCentre(p) < radius * 2 + 32) continue;
                    if (FarData.GetInterpolatedNormal(p.x / 4096, p.y / 4096).y < .75f) continue;
                    Craters.Add(new Vector3(p.x, p.y, radius));
                }
            }

            public float Height(float x, float z)
            {
                float height = Sample(Far, x / 4096, z / 4096);
                float distance = DistanceToCentre(new Vector2(x, z));
                float blend = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(distance / 32));
                if (blend > 0)
                {
                    float nx = Mathf.Clamp(x, CentreOrigin.x, CentreOrigin.x + TileSize);
                    float nz = Mathf.Clamp(z, CentreOrigin.y, CentreOrigin.y + TileSize);
                    float difference = Sample(Near, (nx - CentreOrigin.x) / TileSize, (nz - CentreOrigin.y) / TileSize)
                        - Sample(Far, nx / 4096, nz / 4096);
                    height += difference * blend;
                }
                float outside = Mathf.Min(Mathf.Min(x - Min.x, Max.x - x), Mathf.Min(z - Min.y, Max.y - z));
                float fade = Mathf.SmoothStep(0, 1, Mathf.Clamp01(outside / 32)) * (1 - blend);
                float roughness = (Mathf.PerlinNoise(x * .21f, z * .21f) - .5f) * .16f
                    + (Mathf.PerlinNoise(x * .83f + 17, z * .83f + 41) - .5f) * .045f;
                return height + roughness * fade / NearData.size.y;
            }
        }

        // Each call authors one tile to keep Editor requests bounded and recoverable.
        [MenuItem("Tools/Moon Environment/Theophilus Expansion/Build Next Tile")]
        private static void BuildNextTileMenu() => Debug.Log(BuildNextTile());

        public static string BuildNextTile()
        {
            Scene scene = RequireScene();
            var root = FindRoot(scene);
            if (!root)
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Expand lunar surface");
            }
            Directory.CreateDirectory(AssetRoot);
            AssetDatabase.Refresh();
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    if (x == 0 && z == 0) continue;
                    string name = $"Lunar Detail [{x},{z}] / 25cm";
                    bool built = false;
                    foreach (Transform child in root.transform) if (child.name == name) { built = true; break; }
                    if (built) continue;
                    string path = $"{AssetRoot}/Detail_{x}_{z}.asset";
                    if (File.Exists(path)) throw new InvalidOperationException("Unbound terrain asset already exists: " + path);
                    var field = new Field();
                    var data = Object.Instantiate(field.NearData);
                    data.name = $"Theophilus Detail {x},{z}";
                    Vector2 origin = CentreOrigin + new Vector2(x, z) * TileSize;
                    var heights = new float[Resolution, Resolution];
                    for (int row = 0; row < Resolution; row++)
                        for (int col = 0; col < Resolution; col++)
                            heights[row, col] = field.Height(origin.x + col * .25f, origin.y + row * .25f);
                    int craterCount = StampCraters(heights, field, origin);
                    data.SetHeights(0, 0, heights);
                    var holes = new bool[data.holesResolution, data.holesResolution];
                    for (int row = 0; row < holes.GetLength(0); row++)
                        for (int col = 0; col < holes.GetLength(1); col++) holes[row, col] = true;
                    data.SetHoles(0, 0, holes);
                    Paint(data, field, origin);
                    AssetDatabase.CreateAsset(data, path);
                    GameObject go = Terrain.CreateTerrainGameObject(data);
                    go.name = name;
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = new Vector3(origin.x, 0, origin.y);
                    go.isStatic = true;
                    Undo.RegisterCreatedObjectUndo(go, "Add lunar detail tile");
                    Terrain template = FindCentre(scene);
                    Terrain terrain = go.GetComponent<Terrain>();
                    terrain.materialTemplate = template.materialTemplate;
                    terrain.heightmapPixelError = template.heightmapPixelError;
                    terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
                    terrain.allowAutoConnect = true;
                    terrain.groupingID = 71;
                    terrain.drawInstanced = template.drawInstanced;
                    int rocks = AddRocks(scene, go.transform, terrain, x, z);
                    EditorSceneManager.MarkSceneDirty(scene);
                    AssetDatabase.SaveAssets();
                    return $"Built [{x},{z}]: {craterCount} crater stamps, {rocks} LOD rocks, 512m / 2049 samples";
                }
            return "All eight detail tiles already exist.";
        }

        private static int StampCraters(float[,] heights, Field field, Vector2 origin)
        {
            int count = 0;
            foreach (Vector3 crater in field.Craters)
            {
                float extent = crater.z * 1.8f;
                int x0 = Mathf.Max(0, Mathf.CeilToInt((crater.x - extent - origin.x) * 4));
                int x1 = Mathf.Min(Resolution - 1, Mathf.FloorToInt((crater.x + extent - origin.x) * 4));
                int z0 = Mathf.Max(0, Mathf.CeilToInt((crater.y - extent - origin.y) * 4));
                int z1 = Mathf.Min(Resolution - 1, Mathf.FloorToInt((crater.y + extent - origin.y) * 4));
                if (x0 > x1 || z0 > z1) continue;
                count++;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = origin.x + x * .25f, wz = origin.y + z * .25f;
                        float r = Vector2.Distance(new Vector2(wx, wz), new Vector2(crater.x, crater.y)) / crater.z;
                        if (r > 1.8f) continue;
                        float bowl = -Mathf.Pow(Mathf.Max(0, 1 - r * r), 2);
                        float rim = Mathf.Exp(-Mathf.Pow((r - 1) / .2f, 2)) * .32f;
                        float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.4f, 1.8f, r));
                        heights[z, x] = Mathf.Clamp01(heights[z, x] + (bowl + rim) * fade * crater.z * .14f / field.NearData.size.y);
                    }
            }
            return count;
        }

        private static void Paint(TerrainData data, Field field, Vector2 origin)
        {
            int res = data.alphamapResolution;
            var alpha = new float[res, res, 2];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float u = x / (res - 1f), v = z / (res - 1f);
                    float wx = origin.x + u * TileSize, wz = origin.y + v * TileSize;
                    float blend = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(DistanceToCentre(new Vector2(wx, wz)) / 32));
                    float low = SampleAlpha(field.FarAlpha, wx / 4096, wz / 4096);
                    float near = SampleAlpha(field.NearAlpha, (wx - CentreOrigin.x) / TileSize, (wz - CentreOrigin.y) / TileSize);
                    float edge = Mathf.Min(Mathf.Min(wx - Min.x, Max.x - wx), Mathf.Min(wz - Min.y, Max.y - wz));
                    float interior = Mathf.SmoothStep(0, 1, Mathf.Clamp01(edge / 32)) * (1 - blend);
                    float slope = 1 - data.GetInterpolatedNormal(u, v).y;
                    low = Mathf.Lerp(low, near, blend);
                    low = Mathf.Clamp01(low + ((Mathf.PerlinNoise(wx * .035f, wz * .035f) - .5f) * .2f + slope * .3f) * interior);
                    alpha[z, x, 0] = low;
                    alpha[z, x, 1] = 1 - low;
                }
            data.SetAlphamaps(0, 0, alpha);
        }

        private static int AddRocks(Scene scene, Transform parent, Terrain terrain, int tx, int tz)
        {
            MoonRockLod sourceLod = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == RootName) continue;
                sourceLod = root.GetComponentInChildren<MoonRockLod>(true);
                if (sourceLod) break;
            }
            if (!sourceLod) throw new InvalidOperationException("Existing rock LOD configuration is required.");
            var serialized = new SerializedObject(sourceLod);
            Mesh[] sources = Meshes(serialized, "sourceMeshes"), middle = Meshes(serialized, "middleMeshes"), distant = Meshes(serialized, "distantMeshes");
            var material = new[] { AssetDatabase.LoadAssetAtPath<Material>("Assets/MoonEnvironment/Materials/moon_rock_01.mat"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/MoonEnvironment/Materials/moon_rock_03.mat") };
            var rockRoot = new GameObject("LOD Rocks").transform;
            rockRoot.SetParent(parent, false);
            var random = new System.Random(unchecked(7319 ^ tx * 73856093 ^ tz * 19349663));
            int count = 0;
            for (int attempt = 0; attempt < 768 * 8 && count < 768; attempt++)
            {
                float u = Range(random, .01f, .99f), v = Range(random, .01f, .99f);
                Vector3 normal = terrain.terrainData.GetInterpolatedNormal(u, v);
                if (normal.y < .76f) continue;
                float cluster = Mathf.PerlinNoise((terrain.transform.position.x + u * TileSize) * .018f,
                    (terrain.transform.position.z + v * TileSize) * .018f);
                if (random.NextDouble() > Mathf.Lerp(.18f, .95f, cluster)) continue;
                int variant = random.Next(sources.Length);
                Mesh mesh = sources[variant];
                float diameter = Mathf.Lerp(.22f, 3.8f, Mathf.Pow((float)random.NextDouble(), 2.7f));
                float meshWidth = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
                float scale = diameter / meshWidth;
                var go = new GameObject("Moon Rock " + variant) { isStatic = true };
                go.transform.SetParent(rockRoot, false);
                Vector3 position = terrain.transform.position + new Vector3(u * TileSize, terrain.terrainData.GetInterpolatedHeight(u, v), v * TileSize);
                Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Range(random, 0, 360), 0);
                go.transform.SetPositionAndRotation(position - normal * diameter * .12f - rotation * (mesh.bounds.center * scale), rotation);
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material[variant];
                // All peers load the same saved collision, independent of the viewer's mesh tier.
                if (diameter >= .65f) go.AddComponent<MeshCollider>().sharedMesh = distant[variant];
                var grounding = go.AddComponent<MoonRockGrounding>();
                grounding.Configure(new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, position)),
                    Mathf.Clamp(diameter * .06f, .025f, .2f), Range(random, .82f, 1.15f));
                grounding.SetFreshness(Range(random, 0, 1));
                count++;
            }
            var lod = rockRoot.gameObject.AddComponent<MoonRockLod>();
            // Reuse the existing sources, reduced meshes, and distance settings verbatim.
            EditorUtility.CopySerialized(sourceLod, lod);
            lod.Configure(rockRoot, sources, middle, distant);
            return count;
        }

        [MenuItem("Tools/Moon Environment/Theophilus Expansion/Connect and Save")]
        private static void FinishMenu() => Debug.Log(Finish());

        public static string Finish()
        {
            Scene scene = RequireScene();
            GameObject root = FindRoot(scene);
            if (!root || root.GetComponentsInChildren<Terrain>().Length != 8)
                throw new InvalidOperationException("Build all eight tiles before finishing.");
            Terrain centre = FindCentre(scene), far = null;
            foreach (var r in scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Terrain>(true)) if (t.terrainData.size.x > 1000) far = t;
            var grid = new Dictionary<Vector2Int, Terrain> { [Vector2Int.zero] = centre };
            foreach (var t in root.GetComponentsInChildren<Terrain>())
                grid.Add(new Vector2Int(Mathf.RoundToInt((t.transform.position.x - CentreOrigin.x) / TileSize),
                    Mathf.RoundToInt((t.transform.position.z - CentreOrigin.y) / TileSize)), t);
            foreach (var pair in grid)
            {
                Undo.RecordObject(pair.Value, "Connect lunar terrain neighbours");
                Terrain At(Vector2Int offset) => grid.TryGetValue(pair.Key + offset, out var t) ? t : null;
                // Auto-connect rebuilds these links when the saved scene is loaded.
                pair.Value.allowAutoConnect = true;
                pair.Value.groupingID = 71;
                pair.Value.SetNeighbors(At(Vector2Int.left), At(Vector2Int.up), At(Vector2Int.right), At(Vector2Int.down));
                EditorUtility.SetDirty(pair.Value);
                if (PrefabUtility.IsPartOfPrefabInstance(pair.Value))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(pair.Value);
            }
            Undo.RecordObjects(new Object[] { far, far.GetComponent<TerrainCollider>() }, "Expand far terrain cutout");
            far.terrainData = CutFar("FarDisplay", "R21_FarDisplay.asset");
            far.GetComponent<TerrainCollider>().terrainData = CutFar("FarCollision", "R21_FarCollision.asset");
            // Terrain is nested in the imported environment prefab. Dirtying the scene
            // alone does not preserve its TerrainData property when reopening it.
            if (PrefabUtility.IsPartOfPrefabInstance(far))
                PrefabUtility.RecordPrefabInstancePropertyModifications(far);
            if (PrefabUtility.IsPartOfPrefabInstance(far.GetComponent<TerrainCollider>()))
                PrefabUtility.RecordPrefabInstancePropertyModifications(far.GetComponent<TerrainCollider>());
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Physics.SyncTransforms();
            return "Saved 1536 x 1536m / 9 tiles; original centre and shaft data retained; far display/collision cutouts expanded.";
        }

        private static TerrainData CutFar(string name, string source)
        {
            string path = AssetRoot + "/" + name + ".asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data) return data;
            data = Object.Instantiate(AssetDatabase.LoadAssetAtPath<TerrainData>(SourceRoot + source));
            data.name = "Expanded " + name;
            int res = data.holesResolution;
            bool[,] holes = data.GetHoles(0, 0, res, res);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = x * data.size.x / res, wz = z * data.size.z / res;
                    if (wx >= Min.x && wx < Max.x && wz >= Min.y && wz < Max.y) holes[z, x] = false;
                }
            data.SetHoles(0, 0, holes);
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        private static Mesh[] Meshes(SerializedObject so, string field)
        {
            var p = so.FindProperty(field);
            var meshes = new Mesh[p.arraySize];
            for (int i = 0; i < meshes.Length; i++) meshes[i] = (Mesh)p.GetArrayElementAtIndex(i).objectReferenceValue;
            return meshes;
        }

        private static float DistanceToCentre(Vector2 p) => Vector2.Distance(p,
            new Vector2(Mathf.Clamp(p.x, CentreOrigin.x, CentreOrigin.x + TileSize), Mathf.Clamp(p.y, CentreOrigin.y, CentreOrigin.y + TileSize)));

        private static float Sample(float[,] values, float u, float v)
        {
            float x = Mathf.Clamp01(u) * (values.GetLength(1) - 1), z = Mathf.Clamp01(v) * (values.GetLength(0) - 1);
            int ix = Mathf.Min((int)x, values.GetLength(1) - 2), iz = Mathf.Min((int)z, values.GetLength(0) - 2);
            return Mathf.Lerp(Mathf.Lerp(values[iz, ix], values[iz, ix + 1], x - ix),
                Mathf.Lerp(values[iz + 1, ix], values[iz + 1, ix + 1], x - ix), z - iz);
        }

        private static float SampleAlpha(float[,,] values, float u, float v)
        {
            float x = Mathf.Clamp01(u) * (values.GetLength(1) - 1), z = Mathf.Clamp01(v) * (values.GetLength(0) - 1);
            int ix = Mathf.Min((int)x, values.GetLength(1) - 2), iz = Mathf.Min((int)z, values.GetLength(0) - 2);
            return Mathf.Lerp(Mathf.Lerp(values[iz, ix, 0], values[iz, ix + 1, 0], x - ix),
                Mathf.Lerp(values[iz + 1, ix, 0], values[iz + 1, ix + 1, 0], x - ix), z - iz);
        }

        private static Scene RequireScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before terrain authoring.");
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            return scene;
        }

        private static GameObject FindRoot(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects()) if (root.name == RootName) return root;
            return null;
        }

        private static Terrain FindCentre(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Terrain>(true))
                    if (t.terrainData == AssetDatabase.LoadAssetAtPath<TerrainData>(SourceRoot + "R21_NearDisplay.asset")) return t;
            throw new InvalidOperationException("Original centre terrain not found.");
        }

        private static float Range(System.Random random, float min, float max) => min + (max - min) * (float)random.NextDouble();
    }
}
