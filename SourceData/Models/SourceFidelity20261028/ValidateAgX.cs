using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.MP_FPS.Moon;
using Object=UnityEngine.Object;

// Run through unity command run_script --file <this path> --entry ValidateSourceAgX.Run.
// Uses a private scene and does not change the active scene, lighting or pipeline.
public static class ValidateSourceAgX
{
 public static void Run()
 {
  if(EditorApplication.isCompiling)throw new InvalidOperationException("Wait for compilation.");
  var active=SceneManager.GetActiveScene();bool dirty=active.isDirty;
  var scene=EditorSceneManager.NewPreviewScene();var previous=RenderTexture.active;
  RenderTexture target=null;Texture2D pixels=null;Material unlit=null,display=null;
  var colors=new[]{Vector3.zero,Vector3.one*.018f,Vector3.one*.18f,Vector3.one,Vector3.one*4,new Vector3(.18f,.09f,.03f),new Vector3(2,1.36f,.76f),new Vector3(.1f,.3f,1.2f)};
  var expected=new[]{new[]{0,0,0},new[]{21,21,21},new[]{118,118,118},new[]{208,208,208},new[]{244,244,244},new[]{115,73,24},new[]{230,216,201},new[]{117,159,222}};
  int maximumError=0;var report=new StringBuilder();
  try
  {
   var shader=Shader.Find("Hidden/MoonEnvironment/Source AgX");if(!shader||!shader.isSupported)throw new Exception("Source AgX shader unavailable.");
   display=new Material(shader);
   display.SetTexture("_SourceLut",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MoonEnvironment/Variants/SourceFidelity/Radiance/source_agx_medium_high.png"));
   if(!display.GetTexture("_SourceLut"))throw new Exception("Missing source OCIO LUT.");
   var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(cube,scene);
   unlit=new Material(Shader.Find("Universal Render Pipeline/Unlit"));cube.GetComponent<Renderer>().sharedMaterial=unlit;
   var go=new GameObject("AgX calibration camera");SceneManager.MoveGameObjectToScene(go,scene);
   var camera=go.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
   camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);camera.fieldOfView=48;camera.nearClipPlane=.05f;
   camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
   go.transform.position=new Vector3(0,0,-2);go.transform.LookAt(Vector3.zero);
   camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;go.AddComponent<SourceAgXCamera>().Configure(display);
   target=new RenderTexture(128,128,24,RenderTextureFormat.ARGBHalf);target.Create();
   pixels=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
   for(int k=0;k<colors.Length;k++)
   {
    // Unity's Color property is converted to linear before shader upload.
    unlit.SetColor("_BaseColor",new Color(colors[k].x,colors[k].y,colors[k].z,1).gamma);
    Color Capture(){RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();return pixels.GetPixel(64,64);}
    var first=Capture();var second=Capture();if(first!=second)throw new Exception("Display transform is not deterministic.");
    var encoded=first.gamma;var rgb=new[]{Mathf.RoundToInt(encoded.r*255),Mathf.RoundToInt(encoded.g*255),Mathf.RoundToInt(encoded.b*255)};
    for(int j=0;j<3;j++)maximumError=Math.Max(maximumError,Math.Abs(rgb[j]-expected[k][j]));
    report.AppendLine(k+": source "+string.Join(",",expected[k])+" Unity "+string.Join(",",rgb));
   }
   foreach(var message in ShaderUtil.GetShaderMessages(shader))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(message.message);
  }
  finally
  {
   RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);
   if(target){target.Release();Object.DestroyImmediate(target);}if(pixels)Object.DestroyImmediate(pixels);
   if(unlit)Object.DestroyImmediate(unlit);if(display)Object.DestroyImmediate(display);
  }
  if(SceneManager.GetActiveScene()!=active||active.isDirty!=dirty)throw new Exception("Active scene state changed.");
  report.AppendLine("Maximum channel difference (8-bit): "+maximumError);report.AppendLine("Stable repeated captures; active scene unchanged.");
  Directory.CreateDirectory("LocalData/SourceFidelity20261028");File.WriteAllText("LocalData/SourceFidelity20261028/AgXValidation.txt",report.ToString());
  if(maximumError>2)throw new Exception("Source colour calibration exceeds two 8-bit steps.\n"+report);
  Debug.Log(report.ToString());
 }
}
