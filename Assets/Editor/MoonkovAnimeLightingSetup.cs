using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Authored lilToon look without adding runtime lights or material instances.</summary>
public static class MoonkovAnimeLightingSetup
{
    public const string ProfilePath = "Assets/MoonEnvironment/Settings/MoonAnimePostProcessing.asset";
    public const string OriginalProfilePath = "Assets/MoonEnvironment/Variants/TheophilusBase/VisualPass01/TheophilusLook.asset";
    public const string PipelinePath = "Assets/MoonEnvironment/Settings/MoonSoftAnimePipeline.asset";
    public const string RendererPath = "Assets/MoonEnvironment/Settings/MoonSoftAnimeRenderer.asset";
    public const string MatCapPath = "Assets/DollSinger/Art/Textures/SoftPearlMatCap.png";
    public const string SkinMatCapPath = "Assets/DollSinger/Art/Textures/DewySkinMatCap.png";
    public const string HairMatCapPath = "Assets/DollSinger/Art/Textures/SilkHairMatCap.png";
    public const string ClothMatCapPath = "Assets/DollSinger/Art/Textures/SatinClothMatCap.png";
    public const string ScenePath = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
    private const string Materials = "Assets/DollSinger/Art/Materials/";
    private static readonly string[] Names =
    {
        "Face", "Body", "Hair", "Dress", "InnerDress", "DressDetail", "Shoes",
        "Jewel", "Glasses", "Eyes", "EyeWhite", "Brows", "Lashes"
    };

    [MenuItem("Tools/Moonkov/Rendering/Apply Soft MMD Character Look")]
    public static void ApplyCharacterLook()
    {
        RequireEditMode();
        UpdatePearlMatCap();
        // Validate the entire target set before starting to edit shared assets.
        var materials = Array.ConvertAll(Names, name => Require<Material>(Materials + name + ".mat"));
        foreach (var material in materials)
            if (!material.shader || !material.shader.name.StartsWith("Moonkov/lilToonMoon", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected the existing Moonkov lilToon shader: " + material.name);

        Undo.RecordObjects(materials, "Apply anime character look");
        foreach (var material in materials)
        {
            ConfigureSoftMaterial(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        Debug.Log("Applied soft MMD character shading to 13 shared DollSinger materials.");
    }

    // Also used on temporary preview clones; this function never saves an asset.
    public static void ConfigureSoftMaterial(Material material)
    {
        string name = material.name.Replace(" (Instance)", "");
        bool skin = name == "Face" || name == "Body";
        bool eye = name == "Eyes";
        bool eyeWhite = name == "EyeWhite";
        bool line = name == "Brows" || name == "Lashes";
        bool metal = name == "Jewel" || name == "Glasses";
        bool hair = name == "Hair";
        Float(material, "_LightMaxLimit", 1);
        Float(material, "_LightMinLimit", 0);
        Float(material, "_AsUnlit", 0);
        Float(material, "_UseShadow", 1);
        Float(material, "_ShadowStrength", skin ? .6f : eye || eyeWhite || line ? .4f : .75f);
        Float(material, "_ShadowBorder", skin ? .42f : .48f);
        Float(material, "_ShadowBlur", skin ? .28f : .22f);
        Float(material, "_Shadow2ndBorder", .15f);
        Float(material, "_Shadow2ndBlur", .2f);
        Float(material, "_ShadowBorderRange", .12f);
        Color(material, "_ShadowColor", skin ? new Color(.98f,.86f,.90f) : new Color(.84f,.83f,.96f));
        Color(material, "_Shadow2ndColor", skin ? new Color(.94f,.85f,.9f,.25f) : new Color(.82f,.82f,.95f,.25f));
        if (skin) Color(material, "_Color", new Color(1,.995f,.99f));
        else if (!eye && !eyeWhite && !line && !metal && !hair)
            Color(material, "_Color", new Color(.98f,.99f,1));
        if (eye || eyeWhite || line) Color(material, "_ShadowColor", new Color(.96f,.95f,1));

        // Preserve all of the model's tuned outline geometry and shadow-distance controls.
        Color(material, "_OutlineColor", skin ? new Color(.20f,.13f,.18f) : new Color(.10f,.11f,.17f));
        Float(material, "_UseRim", line ? 0 : 1);
        Float(material, "_RimDirStrength", 0);
        Float(material, "_RimFresnelPower", 3);
        Float(material, "_RimBorder", .35f);
        Float(material, "_RimBlur", .65f);
        Float(material, "_RimEnableLighting", 1);
        Float(material, "_RimShadowMask", 1);
        Float(material, "_RimMainStrength", .6f);
        Color(material, "_RimColor", new Color(.82f,.92f,1,skin ? .10f : eye || eyeWhite ? .16f : .24f));

        bool glossy = !line;
        Float(material, "_UseReflection", glossy ? 1 : 0);
        // All sheen follows visible light, including the camera-relative reflection layers.
        // Do not add unoccluded outdoor probe light to a character hiding indoors.
        Float(material, "_ApplyReflection", 0);
        Float(material, "_ApplySpecular", 1);
        Float(material, "_Metallic", 0);
        Float(material, "_Smoothness", eye ? .86f : eyeWhite ? .72f : metal ? .8f : hair ? .58f : skin ? .56f : .55f);
        Float(material, "_Reflectance", eye ? .04f : metal ? .08f : .025f);
        Float(material, "_SpecularToon", 0);
        Float(material, "_GSAAStrength", .8f);
        // Stretch the real light highlight along the hair instead of round plastic dots.
        Float(material, "_UseAnisotropy", hair ? 1 : 0);
        Float(material, "_Anisotropy2Reflection", hair ? 1 : 0);
        Float(material, "_AnisotropyScale", .65f);
        Float(material, "_AnisotropyTangentWidth", 1.6f);
        Float(material, "_AnisotropyBitangentWidth", .7f);
        Float(material, "_AnisotropySpecularStrength", .6f);
        Float(material, "_Anisotropy2ndSpecularStrength", .12f);
        Float(material, "_Anisotropy2ndTangentWidth", 2.5f);
        Float(material, "_Anisotropy2ndBitangentWidth", 1.2f);
        Float(material, "_Anisotropy2ndShift", .15f);
        Color(material, "_ReflectionColor", new Color(.88f,.94f,1,eye ? .35f : metal ? .4f : hair ? .20f : .12f));

        Float(material, "_UseMatCap", glossy ? 1 : 0);
        if (glossy)
        {
            string path = skin ? SkinMatCapPath : hair ? HairMatCapPath : metal || eye || eyeWhite ? MatCapPath : ClothMatCapPath;
            material.SetTexture("_MatCapTex", Require<Texture2D>(path));
            Float(material, "_MatCapBlendMode", 1);
            Float(material, "_MatCapEnableLighting", 1);
            Float(material, "_MatCapShadowMask", 1);
            Float(material, "_MatCapMainStrength", skin ? .65f : hair ? .55f : metal ? .6f : eye ? .1f : .5f);
            Float(material, "_MatCapLod", 0);
            Color(material, "_MatCapColor", new Color(.92f,.97f,1,eye ? .45f : eyeWhite ? .10f : hair ? .85f : metal ? .55f : skin ? .56f : .55f));
        }
        Float(material, "_UseBacklight", skin ? 1 : 0);
        if (skin)
        {
            Color(material, "_BacklightColor", new Color(1,.72f,.68f,.20f));
            Float(material, "_BacklightBorder", .25f);
            Float(material, "_BacklightBlur", .5f);
            Float(material, "_BacklightDirectivity", 3);
            Float(material, "_BacklightMainStrength", .6f);
            Float(material, "_BacklightReceiveShadow", 1);
        }
    }

    public static void UpdatePearlMatCap()
    {
        RequireEditMode();
        WriteMatCap(MatCapPath, 0);
        WriteMatCap(SkinMatCapPath, 1);
        WriteMatCap(HairMatCapPath, 2);
        WriteMatCap(ClothMatCapPath, 3);
    }

    // Continuous reflected sky / softbox gradients, rather than painted circular glints.
    // Every material has its own shape and contrast; no UV or source texture is replaced.
    private static void WriteMatCap(string path, int surface)
    {
        const int size = 256;
        var texture = new Texture2D(size,size,TextureFormat.RGB24,false);
        try
        {
            var pixels = new Color[size*size];
            for (int y=0;y<size;y++)
            for (int x=0;x<size;x++)
            {
                float u=2f*x/(size-1)-1, v=2f*y/(size-1)-1;
                float radius=Mathf.Min(1,Mathf.Sqrt(u*u+v*v));
                float z=Mathf.Sqrt(1-radius*radius);
                float fresnel=Mathf.Pow(1-z,1.6f);
                float side=Gaussian(u+.68f,.25f)*Gaussian(v-.08f,.95f);
                float opposite=Gaussian(u-.78f,.18f)*Gaussian(v+.12f,.75f);
                Color cool=new Color(.56f,.76f,1), warm=new Color(1,.87f,.78f);
                Color color;
                if (surface==1)
                {
                    // Hydrated skin: a thin grazing veil, soft warm lift, no white cheek spot.
                    float veil=Gaussian(u+.24f,.5f)*Gaussian(v-.18f,.65f);
                    color=cool*(.025f+.38f*fresnel+.33f*side)+warm*(.22f*opposite+.12f*veil);
                }
                else if (surface==2)
                {
                    // Hair follows a curved, wide satin ribbon with a softer second reflection.
                    float ribbon=Gaussian(v-.30f+.12f*u*u,.14f)*(.65f+.35f*Gaussian(u,.8f));
                    float lower=Gaussian(v+.42f-.08f*u,.28f);
                    color=cool*(.025f+.22f*fresnel+.12f*side+.14f*lower)+new Color(.9f,.95f,1)*ribbon*.72f;
                }
                else if (surface==3)
                {
                    // Silk: long gradients pick up the garment's normal / fold changes.
                    float fold=Gaussian(u+.38f+.18f*v,.24f)*Gaussian(v-.15f,.85f);
                    color=cool*(.025f+.34f*fresnel+.36f*fold)+warm*.16f*opposite;
                }
                else
                {
                    // Cornea and polished trim: broad window reflection plus thin edge sparkle.
                    float window=Gaussian(u+.43f,.16f)*Gaussian(v-.4f,.48f);
                    color=cool*(.035f+.38f*fresnel+.12f*opposite)+new Color(.94f,.98f,1)*window*.65f;
                }
                pixels[y*size+x]=color;
            }
            texture.SetPixels(pixels);texture.Apply();
            byte[] bytes=texture.EncodeToPNG();
            // Repeated previews must not reimport unchanged textures or dirty their metadata.
            if (File.Exists(path) && System.Linq.Enumerable.SequenceEqual(File.ReadAllBytes(path),bytes)) return;
            File.WriteAllBytes(path,bytes);
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.mipmapEnabled=true;
        importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static float Gaussian(float value, float width) => Mathf.Exp(-value*value/(width*width));

    [MenuItem("Tools/Moonkov/Rendering/Update Soft MMD Color Grade")]
    public static void UpdateColorGrade()
    {
        RequireEditMode();
        if (!AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath))
        {
            if (!AssetDatabase.CopyAsset(OriginalProfilePath, ProfilePath))
                throw new InvalidOperationException("Could not copy the existing lunar volume profile.");
        }
        var profile = Require<VolumeProfile>(ProfilePath);
        var color = Component<ColorAdjustments>(profile);
        color.postExposure.Override(.4f); color.contrast.Override(-4); color.saturation.Override(-2);
        var split = Component<SplitToning>(profile);
        split.shadows.Override(new Color(.49f,.495f,.515f));
        split.highlights.Override(new Color(.515f,.50f,.495f));
        split.balance.Override(6);
        var bloom = Component<Bloom>(profile);
        bloom.intensity.Override(.18f); bloom.threshold.Override(1.5f); bloom.scatter.Override(.55f);
        // Neutral preserves saturated costume colors while rolling off HDR highlights.
        Component<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
        Component<Vignette>(profile).intensity.Override(.04f);
        foreach (var component in profile.components)
        {
            EditorUtility.SetDirty(component);
            AssetDatabase.SaveAssetIfDirty(component);
        }
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssetIfDirty(profile);
    }

    [MenuItem("Tools/Moonkov/Rendering/Update Soft MMD Renderer")]
    public static void UpdateRenderer()
    {
        RequireEditMode();
        if (!AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath) &&
            !AssetDatabase.CopyAsset("Assets/MoonEnvironment/Settings/MoonRenderer.asset", RendererPath))
            throw new InvalidOperationException("Could not copy the lunar renderer.");
        var renderer = Require<UniversalRendererData>(RendererPath);
        foreach (var feature in renderer.rendererFeatures)
        {
            if (!(feature is ScreenSpaceAmbientOcclusion)) continue;
            Undo.RecordObject(feature, "Preserve clean toon skin");
            var settings = new SerializedObject(feature);
            // Let URP Lit consume AO during lighting. The former fullscreen
            // multiply dirtied lilToon faces and eye whites after shading.
            settings.FindProperty("m_Settings.AfterOpaque").boolValue = false;
            settings.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(feature);
        }
        renderer.SetDirty();
        EditorUtility.SetDirty(renderer);
        AssetDatabase.SaveAssetIfDirty(renderer);
        if (!AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath) &&
            !AssetDatabase.CopyAsset("Assets/MoonEnvironment/Variants/MC00BakedBuilding/MC00BakedPipeline.asset", PipelinePath))
            throw new InvalidOperationException("Could not copy the lunar pipeline.");
        var pipeline = Require<UniversalRenderPipelineAsset>(PipelinePath);
        Undo.RecordObject(pipeline, "Use soft MMD renderer");
        var serializedPipeline = new SerializedObject(pipeline);
        serializedPipeline.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
        serializedPipeline.ApplyModifiedProperties();
        AssetDatabase.SaveAssetIfDirty(pipeline);
    }

    [MenuItem("Tools/Moonkov/Rendering/Use Soft MMD Lighting In Open Theophilus Scene")]
    public static void ApplyToOpenScene()
    {
        RequireEditMode();
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Open the formal Theophilus scene first.");
        var profile = Require<VolumeProfile>(ProfilePath);
        var pipeline = Require<UniversalRenderPipelineAsset>(PipelinePath);
        var original = Require<VolumeProfile>(OriginalProfilePath);
        int count = 0;
        Volume target = null;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var volume in root.GetComponentsInChildren<Volume>(true))
        {
            if (volume.sharedProfile != original && volume.sharedProfile != profile) continue;
            target = volume;
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Expected exactly one lunar volume, found " + count);
        Unity.MP_FPS.Moon.MoonSceneLighting rig = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            rig = root.GetComponentInChildren<Unity.MP_FPS.Moon.MoonSceneLighting>(true);
            if (rig) break;
        }
        if (!rig) throw new InvalidOperationException("Missing the existing lunar lighting rig.");
        var serializedRig = new SerializedObject(rig);
        Undo.RecordObject(target, "Use anime color grade");
        target.sharedProfile = profile;
        EditorUtility.SetDirty(target);
        Undo.RecordObject(rig, "Use soft MMD pipeline");
        serializedRig.FindProperty("pipeline").objectReferenceValue = pipeline;
        serializedRig.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        // Saving remains explicit, so this menu does not save unrelated scene edits.
    }

    private static T Component<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (!profile.TryGet<T>(out var component)) throw new InvalidOperationException("Missing volume component: " + typeof(T).Name);
        Undo.RecordObject(component, "Update anime color grade");
        return component;
    }

    private static T Require<T>(string path) where T : UnityEngine.Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) throw new InvalidOperationException("Missing asset: " + path);
        return asset;
    }

    private static void Float(Material material, string property, float value)
    {
        if (!material.HasProperty(property)) throw new InvalidOperationException("Missing lilToon property: " + property);
        material.SetFloat(property, value);
    }

    private static void Color(Material material, string property, Color value)
    {
        if (!material.HasProperty(property)) throw new InvalidOperationException("Missing lilToon property: " + property);
        material.SetColor(property, value);
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Apply authored look in Edit Mode.");
    }
}
