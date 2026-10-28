using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.Moon;
using Unity.MP_FPS.Moon.Editor;
using Object = UnityEngine.Object;

public static class ValidateSourceWorldIntegration
{
 public static void Run()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)throw new InvalidOperationException("Wait for Edit Mode and compilation.");
  SourceWorldCubeBuilder.ValidateConversion();
  var scene=SceneManager.GetActiveScene();bool dirty=scene.isDirty;
  var beforeMaps=LightmapSettings.lightmaps;var beforeMode=LightmapSettings.lightmapsMode;var beforeProbes=LightmapSettings.lightProbes;
  var beforeStatistics=Shader.GetGlobalTexture("_SourceAOAtlases");float beforeEnabled=Shader.GetGlobalFloat("_SourceAOEnabled");
  var preview=EditorSceneManager.NewPreviewScene();var previousTarget=RenderTexture.active;
  SourceWorldLighting owner=null;SourceWorldLightingData data=null;Texture2DArray statistics=null;RenderTexture target=null;Mesh mesh=null;
  try
  {
   mesh=new Mesh();mesh.SetVertexBufferParams(3,
    new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.Float32,3),
    new VertexAttributeDescriptor(VertexAttribute.Tangent,VertexAttributeFormat.Float32,4),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,4),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,4),new VertexAttributeDescriptor(VertexAttribute.TexCoord2,VertexAttributeFormat.Float32,4),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord3,VertexAttributeFormat.Float32,4),new VertexAttributeDescriptor(VertexAttribute.TexCoord4,VertexAttributeFormat.Float32,4),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord5,VertexAttributeFormat.Float32,4));
   var vertices=new float[102];for(int i=0;i<3;i++){vertices[i*34]=i+1;vertices[i*34+4]=1;vertices[i*34+10]=.25f;vertices[i*34+14]=.75f;}
   mesh.SetVertexBufferData(vertices,0,0,vertices.Length);
   var uv=new List<Vector4>();mesh.GetUVs(1,uv);
   if(mesh.GetVertexBufferStride(0)!=136 || mesh.vertices[2].x!=3 || uv[0].x!=.75f)throw new Exception("Source vertex payload/stride mismatch.");
   data=ScriptableObject.CreateInstance<SourceWorldLightingData>();data.irradiance=new[]{Texture2D.whiteTexture};
   data.displayTransform=AssetDatabase.LoadAssetAtPath<Material>("Assets/MoonEnvironment/Variants/SourceFidelity/Materials/SourceAgX.mat");
   statistics=new Texture2DArray(1,1,1,TextureFormat.RGBA32,false,true);statistics.SetPixels(new[]{new Color(1,0,0,1)},0);statistics.Apply();data.surfaceStatistics=statistics;
   var root=new GameObject("Source owner restoration check");SceneManager.MoveGameObjectToScene(root,preview);owner=root.AddComponent<SourceWorldLighting>();owner.Configure(data,Array.Empty<SourceWorldLighting.RendererBinding>());
   var cameraObject=new GameObject("Source first-frame camera");SceneManager.MoveGameObjectToScene(cameraObject,preview);var camera=cameraObject.AddComponent<Camera>();camera.scene=preview;
   camera.allowHDR=false;camera.clearFlags=CameraClearFlags.Skybox;camera.backgroundColor=Color.red;camera.cullingMask=1;
   var additional=camera.GetUniversalAdditionalCameraData();additional.renderPostProcessing=true;
   additional.requiresColorOption=CameraOverrideOption.UsePipelineSettings;additional.requiresDepthOption=CameraOverrideOption.UsePipelineSettings;
   target=new RenderTexture(16,16,24,RenderTextureFormat.ARGBHalf);target.Create();camera.targetTexture=target;
   owner.Apply();camera.Render();
   if(!owner.IsApplied || additional.renderPostProcessing || !camera.GetComponent<SourceAgXCamera>() || !camera.allowHDR || additional.requiresColorOption!=CameraOverrideOption.On)throw new Exception("Source world camera was not configured on its first frame.");
   // Existing gameplay code writes post-processing again; the source owner wins each frame.
   additional.renderPostProcessing=true;camera.Render();
   if(additional.renderPostProcessing)throw new Exception("A gameplay controller re-enabled the old display transform.");
   int handLayer=LayerMask.NameToLayer("FirstPersonOverlay");if(handLayer<0)throw new Exception("First person overlay layer missing.");
   var handsObject=new GameObject("Source hands overlay check");SceneManager.MoveGameObjectToScene(handsObject,preview);var hands=handsObject.AddComponent<Camera>();hands.scene=preview;hands.cullingMask=1<<handLayer;
   var handsData=hands.GetUniversalAdditionalCameraData();handsData.renderType=CameraRenderType.Overlay;handsData.renderPostProcessing=false;
   additional.cameraStack.Add(hands);camera.Render();
   if(camera.GetComponent<SourceAgXCamera>().DisplayTransform || !hands.GetComponent<SourceAgXCamera>() || hands.GetComponent<SourceAgXCamera>().DisplayTransform!=data.displayTransform || !hands.GetComponent<SourceAgXCamera>().AllowsOverlay)throw new Exception("Source display was not moved after the 3D hands overlay.");
   additional.cameraStack.Remove(hands);camera.Render();
   if(camera.GetComponent<SourceAgXCamera>().DisplayTransform!=data.displayTransform)throw new Exception("Source display did not return to the base camera.");
   owner.Restore();
   if(owner.IsApplied || !additional.renderPostProcessing || camera.allowHDR || camera.clearFlags!=CameraClearFlags.Skybox || camera.backgroundColor!=Color.red || additional.requiresColorOption!=CameraOverrideOption.UsePipelineSettings || additional.requiresDepthOption!=CameraOverrideOption.UsePipelineSettings || camera.GetComponent<SourceAgXCamera>())throw new Exception("Source camera state was not restored.");
   if(LightmapSettings.lightmapsMode!=beforeMode || LightmapSettings.lightProbes!=beforeProbes || Shader.GetGlobalTexture("_SourceAOAtlases")!=beforeStatistics || Shader.GetGlobalFloat("_SourceAOEnabled")!=beforeEnabled || LightmapSettings.lightmaps.Length!=beforeMaps.Length)throw new Exception("Source global lighting state was not restored.");
   if(SceneManager.GetActiveScene()!=scene || scene.isDirty!=dirty)throw new Exception("Source validation changed the active scene.");
   Directory.CreateDirectory("LocalData/SourceFidelity20261028");File.WriteAllText("LocalData/SourceFidelity20261028/WorldIntegrationValidation.txt","PASS: native 136-byte vertex payload; cubemap axes; unit-radiance SH; first-frame AgX camera; persistent post override; final 3D overlay display; camera/color/depth/global lighting restoration; active scene unchanged.");
  }
  finally
  {
   if(owner)owner.Restore();RenderTexture.active=previousTarget;EditorSceneManager.ClosePreviewScene(preview);
   if(target){target.Release();Object.DestroyImmediate(target);}if(mesh)Object.DestroyImmediate(mesh);if(statistics)Object.DestroyImmediate(statistics);if(data)Object.DestroyImmediate(data);
  }
 }
}
