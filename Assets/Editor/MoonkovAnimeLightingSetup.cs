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
        Color(material, "_ShadowColor", skin ? new Color(.98f,.92f,.94f) : new Color(.9f,.88f,.98f));
        Color(material, "_Shadow2ndColor", skin ? new Color(.94f,.85f,.9f,.25f) : new Color(.82f,.82f,.95f,.25f));
        if (skin) Color(material, "_Color", new Color(1,.995f,.99f));
        if (eye || eyeWhite || line) Color(material, "_ShadowColor", new Color(.96f,.95f,1));

        // Preserve all of the model's tuned outline geometry and shadow-distance controls.
        Color(material, "_OutlineColor", skin ? new Color(.20f,.13f,.18f) : new Color(.10f,.11f,.17f));
        Float(material, "_UseRim", eye || eyeWhite || line ? 0 : 1);
        Float(material, "_RimDirStrength", 0);
        Float(material, "_RimFresnelPower", 3.5f);
        Float(material, "_RimBorder", .45f);
        Float(material, "_RimBlur", .5f);
        Float(material, "_RimEnableLighting", 1);
        Float(material, "_RimShadowMask", 1);
        Float(material, "_RimMainStrength", .25f);
        Color(material, "_RimColor", new Color(.94f,.96f,1,skin ? .12f : .22f));

        bool glossy = !line && !eyeWhite;
        Float(material, "_UseReflection", glossy ? 1 : 0);
        // The sheen follows actual light visibility. An outdoor reflection probe
        // must not keep a character's hair/eyes bright inside a dark occluder.
        Float(material, "_ApplyReflection", 0);
        Float(material, "_ApplySpecular", 1);
        Float(material, "_Metallic", 0);
        Float(material, "_Smoothness", eye ? .94f : metal ? .76f : hair ? .62f : skin ? .63f : .48f);
        Float(material, "_Reflectance", eye ? .08f : skin ? .025f : .04f);
        Float(material, "_SpecularToon", 0);
        Float(material, "_GSAAStrength", .6f);
        Float(material, "_UseAnisotropy", 0);
        Color(material, "_ReflectionColor", new Color(.98f,.98f,1,eye ? .75f : metal ? .45f : hair ? .28f : skin ? .18f : .14f));

        Float(material, "_UseMatCap", glossy ? 1 : 0);
        if (glossy)
        {
            material.SetTexture("_MatCapTex", Require<Texture2D>(MatCapPath));
            Float(material, "_MatCapBlendMode", 1);
            Float(material, "_MatCapEnableLighting", 1);
            Float(material, "_MatCapShadowMask", 1);
            Float(material, "_MatCapMainStrength", eye ? 0 : skin ? .5f : .35f);
            Color(material, "_MatCapColor", new Color(.96f,.98f,1,eye ? .65f : hair ? .45f : metal ? .55f : skin ? .25f : .28f));
        }
        Float(material, "_UseBacklight", skin ? 1 : 0);
        if (skin)
        {
            Color(material, "_BacklightColor", new Color(.95f,.68f,.65f,.12f));
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
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(MatCapPath)) return;
        const int size = 128;
        var texture = new Texture2D(size,size,TextureFormat.RGB24,false);
        try
        {
            var pixels = new Color[size*size];
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float u=2f*x/(size-1)-1, v=2f*y/(size-1)-1;
                if(u*u+v*v>1) { pixels[y*size+x]=new Color(0,0,0,1); continue; }
                float key=Mathf.Exp(-Mathf.Pow((u+.23f)/.14f,2)-Mathf.Pow((v-.3f)/.3f,2));
                float fill=Mathf.Exp(-Mathf.Pow((u-.24f)/.3f,2)-Mathf.Pow((v+.05f)/.45f,2));
                float rim=Mathf.Pow(1-Mathf.Sqrt(1-u*u-v*v),5);
                pixels[y*size+x]=new Color(1,.96f,.94f)*key*.9f + new Color(.65f,.78f,1)*fill*.3f + new Color(.82f,.88f,1)*rim*.08f;
            }
            texture.SetPixels(pixels);texture.Apply();
            File.WriteAllBytes(MatCapPath,texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
        AssetDatabase.ImportAsset(MatCapPath,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(MatCapPath);
        importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.mipmapEnabled=true;
        importer.wrapMode=TextureWrapMode.Clamp;importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

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
