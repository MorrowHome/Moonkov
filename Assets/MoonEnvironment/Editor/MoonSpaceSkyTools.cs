using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.Moon.Editor
{
    public static class MoonSpaceSkyTools
    {
        public const string MaterialPath = "Assets/MoonEnvironment/Materials/LunarSpaceSky.mat";
        public const string VolumePath = "Assets/MoonEnvironment/Settings/MoonSpacePostProcessing.asset";
        private const string ScenePath = "Assets/MoonEnvironment/Scenes/MoonGameScene.unity";
        private const string ArtRoot = "Assets/MoonEnvironment/Art/Space/";

        [MenuItem("Tools/Moon Environment/Set Up Space Sky")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            // Keep unsaved user authoring safe when opening the formal map.
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("Save modified scenes before setting up the lunar sky.");
            }
            ConfigureTexture(ArtRoot + "NASA_BlueMarble_2048.png", false, 2048);
            ConfigureTexture(ArtRoot + "NASA_StarMap_2020_4k.exr", true, 4096);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                Shader shader = Shader.Find("MoonEnvironment/SpaceSky");
                if (!shader || ShaderUtil.ShaderHasError(shader))
                    throw new InvalidOperationException("Resolve space sky shader errors first.");
                material = new Material(shader) { name = "LunarSpaceSky" };
                material.SetTexture("_EarthMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + "NASA_BlueMarble_2048.png"));
                material.SetTexture("_StarMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + "NASA_StarMap_2020_4k.exr"));
                // Approximate terrain-centre coordinates from the imported DEM metadata.
                // Synchronous Moon, zero libration: Earth lies near the local west/north sky.
                float latitude = -13.7855f * Mathf.Deg2Rad;
                float longitude = 25.164f * Mathf.Deg2Rad;
                material.SetVector("_EarthDirection", new Vector3(-Mathf.Sin(longitude),
                    Mathf.Cos(latitude) * Mathf.Cos(longitude), -Mathf.Sin(latitude) * Mathf.Cos(longitude)));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            Scene scene = SceneManager.GetActiveScene().path == ScenePath
                ? SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            MoonSceneLighting lighting = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                lighting = root.GetComponentInChildren<MoonSceneLighting>(true);
                if (lighting) break;
            }
            if (!lighting) throw new InvalidOperationException("Formal Moon map has no MoonSceneLighting.");
            var serialized = new SerializedObject(lighting);
            serialized.FindProperty("spaceSkybox").objectReferenceValue = material;
            var sun = serialized.FindProperty("sun").objectReferenceValue as Light;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (sun) material.SetVector("_SunDirection", -sun.transform.forward);
            RenderSettings.skybox = material;
            ConfigurePostProcessing(scene);
            EditorUtility.SetDirty(material);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigurePostProcessing(Scene scene)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/MoonEnvironment/Settings/MoonRenderer.asset");
            var postData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (!renderer || !postData) throw new InvalidOperationException("Moon URP post-processing resources missing.");
            var rendererData = new SerializedObject(renderer);
            rendererData.FindProperty("postProcessData").objectReferenceValue = postData;
            rendererData.ApplyModifiedPropertiesWithoutUndo();
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "MoonSpacePostProcessing";
                AssetDatabase.CreateAsset(profile, VolumePath);
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.value = 0.9f;
                bloom.threshold.value = 1.3f;
                bloom.scatter.value = 0.75f;
                bloom.clamp.value = 128;
                bloom.highQualityFiltering.value = true;
                bloom.maxIterations.value = 7;
                AssetDatabase.AddObjectToAsset(bloom, profile);
                EditorUtility.SetDirty(profile);
            }
            if (!profile.TryGet<Tonemapping>(out var tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
                tonemapping.mode.value = TonemappingMode.ACES;
                AssetDatabase.AddObjectToAsset(tonemapping, profile);
                EditorUtility.SetDirty(profile);
            }
            Volume volume = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "Moon Space Volume") { volume = root.GetComponent<Volume>(); break; }
            if (!volume)
            {
                var volumeObject = new GameObject("Moon Space Volume");
                SceneManager.MoveGameObjectToScene(volumeObject, scene);
                volume = volumeObject.AddComponent<Volume>();
            }
            volume.gameObject.layer = 0; // Matches the existing DollSinger camera's Volume mask.
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            volume.weight = 1;
            EditorUtility.SetDirty(volume);
        }

        private static void ConfigureTexture(string path, bool hdr, int maximumSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing NASA sky texture: " + path);
            if (importer.textureType == TextureImporterType.Default && importer.textureShape == TextureImporterShape.Texture2D &&
                importer.sRGBTexture == !hdr && !importer.isReadable && importer.mipmapEnabled &&
                importer.wrapModeU == TextureWrapMode.Repeat && importer.wrapModeV == TextureWrapMode.Clamp &&
                importer.filterMode == FilterMode.Trilinear && importer.maxTextureSize == maximumSize &&
                importer.textureCompression == TextureImporterCompression.CompressedHQ && importer.alphaSource == TextureImporterAlphaSource.None)
                return;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = !hdr;
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = maximumSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
        }
    }
}
