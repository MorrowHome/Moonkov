using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Captures one powered reflection per circuit; off uses zero intensity, not a second baked state.</summary>
    public static class MC00BuildingReflectionTools
    {
        const string HolderName = "MC00 baked room reflections";
        const string Folder = MC00BuildingBakeGeometry.Folder + "/Reflections";
        public static string Status { get; private set; } = "Idle";
        public static int Completed { get; private set; }

        sealed class Configuration
        {
            public BuildingBakedLightingData data;
            public BuildingBakedLighting.RendererBinding[] renderers;
            public BuildingBakedLighting.Circuit[] circuits;
            public ComputeShader composer;
            public void Apply(BuildingBakedLighting owner) => owner.Configure(data, renderers, circuits, composer);
        }

        [MenuItem("Tools/Moon Environment/MC00 Building Bake/4 - Bake and install room reflections")]
        public static void BakeAndInstall()
        {
            Completed = 0; Status = "Preparing room reflections";
            try { Bake(); }
            catch (Exception error) { Status = "Failed: " + error.Message; throw; }
        }

        static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
                throw new InvalidOperationException("Finish Play and lightmap baking first.");
            var manifest = MC00BuildingBakeTools.ReadManifest();
            var owner = UnityEngine.Object.FindObjectsByType<BuildingBakedLighting>(FindObjectsInactive.Include)
                .Single(o => o.gameObject.scene.path == manifest.sourceScene);
            if (!owner.isActiveAndEnabled || owner.transform.Find(HolderName))
                throw new InvalidOperationException("An active installed owner with no existing reflection holder is required.");
            if ((owner.transform.lossyScale - Vector3.one).sqrMagnitude > .000001f)
                throw new InvalidOperationException("The installed building must retain unit world scale.");
            var original = ReadConfiguration(owner);
            var rooms = manifest.rooms.ToDictionary(r => r.roomId, StringComparer.Ordinal);
            if (rooms.Count != original.circuits.Length || original.circuits.Any(c => !rooms.ContainsKey(c.id)))
                throw new InvalidOperationException("Manifest room bounds do not match installed circuits.");
            for (int i = 0; i < original.circuits.Length; i++)
                if (File.Exists(TexturePath(i))) throw new InvalidOperationException("Refusing to overwrite existing reflection: " + TexturePath(i));

            var previousActiveScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(owner.gameObject.scene);
            var previousPipeline = QualitySettings.renderPipeline;
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbientLight = RenderSettings.ambientLight;
            float previousAmbientIntensity = RenderSettings.ambientIntensity, previousReflectionIntensity = RenderSettings.reflectionIntensity;
            var previousSkybox = RenderSettings.skybox;
            GameObject holder = null;
            var probes = new ReflectionProbe[original.circuits.Length];
            Completed = 0; Status = "Preparing room reflections";
            try
            {
                owner.Restore();
                holder = new GameObject(HolderName); holder.transform.SetParent(owner.transform, false);
                var serialized = new SerializedObject(owner);
                var circuits = serialized.FindProperty("circuits");
                for (int i = 0; i < circuits.arraySize; i++)
                {
                    var circuit = circuits.GetArrayElementAtIndex(i);
                    string id = circuit.FindPropertyRelative("id").stringValue;
                    var bounds = rooms[id].bounds;
                    if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                        throw new InvalidOperationException("Invalid reflection volume: " + id);
                    var go = new GameObject(id + " reflection"); go.transform.SetParent(holder.transform, false);
                    go.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
                    var probe = go.AddComponent<ReflectionProbe>(); probes[i] = probe;
                    probe.mode = ReflectionProbeMode.Custom; probe.resolution = 128; probe.hdr = true;
                    probe.boxProjection = true; probe.size = bounds.size; probe.center = Vector3.zero;
                    probe.blendDistance = 1; probe.importance = 100; probe.nearClipPlane = .15f; probe.farClipPlane = 50;
                    probe.renderDynamicObjects = true; probe.intensity = 0;
                    probe.clearFlags = ReflectionProbeClearFlags.SolidColor; probe.backgroundColor = Color.black;
                    var references = circuit.FindPropertyRelative("reflections");
                    references.arraySize = 1; references.GetArrayElementAtIndex(0).objectReferenceValue = probe;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                // Rebuild the owner's cached circuit lookup after changing serialized references.
                ReadConfiguration(owner).Apply(owner);
                Directory.CreateDirectory(Folder); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                try
                {
                    QualitySettings.renderPipeline = MC00BuildingBakeTools.Pipeline();
                    RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.015f, .015f, .015f, 1);
                    RenderSettings.ambientIntensity = 1; RenderSettings.reflectionIntensity = 0; RenderSettings.skybox = null;
                    if (!owner.Apply()) throw new InvalidOperationException("Could not apply room lighting: " + owner.ConfigurationError);
                    foreach (var probe in probes) probe.intensity = 0;
                    for (int i = 0; i < probes.Length; i++)
                    {
                        Status = "Baking reflection " + (i + 1) + "/" + probes.Length;
                        string path = TexturePath(i);
                        if (!Lightmapping.BakeReflectionProbe(probes[i], path)) throw new InvalidOperationException("Reflection capture failed: " + path);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        var texture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                        if (!texture) throw new InvalidOperationException("Reflection did not import as a Cubemap: " + path);
                        probes[i].customBakedTexture = texture; probes[i].intensity = 0; Completed++;
                    }
                }
                finally
                {
                    try { owner.Restore(); }
                    finally
                    {
                        QualitySettings.renderPipeline = previousPipeline;
                        RenderSettings.ambientMode = previousAmbientMode; RenderSettings.ambientLight = previousAmbientLight;
                        RenderSettings.ambientIntensity = previousAmbientIntensity; RenderSettings.reflectionIntensity = previousReflectionIntensity;
                        RenderSettings.skybox = previousSkybox;
                        foreach (var settings in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")) EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssets();
                    }
                }
                foreach (var probe in probes) { probe.intensity = 1; EditorUtility.SetDirty(probe); }
                EditorUtility.SetDirty(owner); EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
                if (!EditorSceneManager.SaveScene(owner.gameObject.scene)) throw new IOException("Could not save the formal scene with reflections.");
                Status = "Ready: " + Completed + " room reflections";
                Debug.Log(Status + ". Off circuits use zero reflection intensity; cross-room reflection changes are approximate.");
            }
            catch (Exception error)
            {
                owner.Restore(); original.Apply(owner);
                if (holder) UnityEngine.Object.DestroyImmediate(holder);
                EditorUtility.SetDirty(owner);
                Status = "Failed: " + error.Message;
                Debug.LogError(Status + ". Scene bindings restored; generated EXR files retained.");
                throw;
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        static string TexturePath(int index) => Folder + "/Room" + index.ToString("D2") + ".exr";

        static Configuration ReadConfiguration(BuildingBakedLighting owner)
        {
            var serialized = new SerializedObject(owner);
            var rendererArray = serialized.FindProperty("renderers");
            var circuitArray = serialized.FindProperty("circuits");
            var result = new Configuration { data = owner.Data, composer = serialized.FindProperty("composer").objectReferenceValue as ComputeShader,
                renderers = new BuildingBakedLighting.RendererBinding[rendererArray.arraySize], circuits = new BuildingBakedLighting.Circuit[circuitArray.arraySize] };
            for (int i = 0; i < result.renderers.Length; i++)
            {
                var value = rendererArray.GetArrayElementAtIndex(i);
                result.renderers[i] = new BuildingBakedLighting.RendererBinding { renderer = value.FindPropertyRelative("renderer").objectReferenceValue as MeshRenderer,
                    lightmapIndex = value.FindPropertyRelative("lightmapIndex").intValue, scaleOffset = value.FindPropertyRelative("scaleOffset").vector4Value };
            }
            for (int i = 0; i < result.circuits.Length; i++)
            {
                var value = circuitArray.GetArrayElementAtIndex(i);
                result.circuits[i] = new BuildingBakedLighting.Circuit { id = value.FindPropertyRelative("id").stringValue,
                    fixtures = References<Light>(value.FindPropertyRelative("fixtures")), realtimeLights = References<Light>(value.FindPropertyRelative("realtimeLights")),
                    reflections = References<ReflectionProbe>(value.FindPropertyRelative("reflections")) };
            }
            return result;
        }

        static T[] References<T>(SerializedProperty array) where T : UnityEngine.Object
        {
            var result = new T[array.arraySize];
            for (int i = 0; i < result.Length; i++) result[i] = array.GetArrayElementAtIndex(i).objectReferenceValue as T;
            return result;
        }
    }
}
