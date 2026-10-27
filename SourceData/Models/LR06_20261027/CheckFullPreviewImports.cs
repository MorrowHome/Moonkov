using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class LR06CheckFullPreviewImports
{
    public static object Run()
    {
        const string folder = "Assets/MoonEnvironment/Variants/LR06FullPreview";
        var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var plan = json.GetMethod("DeserializeObject", new[] { typeof(string), typeof(Type) }).Invoke(null,
            new object[] { System.IO.File.ReadAllText(folder + "/PreviewPlan.json"), typeof(object) });
        object Field(object value, string name) => value.GetType().GetProperty("Item", new[] { typeof(string) }).GetValue(value, new object[] { name });
        T Read<T>(object value) => (T)value.GetType().GetMethod("ToObject", new[] { typeof(Type) }).Invoke(value, new object[] { typeof(T) });
        object[] Rows(object value) => ((IEnumerable)value).Cast<object>().ToArray();
        var parts = Rows(Field(plan, "parts"));
        var textures = Rows(Field(plan, "textures"));
        var missingModels = parts.Select(row => folder + "/Models/" + System.IO.Path.GetFileName(Read<string>(Field(row, "path"))))
            .Where(path => !AssetDatabase.LoadAssetAtPath<GameObject>(path)).ToArray();
        var missingTextures = textures.Select(row => folder + "/Textures/" + Read<string>(Field(row, "sha256")) + ".png")
            .Where(path => !AssetDatabase.LoadAssetAtPath<Texture2D>(path)).ToArray();
        return new { compiling = EditorApplication.isCompiling, updating = EditorApplication.isUpdating,
            models = parts.Length, textures = textures.Length, missingModels, missingTextures,
            ready = !EditorApplication.isCompiling && !EditorApplication.isUpdating && missingModels.Length == 0 && missingTextures.Length == 0 };
    }
}
