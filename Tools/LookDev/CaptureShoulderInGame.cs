using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
public static class ShoulderWorldCapture {
 const string Output="Docs/Images/ShoulderRim";
 static GameObject actor;static Camera camera;static RenderTexture target,readback;static Scene scene;
 static int frames,phase;static bool ready;static int frame,last;static double started;
 public static string Start(){
  if(!EditorApplication.isPlaying)throw new Exception("Play required");
  var body=UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>().FirstOrDefault(r=>r.name=="DollSinger_Body"&&r.shadowCastingMode!=ShadowCastingMode.ShadowsOnly);
  if(!body)throw new Exception("No game character");
  var animator=body.GetComponentInParent<Animator>();var origin=animator?animator.transform.position:body.transform.position;
  scene=SceneManager.CreateScene("Temporary native lilToon game capture");
  actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Moonkov/MenuCharacterVisual.prefab"));SceneManager.MoveGameObjectToScene(actor,scene);
  foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
  foreach(var l in actor.GetComponentsInChildren<Light>(true))l.enabled=false;
  foreach(var l in actor.GetComponentsInChildren<LineRenderer>(true))l.enabled=false;
  MoonkovLilToonLookDev.Pose(actor);actor.transform.position=origin+new Vector3(0,0,1);
  foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>()){r.forceRenderingOff=false;r.enabled=true;r.updateWhenOffscreen=true;}
  var go=new GameObject("Temporary native game portrait camera");SceneManager.MoveGameObjectToScene(go,scene);camera=go.AddComponent<Camera>();
  camera.allowHDR=true;camera.nearClipPlane=.05f;camera.farClipPlane=2000;camera.depth=100;
  camera.transform.position=actor.transform.position+new Vector3(.08f,1.33f,1.65f);camera.transform.LookAt(actor.transform.position+new Vector3(0,1.27f,0));camera.fieldOfView=28;
  var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.renderShadows=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
  target=new RenderTexture(960,1080,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();readback=new RenderTexture(960,1080,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);readback.Create();camera.targetTexture=target;
  phase=0;frames=0;last=-1;started=EditorApplication.timeSinceStartup;MoonkovShoulderRimLookDev.SetLook(actor,true);
  RenderPipelineManager.endCameraRendering+=Rendered;EditorApplication.update+=Tick;
  File.WriteAllText(Output+"/GameStatus.txt","RUNNING: "+UnityEngine.SceneManagement.SceneManager.GetActiveScene().path+"; pipeline="+QualitySettings.renderPipeline.name);
  return "Actual game light and camera comparison started";
 }
 static void Rendered(ScriptableRenderContext c,Camera cam){if(cam==camera){ready=true;frame=Time.frameCount;}}
 static void Tick(){
  if(!EditorApplication.isPlaying||EditorApplication.timeSinceStartup-started>90){Finish("FAIL interrupted/timeout");return;}
  if(!ready||last==frame)return;ready=false;last=frame;if(++frames<20)return;
  var previous=RenderTexture.active;bool oldSrgb=GL.sRGBWrite;var image=new Texture2D(960,1080,TextureFormat.RGB24,false);
  try{GL.sRGBWrite=true;Graphics.Blit(target,readback);RenderTexture.active=readback;image.ReadPixels(new Rect(0,0,960,1080),0,0);image.Apply();File.WriteAllBytes(Output+"/game-"+(phase==0?"before":"after")+".png",image.EncodeToPNG());}
  finally{GL.sRGBWrite=oldSrgb;RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
  if(phase++==0){frames=0;MoonkovShoulderRimLookDev.SetLook(actor,false);}else Finish("PASS: real map lighting, fixed-pose duplicate; same camera and final Bloom for both; no added lights; FP16 scene target and sRGB readback");
 }
 static void Finish(string result){EditorApplication.update-=Tick;RenderPipelineManager.endCameraRendering-=Rendered;File.AppendAllText(Output+"/GameStatus.txt","\n"+result);if(camera)camera.enabled=false;if(target){target.Release();UnityEngine.Object.Destroy(target);}if(readback){readback.Release();UnityEngine.Object.Destroy(readback);}if(scene.IsValid())SceneManager.UnloadSceneAsync(scene);}
}
