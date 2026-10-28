using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Install the complete, immutable source environment into the actual gameplay scene.</summary>
    public static class SourceWorldSceneInstall
    {
        public const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        public const string RootName = "Source World Static";
        private static readonly HashSet<string> VisualRoots = new HashSet<string>
        {
            "THEOPHILUS_OUTER_BUILDINGS_BATCH01", "THEOPHILUS_OUTER_BUILDINGS_BATCH02", "THEOPHILUS_OUTER_BUILDINGS_BATCH03",
            "Theophilus_A_Start", "R35_FROM_R29_DISPLAY_ROOT", "B_CENTRAL_DESCENT_TOWER_DETAIL_R10",
            "B_RADIAL_SUPPORT_0197_DETAIL_R9", "B_WEST_TRANSFER_06_DETAIL_R6", "MC00_ROOT_Standard_YUp_Frame",
            "R29_FROM_R24_FINAL_DISPLAY_ROOT", "R36R38_FROM_R35_DISPLAY_ROOT", "R21_OPEN_SHAFT_WITH_R20_MECHANICAL_ROOT",
            "R7_A_START_SAMPLE_SERVICE_ROOT", "R24_HERO_FINISH_DISPLAY_ROOT", "R21_RETAINED_R18_R19_ROOT"
        };
        private static readonly HashSet<string> RetiredControllers = new HashSet<string>
        {
            "BuildingBakedLighting", "BakedRoomLighting", "InteriorLightingRig", "LocalLightBudget", "LocalLightVisibility",
            "LightCircuitControls", "LightSwitchButton", "BuildingRenderClusters", "InteriorVisibility", "MoonStochasticLighting",
            "MoonSilverPrint", "MoonLowSampleMaterialScope", "MoonRockLod", "MoonRockGrounding"
        };
        [MenuItem("Tools/Moon Environment/Source World/Install complete world in formal game")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install in Edit Mode.");
            var geometry = SourceWorldAssetImport.Geometry; var manifest = SourceWorldAssetImport.Manifest;
            var data = AssetDatabase.LoadAssetAtPath<SourceWorldLightingData>(SourceWorldAssetImport.DataPath);
            if (!data || !data.characterProbes || !data.reflections || data.reflections.cubemaps.Any(c => !c) ||
                data.irradiance == null || data.irradiance.Length != manifest.atlases.Length ||
                geometry.atlasCount != manifest.atlases.Length || data.irradiance.Any(t => !t))
                throw new InvalidOperationException("All source world assets must finish importing before formal installation.");
            if (data.sourceSha256 != manifest.sourceSha256 || geometry.sourceSha256 != data.sourceSha256) throw new InvalidOperationException("Source provenance mismatch.");
            var materials = manifest.materials.ToDictionary(m => m.id, m => AssetDatabase.LoadAssetAtPath<Material>(m.asset));
            if (materials.Values.Any(m => !m || !m.shader || !m.shader.isSupported)) throw new InvalidOperationException("A source material is unavailable.");
            var meshes = new Dictionary<string, Mesh>();
            foreach (int atlas in geometry.chunks.Select(c => c.atlas).Distinct())
            {
                var bundle = AssetDatabase.LoadAssetAtPath<SourceWorldMeshBundle>(SourceWorldAssetImport.Root + "Meshes/Atlas_" + atlas.ToString("000") + ".asset");
                if (!bundle || bundle.meshes == null) throw new InvalidOperationException("Missing source mesh atlas " + atlas);
                foreach (var mesh in bundle.meshes) meshes.Add(mesh.name, mesh);
            }
            if (geometry.chunks.Any(c => !meshes.ContainsKey("SourceMesh_" + c.id.ToString("00000")))) throw new InvalidOperationException("Source meshes incomplete.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(s => s.isDirty))
                    throw new InvalidOperationException("Preserve the shared Editor's unsaved scene before formal installation.");
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            if (scene.GetRootGameObjects().Any(r => r.name == RootName)) throw new InvalidOperationException("Formal source world is already installed; validate the existing installation.");
            string backup = SourceWorldAssetImport.Work + "TheophilusGameScene.before-source.unity";
            if (!File.Exists(backup)) File.Copy(ScenePath, backup);
            var roots = scene.GetRootGameObjects();
            var protectedRenderers = GameplayRenderers(roots);
            var lighting = roots.SelectMany(r => r.GetComponentsInChildren<MoonSceneLighting>(true)).Single();
            var lightingSettings = new SerializedObject(lighting);
            var sun = lightingSettings.FindProperty("sun").objectReferenceValue as Light;
            if (!sun) throw new InvalidOperationException("Formal scene has no main Sun.");
            foreach (var root in roots)
            {
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour && RetiredControllers.Contains(behaviour.GetType().Name))
                    {
                        behaviour.enabled = false;
                        var serialized = new SerializedObject(behaviour); var property = serialized.GetIterator();
                        while (property.NextVisible(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                                (property.objectReferenceValue is Texture || property.objectReferenceValue is Material || property.objectReferenceValue is ScriptableObject || property.objectReferenceValue is Mesh))
                                property.objectReferenceValue = null;
                        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(behaviour);
                    }
                // Retired controllers restore old renderers when disabled. Hide after that.
                if (VisualRoots.Contains(root.name))
                {
                    foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (protectedRenderers.Contains(renderer)) continue;
                        renderer.forceRenderingOff = true; renderer.enabled = false; renderer.sharedMaterials = Array.Empty<Material>(); EditorUtility.SetDirty(renderer);
                        if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter) { filter.sharedMesh = null; EditorUtility.SetDirty(filter); if (PrefabUtility.IsPartOfPrefabInstance(filter)) PrefabUtility.RecordPrefabInstancePropertyModifications(filter); }
                    }
                    foreach (var terrain in root.GetComponentsInChildren<Terrain>(true)) { terrain.drawHeightmap = false; terrain.drawTreesAndFoliage = false; EditorUtility.SetDirty(terrain); }
                }
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                    if (light != sun) { light.enabled = false; light.cullingMask = 0; EditorUtility.SetDirty(light); }
                foreach (var probe in root.GetComponentsInChildren<ReflectionProbe>(true)) { probe.enabled = false; EditorUtility.SetDirty(probe); }
                foreach (var environment in root.GetComponentsInChildren<MoonEnvironment>(true)) { environment.ConfigureSourceVisuals(true); EditorUtility.SetDirty(environment); }
            }
            var sourceRoot = new GameObject(RootName); SceneManager.MoveGameObjectToScene(sourceRoot, scene);
            var bindings = new List<SourceWorldLighting.RendererBinding>(geometry.chunks.Length);
            foreach (var chunk in geometry.chunks)
            {
                var go = new GameObject("Source_" + chunk.id.ToString("00000")); go.transform.SetParent(sourceRoot.transform, false); go.transform.position = chunk.position;
                go.AddComponent<MeshFilter>().sharedMesh = meshes["SourceMesh_" + chunk.id.ToString("00000")];
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = materials[chunk.material];
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided; renderer.receiveShadows = true;
                renderer.lightmapIndex = chunk.atlas; renderer.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                // Preserve the source payload and spatial chunk boundaries; no Unity static batching copies.
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                bindings.Add(new SourceWorldLighting.RendererBinding { renderer = renderer, atlas = chunk.atlas });
            }
            var probeRoot = new GameObject("Source room reflections"); probeRoot.transform.SetParent(sourceRoot.transform, false);
            for (int i = 0; i < data.reflections.probes.Length; i++)
            {
                var p = data.reflections.probes[i]; if (p.importance == 0) continue;
                var go = new GameObject("SourceReflection_" + i.ToString("000")); go.transform.SetParent(probeRoot.transform, false); go.transform.position = p.position;
                var probe = go.AddComponent<ReflectionProbe>(); probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = data.reflections.cubemaps[i];
                probe.center = (p.boxMin + p.boxMax) * .5f - p.position; probe.size = Vector3.Max(p.boxMax - p.boxMin, Vector3.one * .1f);
                probe.boxProjection = true; probe.blendDistance = .4f; probe.importance = p.importance; probe.intensity = 1;
            }
            sourceRoot.AddComponent<SourceWorldLighting>().Configure(data, bindings.ToArray());
            sun.transform.rotation = Quaternion.LookRotation(-data.towardSun, Mathf.Abs(data.towardSun.y) > .999f ? Vector3.forward : Vector3.up);
            sun.color = data.linearSunColor.gamma; sun.intensity = data.sourceSunEnergy / Mathf.PI; sun.enabled = true; sun.cullingMask = ~0;
            sun.shadows = LightShadows.Soft; EditorUtility.SetDirty(sun);
            var pipeline = ConfigurePipeline(lightingSettings.FindProperty("pipeline").objectReferenceValue as UniversalRenderPipelineAsset);
            lightingSettings.FindProperty("pipeline").objectReferenceValue = pipeline;
            lightingSettings.FindProperty("adaptInteriorShadows").boolValue = false;
            lightingSettings.ApplyModifiedPropertiesWithoutUndo(); lighting.ConfigureSourceStaticLighting(data); EditorUtility.SetDirty(lighting);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets(); Validate();
        }
        private static HashSet<MeshRenderer> GameplayRenderers(GameObject[] roots)
        {
            var result = new HashSet<MeshRenderer>();
            foreach (var component in roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)))
            {
                if (!component) continue;
                if (component.GetType().Name == "AirlockButton")
                    foreach (var renderer in component.GetComponentsInChildren<MeshRenderer>(true)) result.Add(renderer);
                if (component.GetType().Name != "AirlockController") continue;
                var serialized = new SerializedObject(component);
                foreach (string name in new[] { "m_OuterPanels", "m_InnerPanels" })
                {
                    var panels = serialized.FindProperty(name); if (panels == null) continue;
                    for (int i = 0; i < panels.arraySize; i++)
                    {
                        var motion = panels.GetArrayElementAtIndex(i).FindPropertyRelative("Motion").objectReferenceValue as Transform;
                        if (motion) foreach (var renderer in motion.GetComponentsInChildren<MeshRenderer>(true)) result.Add(renderer);
                    }
                }
            }
            return result;
        }
        private static UniversalRenderPipelineAsset ConfigurePipeline(UniversalRenderPipelineAsset pipeline)
        {
            if (!pipeline) throw new InvalidOperationException("Missing formal source render pipeline.");
            string path = SourceWorldAssetImport.Root + "Lighting/SourceWorldForwardRenderer.asset";
            var settings = new SerializedObject(pipeline); var renderers = settings.FindProperty("m_RendererDataList");
            var original = renderers.GetArrayElementAtIndex(0).objectReferenceValue as UniversalRendererData;
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (!renderer)
            {
                if (!original) throw new InvalidOperationException("Formal renderer is not URP UniversalRendererData.");
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), path)) throw new InvalidOperationException("Could not copy the formal renderer and its features.");
                renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path); renderer.name = "SourceWorldForwardRenderer";
            }
            foreach (var feature in renderer.rendererFeatures) if (feature) { feature.SetActive(false); EditorUtility.SetDirty(feature); }
            var rendererSettings = new SerializedObject(renderer); rendererSettings.FindProperty("m_RenderingMode").intValue = 2; rendererSettings.ApplyModifiedPropertiesWithoutUndo();
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            foreach (string property in new[] { "m_ReflectionProbeBlending", "m_ReflectionProbeBoxProjection", "m_SupportsHDR", "m_RequireOpaqueTexture", "m_RequireDepthTexture", "m_AdditionalLightShadowsSupported" })
            { var p = settings.FindProperty(property); if (p != null) p.boolValue = true; }
            var downsampling = settings.FindProperty("m_OpaqueDownsampling"); if (downsampling != null) downsampling.intValue = 0;
            var mode = settings.FindProperty("m_AdditionalLightsRenderingMode"); if (mode != null) mode.intValue = 1;
            settings.ApplyModifiedPropertiesWithoutUndo(); pipeline.msaaSampleCount = 4; EditorUtility.SetDirty(renderer); EditorUtility.SetDirty(pipeline); return pipeline;
        }
        [MenuItem("Tools/Moon Environment/Source World/Validate formal world installation")]
        public static void Validate()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Open the formal game scene to validate.");
            var root = scene.GetRootGameObjects().Single(r => r.name == RootName); var owner = root.GetComponent<SourceWorldLighting>();
            var geometry = SourceWorldAssetImport.Geometry; var data = owner.Data;
            if (owner.Bindings.Length != geometry.chunks.Length || owner.Bindings.Any(b => !b.renderer || !b.renderer.sharedMaterial || !b.renderer.GetComponent<MeshFilter>().sharedMesh))
                throw new InvalidOperationException("Incomplete formal source world bindings.");
            if (root.GetComponentsInChildren<ReflectionProbe>(true).Length != data.reflections.probes.Count(p => p.importance != 0)) throw new InvalidOperationException("Incomplete formal reflections.");
            var report = new { scene = ScenePath, sourceSha256 = data.sourceSha256, chunks = owner.Bindings.Length,
                triangles = geometry.triangles, materials = owner.Bindings.Select(b => b.renderer.sharedMaterial).Distinct().Count(), atlases = data.irradiance.Length,
                reflectionCaptures = data.reflections.cubemaps.Length, characterProbes = data.characterProbes.countSelf,
                colliders = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Collider>(true).Length) };
            File.WriteAllText(SourceWorldAssetImport.Work + "FormalInstallation.txt", "scene=" + report.scene + "\nsource=" + report.sourceSha256 + "\nchunks=" + report.chunks + "\ntriangles=" + report.triangles + "\nmaterials=" + report.materials + "\natlases=" + report.atlases + "\nreflectionCaptures=" + report.reflectionCaptures + "\ncharacterProbes=" + report.characterProbes + "\ncolliders=" + report.colliders);
            Debug.Log("Formal source world installed and bound: " + report.chunks + " chunks, " + report.materials + " materials, " + report.atlases + " irradiance atlases, " + report.reflectionCaptures + " reflection captures.");
        }
    }
}
