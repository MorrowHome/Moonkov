using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Run in Edit mode after the candidate has been applied. Baseline is 857b6833,
// with independent copies of the two changed mask textures.
public static class ValidateHairSkirtAssets
{
 public static object Main(){
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var changes=new Dictionary<string,string[]>();
  var expected=new[]{"Hair","Dress","InnerDress","DressDetail"};
  foreach(var n in MoonkovFullBodyLookDev.Names){
   var before=AssetDatabase.LoadAssetAtPath<Material>(MoonkovFullBodyLookDev.Root+"/HairSkirtBefore/"+n+".mat");
   var after=MoonkovFullBodyLookDev.Get(n);var differences=new List<string>();
   if(before.shader!=after.shader||before.renderQueue!=after.renderQueue||!before.shaderKeywords.OrderBy(x=>x).SequenceEqual(after.shaderKeywords.OrderBy(x=>x)))throw new Exception("Shader variant changed: "+n);
   for(int i=0;i<after.shader.GetPropertyCount();i++){
    string p=after.shader.GetPropertyName(i);bool same;
    switch(after.shader.GetPropertyType(i)){
     case ShaderPropertyType.Color:same=before.GetColor(p)==after.GetColor(p);break;
     case ShaderPropertyType.Vector:same=before.GetVector(p)==after.GetVector(p);break;
     case ShaderPropertyType.Texture:same=before.GetTexture(p)==after.GetTexture(p)&&before.GetTextureScale(p)==after.GetTextureScale(p)&&before.GetTextureOffset(p)==after.GetTextureOffset(p);break;
     default:same=before.GetFloat(p)==after.GetFloat(p);break;
    }
    if(!same)differences.Add(p);
   }
   var allowed=new[]{"_RimFresnelPower","_RimBorder","_RimBlur","_RimDirRange","_RimColor","_RimMainStrength","_RimColorTex"};
   if(differences.Except(allowed).Any()||(!expected.Contains(n)&&differences.Any())||(expected.Contains(n)&&!differences.Any()))throw new Exception("Unexpected material delta: "+n+" "+string.Join(",",differences));
   if(differences.Any())changes[n]=differences.ToArray();
   foreach(var formal in new[]{AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+n+".mat"),MoonkovLilToonLookDev.Get(n)})if(formal.ComputeCRC()!=after.ComputeCRC())throw new Exception("Formal copy differs: "+n);
   if(after.GetFloat("_AsUnlit")!=0||after.GetFloat("_LightMinLimit")!=0||after.GetFloat("_UseEmission")!=0||after.GetFloat("_UseEmission2nd")!=0)throw new Exception("Self lighting: "+n);
  }
  var errors=MoonkovFullBodyLookDev.Names.Select(n=>MoonkovFullBodyLookDev.Get(n).shader).Distinct().SelectMany(s=>ShaderUtil.GetShaderMessages(s).Where(m=>m.severity.ToString()=="Error")).ToArray();
  if(errors.Length>0)throw new Exception("Shader errors: "+errors.Length);
  return new{pass=true,baseline="857b6833",changes,unchangedMaterials=MoonkovFullBodyLookDev.Names.Except(expected).ToArray(),shaderErrors=errors.Length,formalCopiesMatch=true,noEmissionOrUnlitFloor=true};
 }
}
