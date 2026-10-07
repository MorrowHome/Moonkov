using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

public static class MoonkovLocalizationBuilder
{
    [MenuItem("Tools/Moonkov/Build Chinese Localization")]
    public static void Build()
    {
        const string folder = "Assets/Localization";
        Directory.CreateDirectory(folder + "/Tables");
        AssetDatabase.Refresh();
        var settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(folder + "/LocalizationSettings.asset");
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, folder + "/LocalizationSettings.asset");
        }
        LocalizationEditorSettings.ActiveLocalizationSettings = settings;
        LocalizationSettings.Instance = settings;
        foreach (var code in new[] { "en", "zh-Hans" })
        {
            if (LocalizationEditorSettings.GetLocales().Any(l => l.Identifier.Code == code)) continue;
            var locale = Locale.CreateLocale(code);
            locale.LocaleName = code == "en" ? "English" : "简体中文";
            AssetDatabase.CreateAsset(locale, folder + "/" + code + ".asset");
            LocalizationEditorSettings.AddLocale(locale);
        }
        LocalizationSettings.StartupLocaleSelectors.Clear();
        LocalizationSettings.StartupLocaleSelectors.Add(new PlayerPrefLocaleSelector { PlayerPreferenceKey = "Moonkov.Locale" });
        LocalizationSettings.StartupLocaleSelectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier("zh-Hans") });
        LocalizationSettings.InitializeSynchronously = true;
        EditorUtility.SetDirty(settings);

        var translations = JObject.Parse(File.ReadAllText(folder + "/Translations.json"));
        var strings = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI") ??
            LocalizationEditorSettings.CreateStringTableCollection("MoonkovUI", folder + "/Tables");
        foreach (var reference in strings.Tables)
        {
            var table = (StringTable)reference.asset;
            foreach (var pair in translations.Properties()) table.AddEntry(pair.Name, table.LocaleIdentifier.Code == "zh-Hans" ? pair.Value.Value<string>() : pair.Name);
            EditorUtility.SetDirty(table);
        }
        strings.SetPreloadTableFlag(true);
        EditorUtility.SetDirty(strings); EditorUtility.SetDirty(strings.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, strings);

        string glyphs = string.Join("", translations.Properties().Select(p => p.Value.Value<string>())) +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;/\\()[]{}+-×%_#@!?";
        var chinese = Font("NotoSansCJKsc", folder + "/Fonts/NotoSansCJKsc-Regular.otf", glyphs);
        var english = Font("Inter", "Assets/UI Toolkit/Fonts/Inter-Regular.ttf", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;/()[]{}+-×%_#@!?");
        var fonts = LocalizationEditorSettings.GetAssetTableCollection("MoonkovFonts") ??
            LocalizationEditorSettings.CreateAssetTableCollection("MoonkovFonts", folder + "/Tables");
        fonts.AddAssetToTable(new LocaleIdentifier("zh-Hans"), "UI", chinese);
        fonts.AddAssetToTable(new LocaleIdentifier("en"), "UI", english);
        fonts.SetPreloadTableFlag(true);
        foreach (var reference in fonts.Tables) EditorUtility.SetDirty(reference.asset);
        EditorUtility.SetDirty(fonts); EditorUtility.SetDirty(fonts.SharedData);
        LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, fonts);
        AssetDatabase.SaveAssets();
        Debug.Log("Moonkov localization: " + translations.Count + " bilingual strings; Chinese/English fonts registered.");
    }

    private static FontAsset Font(string name, string source, string glyphs)
    {
        string path = "Assets/Localization/Fonts/" + name + ".asset";
        var font = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
        if (font != null) return font;
        var sourceFont = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(source);
        if (sourceFont == null) throw new System.InvalidOperationException("Font import failed: " + source);
        font = FontAsset.CreateFontAsset(sourceFont, 40, 5, GlyphRenderMode.SDF16, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        font.name = name;
        font.isMultiAtlasTexturesEnabled = true;
        AssetDatabase.CreateAsset(font, path);
        if (!font.TryAddCharacters(new string(glyphs.Distinct().ToArray()), out var missing))
            throw new System.InvalidOperationException("Font is missing translated glyphs: " + missing);
        if (font.material != null) AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var atlas in font.atlasTextures) if (atlas != null && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
        EditorUtility.SetDirty(font);
        return font;
    }
}
