using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Reapplies the measured Full v02 lighting preset after rebuilding its candidate.
    /// Only this candidate owns the new pipeline; the shared Apollo12 asset stays intact.
    /// This is an indoor preview preset, not a hardware-independent 60 FPS guarantee.
    /// </summary>
    public static class MC00LightingPerformanceTools
    {
        public const string PrefabPath = MC00FullLightControlTools.Folder + "/Prefabs/Apollo12MC00Candidate.prefab";
        public const string PipelinePath = MC00FullLightControlTools.Folder + "/Settings/MC00InteriorPipeline.asset";
        private const string BasePipelinePath = "Assets/MoonEnvironment/Variants/Apollo12Base/Settings/Apollo12LaptopPipeline.asset";

        [MenuItem("Tools/Moon Environment/MC00 Full Lights v02/5 - Apply measured interior lighting preset")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play before changing the candidate asset.");
            var sourcePipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(BasePipelinePath);
            if (!sourcePipeline) throw new InvalidOperationException("Missing Apollo12 baseline pipeline.");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var fixtures = root.GetComponentsInChildren<Light>(true)
                    .Where(l => l.name == "Runtime Light" && l.type == LightType.Spot).ToArray();
                var environment = root.GetComponentsInChildren<MoonSceneLighting>(true).Single();
                if (fixtures.Length != 118)
                    throw new InvalidOperationException("Expected all 118 Full v02 fixtures; refusing a partial preset.");

                string backup = "Library/BuildValidation/MC00LightingBefore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
                Directory.CreateDirectory(backup);
                File.Copy(PrefabPath, backup + "/Apollo12MC00Candidate.prefab");
                if (File.Exists(PipelinePath)) File.Copy(PipelinePath, backup + "/MC00InteriorPipeline.asset");
                string folder = MC00FullLightControlTools.Folder + "/Settings";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(MC00FullLightControlTools.Folder, "Settings");
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                if (!pipeline)
                {
                    pipeline = UnityEngine.Object.Instantiate(sourcePipeline);
                    pipeline.name = "MC00InteriorPipeline";
                    AssetDatabase.CreateAsset(pipeline, PipelinePath);
                }
                // Fixed-pose A/B: 350 m submitted distant interior shadows; 30 m
                // keeps nearby occlusion with URP's existing fade, without switching power.
                pipeline.shadowDistance = 30;
                // This mesh/light mix was slower with GRD (19.3 vs 13.8 ms).
                // Keep Forward+, SRP Batcher, resolution and all light intensities unchanged.
                pipeline.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
                EditorUtility.SetDirty(pipeline);
                AssetDatabase.SaveAssetIfDirty(pipeline);
                foreach (var light in fixtures)
                {
                    // Near-180 cones caused the regression. Retain the broad fully lit
                    // core while bounding the shadow projection and its edge penumbra.
                    light.spotAngle = 150;
                    light.innerSpotAngle = 120;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                }
                var settings = new SerializedObject(environment);
                settings.FindProperty("pipeline").objectReferenceValue = pipeline;
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(environment);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Candidate prefab save failed.");
                Debug.Log("MC00 interior preset saved: 118 lights 120/150, shadows 30 m, GRD disabled. Backup: " + backup);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
