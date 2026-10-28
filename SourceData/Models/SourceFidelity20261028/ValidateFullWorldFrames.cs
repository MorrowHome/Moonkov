using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.Moon;
using Unity.MP_FPS.Moon.Editor;
using Object=UnityEngine.Object;

// Whole-map diagnostic. Formal scene installation has its separate completeness gate.
public static class ValidateFullSourceWorldFrames
{
 private static bool meanOnly;
 private static string variant="";
 private static bool noSpec;
 public static void RunNoSpecMean(){meanOnly=true;noSpec=true;variant="nospec_";Run();}
 public static void RunPilotMean(){meanOnly=true;variant="pilot_";Run();}
 public static void RunMean(){meanOnly=true;Run();}
 public static void Run()
 {
  const string work="LocalData/SourceFidelity20261028/World/";
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Use Edit Mode.");
  File.WriteAllText(work+"FullFrame.state","BEGIN");
  var active=SceneManager.GetActiveScene();bool dirty=active.isDirty;
  GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();
  var scene=EditorSceneManager.NewPreviewScene();var oldTarget=RenderTexture.active;
  var previousPipeline=QualitySettings.renderPipeline;var previousSky=RenderSettings.skybox;
  var previousReflection=RenderSettings.customReflectionTexture;var previousReflectionMode=RenderSettings.defaultReflectionMode;float previousReflectionIntensity=RenderSettings.reflectionIntensity;bool previousFog=RenderSettings.fog;
  SourceWorldLighting owner=null;SourceWorldLightingData data=null;UniversalRenderPipelineAsset pipeline=null;RenderTexture target=null;Texture2D pixels=null;
  try
  {
   var source=AssetDatabase.LoadAssetAtPath<SourceWorldLightingData>(SourceWorldAssetImport.DataPath);
   if(!source||!source.surfaceStatistics||source.irradiance.Length!=27||source.irradiance.Any(t=>!t))throw new Exception("Whole-map static lighting is not ready.");
   data=Object.Instantiate(source); if(meanOnly)data.diffuseSamples=0;
   var field=AssetDatabase.LoadAssetAtPath<SourceWorldReflectionData>(SourceWorldAssetImport.ReflectionPath);
   if(!field||!field.cubemaps[0])throw new Exception("Missing source world fallback radiance.");
   data.fallbackReflection=field.cubemaps[0];
   if(noSpec){var black=new Cubemap(1,TextureFormat.RGBAHalf,false);for(int f=0;f<6;f++)black.SetPixels(new[]{Color.black},(CubemapFace)f);black.Apply();data.fallbackReflection=black;}
   pipeline=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/MoonEnvironment/Variants/MC00BakedBuilding/MC00BakedPipeline.asset"));
   var configure=typeof(SourceWorldSceneInstall).GetMethod("ConfigurePipeline",BindingFlags.NonPublic|BindingFlags.Static);configure.Invoke(null,new object[]{pipeline});
   QualitySettings.renderPipeline=pipeline;RenderSettings.skybox=null;RenderSettings.fog=false;RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=data.fallbackReflection;RenderSettings.reflectionIntensity=1;
   var rendererPath=SourceWorldAssetImport.Root+"Lighting/SourceWorldForwardRenderer.asset";AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath));
   var materials=SourceWorldAssetImport.Manifest.materials.ToDictionary(m=>m.id,m=>AssetDatabase.LoadAssetAtPath<Material>(m.asset));
   var meshes=new Dictionary<string,Mesh>();
   for(int atlas=0;atlas<27;atlas++)foreach(var mesh in AssetDatabase.LoadAssetAtPath<SourceWorldMeshBundle>(SourceWorldAssetImport.Root+"Meshes/Atlas_"+atlas.ToString("000")+".asset").meshes)meshes.Add(mesh.name,mesh);
   var root=new GameObject("Complete source world diagnostic");SceneManager.MoveGameObjectToScene(root,scene);var bindings=new List<SourceWorldLighting.RendererBinding>();
   foreach(var chunk in SourceWorldAssetImport.Geometry.chunks)
   {
    var go=new GameObject("Source_"+chunk.id.ToString("00000"));go.transform.SetParent(root.transform,false);go.transform.position=chunk.position;go.AddComponent<MeshFilter>().sharedMesh=meshes["SourceMesh_"+chunk.id.ToString("00000")];
    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[chunk.material];renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;
    bindings.Add(new SourceWorldLighting.RendererBinding{renderer=renderer,atlas=chunk.atlas});
   }
   var plan=JsonUtility.FromJson<SourceReflectionPlan>(File.ReadAllText(work+"WorldReflectionPlan.json"));int validProbes=0;
   for(int i=1;i<field.probes.Length&&!noSpec;i++)
   {
    var p=field.probes[i];if(p==null||!field.cubemaps[i]||(p.position-plan.probes[i].position).sqrMagnitude>.000001f)continue;
    var go=new GameObject("Source reflection "+i);go.transform.SetParent(root.transform,false);go.transform.position=p.position;var probe=go.AddComponent<ReflectionProbe>();probe.mode=ReflectionProbeMode.Custom;probe.customBakedTexture=field.cubemaps[i];probe.center=(p.boxMin+p.boxMax)*.5f-p.position;probe.size=Vector3.Max(p.boxMax-p.boxMin,Vector3.one*.1f);probe.boxProjection=true;probe.blendDistance=.4f;probe.importance=p.importance;validProbes++;
   }
   owner=root.AddComponent<SourceWorldLighting>();owner.Configure(data,bindings.ToArray());owner.Apply();
   var cameraObject=new GameObject("Complete source world camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.fieldOfView=48;camera.nearClipPlane=.05f;camera.farClipPlane=5000;camera.allowHDR=true;camera.allowMSAA=true;
   target=new RenderTexture(960,600,24,RenderTextureFormat.ARGBHalf){antiAliasing=4};target.Create();pixels=new Texture2D(960,600,TextureFormat.RGBAFloat,false,true);
   var positions=new[]{new Vector3(1998.4f,253.3f,2462),new Vector3(1998.4f,253.3f,2477),new Vector3(1997,253.3f,2466),new Vector3(1800,560,1680),plan.probes[1].position};
   var aims=new[]{new Vector3(1998.4f,254.3f,2478),new Vector3(1998.4f,254.3f,2462),new Vector3(2000.5f,254.3f,2472),new Vector3(2048,180,2100),plan.probes[1].position+Vector3.forward*10};
   var report=new System.Text.StringBuilder();report.AppendLine("Complete static map: "+bindings.Count+" chunks; "+materials.Count+" materials; 27 atlases. Correct-position reflection captures available: "+validProbes+". This is not formal runtime acceptance.");
   for(int i=0;i<positions.Length;i++)
   {
    cameraObject.transform.position=positions[i];cameraObject.transform.LookAt(aims[i]);var began=System.Diagnostics.Stopwatch.StartNew();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});began.Stop();
    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,600),0,0);pixels.Apply();var colors=pixels.GetPixels();double mean=0;int nonfinite=0;
    for(int k=0;k<colors.Length;k++){if(float.IsNaN(colors[k].r)||float.IsInfinity(colors[k].r))nonfinite++;mean+=.2126*colors[k].r+.7152*colors[k].g+.0722*colors[k].b;colors[k]=colors[k].gamma;}
    pixels.SetPixels(colors);pixels.Apply();File.WriteAllBytes(work+"full_static_unity_"+variant+(meanOnly?"mean_":"")+i+".png",pixels.EncodeToPNG());report.AppendLine(i+": mean linear display luminance="+mean/colors.Length+" nonfinite="+nonfinite+" synchronous render ms="+began.Elapsed.TotalMilliseconds);
    if(nonfinite!=0)throw new Exception("Non-finite source frame.");
   }
   File.WriteAllText(work+variant+(meanOnly?"FullStaticMeanFrames.txt":"FullStaticFrames.txt"),report.ToString());
  }
  finally
  {
   if(owner)owner.Restore();RenderTexture.active=oldTarget;QualitySettings.renderPipeline=previousPipeline;RenderSettings.skybox=previousSky;RenderSettings.customReflectionTexture=previousReflection;RenderSettings.defaultReflectionMode=previousReflectionMode;RenderSettings.reflectionIntensity=previousReflectionIntensity;RenderSettings.fog=previousFog;
   EditorSceneManager.ClosePreviewScene(scene);if(target){target.Release();Object.DestroyImmediate(target);}if(pixels)Object.DestroyImmediate(pixels);if(data){if(noSpec&&data.fallbackReflection)Object.DestroyImmediate(data.fallbackReflection);Object.DestroyImmediate(data);}if(pipeline)Object.DestroyImmediate(pipeline);
   File.WriteAllText(work+"FullFrame.state","END");
  }
  if(SceneManager.GetActiveScene()!=active||active.isDirty!=dirty)throw new Exception("The active scene changed during source frame validation.");
 }
}
