using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class ClassicDust2Install
{
    private const string LocalAssets = "Assets/LocalMaps/Dust2";
    private const string ResourceFolder = LocalAssets + "/Resources/Moonkov/Maps";
    private const string PrefabPath = ResourceFolder + "/ClassicDust2.prefab";
    private const string Installed = ResourceFolder + "/ClassicDust2Installed.txt";

    [Serializable]
    private sealed class Anchors
    {
        public Vector3[] attackers, defenders;
        public Vector3 siteA, siteB;
        public Vector3 sizeA = new(8, 4, 8), sizeB = new(9, 4, 9);
    }

    public static object CheckRoutes(string collisionFile, string anchorFile)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("在编辑模式检查沙2。");
        var scene = EditorSceneManager.NewPreviewScene();
        var meshes = new List<Mesh>();
        NavMeshData nav = null;
        try
        {
            var root = NewRoot(scene);
            var collision = BuildCollision(root, collisionFile, meshes);
            nav = BuildNavigation(collision);
            var anchors = JsonUtility.FromJson<Anchors>(File.ReadAllText(anchorFile));
            var snapped = ValidateAnchors(scene, nav, anchors);
            return new { snapped.attackers, snapped.defenders, snapped.siteA, snapped.siteB, colliders = collision.Count };
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (nav) UnityEngine.Object.DestroyImmediate(nav);
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    public static object Install(string extractedDirectory, string anchorFile)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("在编辑模式导入沙2。");
        var anchors = JsonUtility.FromJson<Anchors>(File.ReadAllText(anchorFile));
        var scene = EditorSceneManager.NewPreviewScene();
        var meshes = new List<Mesh>();
        NavMeshData nav = null;
        try
        {
            var root = NewRoot(scene);
            var collision = BuildCollision(root, Path.Combine(extractedDirectory, "collision.bin"), meshes);
            nav = BuildNavigation(collision);
            anchors = ValidateAnchors(scene, nav, anchors);
            var materials = ImportTextures(extractedDirectory);
            EnsureFolder(ResourceFolder);
            EnsureFolder(LocalAssets + "/Geometry");
            EnsureFolder(LocalAssets + "/Materials");
            int renderers = 0, textured = 0;
            long renderTriangles = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < collision.Count; i++)
                    collision[i].sharedMesh = Save(meshes[i], LocalAssets + $"/Geometry/Collision_{i:000}.asset");
                nav = Save(nav, LocalAssets + "/Geometry/Navigation.asset");
                var rendererRoot = Child(root, "Original render geometry");
                var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                var fallback = Save(new Material(shader) { color = new Color(.59f, .53f, .42f) }, LocalAssets + "/Materials/Sandstone.mat");
                var mapped = new Dictionary<ulong, Material>();
                using var reader = new BinaryReader(File.OpenRead(Path.Combine(extractedDirectory, "render.bin")));
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    ulong key = reader.ReadUInt64();
                    var mesh = ReadMesh(reader, $"Render {renderers:000}", true);
                    renderTriangles += mesh.GetIndexCount(0) / 3;
                    var material = fallback;
                    if (materials.TryGetValue(key, out var source) && source.Texture)
                    {
                        if (!mapped.TryGetValue(key, out material))
                        {
                            material = MakeMaterial(shader, source.Texture, source.Name);
                            material = Save(material, LocalAssets + $"/Materials/Source_{key:x16}.mat");
                            mapped.Add(key, material);
                        }
                        textured++;
                    }
                    var part = Child(rendererRoot, mesh.name);
                    part.gameObject.isStatic = true;
                    part.gameObject.AddComponent<MeshFilter>().sharedMesh = Save(mesh, LocalAssets + $"/Geometry/Render_{renderers:000}.asset");
                    var renderer = part.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderers++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            var map = root.gameObject.AddComponent<ClassicDust2Map>();
            var config = new SerializedObject(map);
            SetSpawns(config.FindProperty("m_AttackSpawns"), root, "T Spawn", anchors.attackers, (anchors.siteA + anchors.siteB) * .5f);
            SetSpawns(config.FindProperty("m_DefenseSpawns"), root, "CT Spawn", anchors.defenders, (anchors.siteA + anchors.siteB) * .5f);
            config.FindProperty("m_SiteA").objectReferenceValue = Site(root, "Bombsite A", anchors.siteA, anchors.sizeA);
            config.FindProperty("m_SiteB").objectReferenceValue = Site(root, "Bombsite B", anchors.siteB, anchors.sizeB);
            config.FindProperty("m_Navigation").objectReferenceValue = nav;
            config.ApplyModifiedPropertiesWithoutUndo();

            var sun = Child(root, "Sun");
            sun.rotation = Quaternion.Euler(52, -128, 0);
            var light = sun.gameObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, .94f, .84f);
            light.intensity = 1.35f;
            light.shadows = LightShadows.Soft;

            if (!map.Configured || !PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath))
                throw new InvalidOperationException("沙2预制体保存失败。");
            File.WriteAllText(Installed, "Dust II (CS2)\nThunderstore anton-CS2_Dust2-1.0.0\n");
            AssetDatabase.ImportAsset(Installed);
            AssetDatabase.SaveAssets();
            return new { renderers, textured, renderTriangles, colliders = collision.Count, navigation = AssetDatabase.GetAssetPath(nav),
                prefab = PrefabPath, routes = 20, spawns = 10 };
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (nav && !EditorUtility.IsPersistent(nav)) UnityEngine.Object.DestroyImmediate(nav);
            foreach (var mesh in meshes) if (mesh && !EditorUtility.IsPersistent(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static Transform NewRoot(Scene scene)
    {
        var root = new GameObject("Dust II (CS2)");
        SceneManager.MoveGameObjectToScene(root, scene);
        return root.transform;
    }

    private static Material MakeMaterial(Shader shader, Texture2D color, string sourceName)
    {
        var material = new Material(shader);
        material.SetTexture("_BaseMap", color);
        material.SetFloat("_Smoothness", .08f);
        // The source lightmaps aren't included. Keep color visible in covered routes.
        material.SetTexture("_EmissionMap", color);
        material.SetColor("_EmissionColor", Color.white * .17f);
        material.EnableKeyword("_EMISSION");
        if (sourceName.Contains("glass", StringComparison.Ordinal))
            material.SetColor("_BaseColor", new Color(.82f, .85f, .86f));

        bool foliage = sourceName.Contains("weeds", StringComparison.Ordinal) ||
            sourceName.Contains("zebra_grass", StringComparison.Ordinal) ||
            sourceName.Contains("palm_frond", StringComparison.Ordinal) ||
            sourceName.Contains("agave_plant", StringComparison.Ordinal) ||
            sourceName.Contains("grape_leaves", StringComparison.Ordinal);
        bool cutout = foliage || sourceName.Contains("_alpha", StringComparison.Ordinal) ||
            sourceName.Contains("bombsite_x_spray", StringComparison.Ordinal) ||
            sourceName.Contains("hr_dust_overlay", StringComparison.Ordinal) ||
            sourceName.Contains("hr_dust_decal", StringComparison.Ordinal) ||
            sourceName.Contains("chainlink_fence_001", StringComparison.Ordinal) ||
            sourceName.Contains("chainlink_fence_wire", StringComparison.Ordinal);
        if (cutout)
        {
            material.SetFloat("_AlphaClip", 1);
            material.SetFloat("_Cutoff", .32f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }
        if (foliage) material.SetFloat("_Cull", (float)CullMode.Off);
        return material;
    }

    private static Transform Child(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static List<MeshCollider> BuildCollision(Transform root, string file, List<Mesh> meshes)
    {
        var container = Child(root, "Original collision");
        var colliders = new List<MeshCollider>();
        using var reader = new BinaryReader(File.OpenRead(file));
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var mesh = ReadMesh(reader, $"Collision {colliders.Count:000}", false);
            var part = Child(container, mesh.name);
            part.gameObject.isStatic = true;
            var collider = part.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            colliders.Add(collider);
            meshes.Add(mesh);
        }
        return colliders;
    }

    private static Mesh ReadMesh(BinaryReader reader, string name, bool render)
    {
        int count = reader.ReadInt32(), faceCount = reader.ReadInt32();
        if (count <= 2 || faceCount <= 0 || count > 8_000_000 || faceCount > 8_000_000)
            throw new InvalidDataException("Invalid Dust II mesh counts.");
        var positions = new Vector3[count];
        var uv = render ? new Vector2[count] : null;
        for (int i = 0; i < count; i++)
        {
            positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var uv0 = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            if (render) uv[i] = uv0;
        }
        var triangles = new int[faceCount * 3];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();
        var mesh = new Mesh { name = name, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = positions;
        if (render) mesh.uv = uv;
        mesh.triangles = triangles;
        if (render) mesh.RecalculateNormals();
        return mesh;
    }

    private static NavMeshData BuildNavigation(List<MeshCollider> colliders)
    {
        if (colliders.Count == 0) throw new InvalidDataException("沙2碰撞为空。");
        var sources = new List<NavMeshBuildSource>(colliders.Count);
        var bounds = new Bounds(colliders[0].sharedMesh.bounds.center, Vector3.zero);
        foreach (var collider in colliders)
        {
            bounds.Encapsulate(collider.sharedMesh.bounds);
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = collider.sharedMesh,
                transform = collider.transform.localToWorldMatrix });
        }
        bounds.Expand(2);
        var settings = NavMesh.GetSettingsByIndex(0);
        settings.agentHeight = 1.78f;
        settings.agentRadius = .35f;
        settings.agentSlope = 45f;
        settings.agentClimb = .45f;
        settings.overrideVoxelSize = true;
        settings.voxelSize = .14f;
        settings.maxJobWorkers = 2;
        var nav = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (!nav) throw new InvalidOperationException("沙2导航构建失败。");
        nav.name = "Dust II";
        return nav;
    }

    private static Anchors ValidateAnchors(Scene scene, NavMeshData nav, Anchors authored)
    {
        if (authored == null || authored.attackers?.Length != 5 || authored.defenders?.Length != 5 ||
            authored.sizeA.x <= 0 || authored.sizeA.z <= 0 || authored.sizeB.x <= 0 || authored.sizeB.z <= 0)
            throw new InvalidDataException("沙2需要两组各五个出生点和两个包点。");
        var instance = NavMesh.AddNavMeshData(nav);
        var path = new NavMeshPath();
        var colliders = new Collider[8];
        var physics = scene.GetPhysicsScene();
        UnityEngine.Physics.SyncTransforms();
        try
        {
            Vector3 Sample(Vector3 point)
            {
                if (!NavMesh.SamplePosition(point, out var hit, 1.65f, NavMesh.AllAreas))
                    throw new InvalidDataException("导航采样失败 " + point);
                if (physics.OverlapCapsule(hit.position + Vector3.up * .41f, hit.position + Vector3.up * 1.46f, .35f,
                    colliders, ~0, QueryTriggerInteraction.Ignore) != 0)
                    throw new InvalidDataException("站立空间不足 " + hit.position);
                return hit.position + Vector3.up * .045f;
            }

            authored.attackers = authored.attackers.Select(Sample).ToArray();
            authored.defenders = authored.defenders.Select(Sample).ToArray();
            authored.siteA = Sample(authored.siteA);
            authored.siteB = Sample(authored.siteB);
            var spawns = authored.attackers.Concat(authored.defenders).ToArray();
            for (int i = 0; i < spawns.Length; i++)
            {
                for (int j = i + 1; j < spawns.Length; j++)
                    if (Vector3.Distance(spawns[i], spawns[j]) < 1.2f)
                        throw new InvalidDataException("两个沙2出生点太近。");
                foreach (var site in new[] { authored.siteA, authored.siteB })
                    if (!NavMesh.CalculatePath(spawns[i], site, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidDataException($"出生点 {spawns[i]} 到包点 {site} 不可达。");
            }
            return authored;
        }
        finally { if (instance.valid) instance.Remove(); }
    }

    private static void SetSpawns(SerializedProperty property, Transform root, string name, Vector3[] values, Vector3 target)
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            var point = Child(root, name + " " + (i + 1));
            point.position = values[i];
            point.LookAt(new Vector3(target.x, point.position.y, target.z));
            property.GetArrayElementAtIndex(i).objectReferenceValue = point;
        }
    }

    private static BoxCollider Site(Transform root, string name, Vector3 point, Vector3 size)
    {
        var anchor = Child(root, name);
        anchor.position = point;
        var zone = anchor.gameObject.AddComponent<BoxCollider>();
        zone.size = size;
        zone.center = new Vector3(0, .6f, 0);
        zone.isTrigger = true;
        return zone;
    }

    private static Dictionary<ulong, (Texture2D Texture, string Name)> ImportTextures(string source)
    {
        var rows = File.ReadAllLines(Path.Combine(source, "materials.tsv")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 3 && parts[1].Length > 0).ToArray();
        var folder = LocalAssets + "/Textures";
        EnsureFolder(folder);
        foreach (string file in rows.Select(parts => parts[1]).Distinct())
            File.Copy(Path.Combine(source, file), folder + "/" + file, overwrite: true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return rows.ToDictionary(parts => ulong.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            parts => (AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + parts[1]), parts[2]));
    }

    private static T Save<T>(T value, string path) where T : UnityEngine.Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!existing) { AssetDatabase.CreateAsset(value, path); return value; }
        EditorUtility.CopySerialized(value, existing);
        UnityEngine.Object.DestroyImmediate(value);
        return existing;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
