using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// The source mesh stays editor-only; the menu ships a single lit background texture.
public static class MoonkovBackgroundBaker
{
    public const string OutputPath = "Assets/UI Toolkit/GameUI/Textures/MoonkovSpaceBackground.png";
    private const string ModelPath = "Assets/FBX/Moon/Moon_NASA_LRO_23k_Topo_Unity.fbx";
    private const int Width = 2560, Height = 1440;

    public static void BakeStarfield()
    {
        const string path = "Assets/UI Toolkit/GameUI/Textures/MoonkovStarfield.png";
        var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        try { image.SetPixels32(CreateStarfield()); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { UnityEngine.Object.DestroyImmediate(image); }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.maxTextureSize = 4096; importer.npotScale = TextureImporterNPOTScale.None; importer.mipmapEnabled = false;
        importer.isReadable = false; importer.alphaSource = TextureImporterAlphaSource.None; importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        // A stylesheet imported before this generated texture exists caches the warning icon.
        AssetDatabase.ImportAsset("Assets/UI Toolkit/GameUI/MoonkovTerminal.uss", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
    }

    [MenuItem("Tools/Moonkov/Bake Space Background", true)]
    private static bool CanBake() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/Moonkov/Bake Space Background")]
    public static void Bake()
    {
        if (!CanBake()) throw new InvalidOperationException("Bake the menu background in Edit mode.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null) throw new InvalidOperationException("The NASA moon model is missing: " + ModelPath);
        var scene = EditorSceneManager.NewPreviewScene();
        var previousAmbientMode = RenderSettings.ambientMode;
        var previousAmbientLight = RenderSettings.ambientLight;
        float previousAmbientIntensity = RenderSettings.ambientIntensity;
        float previousReflectionIntensity = RenderSettings.reflectionIntensity;
        var previousSkybox = RenderSettings.skybox;
        bool previousFog = RenderSettings.fog;
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D image = null;
        Material material = null;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            var moon = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            var renderers = moon.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("The moon has no renderer.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = 3.9f / Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
            moon.transform.localScale *= scale;
            moon.transform.position = new Vector3(-3.45f, .35f, 0) - bounds.center * scale;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetTexture("_BaseMap", renderers[0].sharedMaterial.mainTexture);
            material.SetColor("_BaseColor", new Color(.86f, .87f, .89f));
            material.SetFloat("_Smoothness", 0);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_SpecularHighlights", 0);
            material.SetFloat("_EnvironmentReflections", 0);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            foreach (var renderer in renderers)
            {
                renderer.gameObject.layer = 5;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            CreateLight(scene, "Sun / upper right", new Vector3(-.82f, .46f, .32f), new Color(1, .96f, .89f), 3.2f);
            CreateLight(scene, "Faint earthshine", new Vector3(.6f, -.2f, 1), new Color(.45f, .62f, 1), .025f);
            var cameraObject = new GameObject("Background bake camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.cullingMask = 1 << 5;
            camera.orthographic = true;
            camera.orthographicSize = 5.25f;
            camera.aspect = (float)Width / Height;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 30;
            camera.transform.position = new Vector3(0, 0, 12);
            camera.transform.rotation = Quaternion.Euler(0, 180, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.volumeLayerMask = 0;
            var descriptor = new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4 };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            target = new RenderTexture(descriptor);
            target.Create();
            // Warm up the isolated preview lighting before reading the final frame.
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();
            var moonPixels = image.GetPixels32();
            var spacePixels = CreateStarfield();
            int litPixels = 0;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    var lunar = moonPixels[i];
                    float alpha = lunar.a / 255f;
                    if (alpha > .9f && Mathf.Max(lunar.r, lunar.g, lunar.b) > 35) litPixels++;
                    Color pixel = Color.Lerp(spacePixels[i], lunar, alpha);
                    float u = (float)x / Width, v = (float)y / Height;
                    // Keep navigation readable and leave the sunward limb luminous.
                    float leftShade = Mathf.Lerp(.36f, 1, Mathf.SmoothStep(0, 1, u / .62f));
                    float edgeShade = 1 - .28f * Mathf.Pow(Mathf.Clamp01(Vector2.Distance(new Vector2(u, v), new Vector2(.6f, .52f)) / .85f), 2);
                    pixel *= leftShade * edgeShade;
                    pixel.a = 1;
                    spacePixels[i] = pixel;
                }
            if (litPixels < Width * Height / 100) throw new InvalidOperationException("Moon render was empty or unlit; the existing background was preserved.");
            image.SetPixels32(spacePixels);
            image.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllBytes(OutputPath, image.EncodeToPNG());
            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(OutputPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            Debug.Log("Baked NASA moon and lighting to " + OutputPath + ". Menu background requires no live mesh, camera or repaint scheduler.");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbientLight;
            RenderSettings.ambientIntensity = previousAmbientIntensity;
            RenderSettings.reflectionIntensity = previousReflectionIntensity;
            RenderSettings.skybox = previousSkybox;
            RenderSettings.fog = previousFog;
            EditorSceneManager.ClosePreviewScene(scene);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void CreateLight(Scene scene, string name, Vector3 direction, Color color, float intensity)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.cullingMask = 1 << 5;
        light.shadows = LightShadows.None;
        go.transform.rotation = Quaternion.LookRotation(-direction.normalized);
    }

    private static Color32[] CreateStarfield()
    {
        var pixels = new Color32[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float u = (float)x / Width, v = (float)y / Height;
                float haze = Mathf.Exp(-Mathf.Pow((v - .27f - u * .3f) / .18f, 2));
                pixels[y * Width + x] = new Color(.011f + .012f * haze, .015f + .017f * haze, .026f + .028f * haze, 1);
            }
        var random = new System.Random(384400);
        for (int i = 0; i < 620; i++)
        {
            int x = random.Next(4, Width - 4), y = random.Next(4, Height - 4);
            float brightness = Mathf.Lerp(.13f, .75f, Mathf.Pow((float)random.NextDouble(), 3));
            Color tint = Color.Lerp(new Color(.66f, .79f, 1), new Color(1, .88f, .71f), (float)random.NextDouble());
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    float falloff = Mathf.Exp(-(dx * dx + dy * dy) * (i % 29 == 0 ? .7f : 2.8f));
                    int index = (y + dy) * Width + x + dx;
                    Color pixel = pixels[index];
                    pixel += tint * (brightness * falloff);
                    pixel.a = 1;
                    pixels[index] = pixel;
                }
        }
        return pixels;
    }
}
