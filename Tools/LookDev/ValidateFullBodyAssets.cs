using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class ValidateFullBodyAssets
{
    public static object Main()
    {
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovAnimeLightingSetup.ProfilePath);
        var grade=profile.components.Select(c=>EditorJsonUtility.ToJson(c)).ToArray();
        MoonkovFullBodyLookDev.Apply();Check(false);MoonkovFullBodyLookDev.Restore();Check(true);MoonkovFullBodyLookDev.Apply();Check(false);
        if(!grade.SequenceEqual(profile.components.Select(c=>EditorJsonUtility.ToJson(c))))throw new Exception("Global grade changed");
        var names=MoonkovFullBodyLookDev.Names;
        var changed=names.Where(n=>MoonkovFullBodyLookDev.Get(n).ComputeCRC()!=MoonkovFullBodyLookDev.Get(n,true).ComputeCRC()).ToArray();
        foreach(var n in new[]{"Brows","Lashes"})if(changed.Contains(n))throw new Exception("Line material changed");
        var errors=names.Select(n=>MoonkovFullBodyLookDev.Get(n).shader).Distinct().SelectMany(s=>ShaderUtil.GetShaderMessages(s).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message)).ToArray();
        if(errors.Length>0)throw new Exception(string.Join(";",errors));
        var maps=AssetDatabase.FindAssets("t:Texture2D",new[]{MoonkovFullBodyLookDev.Root+"/Maps"}).Select(g=>AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
        return new {pass=true,changedMaterials=changed,unchangedMaterials=names.Except(changed).ToArray(),restoreRoundtrip=true,globalGradeUnchanged=true,sourceAtlasUvShaderQueuePreserved=true,diffuseColorAndRampsPreserved=true,shaderErrors=errors.Length,mapCount=maps.Length,mapMemoryBytes=maps.Sum(t=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t))};
    }
    static void Check(bool before)
    {
        foreach(string n in MoonkovFullBodyLookDev.Names)
        foreach(var m in new[]{AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+n+".mat"),MoonkovLilToonLookDev.Get(n)})
        {
            var b=MoonkovFullBodyLookDev.Get(n,true);
            if(m.ComputeCRC()!=MoonkovFullBodyLookDev.Get(n,before).ComputeCRC())throw new Exception("CRC mismatch: "+n);
            if(m.shader!=b.shader||m.renderQueue!=b.renderQueue||m.GetTexture("_MainTex")!=b.GetTexture("_MainTex")||m.GetTextureOffset("_MainTex")!=b.GetTextureOffset("_MainTex")||m.GetTextureScale("_MainTex")!=b.GetTextureScale("_MainTex"))throw new Exception("Source identity changed: "+n);
            if(m.GetColor("_Color")!=b.GetColor("_Color")||m.GetVector("_MainTexHSVG")!=b.GetVector("_MainTexHSVG")||m.GetColor("_ShadowColor")!=b.GetColor("_ShadowColor")||m.GetFloat("_ShadowStrength")!=b.GetFloat("_ShadowStrength")||m.GetFloat("_ShadowBorder")!=b.GetFloat("_ShadowBorder")||m.GetFloat("_ShadowBlur")!=b.GetFloat("_ShadowBlur"))throw new Exception("Diffuse/ramp changed: "+n);
            if(m.GetFloat("_AsUnlit")!=0||m.GetFloat("_LightMinLimit")!=0||m.GetFloat("_UseEmission")!=0||m.GetFloat("_UseEmission2nd")!=0)throw new Exception("Unlit material: "+n);
        }
    }
}
