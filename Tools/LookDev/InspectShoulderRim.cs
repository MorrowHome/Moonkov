using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class ResearchShoulderRim {
 public static object Main(){
  var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/MoonEnvironment/Settings/MoonAnimePostProcessing.asset");
  profile.TryGet<Bloom>(out var b);
  var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MoonEnvironment/Settings/MoonSoftAnimePipeline.asset");
  float t=Mathf.GammaToLinearSpace(b.threshold.value);
  return new {playing=EditorApplication.isPlaying,unity=Application.unityVersion,authoredCharacterPipelineHDR=pipeline.supportsHDR,bloomActive=b.active,threshold=b.threshold.value,linearThreshold=t,linearSoftKneeStarts=t*.5f,intensity=b.intensity.value,scatter=b.scatter.value,
  materials=new[]{"Body","Face","Dress","Hair"}.Select(n=>{var m=AssetDatabase.LoadAssetAtPath<Material>("Assets/DollSinger/Art/Materials/"+n+".mat");return new {name=n,rimEnabled=m.GetFloat("_UseRim"),rimAlpha=m.GetColor("_RimColor").a,rimFresnelPower=m.GetFloat("_RimFresnelPower"),rimBorder=m.GetFloat("_RimBorder"),rimBlur=m.GetFloat("_RimBlur"),rimMainStrength=m.GetFloat("_RimMainStrength"),rimLighting=m.GetFloat("_RimEnableLighting"),rimShadowMask=m.GetFloat("_RimShadowMask"),lightMax=m.GetFloat("_LightMaxLimit")};}).ToArray(),
  scope="Read-only authored asset inspection. Not a runtime Volume stack or rendered shoulder test."};
 }
}
