using System.IO;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

public static class EnergyGrenadeSetup
{
    public static readonly string[] TextKeys = {
        "E-01 Energy grenade", "3s FUSE / 6m BLAST / UP TO 480 DAMAGE",
        "[G] throws from pockets or an equipped rig. Bounces, then releases an energy blast after 3 seconds. Cover blocks the blast; nearby damage can kill the thrower. Move grenades out of backpacks before throwing.",
        "[G] ENERGY GRENADE / {0}", "THROW REQUESTED...", "THROW REJECTED / CHECK RIG AND CLEARANCE"
    };

    [MenuItem("Moonkov/Energy Grenade/Build Assets")]
    public static void Build()
    {
        const string folder = "Assets/Data/EnergyGrenade";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Data", "EnergyGrenade");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/EnergyGrenade.shader");
        if (!shader) throw new System.InvalidOperationException("Missing energy grenade shader.");
        Material Material(string name, int mode)
        {
            string path = folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.SetFloat("_Mode", mode);
            material.SetColor("_Color", mode == 0 ? new Color(.12f, 1.1f, 2f, 1) : Color.white);
            EditorUtility.SetDirty(material); return material;
        }
        const string stylePath = "Assets/Resources/Moonkov/EnergyGrenade.asset";
        var style = AssetDatabase.LoadAssetAtPath<EnergyGrenadeStyle>(stylePath);
        if (!style) { style = ScriptableObject.CreateInstance<EnergyGrenadeStyle>(); AssetDatabase.CreateAsset(style, stylePath); }
        style.Orb = Material("EnergyOrb", 0); style.Ribbon = Material("EnergyRibbon", 1); style.Spark = Material("EnergySpark", 2);
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        style.Sphere = sphere.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(sphere);
        style.ThrowSound = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Sounddefs/Weapons/Grenade Launcher/Shoot/SoundDef_Shoot_GrenadeLauncher.asset");
        style.ExplosionSound = AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Sounddefs/Weapons/Grenade Launcher/Explode/SoundDef_Explode_Grenade.asset");
        EditorUtility.SetDirty(style);
        var translations = JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var strings = LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach (var reference in strings.Tables)
        {
            var table = (StringTable)reference.asset;
            foreach (string key in TextKeys)
                table.AddEntry(key, table.LocaleIdentifier.Code == "zh-Hans" ? translations[key].Value<string>() : key);
            EditorUtility.SetDirty(table);
        }
        EditorUtility.SetDirty(strings.SharedData);
        AssetDatabase.SaveAssets();
        Debug.Log("Energy grenade assets, pooled audio references and six localized strings saved.");
    }
}
