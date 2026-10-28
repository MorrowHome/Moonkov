using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>A requested validation survives unrelated shared-Editor domain reloads.</summary>
    [InitializeOnLoad]
    public static class SourceWorldShaderValidation
    {
        private const string Folder = "LocalData/SourceFidelity20261028";
        private const string State = Folder + "/ShaderValidation.state";
        private const int Count = 477;
        static SourceWorldShaderValidation()
        {
            if (File.Exists(State)) EditorApplication.update += Tick;
        }
        [MenuItem("Tools/Moon Environment/Source World/Validate all source shaders")]
        public static void Start()
        {
            Directory.CreateDirectory(Folder); File.WriteAllText(State, "0");
            File.WriteAllText(Folder + "/AllShaderErrors.txt", "");
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (!File.Exists(State)) { EditorApplication.update -= Tick; return; }
            int index = int.Parse(File.ReadAllText(State));
            if (index >= Count) { EditorApplication.update -= Tick; return; }
            var shader = Shader.Find("MoonEnvironment/Source/" + index.ToString("000"));
            var errors = new List<string>();
            if (!shader) errors.Add(index + ": missing shader");
            else
            {
                var material = new Material(shader);
                try
                {
                    foreach (var keyword in new[] { "LIGHTMAP_ON", "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION", "_ADDITIONAL_LIGHTS" }) material.EnableKeyword(keyword);
                    ShaderUtil.CompilePass(material, 0, true);
                    for (int pass = 1; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
                    errors.AddRange(ShaderUtil.GetShaderMessages(shader)
                        .Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        .Select(m => index + ": " + m.message + " " + m.file + ":" + m.line));
                }
                catch (Exception exception) { errors.Add(index + ": " + exception.Message); }
                finally { UnityEngine.Object.DestroyImmediate(material); }
            }
            if (errors.Count != 0) File.AppendAllLines(Folder + "/AllShaderErrors.txt", errors);
            index++; File.WriteAllText(State, index.ToString());
            File.WriteAllText(Folder + "/AllShaderProgress.txt", index + "/" + Count + " errors=" + File.ReadAllLines(Folder + "/AllShaderErrors.txt").Length);
            if (index == Count) { EditorApplication.update -= Tick; Debug.Log("Source shader validation complete: " + File.ReadAllText(Folder + "/AllShaderProgress.txt")); }
        }
    }
}
