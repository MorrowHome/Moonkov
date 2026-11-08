using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// Run through the existing Editor's Pipeline run_script command, outside Assets.
public static class BuildShaderWarningReview
{
    public static object Compile(string label)
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isPlaying)
            throw new InvalidOperationException("Use the existing idle Editor.");

        var rows = new List<object>();
        int tested = 0, failed = 0, warnings = 0;
        void Record(string name, string variant, bool success, ShaderMessage[] messages)
        {
            tested++;
            if (!success) failed++;
            warnings += messages.Count(m => m.severity == ShaderCompilerMessageSeverity.Warning);
            rows.Add(new { name, variant, success, messages = messages.Select(m => new { m.message, m.file, m.line, severity = m.severity.ToString() }).ToArray() });
        }

        var compute = typeof(ShaderUtil).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "CompileComputeShaderVariant");
        var successProperty = compute.ReturnType.GetProperty("Success");
        var messagesProperty = compute.ReturnType.GetProperty("Messages");
        var fluid = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/UmbrellaSpiderLab/Resources/Umbra08Fluid.compute");
        foreach (var api in new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Metal })
        foreach (string kernel in new[] { "Initialize", "Transport", "ClearGrid", "BuildGrid", "Density", "Integrate", "ClearVolume", "Deposit", "ResolveX", "BlurY", "BlurZ" })
        {
            var result = compute.Invoke(null, new object[] { fluid, fluid.FindKernel(kernel), Array.Empty<string>(), api,
                api == GraphicsDeviceType.Metal ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64 });
            Record(fluid.name, api + "/" + kernel, (bool)successProperty.GetValue(result), (ShaderMessage[])messagesProperty.GetValue(result));
        }

        foreach (string path in new[] {
            "Assets/Resources/Moonkov/TacticalVolume.shader", "Assets/Resources/Moonkov/HaloScan.shader",
            "Assets/MoonEnvironment/Shaders/Regolith.shader", "Assets/MoonEnvironment/Shaders/LunarRock.shader" })
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            var subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            for (int pass = 0; pass < subshader.PassCount; pass++)
            foreach (var platform in new[] { ShaderCompilerPlatform.D3D, ShaderCompilerPlatform.Metal })
            foreach (var type in new[] { ShaderType.Vertex, ShaderType.Fragment })
            foreach (var keywords in new[] {
                Array.Empty<string>(),
                new[] { "_MOON_LUNAR_LIGHTING", "_MAIN_LIGHT_SHADOWS", "_SHADOWS_SOFT" },
                new[] { "_MOON_LUNAR_LIGHTING", "_ADDITIONAL_LIGHTS", "_MAIN_LIGHT_SHADOWS_SCREEN" } })
            {
                if (!path.Contains("MoonEnvironment") && keywords.Length > 0) continue;
                var result = subshader.GetPass(pass).CompileVariant(type, keywords, platform,
                    platform == ShaderCompilerPlatform.Metal ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64);
                Record(shader.name, platform + "/" + pass + "/" + type + "/" + string.Join(",", keywords), result.Success, result.Messages);
            }
        }

        string directory = "Library/BuildValidation/WarningCleanup";
        Directory.CreateDirectory(directory);
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json")
            .GetType("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject", new[] { typeof(object) });
        File.WriteAllText(directory + "/" + label + ".json", (string)json.Invoke(null, new object[] { new { tested, failed, warnings, rows } }));
        return new { tested, failed, warnings, report = directory + "/" + label + ".json" };
    }

}
