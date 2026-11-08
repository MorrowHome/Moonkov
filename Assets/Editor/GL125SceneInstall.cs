using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.MP_FPS;
using Unity.MP_FPS.Moon;
using Unity.MP_FPS.Moon.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GL125SceneInstall
{
    private const string FormalScene = SourceWorldSceneInstall.ScenePath;
    private const string ScratchScene = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/GL125_ImportScratch.unity";
    private const string WorldName = "GL125 World";
    private static readonly HashSet<string> OldWorldRoots = new(StringComparer.Ordinal)
    {
        SourceWorldSceneInstall.RootName, "MoonEnvironment", "Theophilus_A_Start", "MC00_ROOT_Standard_YUp_Frame",
        "MOONKOV_ENTRY_STATIC_VISUAL_ROOT", "B_RING_CONCEPT_ROOT", "B_WEST_PASSIVE_STAIR_ACCESS_R3", "Theophilus Visual Pass 01",
        "THEOPHILUS_OUTER_BUILDINGS_BATCH01", "THEOPHILUS_OUTER_BUILDINGS_BATCH02", "THEOPHILUS_OUTER_BUILDINGS_BATCH03",
        "B_WEST_TRANSFER_06_DETAIL_R6", "R7_A_START_SAMPLE_SERVICE_ROOT", "B_RADIAL_SUPPORT_0197_DETAIL_R9",
        "B_CENTRAL_DESCENT_TOWER_DETAIL_R10", "R21_OPEN_SHAFT_WITH_R20_MECHANICAL_ROOT", "R21_RETAINED_R18_R19_ROOT",
        "R24_HERO_FINISH_DISPLAY_ROOT", "R29_FROM_R24_FINAL_DISPLAY_ROOT", "R35_FROM_R29_DISPLAY_ROOT", "R36R38_FROM_R35_DISPLAY_ROOT",
        "REBUILD_R08_DISPLAY_ROOT", "REBUILD_R09_R18_DISPLAY_ROOT", "REBUILD_R19_ADDITIVE_DISPLAY_ROOT", "R23_Ring01_Joint_DisplayOnly",
        "OUTER_WALK_SURFACES_COLLISION_20261023", "BUILDING_GAPS_STATIC_COLLISION_20261024", "Local light visibility", "Theophilus Silver Print"
    };

    public static void QueueInstall(string stage)
    {
        RequireEditMode();
        EditorApplication.delayCall += () =>
        {
            try { Debug.Log(Install(stage, true)); }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(stage, "UnityInstall.error.txt"), e.ToString());
                Debug.LogException(e);
            }
        };
    }

    public static string Install(string stage, bool publish)
    {
        RequireEditMode();
        var assets = ReadStage(stage);
        var chunks = assets.Chunks.Concat(assets.StaticCollision).ToArray();
        var maps = assets.ImportLightmaps(Enumerable.Range(0, assets.AtlasCount), assets.InteriorAtlasIndices);
        assets.ImportChunks(chunks);
        var previous = SceneManager.GetActiveScene();
        string backup = Path.Combine(stage, "TheophilusGameScene.before-GL125.unity");
        if (!File.Exists(backup)) File.Copy(FormalScene, backup);
        var scene = OpenShell(stage);
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            if (roots.Any(r => r.name == WorldName))
                throw new InvalidOperationException("The saved formal map is not the expected pre-GL125 scene.");
            var airlock = Components<AirlockController>(roots).Single();
            if (OldWorldRoots.Contains(airlock.transform.root.name)) airlock.transform.SetParent(null, true);
            var root = BuildWorld(scene, assets, chunks, out var bindings);
            var sun = MakeSun(root.transform, assets);
            var lighting = Components<MoonSceneLighting>(roots).Single();
            ConfigureLighting(lighting, sun);
            RebindFurniture(roots, root.transform);
            RebindAirlockAudio(airlock, root.transform.Find("MC00 structural collision"));
            RetireOldRoots(scene, roots);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.012f, .016f, .022f).gamma;
            Physics.SyncTransforms();
            string gameplay = ValidateGameplay(scene, root.transform);
            if (!publish) return gameplay;

            var probes = BuildProbes(assets);
            root.AddComponent<BlenderLightmapSet>().Configure(maps, bindings.ToArray(), probes);
            if (bindings.Count != assets.Chunks.Count() ||
                LightmapSettings.lightmaps.Length != assets.AtlasCount || LightmapSettings.lightProbes != probes)
                throw new InvalidOperationException("Incomplete GL125 scene lighting binding.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, FormalScene, true)) throw new IOException("Unity did not save the GL125 formal map.");
            string result = $"GL125 installed. {bindings.Count} renderers, {assets.AtlasCount} EXR maps, {probes.countSelf} character probes. {gameplay}";
            File.WriteAllText(Path.Combine(stage, "UnityInstall.txt"), result);
            return result;
        }
        finally
        {
            CloseShell(scene);
            SceneManager.SetActiveScene(previous);
        }
    }

    public static string ReplaceStaticCollision(string stage)
    {
        RequireEditMode();
        var assets = ReadStage(stage);
        var chunks = assets.StaticCollision;
        string backup = Path.Combine(stage, "TheophilusGameScene.before-GL125Collision.unity");
        if (!File.Exists(backup)) File.Copy(FormalScene, backup);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(FormalScene, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var world = scene.GetRootGameObjects().Single(root => root.name == WorldName).transform;
            var overrides = GL125LiftInstall.CollisionOverrides(world, Path.Combine(stage, "PreparedQuality", "StaticCollision", "Collision.json"));
            assets.ImportChunks(chunks);
            var collision = world.Find("Static collision");
            var structure = world.Find("MC00 structural collision");
            if (!collision || !structure) throw new InvalidOperationException("GL125 collision roots are missing.");
            var oldChunks = collision.Cast<Transform>().Concat(structure.Cast<Transform>()).ToArray();
            if (oldChunks.Length == 0 || oldChunks.Any(chunk => !chunk.GetComponent<MeshCollider>() || chunk.childCount != 0))
                throw new InvalidOperationException("Unexpected contents in the generated collision roots.");
            foreach (var chunk in oldChunks) Object.DestroyImmediate(chunk.gameObject);
            int layer = LayerMask.NameToLayer("Ground");
            if (layer < 0) throw new InvalidOperationException("Ground layer is missing.");
            foreach (var chunk in chunks)
                AddColliderChunk(chunk, collision, structure, overrides.TryGetValue(chunk.name, out var split) ? split : assets.MeshFor(chunk), layer);
            Physics.SyncTransforms();
            string gameplay = ValidateGameplay(scene, world);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Unity did not save the repaired GL125 collision.");
            string result = $"Replaced {oldChunks.Length} static collider chunks with {chunks.Count} current surface chunks. {gameplay}";
            File.WriteAllText(Path.Combine(stage, "UnityCollisionRepair.txt"), result);
            return result;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }

    public static string CheckCollisionPreview(string stage)
    {
        RequireEditMode();
        var assets = ReadStage(stage);
        var previous = SceneManager.GetActiveScene();
        var scene = OpenShell(stage);
        var temporaryMeshes = new List<Mesh>();
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var airlock = Components<AirlockController>(roots).Single();
            if (OldWorldRoots.Contains(airlock.transform.root.name)) airlock.transform.SetParent(null, true);
            var root = new GameObject(WorldName);
            SceneManager.MoveGameObjectToScene(root, scene);
            ConfigureLighting(Components<MoonSceneLighting>(roots).Single(), MakeSun(root.transform, assets));
            RebindFurniture(roots, root.transform);
            var structure = CreateStructureRoot(root.transform);
            RebindAirlockAudio(airlock, structure);
            RetireOldRoots(scene, roots);
            int layer = LayerMask.NameToLayer("Ground");
            foreach (var chunk in assets.Chunks.Where(c => c.collider).Concat(assets.StaticCollision))
            {
                var mesh = assets.ReadCollider(chunk);
                temporaryMeshes.Add(mesh);
                AddColliderChunk(chunk, root.transform, structure, mesh, layer);
            }
            Physics.SyncTransforms();
            return ValidateGameplay(scene, root.transform);
        }
        finally
        {
            CloseShell(scene);
            foreach (var mesh in temporaryMeshes) Object.DestroyImmediate(mesh);
            SceneManager.SetActiveScene(previous);
        }
    }

    public static string UpdateSupersampledLighting(string stage)
    {
        RequireEditMode();
        try
        {
            var assets = ReadStage(stage);
            var interior = assets.InteriorAtlasIndices.ToArray();
            var maps = assets.ImportLightmaps(interior, interior);
            string scene = VerifySaved(stage, "UnityFinal.png", false);
            string result = $"Updated {maps.Length} supersampled interior lightmaps. {scene}";
            File.WriteAllText(Path.Combine(stage, "UnityQuality.txt"), result);
            return result;
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(stage, "UnityQuality.error.txt"), e.ToString());
            throw;
        }
    }

    public static string VerifySaved(string stage, string view, bool bakeMap)
    {
        RequireEditMode();
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(FormalScene, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var root = roots.Single(r => r.name == WorldName);
            var lighting = root.GetComponent<BlenderLightmapSet>();
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            var lightmaps = LightmapSettings.lightmaps;
            if (!lighting || roots.Any(r => OldWorldRoots.Contains(r.name)) || lightmaps.Length != ReadStage(stage).AtlasCount ||
                lightmaps.Any(l => !l.lightmapColor) || !LightmapSettings.lightProbes || renderers.Any(r =>
                    r.lightmapIndex < 0 || r.lightmapIndex >= lightmaps.Length || r.sharedMaterials.Any(m => !m)))
                throw new InvalidOperationException("Saved GL125 world has missing or stale references.");
            string gameplay = ValidateGameplay(scene, root.transform, false);
            if (bakeMap) RaidMapBackgroundBake.BakeScene(scene);
            RenderPilot(scene, Path.Combine(stage, view));
            string result = $"Saved GL125 world reloaded: {renderers.Length} renderers, {lightmaps.Length} lightmaps. {gameplay}";
            File.WriteAllText(Path.Combine(stage, "UnitySavedScene.txt"), result);
            return result;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }

    private static Scene OpenShell(string stage)
    {
        string expected = File.ReadAllText(Path.Combine(stage, "FormalSceneSha.txt")).Trim();
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(FormalScene))
            if (!string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The formal scene changed after GL125 staging.");
        if (File.Exists(ScratchScene)) throw new IOException("GL125 import scratch scene already exists.");
        File.Copy(Path.Combine(stage, "FormalSceneShell.unity"), ScratchScene);
        AssetDatabase.ImportAsset(ScratchScene, ImportAssetOptions.ForceSynchronousImport);
        try { return EditorSceneManager.OpenScene(ScratchScene, OpenSceneMode.Additive); }
        catch { AssetDatabase.DeleteAsset(ScratchScene); throw; }
    }

    private static void CloseShell(Scene scene)
    {
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.DeleteAsset(ScratchScene);
    }

    private static void ConfigureLighting(MoonSceneLighting lighting, Light sun)
    {
        var settings = new SerializedObject(lighting);
        settings.FindProperty("sun").objectReferenceValue = sun;
        settings.FindProperty("clockMode").enumValueIndex = (int)MoonSceneLighting.ClockMode.Fixed;
        settings.FindProperty("sourceStaticLighting").objectReferenceValue = null;
        settings.FindProperty("ambientFill").colorValue = new Color(.012f, .016f, .022f).gamma;
        settings.FindProperty("adaptExposure").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static IEnumerable<T> Components<T>(IEnumerable<GameObject> roots) where T : Component =>
        roots.SelectMany(r => r.GetComponentsInChildren<T>(true));

    private static Transform CreateStructureRoot(Transform world)
    {
        var structure = new GameObject("MC00 structural collision").transform;
        structure.SetParent(world, false);
        return structure;
    }

    private static bool IsMc00Structure(GL125WorldAssets.Chunk chunk, Vector3 origin) => chunk.atlas < 0 &&
        Mathf.Abs(origin.x - 1614.54f) < 85f && Mathf.Abs(origin.z - 2149.01f) < 85f && origin.y >= 200 && origin.y <= 278;

    private static void AddColliderChunk(GL125WorldAssets.Chunk chunk, Transform collision, Transform structure, Mesh mesh, int layer)
    {
        var origin = GL125WorldAssets.ToUnity(chunk.origin);
        var obj = new GameObject(chunk.name) { layer = layer };
        obj.transform.SetParent(IsMc00Structure(chunk, origin) ? structure : collision, false);
        obj.transform.localPosition = origin;
        obj.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private static void RebindAirlockAudio(AirlockController airlock, Transform structure)
    {
        var audio = airlock.GetComponent<AirlockAudio>();
        if (!audio || !structure) throw new InvalidOperationException("Airlock structure audio could not be rebound.");
        audio.ConfigureStructure(structure);
        EditorUtility.SetDirty(audio);
    }

    private static void RebindFurniture(GameObject[] roots, Transform world)
    {
        var furniture = Components<RaidFurnitureLoot>(roots).Single();
        var parent = new GameObject("Furniture interaction anchors").transform;
        parent.SetParent(world, false);
        var replacements = new Dictionary<Transform, Transform>();
        foreach (var drawer in furniture.Drawers)
        {
            var target = drawer?.Target;
            if (!target) throw new InvalidOperationException("An existing furniture interaction has no target.");
            if (!OldWorldRoots.Contains(target.root.name)) continue;
            if (!replacements.TryGetValue(target, out var anchor))
            {
                anchor = new GameObject(target.name).transform;
                anchor.SetParent(parent, false);
                anchor.SetPositionAndRotation(target.position, target.rotation);
                anchor.localScale = target.lossyScale;
                replacements.Add(target, anchor);
            }
            if (Vector3.Distance(anchor.TransformPoint(drawer.LocalPoint), drawer.Position) > .002f)
                throw new InvalidOperationException($"Furniture anchor would move {drawer.Label}.");
            drawer.Target = anchor;
        }
        EditorUtility.SetDirty(furniture);
    }

    private static void RetireOldRoots(Scene scene, GameObject[] roots)
    {
        var retired = roots.Where(r => OldWorldRoots.Contains(r.name)).ToHashSet();
        var kept = scene.GetRootGameObjects().Where(r => !retired.Contains(r) && r.name != WorldName).ToArray();
        if (Components<AirlockController>(retired).Any() || Components<AirlockButton>(retired).Any() ||
            Components<RaidMissionSite>(retired).Any() || Components<RaidMissionDevice>(retired).Any() ||
            Components<OxygenRefillZone>(retired).Any() || Components<BreathableZone>(retired).Any())
            throw new InvalidOperationException("The retired geometry still contains gameplay components.");

        var dangling = new List<string>();
        foreach (var behaviour in Components<MonoBehaviour>(kept).Where(b => b))
        {
            var property = new SerializedObject(behaviour).GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                var referenced = property.objectReferenceValue;
                var obj = referenced as GameObject;
                if (!obj && referenced is Component component) obj = component.gameObject;
                if (obj && retired.Contains(obj.transform.root.gameObject))
                    dangling.Add($"{behaviour.name}/{property.propertyPath} uses {obj.name}");
            }
        }
        if (dangling.Count != 0)
            throw new InvalidOperationException("Old world references need rebinding:\n" + string.Join("\n", dangling));
        foreach (var root in retired) Object.DestroyImmediate(root);
    }

    private static string ValidateGameplay(Scene scene, Transform world, bool checkSupport = true)
    {
        var roots = scene.GetRootGameObjects();
        var airlocks = Components<AirlockController>(roots).ToArray();
        var buttons = Components<AirlockButton>(roots).ToArray();
        var site = Components<RaidMissionSite>(roots).Single();
        var devices = Components<RaidMissionDevice>(roots).ToArray();
        var oxygen = Components<OxygenRefillZone>(roots).Single();
        var map = Components<MoonRaidMap>(roots).Single();
        var furniture = Components<RaidFurnitureLoot>(roots).Single();
        if (airlocks.Length == 0 || airlocks.Any(owner => !buttons.Any(b => b.Owner == owner)) ||
            buttons.Any(b => !airlocks.Contains(b.Owner) || !b.isActiveAndEnabled) ||
            buttons.Select(b => b.ButtonId).Distinct().Count() != buttons.Length ||
            site.Anchors.Length != 5 || devices.Length != 5 || devices.Any(d => site.Anchor(d.AnchorId) == null) || !oxygen.Powered ||
            furniture.Drawers.Any(drawer => drawer == null || !drawer.Target))
            throw new InvalidOperationException("The original airlock, mission, or oxygen references are incomplete.");
        string bindings = $"{buttons.Length} airlock buttons, {devices.Length} mission devices and {furniture.Drawers.Length} furniture targets checked.";
        if (!checkSupport) return bindings;

        var failures = new List<string>();
        int checks = 0, moved = 0;
        bool TryGround(Vector3 point, float allowance, out RaycastHit support)
        {
            support = Physics.RaycastAll(point + Vector3.up * 1.25f, Vector3.down, allowance + 1.25f,
                LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)
                .Where(h => h.collider.transform.IsChildOf(world) && h.normal.y >= .7f && Mathf.Abs(h.point.y - point.y) <= allowance)
                .OrderBy(h => Mathf.Abs(h.point.y - point.y)).FirstOrDefault();
            return support.collider != null;
        }
        bool TryAdjacentEnemyGround(Vector3 point, out RaycastHit support)
        {
            foreach (var direction in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
            {
                if (!TryGround(point + direction * .45f, .4f, out support)) continue;
                var feet = support.point + Vector3.up * .06f;
                if (!Physics.OverlapCapsule(feet + Vector3.up * .32f, feet + Vector3.up * 1.4f, .27f,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)
                    .Any(hit => hit.transform.IsChildOf(world))) return true;
            }
            support = default;
            return false;
        }
        void Ground(string label, Vector3 point, float allowance)
        {
            if (!TryGround(point, allowance, out _))
                failures.Add($"{label} ({point.x:F2}, {point.y:F2}, {point.z:F2})");
            checks++;
        }
        foreach (var anchor in site.Anchors) Ground("Mission " + anchor.Id, anchor.Approach, .7f);
        Ground("Oxygen station", oxygen.transform.TransformPoint(new Vector3(0, .06f, 1.05f)), .45f);
        Ground("GL125 U40 connection", new Vector3(2177.87f, 236.73f, 2462.91f), .25f);
        Ground("GL125 U62 connection", new Vector3(2109.36f, 262.16f, 2535.97f), .25f);
        foreach (var airlock in airlocks)
        {
            var airlockData = new SerializedObject(airlock);
            foreach (string name in new[] { "m_OuterAperture", "m_InnerAperture" })
            {
                var aperture = airlockData.FindProperty(name).objectReferenceValue as Collider;
                if (!aperture) throw new InvalidOperationException($"Missing airlock aperture {name}.");
                var forward = aperture.transform.forward;
                var origin = aperture.bounds.center - forward * .5f;
                if (Physics.RaycastAll(origin, forward, 1, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)
                    .Any(hit => hit.collider.transform.IsChildOf(world)))
                    failures.Add(airlock.AirlockId + " " + name + " is obstructed by the new static world");
                checks++;
            }
        }
        string spawnPath = Path.Combine(Path.GetDirectoryName(FormalScene)!, "TheophilusSpawnPointsSubScene.unity").Replace('\\', '/');
        var spawnScene = SceneManager.GetSceneByPath(spawnPath);
        bool opened = !spawnScene.isLoaded;
        if (opened) spawnScene = EditorSceneManager.OpenScene(spawnPath, OpenSceneMode.Additive);
        try
        {
            var spawns = spawnScene.GetRootGameObjects().Where(r => r.name.StartsWith("MC00_IndoorSpawn_", StringComparison.Ordinal)).ToArray();
            if (spawns.Length != 8) throw new InvalidOperationException("The eight formal player starts are missing.");
            foreach (var spawn in spawns)
            {
                var point = spawn.transform.position;
                Ground(spawn.name, point, .45f);
                var hits = Physics.OverlapCapsule(point + Vector3.up * .32f, point + Vector3.up * 1.4f, .27f,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
                if (hits.Any(hit => hit.transform.IsChildOf(world))) failures.Add(spawn.name + " overlaps the new static world");
                checks++;
            }
        }
        finally { if (opened) EditorSceneManager.CloseScene(spawnScene, true); }

        var authored = new SerializedObject(map);
        foreach (string arrayName in new[] { "m_LootPositions", "m_EnemySpawnPositions", "m_AdditionalExtractionPositions" })
        {
            var array = authored.FindProperty(arrayName);
            var normals = arrayName == "m_LootPositions" ? authored.FindProperty("m_LootGroundNormals") : null;
            bool enemySpawns = arrayName == "m_EnemySpawnPositions";
            bool canAdjust = normals != null || enemySpawns;
            for (int i = 0; i < array.arraySize; i++)
            {
                var slot = array.GetArrayElementAtIndex(i);
                var position = slot.vector3Value;
                bool supported = TryGround(position, 1.0f, out _);
                if (!supported && canAdjust && (TryGround(position, 4.0f, out var hit) ||
                    (enemySpawns && TryAdjacentEnemyGround(position, out hit))))
                {
                    slot.vector3Value = hit.point + Vector3.up * .06f;
                    if (normals != null && i < normals.arraySize) normals.GetArrayElementAtIndex(i).vector3Value = hit.normal;
                    supported = true;
                    moved++;
                }
                if (!supported) failures.Add($"{arrayName} #{i} ({position.x:F2}, {position.y:F2}, {position.z:F2})");
                checks++;
            }
        }
        Ground("Main extraction", authored.FindProperty("m_ExtractionPosition").vector3Value, 1.0f);
        if (failures.Count != 0) throw new InvalidOperationException($"GL125 has no verified support at {failures.Count}/{checks} route points:\n" + string.Join("\n", failures));
        authored.ApplyModifiedPropertiesWithoutUndo();
        return $"{checks} physics checks, {moved} point positions adjusted. {bindings}";
    }

    internal static LightProbes BuildProbes(GL125WorldAssets assets, IEnumerable<GL125WorldAssets.LightSpec> additional = null)
    {
        const string templatePath = "Assets/MoonEnvironment/Variants/MC00BakedBuilding/States/Baseline/Probes.asset";
        string assetPath = GL125WorldAssets.Root + "/Lighting/CharacterProbes.asset";
        GL125WorldAssets.EnsureFolder(GL125WorldAssets.Root + "/Lighting");
        var template = AssetDatabase.LoadAssetAtPath<LightProbes>(templatePath);
        if (!template) throw new InvalidOperationException("The native LightProbes structure is missing.");
        var probes = AssetDatabase.LoadAssetAtPath<LightProbes>(assetPath);
        if (!probes)
        {
            probes = Object.Instantiate(template);
            probes.name = "GL125 character lighting";
            AssetDatabase.CreateAsset(probes, assetPath);
        }
        var fixtures = assets.Lights.Concat(additional ?? Array.Empty<GL125WorldAssets.LightSpec>())
            .Where(l => l.type == "AREA" && l.energy > 0).Select(l => (
            position: GL125WorldAssets.ToUnity(l.position), direction: GL125WorldAssets.ToUnity(l.forward).normalized,
            color: new Color(l.color[0], l.color[1], l.color[2]), power: l.energy, shadow: l.shadow,
            rangeSquared: l.probeRange > 0 ? l.probeRange * l.probeRange : 625f)).ToArray();
        var positions = probes.GetPositionsSelf();
        var coefficients = new SphericalHarmonicsL2[positions.Length];
        int mask = LayerMask.GetMask("Default", "Ground");
        for (int i = 0; i < positions.Length; i++)
        {
            var point = positions[i];
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(.012f, .016f, .022f));
            foreach (var fixture in fixtures)
            {
                var delta = fixture.position - point;
                float sq = delta.sqrMagnitude;
                if (sq > fixture.rangeSquared || sq < .04f) continue;
                float distance = Mathf.Sqrt(sq);
                var direction = delta / distance;
                float cosine = -Vector3.Dot(direction, fixture.direction);
                float intensity = Mathf.Max(cosine, 0) * fixture.power / (120f * Mathf.Max(sq, .2f));
                if (intensity < .003f || fixture.shadow && Physics.Raycast(point, direction, distance - .15f, mask, QueryTriggerInteraction.Ignore)) continue;
                sh.AddDirectionalLight(direction, fixture.color, Mathf.Min(intensity, 1));
            }
            coefficients[i] = sh;
        }
        probes.bakedProbes = coefficients;
        EditorUtility.SetDirty(probes);
        AssetDatabase.SaveAssetIfDirty(probes);
        return probes;
    }

    public static string ValidatePilot(string stage, int atlas)
    {
        RequireEditMode();
        var assets = ReadStage(stage);
        var chunks = assets.Chunks.Where(c => c.atlas == atlas).ToArray();
        if (chunks.Length == 0) throw new InvalidOperationException("No geometry for GL125 pilot.");
        var maps = assets.ImportLightmaps(new[] { atlas });
        assets.ImportChunks(chunks);
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var root = BuildWorld(scene, assets, chunks, out var bindings);
            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                binding.atlas = 0;
                bindings[i] = binding;
            }
            root.AddComponent<BlenderLightmapSet>().Configure(maps, bindings.ToArray(), null);
            if (LightmapSettings.lightmaps.Length != 1 || LightmapSettings.lightmaps[0].lightmapColor != maps[0] ||
                bindings.Any(b => b.renderer.lightmapIndex != 0 || !b.renderer.sharedMaterial))
                throw new InvalidOperationException("The Cycles atlas failed to bind to the GL125 pilot renderers.");
            var sun = MakeSun(root.transform, assets);
            RenderSettings.sun = sun;
            RenderPilot(scene, Path.Combine(stage, "UnityPilot.png"));
            return $"GL125 atlas {atlas}: {bindings.Count} renderers, {chunks.Sum(c => c.triangles)} triangles, {maps[0].width}² linear lightmap.";
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }

    private static GL125WorldAssets ReadStage(string stage) => new GL125WorldAssets(Path.Combine(stage, "PreparedQuality"), Path.Combine(stage, "Evaluated"));

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Wait until the existing Editor is idle in Edit Mode.");
        if (SceneManager.GetSceneByPath(FormalScene).isLoaded)
            throw new InvalidOperationException("Close the formal map before importing GL125.");
    }

    private static GameObject BuildWorld(Scene scene, GL125WorldAssets assets, IEnumerable<GL125WorldAssets.Chunk> chunks,
        out List<BlenderLightmapSet.Binding> bindings)
    {
        var root = new GameObject(WorldName);
        SceneManager.MoveGameObjectToScene(root, scene);
        var geometry = new GameObject("Static geometry");
        geometry.transform.SetParent(root.transform, false);
        var collision = new GameObject("Static collision");
        collision.transform.SetParent(root.transform, false);
        var structure = CreateStructureRoot(root.transform);
        bindings = new List<BlenderLightmapSet.Binding>();
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) throw new InvalidOperationException("Ground layer is missing.");
        foreach (var chunk in chunks)
        {
            bool visible = chunk.atlas >= 0;
            var obj = new GameObject(chunk.name);
            var origin = GL125WorldAssets.ToUnity(chunk.origin);
            var parent = visible ? geometry.transform : IsMc00Structure(chunk, origin) ? structure : collision.transform;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = origin;
            Mesh mesh = assets.MeshFor(chunk);
            if (visible)
            {
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = assets.MaterialsFor(chunk);
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                bindings.Add(new BlenderLightmapSet.Binding { renderer = renderer, atlas = chunk.atlas });
                GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.OccludeeStatic);
            }
            if (chunk.collider)
            {
                obj.layer = groundLayer;
                obj.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }
        return root;
    }

    private static Light MakeSun(Transform root, GL125WorldAssets assets)
    {
        var row = assets.Lights.Single(l => l.name == GL125WorldAssets.MainSun);
        var obj = new GameObject("GL125 Sun");
        obj.transform.SetParent(root, false);
        obj.transform.rotation = Quaternion.LookRotation(GL125WorldAssets.ToUnity(row.forward), GL125WorldAssets.ToUnity(row.up));
        var sun = obj.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = GL125WorldAssets.ColorFromLinear(row.color);
        sun.intensity = row.energy / Mathf.PI;
        sun.shadows = LightShadows.Soft;
        return sun;
    }

    private static void RenderPilot(Scene scene, string output)
    {
        var cameraObject = new GameObject("GL125 pilot camera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        cameraObject.transform.position = new Vector3(1611f, 213.3f, 2146f);
        cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(1, -.06f, .35f));
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.nearClipPlane = .04f;
        camera.farClipPlane = 100;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.015f, .017f, .022f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var destination = RenderTexture.GetTemporary(800, 450, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = RenderTexture.active;
        var image = new Texture2D(destination.width, destination.height, TextureFormat.RGB24, false);
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = destination };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = destination;
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
            File.WriteAllBytes(output, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(destination);
            Object.DestroyImmediate(image);
        }
    }
}
