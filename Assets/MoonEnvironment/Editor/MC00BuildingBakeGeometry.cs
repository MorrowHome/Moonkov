using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Creates a world-space baking copy without changing the formal building's renderers or behaviours.</summary>
    public static class MC00BuildingBakeGeometry
    {
        public const string Folder = "Assets/MoonEnvironment/Variants/MC00BakedBuilding";
        public const string ScenePath = Folder + "/MC00Bake.unity";
        public const string ManifestPath = Folder + "/manifest.json";
        public const string BackupFolder = "Library/BuildValidation/MC00BakedBuilding";
        const string SourceFolder = "SourceData/Models/MC00LightControlsFullV02";
        const string RootName = "MC00_ROOT_Standard_YUp_Frame";

        [Serializable] public sealed class Chunk
        {
            public string name, meshPath, materialPath;
            public string[] sourcePaths;
            public int shadowMode, layer, lightmapIndex = -1;
            public uint renderingLayer;
            public bool receiveShadows;
            public Vector4 scaleOffset;
        }
        [Serializable] public sealed class Fixture
        {
            public string sourcePath, cloneName, roomId;
            public bool key;
        }
        [Serializable] public sealed class LooseRenderer
        {
            public string sourcePath, clonePath;
        }
        [Serializable] public sealed class RoomBounds
        {
            public string roomId;
            public Bounds bounds;
        }
        [Serializable] public sealed class Manifest
        {
            public int version = 1;
            public string sourceRootName, sourceScene, status;
            public int sourceRendererCount, staticSourceRendererCount, looseRendererCount, uvCompleted;
            public List<Chunk> chunks = new();
            public List<Fixture> lights = new();
            public List<LooseRenderer> looseRenderers = new();
            public string[] roomIds;
            public List<RoomBounds> rooms = new();
        }
        [Serializable] sealed class SceneBackup { public SceneSetup[] scenes; }
        sealed class Part { public MeshRenderer source; public Mesh mesh; public int subMesh; }

        static readonly Dictionary<Mesh, Mesh> readable = new();
        static readonly Queue<Mesh> unwrap = new();
        static readonly string[] movingWords = { "leaf", "telescop", "sliding", "drawer", "hinge", "doordog",
            "removable", "closeddoorshield", "cabinet_door", "crossslide", "button", "indicator", "keycap", "access" };
        static Scene scratch;
        static Manifest manifest;
        public static string Status { get; private set; } = "Idle";
        public static int ChunkCount => manifest?.chunks.Count ?? 0;
        public static int StaticSourceCount => manifest?.staticSourceRendererCount ?? 0;
        public static int LooseSourceCount => manifest?.looseRendererCount ?? 0;
        public static int CompletedChunks => manifest?.uvCompleted ?? 0;
        public static int PendingChunks => unwrap.Count;
        public static int LightCount => manifest?.lights.Count ?? 0;
        public static int RoomCount => manifest?.roomIds?.Length ?? 0;

        [MenuItem("Tools/Moon Environment/Building Bake/Prepare MC00 geometry")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning || unwrap.Count > 0)
                throw new InvalidOperationException("Finish Play, baking and pending geometry preparation first.");
            if (File.Exists(ScenePath) || File.Exists(ManifestPath))
                throw new InvalidOperationException("The building bake already exists; it will not be overwritten.");
            Status = "Preparing";
            try
            {
                var building = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Single(t => t.name == RootName);
                if (string.IsNullOrEmpty(building.gameObject.scene.path)) throw new InvalidOperationException("Save the formal scene first.");
                var authoring = JsonUtility.FromJson<MC00GameExportTools.Manifest>(File.ReadAllText(SourceFolder + "/unity-manifest.json"));
                var authoredNames = new HashSet<string>(authoring.geometry.Select(g => g.name), StringComparer.Ordinal);
                var motionNames = new HashSet<string>(Regex.Matches(File.ReadAllText(SourceFolder + "/AuthorNotes/MC00_MOTION_CONTRACT.json"),
                    "^    \"(MC[^\"]+)\":", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);
                var excluded = EmissionRenderers(building);
                var combined = new HashSet<MeshRenderer>();
                var represented = new HashSet<MeshRenderer>();
                ReadClusters(building, combined, represented);
                var sources = building.GetComponentsInChildren<MeshRenderer>(true).Where(r => !combined.Contains(r) &&
                    r.gameObject.activeInHierarchy && (represented.Contains(r) || r.enabled && !r.forceRenderingOff) &&
                    r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh).ToArray();
                var lights = building.GetComponentsInChildren<Light>(true);
                manifest = new Manifest { sourceRootName = RootName, sourceScene = building.gameObject.scene.path,
                    sourceRendererCount = sources.Length, status = Status };
                BackupWorkspace();
                Directory.CreateDirectory(Folder + "/Meshes");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scratch);
                var staticRoot = new GameObject("MC00 baked static geometry").transform;
                var looseRoot = new GameObject("MC00 non-GI source geometry").transform;
                var transforms = new Dictionary<Transform, Transform>();
                var buckets = new Dictionary<(Material, Vector3Int, uint, ShadowCastingMode, int, bool), List<Part>>();
                foreach (var renderer in sources)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    var materials = renderer.sharedMaterials;
                    // Authored furniture and equipment shells also receive baked light. Motion contracts,
                    // interactive renderers and unknown behaviour owners stay independent.
                    bool fixedGeometry = authoredNames.Contains(renderer.name) && !excluded.Contains(renderer) &&
                        !renderer.GetComponentInParent<Rigidbody>() && !renderer.GetComponentInParent<Animator>() &&
                        !renderer.GetComponentInParent<Animation>() &&
                        !HasUnknownBehaviour(renderer.transform, building) &&
                        renderer.localToWorldMatrix.determinant > 0 &&
                        !renderer.GetComponentsInParent<Transform>(true).Any(t => IsMoving(t.name, motionNames)) &&
                        filter.sharedMesh.subMeshCount == materials.Length && materials.All(m => m && m.renderQueue < 2500 &&
                            !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m)));
                    if (!fixedGeometry)
                    {
                        var node = CopyTransform(renderer.transform, looseRoot, transforms);
                        node.gameObject.layer = renderer.gameObject.layer;
                        node.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                        var copy = node.gameObject.AddComponent<MeshRenderer>();
                        EditorUtility.CopySerialized(renderer, copy);
                        copy.enabled = true; copy.forceRenderingOff = false; copy.receiveGI = ReceiveGI.LightProbes;
                        copy.lightmapIndex = -1; copy.scaleInLightmap = 0;
                        GameObjectUtility.SetStaticEditorFlags(node.gameObject, 0);
                        manifest.looseRenderers.Add(new LooseRenderer { sourcePath = RelativePath(renderer.transform, building),
                            clonePath = looseRoot.name + "/" + RelativePath(node, looseRoot) });
                        continue;
                    }
                    var mesh = Read(filter.sharedMesh);
                    var center = renderer.bounds.center;
                    var cell = new Vector3Int(Mathf.FloorToInt(center.x / 6), Mathf.FloorToInt(center.y / 6), Mathf.FloorToInt(center.z / 6));
                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var key = (materials[sub], cell, renderer.renderingLayerMask, renderer.shadowCastingMode,
                            renderer.gameObject.layer, renderer.receiveShadows);
                        if (!buckets.TryGetValue(key, out var parts)) buckets[key] = parts = new List<Part>();
                        parts.Add(new Part { source = renderer, mesh = mesh, subMesh = sub });
                    }
                }
                foreach (var bucket in buckets)
                {
                    var parts = new List<Part>(); int vertices = 0;
                    foreach (var part in bucket.Value)
                    {
                        if (parts.Count > 0 && (vertices + part.mesh.vertexCount > 64000 || parts.Count >= 64))
                        { CreateChunk(parts, bucket.Key, building, staticRoot); parts.Clear(); vertices = 0; }
                        parts.Add(part); vertices += part.mesh.vertexCount;
                    }
                    if (parts.Count > 0) CreateChunk(parts, bucket.Key, building, staticRoot);
                }
                ReleaseReadable();
                CreateLights(lights, sources, building);
                manifest.looseRendererCount = manifest.looseRenderers.Count;
                manifest.staticSourceRendererCount = sources.Length - manifest.looseRendererCount;
                if (!EditorSceneManager.SaveScene(scratch, ScenePath)) throw new IOException("Could not save the scratch scene.");
                foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s != scratch).ToArray())
                    EditorSceneManager.CloseScene(scene, true);
                Status = "GeneratingUV"; SaveManifest();
                EditorApplication.update -= UnwrapTick; EditorApplication.update += UnwrapTick;
            }
            catch (Exception e)
            {
                ReleaseReadable(); Status = "Failed: " + e.Message;
                if (manifest != null && Directory.Exists(Folder)) SaveManifest();
                Debug.LogException(e); throw;
            }
        }

        static void BackupWorkspace()
        {
            Directory.CreateDirectory(BackupFolder);
            string setupPath = BackupFolder + "/scene-setup.json";
            if (File.Exists(setupPath)) throw new InvalidOperationException("An original workspace backup already exists; preserve or restore it before preparing again.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            if (setup.Any(s => string.IsNullOrEmpty(s.path))) throw new InvalidOperationException("Save all untitled scenes first.");
            File.WriteAllText(setupPath, JsonUtility.ToJson(new SceneBackup { scenes = setup }, true));
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                File.Copy(scene.path, BackupFolder + "/" + i.ToString("D2") + "-" + scene.name + "-before-save.unity", false);
                if (scene.isDirty && !EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scene.path);
            }
        }

        static HashSet<Renderer> EmissionRenderers(Transform building)
        {
            var result = new HashSet<Renderer>();
            foreach (var controls in building.GetComponentsInChildren<LightCircuitControls>(true))
            {
                var bindings = new SerializedObject(controls).FindProperty("lights");
                for (int i = 0; i < bindings.arraySize; i++)
                {
                    var targets = bindings.GetArrayElementAtIndex(i).FindPropertyRelative("emissiveTargets");
                    for (int j = 0; j < targets.arraySize; j++)
                        if (targets.GetArrayElementAtIndex(j).FindPropertyRelative("renderer").objectReferenceValue is Renderer renderer) result.Add(renderer);
                }
            }
            return result;
        }

        static void ReadClusters(Transform building, HashSet<MeshRenderer> combined, HashSet<MeshRenderer> represented)
        {
            foreach (var clusters in building.GetComponentsInChildren<BuildingRenderClusters>(true))
            {
                var groups = new SerializedObject(clusters).FindProperty("groups");
                for (int i = 0; i < groups.arraySize; i++)
                {
                    var group = groups.GetArrayElementAtIndex(i);
                    var renderer = group.FindPropertyRelative("combined").objectReferenceValue as MeshRenderer;
                    if (!renderer) continue;
                    combined.Add(renderer);
                    if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                    var originals = group.FindPropertyRelative("sources");
                    for (int j = 0; j < originals.arraySize; j++)
                        if (originals.GetArrayElementAtIndex(j).objectReferenceValue is MeshRenderer source) represented.Add(source);
                }
            }
        }

        static bool IsMoving(string name, HashSet<string> motionNames)
        {
            if (motionNames.Contains(name)) return true;
            string lower = name.ToLowerInvariant();
            return lower.StartsWith("sw_", StringComparison.Ordinal) || movingWords.Any(lower.Contains);
        }

        static bool HasUnknownBehaviour(Transform source, Transform building)
        {
            for (var t = source; t; t = t.parent)
            {
                foreach (var behaviour in t.GetComponents<MonoBehaviour>())
                    if (behaviour && !(behaviour is BuildingRenderClusters) && !(behaviour is LightCircuitControls) &&
                        !(behaviour is InteriorVisibility) && !(behaviour is LocalLightBudget) && !(behaviour is InteriorLightingRig)) return true;
                if (t == building) break;
            }
            return false;
        }

        static Transform CopyTransform(Transform source, Transform root, Dictionary<Transform, Transform> copies)
        {
            if (copies.TryGetValue(source, out var existing)) return existing;
            var parent = source.parent ? CopyTransform(source.parent, root, copies) : root;
            var copy = new GameObject(source.name).transform; copy.SetParent(parent, false);
            copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
            copies.Add(source, copy); return copy;
        }

        static void CreateChunk(List<Part> parts, (Material material, Vector3Int cell, uint renderingLayer,
            ShadowCastingMode shadowMode, int layer, bool receiveShadows) key, Transform building, Transform parent)
        {
            string name = "MC00Surface" + manifest.chunks.Count.ToString("D4");
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.Select(p => new CombineInstance { mesh = p.mesh, subMeshIndex = p.subMesh,
                transform = p.source.localToWorldMatrix }).ToArray(), true, true);
            mesh.RecalculateBounds();
            string path = Folder + "/Meshes/" + name + ".asset";
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = key.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = key.material;
            renderer.shadowCastingMode = key.shadowMode; renderer.renderingLayerMask = key.renderingLayer;
            renderer.receiveShadows = key.receiveShadows; renderer.receiveGI = ReceiveGI.Lightmaps;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
            manifest.chunks.Add(new Chunk { name = name, meshPath = path, materialPath = AssetDatabase.GetAssetPath(key.material),
                sourcePaths = parts.Select(p => RelativePath(p.source.transform, building)).Distinct().ToArray(),
                shadowMode = (int)key.shadowMode, renderingLayer = key.renderingLayer, layer = key.layer, receiveShadows = key.receiveShadows });
            unwrap.Enqueue(mesh);
        }

        static void CreateLights(Light[] lights, MeshRenderer[] sources, Transform building)
        {
            var root = new GameObject("MC00 bake fixtures").transform;
            var groups = lights.GroupBy(l => RoomId(l.transform, building)).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            manifest.roomIds = groups.Select(g => g.Key).ToArray();
            foreach (var group in groups)
            {
                var fixtures = group.ToArray();
                var primary = fixtures.Where(l => l.transform.parent && l.transform.parent.name.IndexOf("room task light actual", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                var candidates = primary.Length > 0 ? primary : fixtures;
                var keys = new HashSet<Light>();
                if (primary.Length >= 2)
                {
                    var pair = primary.SelectMany(a => primary.Where(b => a != b).Select(b => (a, b)))
                        .OrderByDescending(p => (p.a.transform.position - p.b.transform.position).sqrMagnitude).First();
                    keys.Add(pair.a); keys.Add(pair.b);
                }
                else
                {
                    var first = candidates.OrderByDescending(l => l.intensity).First(); keys.Add(first);
                    var second = fixtures.Where(l => l != first).OrderByDescending(l => l.intensity)
                        .ThenByDescending(l => (l.transform.position - first.transform.position).sqrMagnitude).FirstOrDefault();
                    if (second) keys.Add(second);
                }
                var roomSources = sources.Where(r => RoomId(r.transform, building) == group.Key).ToArray();
                var bounds = new Bounds(fixtures[0].transform.position, Vector3.zero);
                foreach (var renderer in roomSources) bounds.Encapsulate(renderer.bounds);
                foreach (var light in fixtures) bounds.Encapsulate(light.transform.position);
                manifest.rooms.Add(new RoomBounds { roomId = group.Key, bounds = bounds });
                foreach (var source in fixtures)
                {
                    string name = "Fixture" + manifest.lights.Count.ToString("D3");
                    var go = new GameObject(name); go.transform.SetParent(root, false);
                    go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    var copy = go.AddComponent<Light>(); EditorUtility.CopySerialized(source, copy);
                    bool key = keys.Contains(source); copy.lightmapBakeType = key ? LightmapBakeType.Mixed : LightmapBakeType.Baked;
                    copy.enabled = true;
                    var originalExtra = source.GetComponent<UniversalAdditionalLightData>();
                    var extra = go.AddComponent<UniversalAdditionalLightData>();
                    if (originalExtra) EditorUtility.CopySerialized(originalExtra, extra);
                    manifest.lights.Add(new Fixture { sourcePath = RelativePath(source.transform, building), cloneName = name,
                        roomId = group.Key, key = key });
                }
            }
        }

        static string RoomId(Transform source, Transform building)
        {
            for (var t = source; t && t != building; t = t.parent)
            {
                if (t.name.StartsWith("ORG_ROOM_", StringComparison.Ordinal) || t.name.StartsWith("ORG_STAIR_", StringComparison.Ordinal) ||
                    t.name.StartsWith("ORG_", StringComparison.Ordinal) && t.name.Contains("Pressure_Entry")) return t.name;
                if (t.name.StartsWith("ORG_CORRIDOR", StringComparison.Ordinal)) return t.parent.name + "/" + t.name;
                if (t.name.StartsWith("ORG_FLOOR_", StringComparison.Ordinal)) return t.name;
            }
            return "MC00_SHARED";
        }

        static string RelativePath(Transform source, Transform root)
        {
            var names = new List<string>();
            for (var t = source; t && t != root; t = t.parent) names.Add(t.name);
            names.Reverse(); return string.Join("/", names);
        }

        static void UnwrapTick()
        {
            try
            {
                if (unwrap.Count == 0)
                {
                    EditorApplication.update -= UnwrapTick;
                    AssetDatabase.SaveAssets();
                    if (!EditorSceneManager.SaveScene(scratch)) throw new IOException("Could not save completed geometry.");
                    Status = "Ready"; SaveManifest();
                    Debug.Log("MC00 bake geometry ready: " + ChunkCount + " chunks, " + LightCount + " lights, " + RoomCount + " circuits.");
                    return;
                }
                var mesh = unwrap.Peek();
                if (!Unwrapping.GenerateSecondaryUVSet(mesh)) throw new InvalidOperationException("UV unwrap failed: " + mesh.name);
                EditorUtility.SetDirty(mesh); unwrap.Dequeue(); manifest.uvCompleted++;
            }
            catch (Exception e)
            {
                EditorApplication.update -= UnwrapTick; Status = "Failed: " + e.Message; SaveManifest(); Debug.LogException(e);
            }
        }

        static void SaveManifest()
        {
            manifest.status = Status;
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
        }

        static void ReleaseReadable()
        {
            foreach (var mesh in readable.Values) UnityEngine.Object.DestroyImmediate(mesh);
            readable.Clear();
        }

        static Mesh Read(Mesh source)
        {
            if (readable.TryGetValue(source, out var cached)) return cached;
            var mesh = new Mesh { name = source.name };
            using (var all = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var data = all[0]; mesh.SetVertexBufferParams(data.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < data.vertexBufferCount; stream++)
                { var bytes = data.GetVertexData<byte>(stream); mesh.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream); }
                if (source.indexFormat == IndexFormat.UInt16)
                { var ids = data.GetIndexData<ushort>(); mesh.SetIndexBufferParams(ids.Length, IndexFormat.UInt16); mesh.SetIndexBufferData(ids, 0, 0, ids.Length); }
                else
                { var ids = data.GetIndexData<uint>(); mesh.SetIndexBufferParams(ids.Length, IndexFormat.UInt32); mesh.SetIndexBufferData(ids, 0, 0, ids.Length); }
                mesh.subMeshCount = data.subMeshCount;
                for (int i = 0; i < data.subMeshCount; i++) mesh.SetSubMesh(i, data.GetSubMesh(i), MeshUpdateFlags.DontRecalculateBounds);
                mesh.bounds = source.bounds;
            }
            readable.Add(source, mesh); return mesh;
        }
    }
}
