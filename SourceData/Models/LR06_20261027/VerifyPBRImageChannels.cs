// Compare imported GPU texture channels with the original packed PNGs.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LR06VerifyPBRImageChannels
{
    [Serializable] public class MapInfo { public string asset_path; }
    [Serializable] public class MaterialRow { public string name; public MapInfo normal, metallic_smoothness; }
    [Serializable] public class Plan { public MaterialRow[] materials; }
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new Exception("Editor busy");
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var plan = (Plan)json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { System.IO.File.ReadAllText("SourceData/Models/LR06_20261027/SharedPBRPlan.json"), typeof(Plan) });
        var entries = plan.materials.SelectMany(m => new[] { (m.normal.asset_path, true), (m.metallic_smoothness.asset_path, false) })
            .GroupBy(x => x.Item1).Select(g => g.First()).ToArray();
        var previous = RenderTexture.active;
        var reports = new List<object>();
        try
        {
            foreach (var entry in entries)
            {
                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                Texture2D sourcePixels = null, importedPixels = null;
                var rt = RenderTexture.GetTemporary(1024, 1024, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                try
                {
                    if (!raw.LoadImage(System.IO.File.ReadAllBytes(entry.Item1))) throw new Exception("Source PNG unreadable");
                    var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Item1);
                    if (!imported) throw new Exception("Imported image missing");
                    Texture2D Read(Texture source)
                    {
                        Graphics.Blit(source, rt); RenderTexture.active = rt;
                        var result = new Texture2D(1024, 1024, TextureFormat.RGBA32, false, true);
                        result.ReadPixels(new Rect(0,0,1024,1024),0,0); result.Apply(); return result;
                    }
                    sourcePixels = Read(raw); importedPixels = Read(imported);
                    double red = 0, alpha = 0, green = 0, normalAlpha = 0;
                    int count = 0;
                    for (int y = 23; y < 1024; y += 73) for (int x = 19; x < 1024; x += 71)
                    {
                        var a = sourcePixels.GetPixel(x,y); var b = importedPixels.GetPixel(x,y);
                        red += Math.Abs(a.r-b.r); green += Math.Abs(a.g-b.g); alpha += Math.Abs(a.a-b.a);
                        normalAlpha += Math.Abs(a.r-b.a); count++;
                    }
                    red /= count; green /= count; alpha /= count; normalAlpha /= count;
                    if (!entry.Item2 && (red > .025 || alpha > .035)) throw new Exception("Metallic/smoothness channel mismatch: " + entry.Item1);
                    if (entry.Item2 && (Math.Min(red, normalAlpha) > .04 || green > .04)) throw new Exception("Normal map channel mismatch: " + entry.Item1);
                    reports.Add(new { path = entry.Item1, role = entry.Item2 ? "normal" : "metallic_smoothness", samples = count,
                        redMeanError = red, alphaMeanError = alpha, greenMeanError = green, normalXfromAlphaMeanError = normalAlpha });
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(raw);
                    if (sourcePixels) UnityEngine.Object.DestroyImmediate(sourcePixels);
                    if (importedPixels) UnityEngine.Object.DestroyImmediate(importedPixels);
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            var result = new { status = "gpu_import_channels_checked", textures = reports.Count, reports };
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { result });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06PBR/UnityPBRChannels.json", serialized);
            return result;
        }
        finally { RenderTexture.active = previous; }
    }
}
