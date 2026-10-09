using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class RifleFeedbackSetup
{
    private const string Definitions = "Assets/Audio/Moonkov/SoundDefs/";
    private const string LibraryPath = "Assets/Resources/Moonkov/RifleFeedback.asset";

    [MenuItem("Tools/Moonkov/Weapons/Create Missing Rifle Feedback")]
    public static void CreateMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Create feedback in Edit mode.");
        var library = AssetDatabase.LoadAssetAtPath<RifleFeedbackLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<RifleFeedbackLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        const string materialPath = "Assets/Resources/Moonkov/RifleImpact.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Moonkov/Rifle Impact");
            if (shader == null) throw new InvalidOperationException("Rifle impact shader is missing.");
            material = new Material(shader) { name = "Rifle Impact" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        if (!library.ImpactMaterial) library.ImpactMaterial = material;
        if (!library.RockImpact) library.RockImpact = Define("RifleRockImpact", "HaloImpact", 1, 1, 0);
        if (!library.MetalImpact) library.MetalImpact = Define("RifleMetalImpact", "HaloSnap", 1, .8f, -200);
        if (!library.BodyImpact) library.BodyImpact = Define("RifleBodyImpact", "Hit", 1, .55f, 0);
        if (!library.HitConfirm) library.HitConfirm = Define("RifleHitConfirm", "HaloSnap", 0, .3f, 350);
        if (!library.KillConfirm) library.KillConfirm = Define("RifleKillConfirm", "HaloSnap", 0, .55f, -450);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Debug.Log("Rifle feedback library ready. Existing Inspector tuning was preserved.");
    }

    private static SoundDef Define(string name, string source, float spatial, float volume, float pitch)
    {
        string path = Definitions + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<SoundDef>(path);
        if (existing) return existing;
        var template = AssetDatabase.LoadAssetAtPath<SoundDef>(Definitions + source + ".asset");
        if (!template) throw new InvalidOperationException("Missing sound template: " + source);
        var sound = UnityEngine.Object.Instantiate(template); sound.name = name;
        sound.VolumeScale *= volume; sound.BasePitchInCents += pitch;
        sound.DistanceInfo.SpatialBlend = spatial;
        sound.DistanceInfo.VolumeDistMax = 28;
        sound.StartStopInfo.StopDelay = spatial == 0 ? .12f : .3f;
        sound.PitchAndVolumeInfo.PitchMin = sound.PitchAndVolumeInfo.PitchMax = 0;
        AssetDatabase.CreateAsset(sound, path);
        return sound;
    }
}
