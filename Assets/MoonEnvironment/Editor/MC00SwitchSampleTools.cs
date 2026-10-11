using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Moon.Preview;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Authored switch adapter with a preserved v07 preset and separate delivery paths.</summary>
    public static class MC00SwitchSampleTools
    {
        public const string Source = "SourceData/Models/MC00SwitchSampleV07";
        public const string Folder = "Assets/MoonEnvironment/Variants/MC00SwitchSampleV07";
        public const string ScenePath = Folder + "/Scenes/MC00GameExportPreview.unity";
        public const string EnvironmentPath = Folder + "/Prefabs/Apollo12MC00Candidate.prefab";
        const string Hash = "c985ea16ff85e0ff53d8566860ec33374d8213bd4e35976fc200874056627fc0";
        const string InteractionFolder = Folder + "/SwitchMaterials";
        [Serializable] public class Interactions { public string sourceSha256; public ButtonInfo[] buttons; }
        [Serializable] public class ButtonInfo
        {
            public string switch_id, button_object, interaction_root, indicator_object, indicator_material_name;
            public float press_travel_m;
            public float[] position_world, press_axis_world, bounds_min, bounds_max;
        }
        [Serializable] class PanelMap { public Panel[] sample_panels, panels; }
        [Serializable] class Panel { public string panel_id; public float[] approach_world, mount_origin_world; }
        // Values come from the real delivery, never from an invented future asset.
        // The v07 menu is intentionally a fixed preset; newer files use these methods.
        [Serializable] public sealed class Delivery
        {
            public string sourceFolder, assetFolder, sourceSha256, revision;
            public int lightCount = 118, switchCount, controlledLightCount = 118;
        }
        static void ValidateDelivery(Delivery delivery)
        {
            if (delivery == null || string.IsNullOrWhiteSpace(delivery.revision) || string.IsNullOrEmpty(delivery.sourceSha256) ||
                delivery.sourceSha256.Length != 64 || !delivery.sourceSha256.All(Uri.IsHexDigit) ||
                string.IsNullOrEmpty(delivery.sourceFolder) || string.IsNullOrEmpty(delivery.assetFolder) ||
                !delivery.sourceFolder.StartsWith("SourceData/Models/", StringComparison.Ordinal) ||
                !delivery.assetFolder.StartsWith("Assets/MoonEnvironment/Variants/", StringComparison.Ordinal) ||
                delivery.sourceFolder.Contains("..") || delivery.assetFolder.Contains("..") ||
                delivery.lightCount <= 0 || delivery.controlledLightCount <= 0 || delivery.switchCount < 0)
                throw new ArgumentException("Require actual versioned delivery paths, source SHA256 and positive light counts.");
            var manifest = JsonUtility.FromJson<MC00GameExportTools.Manifest>(File.ReadAllText(delivery.sourceFolder + "/unity-manifest.json"));
            if (!string.Equals(manifest.sourceSha256, delivery.sourceSha256, StringComparison.OrdinalIgnoreCase) || manifest.lights.Length != delivery.lightCount)
                throw new InvalidOperationException("Delivery hash/light registry does not match the exported source.");
        }
        static void NewVersion(Delivery delivery, Action operation)
        {
            ValidateDelivery(delivery);
            delivery.sourceFolder = delivery.sourceFolder.Replace('\\', '/').TrimEnd('/');
            delivery.assetFolder = delivery.assetFolder.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(delivery.sourceFolder, Source, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(delivery.assetFolder, Folder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(delivery.sourceFolder, MC00GameExportTools.Source, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(delivery.assetFolder, MC00GameExportTools.Folder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Keep the original and v07 assets; choose new delivery folders.");
            MC00GameExportTools.WithVariant(delivery.sourceFolder, delivery.assetFolder, operation);
        }
        public static void ImportDelivery(Delivery delivery) => NewVersion(delivery, MC00GameExportTools.ImportAssets);
        public static void CreateDelivery(Delivery delivery) => NewVersion(delivery, MC00GameExportTools.CreateCandidate);
        public static void OptimizeDelivery(Delivery delivery) => NewVersion(delivery, MC00CpuOptimizationTools.Optimize);
        public static void BindDelivery(Delivery delivery) => NewVersion(delivery, () => BindVersion(delivery, true));

        static void Version(Action operation)
        {
            var manifest = JsonUtility.FromJson<MC00GameExportTools.Manifest>(File.ReadAllText(Source + "/unity-manifest.json"));
            if (manifest.sourceSha256 != Hash || manifest.renderObjects != 9122 || manifest.proxyObjects != 277)
                throw new InvalidOperationException("The v07 export contract does not match this source.");
            MC00GameExportTools.WithVariant(Source, Folder, operation);
        }
        [MenuItem("Tools/Moon Environment/MC00 Switch v07/1 - Import assets")]
        public static void Import() => Version(MC00GameExportTools.ImportAssets);
        [MenuItem("Tools/Moon Environment/MC00 Switch v07/2 - Create separate candidate")]
        public static void Create() => Version(MC00GameExportTools.CreateCandidate);
        [MenuItem("Tools/Moon Environment/MC00 Switch v07/3 - Optimize CPU submission")]
        public static void Optimize() => Version(MC00CpuOptimizationTools.Optimize);

        [MenuItem("Tools/Moon Environment/MC00 Switch v07/4 - Bind six authored switches")]
        public static void Bind() => BindVersion(new Delivery {
            sourceFolder = Source, assetFolder = Folder, sourceSha256 = Hash, revision = "switch-sample-v07",
            lightCount = 118, switchCount = 6, controlledLightCount = 9 }, false);

        static void BindVersion(Delivery delivery, bool fullDelivery)
        {
            RequireCleanEditScene();
            ValidateDelivery(delivery);
            string interactionFolder = delivery.assetFolder + "/SwitchMaterials";
            string environmentPath = delivery.assetFolder + "/Prefabs/Apollo12MC00Candidate.prefab";
            string scenePath = delivery.assetFolder + "/Scenes/MC00GameExportPreview.unity";
            string mappingText = File.ReadAllText(delivery.sourceFolder + "/AuthorNotes/MC00_LIGHT_SWITCH_MAP_V2.json");
            var map = JsonUtility.FromJson<MC00LightSwitchTools.Mapping>(mappingText);
            var interactions = JsonUtility.FromJson<Interactions>(File.ReadAllText(delivery.sourceFolder + "/unity-switch-interactions.json"));
            if (!string.Equals(interactions.sourceSha256, delivery.sourceSha256, StringComparison.OrdinalIgnoreCase) || interactions.buttons == null || map.switches == null || map.lights == null ||
                interactions.buttons.Length != map.switches.Length || map.switches.Length == 0 || map.lights.Length != delivery.lightCount ||
                delivery.switchCount > 0 && map.switches.Length != delivery.switchCount)
                throw new InvalidOperationException("Incomplete delivery mapping.");
            if (map.switches.Any(s => s == null || s.target_light_ids == null || s.target_light_ids.Length == 0))
                throw new InvalidOperationException("Every switch needs an explicit target list.");
            // The source keeps the complete registry. Only the nine controlled lamps
            // receive switchable material variants; other lighting remains authored.
            var targetIds = map.switches.SelectMany(s => s.target_light_ids).Distinct().ToArray();
            if (targetIds.Length != delivery.controlledLightCount || targetIds.Any(id => !map.lights.Any(l => l.target_light_id == id)))
                throw new InvalidOperationException("Controlled lamp coverage differs from the delivery contract.");
            if (fullDelivery) ValidateFullCoverage(map, interactions, delivery.lightCount);
            map.lights = map.lights.Where(l => targetIds.Contains(l.target_light_id)).ToArray();
            map.asset_id = "MC00"; map.asset_revision = delivery.revision;
            Directory.CreateDirectory(interactionFolder); AssetDatabase.Refresh();
            var environment = PrefabUtility.LoadPrefabContents(environmentPath);
            try
            {
                var model = environment.GetComponentsInChildren<Transform>(true).Single(t => t.name == "MC00_Render");
                var nodes = model.GetComponentsInChildren<Transform>(true).ToLookup(t => t.name);
                var root = nodes["MC00_ROOT"].Single();
                if (root.GetComponent<LightCircuitControls>()) throw new InvalidOperationException("Already bound; preserve candidate edits.");
                var budget = model.GetComponentInParent<LocalLightBudget>();
                var indicatorSlots = new HashSet<(Renderer, int)>();
                var lampSurfaces = new HashSet<string>(map.lights.SelectMany(l => l.emissive_targets ?? Array.Empty<MC00LightSwitchTools.Emission>()).Select(e => e.renderer_object));
                // Complete all geometry, identity and indicator checks before adding components.
                foreach (var info in interactions.buttons)
                {
                    var node = nodes[info.button_object].Single();
                    if (!node.IsChildOf(nodes[info.interaction_root].Single())) throw new InvalidOperationException("Button lost its interaction parent.");
                    var mesh = node.GetComponent<MeshFilter>().sharedMesh;
                    if ((node.position - model.TransformPoint(Point(info.position_world))).magnitude > .001f)
                        throw new InvalidOperationException("Button position changed in FBX: " + node.name);
                    var expected = WorldBounds(model, info.bounds_min, info.bounds_max);
                    var actual = node.GetComponent<Renderer>().bounds;
                    if ((expected.center - actual.center).magnitude > .002f || (expected.size - actual.size).magnitude > .002f || mesh.bounds.size.sqrMagnitude == 0)
                        throw new InvalidOperationException("Button bounds changed in FBX: " + node.name);
                    var indicator = nodes[info.indicator_object].Single().GetComponent<Renderer>();
                    if (!indicator || indicator.sharedMaterials.Count(m => m && m.name == info.indicator_material_name) != 1)
                        throw new InvalidOperationException("Indicator material must resolve exactly once.");
                    int indicatorSlot = Array.FindIndex(indicator.sharedMaterials, m => m && m.name == info.indicator_material_name);
                    if (!indicatorSlots.Add((indicator, indicatorSlot)) || lampSurfaces.Contains(indicator.name))
                        throw new InvalidOperationException("Indicator needs one state owner and a Renderer separate from lamp emission bindings: " + info.indicator_object);
                    if (info.press_travel_m != 0 && Mathf.Abs(info.press_travel_m - .003f) > .00001f)
                        throw new InvalidOperationException("Unexpected authored press travel.");
                }
                foreach (var info in interactions.buttons)
                {
                    var node = nodes[info.button_object].Single();
                    // Imported Mesh.bounds already carries FBX basis conversion. Never
                    // copy Blender local X/Y/Z bounds directly onto a Unity collider.
                    foreach (var collider in node.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                    var bounds = node.GetComponent<MeshFilter>().sharedMesh.bounds;
                    var box = node.gameObject.AddComponent<BoxCollider>(); box.center = bounds.center; box.size = bounds.size;
                    var clusters = node.GetComponentInParent<BuildingRenderClusters>();
                    if (clusters) clusters.ReleaseFor(node.GetComponent<Renderer>());
                    GameObjectUtility.SetStaticEditorFlags(node.gameObject, 0);
                }
                var controls = MC00LightSwitchTools.Bind(map, model, root, budget, delivery.sourceFolder, interactionFolder);
                foreach (var info in interactions.buttons)
                {
                    var item = map.switches.Single(s => s.switch_id == info.switch_id);
                    if (item.button_object != info.button_object) throw new InvalidOperationException("Switch identity mismatch.");
                    var node = nodes[info.button_object].Single(); var indicator = nodes[info.indicator_object].Single().GetComponent<Renderer>();
                    var slots = indicator.sharedMaterials;
                    int slot = Array.FindIndex(slots, m => m && m.name == info.indicator_material_name);
                    var original = slots[slot];
                    var cluster = indicator.GetComponentInParent<BuildingRenderClusters>(); if (cluster) cluster.ReleaseFor(indicator);
                    GameObjectUtility.SetStaticEditorFlags(indicator.gameObject, GameObjectUtility.GetStaticEditorFlags(indicator.gameObject) & ~StaticEditorFlags.ContributeGI);
                    // delivery.sourceFolder root +Z is converted in world space first, then stored in
                    // the cap parent's local coordinates. This handles tilted L22 keys.
                    var offset = node.parent.InverseTransformVector(model.TransformVector(Point(info.press_axis_world).normalized * info.press_travel_m));
                    node.GetComponent<LightSwitchButton>().Configure(item.switch_id, item.label, controls, item.target_light_ids,
                        (LightCommand)Enum.Parse(typeof(LightCommand), item.action), null, indicator,
                        Indicator(original,"On",new Color(.15f,.65f,.32f),interactionFolder,fullDelivery,info.indicator_object == info.button_object), Indicator(original,"Off",new Color(.04f,.055f,.06f),interactionFolder,fullDelivery,info.indicator_object == info.button_object),
                        Indicator(original,"Mixed",new Color(.9f,.52f,.08f),interactionFolder,fullDelivery,info.indicator_object == info.button_object), Indicator(original,"Unavailable",new Color(.65f,.12f,.12f),interactionFolder,fullDelivery,info.indicator_object == info.button_object), slot, offset);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(indicator);
                }
                PrefabUtility.SaveAsPrefabAsset(environment, environmentPath);
                File.WriteAllText(delivery.sourceFolder + "/UnityBoundMapping.v2.json", JsonUtility.ToJson(map, true));
            }
            finally { PrefabUtility.UnloadPrefabContents(environment); }
            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.OpenScene(scenePath);
            var roots = scene.GetRootGameObjects();
            var input = roots.SelectMany(r => r.GetComponentsInChildren<DollSingerInput>(true)).Single();
            var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.enabled);
            var controller = roots.SelectMany(r => r.GetComponentsInChildren<LightCircuitControls>(true)).Single();
            controller.gameObject.AddComponent<LightSwitchPreviewInteractor>().Configure(camera, input);
            EditorSceneManager.SaveScene(scene);
            Debug.Log(delivery.revision + ": " + map.switches.Length + " buttons, " + map.lights.Length + " controlled lights bound. Play acceptance remains manual.");
        }

        // Optional editor-only station shortcuts avoid a debug teleport feature in gameplay.
        [MenuItem("Tools/Moon Environment/MC00 Switch v07/Stand at G03 ceiling controls")]
        public static void G03() => StandAt("PANEL_MC00_G03_CEILING");
        [MenuItem("Tools/Moon Environment/MC00 Switch v07/Stand at L22 controls")]
        public static void L22() => StandAt("PANEL_MC00_L22_OPERATIONS");
        public static void StandAt(string panelId, string scenePath = ScenePath, string sourceFolder = Source)
        {
            RequireCleanEditScene();
            var scene = SceneManager.GetActiveScene();
            if (scene.path != scenePath) throw new InvalidOperationException("Open the matching candidate scene first.");
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var model = all.Single(t => t.name == "MC00_Render");
            var input = all.Select(t => t.GetComponent<DollSingerInput>()).Single(x => x);
            var map = JsonUtility.FromJson<PanelMap>(File.ReadAllText(sourceFolder + "/AuthorNotes/MC00_LIGHT_SWITCH_MAP_V2.json"));
            var panel = (map.sample_panels ?? map.panels).Single(p => p.panel_id == panelId);
            if (panel.approach_world == null || panel.approach_world.Length != 3)
            {
                // The full delivery retains the old operations panel but omits its
                // stance. Reuse that authored stance only if its mount has not moved.
                var old = JsonUtility.FromJson<PanelMap>(File.ReadAllText(Source + "/AuthorNotes/MC00_LIGHT_SWITCH_MAP_V2.json"))
                    .sample_panels.Single(p => p.panel_id == panelId);
                if ((Point(old.mount_origin_world) - Point(panel.mount_origin_world)).magnitude > .001f)
                    throw new InvalidOperationException("No authored approach point for the moved panel.");
                panel.approach_world = old.approach_world;
            }
            var eye = model.TransformPoint(Point(panel.approach_world));
            Physics.SyncTransforms();
            var floors = Physics.RaycastAll(eye, Vector3.down, 4, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(h => !h.collider.transform.IsChildOf(input.transform) && h.normal.y > .6f).OrderBy(h => h.distance).ToArray();
            if (floors.Length == 0) throw new InvalidOperationException("No floor below author approach point; leave spawn unchanged.");
            var facing = model.TransformPoint(Point(panel.mount_origin_world)) - eye; facing.y = 0;
            Undo.RecordObject(input.transform, "Move v07 preview spawn");
            input.transform.SetPositionAndRotation(floors[0].point + Vector3.up * .05f, Quaternion.LookRotation(facing));
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = input.gameObject;
        }
        static void ValidateFullCoverage(MC00LightSwitchTools.Mapping map, Interactions interactions, int lightCount)
        {
            var ids = new HashSet<string>(map.lights.Select(l => l.target_light_id), StringComparer.Ordinal);
            if (ids.Count != lightCount || !ids.SetEquals(map.switches.SelectMany(s => s.target_light_ids)))
                throw new InvalidOperationException("Full delivery must cover every stable lamp ID without unknown targets.");
            if (map.switches.Select(s => s.switch_id).Distinct().Count() != map.switches.Length ||
                interactions.buttons.Select(b => b.switch_id).Distinct().Count() != interactions.buttons.Length)
                throw new InvalidOperationException("Duplicate switch identity in the delivery.");
            // Six published keys remain the rollback reference. New keys use their own
            // Renderer for indication; no additional per-key indicator object is required.
            var previous = JsonUtility.FromJson<MC00LightSwitchTools.Mapping>(File.ReadAllText(Source + "/AuthorNotes/MC00_LIGHT_SWITCH_MAP_V2.json"));
            var previousHints = JsonUtility.FromJson<Interactions>(File.ReadAllText(Source + "/unity-switch-interactions.json"));
            foreach (var old in previous.switches)
            {
                var next = map.switches.SingleOrDefault(s => s.switch_id == old.switch_id);
                if (next == null || next.button_object != old.button_object || next.action != old.action ||
                    next.indicator_object != old.indicator_object || next.indicator_material_name != old.indicator_material_name ||
                    next.interaction_anchor != old.interaction_anchor || !new HashSet<string>(old.target_light_ids).SetEquals(next.target_light_ids))
                    throw new InvalidOperationException("Existing v07 switch contract changed: " + old.switch_id);
                var oldHint = previousHints.buttons.Single(h => h.switch_id == old.switch_id);
                var nextHint = interactions.buttons.Single(h => h.switch_id == old.switch_id);
                if ((Point(oldHint.position_world) - Point(nextHint.position_world)).magnitude > .001f ||
                    Mathf.Abs(oldHint.press_travel_m - nextHint.press_travel_m) > .00001f ||
                    (Point(oldHint.press_axis_world) - Point(nextHint.press_axis_world)).magnitude > .0001f)
                    throw new InvalidOperationException("Existing v07 key position/travel changed: " + old.switch_id);
            }
            foreach (var item in map.switches)
            {
                var hint = interactions.buttons.Single(h => h.switch_id == item.switch_id);
                if (hint.button_object != item.button_object || hint.indicator_object != item.indicator_object ||
                    hint.indicator_material_name != item.indicator_material_name || hint.interaction_root != item.interaction_anchor)
                    throw new InvalidOperationException("Switch map and interaction hints disagree: " + item.switch_id);
                if (!previous.switches.Any(s => s.switch_id == item.switch_id) && item.indicator_object != item.button_object)
                    throw new InvalidOperationException("New keys should use their own Renderer as indicator: " + item.switch_id);
            }
        }
        static Material Indicator(Material original, string state, Color color, string folder, bool separateFamilies, bool keepMaps)
        {
            string family = separateFamilies ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(original)) + (keepMaps ? "_Keycap_" : "_Legacy_") : "";
            string path = folder + "/" + family + "Indicator" + state + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path); if (material) return material;
            material = new Material(original) { name = "Indicator" + state, enableInstancing = true, globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive };
            // Legacy six indicators retain the plain palette. A new self-indicating
            // cap preserves its authored textures; its dedicated state slot is tinted.
            if (!keepMaps) { material.SetTexture("_BaseMap", null); material.SetTexture("_EmissionMap", null); }
            material.SetColor("_BaseColor", color); material.SetColor("_EmissionColor", color * .7f); material.EnableKeyword("_EMISSION");
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static Vector3 Point(float[] p) => new Vector3(-p[0], p[2], -p[1]);
        static Bounds WorldBounds(Transform model, float[] min, float[] max)
        {
            var result = new Bounds(model.TransformPoint(Point(min)), Vector3.zero);
            for (int i = 0; i < 8; i++) result.Encapsulate(model.TransformPoint(Point(new[] { (i & 1) == 0 ? min[0] : max[0], (i & 2) == 0 ? min[1] : max[1], (i & 4) == 0 ? min[2] : max[2] })));
            return result;
        }
        static void RequireCleanEditScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene first.");
        }
    }
}
