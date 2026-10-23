using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Fail early for known player-rendering packaging hazards, before shader compilation.</summary>
public sealed class MoonkovPlayerBuildChecks : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
            EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server) return;
        Validate();
    }

    [MenuItem("Tools/Moonkov/Check Player Build Rendering")]
    public static void Validate()
    {
        var errors = new List<string>();
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        // BuildingBakedLighting installs non-directional atlases at runtime; scene analysis cannot infer them.
        if (graphics.FindProperty("m_LightmapStripping").intValue != 1 ||
            !graphics.FindProperty("m_LightmapKeepPlain").boolValue)
            errors.Add("Graphics: use Custom Lightmap Modes with Baked Non-Directional retained (BuildingBakedLighting).");
        // Resources retention materials preserve only the two runtime Lit instancing combinations.
        if (graphics.FindProperty("m_InstancingStripping").intValue != 0)
            errors.Add("Graphics: use Strip Unused instancing variants; runtime Lit variants have explicit retention materials.");
        var stripping = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
        if (stripping == null || !stripping.stripUnusedVariants)
            errors.Add("URP: enable Strip Unused Variants to avoid compiling unused rendering feature combinations.");
        var inclusion = GraphicsSettings.GetRenderPipelineSettings<IncludeAdditionalRPAssets>();
        if (inclusion == null || !inclusion.includeReferencedInScenes)
            errors.Add("Graphics: include render pipeline assets referenced by build scenes.");

        foreach (bool emission in new[] { false, true })
        {
            string resource = "Moonkov/RuntimeLitInstanced" + (emission ? "Emission" : "");
            var retained = Resources.Load<Material>(resource);
            if (!retained || !retained.shader || retained.shader.name != "Universal Render Pipeline/Lit" ||
                !retained.enableInstancing || retained.IsKeywordEnabled("_EMISSION") != emission)
                errors.Add("Missing runtime Lit variant retention material: " + resource);
        }

        var remains = Resources.Load<Material>("Moonkov/AlienRemains");
        if (!remains || !remains.shader || remains.shader.name != "Universal Render Pipeline/Lit" ||
            remains.shaderKeywords.Length != 0 || remains.renderQueue != (int)RenderQueue.Geometry ||
            !remains.GetShaderPassEnabled("ForwardLit"))
            errors.Add("Resources/Moonkov/AlienRemains must be an opaque, keyword-free Lit material with ForwardLit enabled.");

        // Inspect actual scene dependencies, not every experimental shader in the repository.
        var scenes = GetBuildScenes();
        if (scenes.Length == 0 || scenes[0] != "Assets/Scenes/MainMenu.unity")
            errors.Add("The player build must start with Assets/Scenes/MainMenu.unity.");
        const string map = "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        if (!scenes.Contains(map)) errors.Add("The formal TheophilusGameScene is missing from the enabled build scenes.");
        foreach (var scene in scenes)
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(scene)) errors.Add("Missing build scene: " + scene);

        var dependencies = AssetDatabase.GetDependencies(scenes, true);
        var shaders = new HashSet<Shader>();
        int materialCount = 0;
        foreach (var path in dependencies.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            materialCount++;
            if (!material || !material.shader) errors.Add("Missing material or shader: " + path);
            else shaders.Add(material.shader);
        }
        if (remains && remains.shader) shaders.Add(remains.shader);
        foreach (var shader in shaders)
            if (ShaderUtil.ShaderHasError(shader)) errors.Add("Shader compilation error: " + AssetDatabase.GetAssetPath(shader));

        if (errors.Count != 0)
            throw new BuildFailedException("Moonkov player rendering preflight failed:\n" + string.Join("\n", errors));
        Debug.Log($"[Moonkov build] Rendering preflight passed: {scenes.Length} scenes, {materialCount} materials, {shaders.Count} shaders. " +
            "This checks configuration/dependencies; a new player build and visual acceptance are still required.");
    }

    private static string[] GetBuildScenes()
    {
        var profile = UnityEditor.Build.Profile.BuildProfile.GetActiveBuildProfile();
        if (profile)
        {
            var serialized = new SerializedObject(profile);
            if (serialized.FindProperty("m_OverrideGlobalSceneList").boolValue)
            {
                var scenes = serialized.FindProperty("m_Scenes");
                var paths = new List<string>();
                for (int i = 0; i < scenes.arraySize; i++)
                {
                    var scene = scenes.GetArrayElementAtIndex(i);
                    if (scene.FindPropertyRelative("enabled").boolValue)
                        paths.Add(scene.FindPropertyRelative("path").stringValue);
                }
                return paths.ToArray();
            }
        }
        return EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
    }
}
