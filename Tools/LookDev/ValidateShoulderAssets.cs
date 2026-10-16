using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class ApplyShoulderAudit {
 public static object Main(){
 var others=MoonkovLilToonLookDev.Names.Where(n=>!MoonkovShoulderRimLookDev.Names.Contains(n)).ToDictionary(n=>n,n=>AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+n+".mat").ComputeCRC());
 MoonkovShoulderRimLookDev.Apply();Check(false);MoonkovShoulderRimLookDev.Restore();Check(true);MoonkovShoulderRimLookDev.Apply();Check(false);
 foreach(var pair in others)if(AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+pair.Key+".mat").ComputeCRC()!=pair.Value)throw new Exception("Unrelated material changed: "+pair.Key);
 var maps=AssetDatabase.FindAssets("t:Texture2D",new[]{MoonkovShoulderRimLookDev.Root+"/Maps"}).Select(g=>AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
 var shaders=MoonkovShoulderRimLookDev.Names.Select(n=>MoonkovShoulderRimLookDev.Get(n).shader).Concat(new[]{AssetDatabase.LoadAssetAtPath<Shader>("Assets/DollSinger/Shaders/MoonToonPass.lilcontainer")}).Distinct();var errors=shaders.SelectMany(s=>ShaderUtil.GetShaderMessages(s).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message)).ToArray();if(errors.Length>0)throw new Exception(string.Join(";",errors));
 return new {pass=true,changedMaterials=MoonkovShoulderRimLookDev.Names,otherMaterialsUnchanged=others.Count,restoreRoundtrip=true,sourceAtlasUvShaderQueuePreserved=true,mapMemoryBytes=maps.Sum(t=>UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t)),shaderErrors=errors.Length};
 }
 static void Check(bool before){foreach(var n in MoonkovShoulderRimLookDev.Names){var m=AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Source+n+".mat");var reference=MoonkovShoulderRimLookDev.Get(n,before);var b=MoonkovShoulderRimLookDev.Get(n,true);if(m.ComputeCRC()!=reference.ComputeCRC())throw new Exception("CRC mismatch: "+n);if(m.shader!=b.shader||m.GetTexture("_MainTex")!=b.GetTexture("_MainTex")||m.GetTextureScale("_MainTex")!=b.GetTextureScale("_MainTex")||m.GetTextureOffset("_MainTex")!=b.GetTextureOffset("_MainTex")||m.renderQueue!=b.renderQueue)throw new Exception("Source identity changed: "+n);}}
}
