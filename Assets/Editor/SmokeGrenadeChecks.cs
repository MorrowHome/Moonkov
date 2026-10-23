using System;
using System.IO;
using System.Reflection;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SmokeGrenadeChecks
{
    public static void Run()
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var r=new Vector3(5,3,5);
        Check(SmokeVisibility.OpticalDepth(new Vector3(-8,0,0),new Vector3(8,0,0),Vector3.zero,r,1)>1.6f,"Cross cloud");
        Check(SmokeVisibility.OpticalDepth(new Vector3(-8,3,0),new Vector3(8,3,0),Vector3.zero,r,1)==0,"Tangent ray");
        Check(SmokeVisibility.OpticalDepth(Vector3.zero,Vector3.zero,Vector3.zero,r,1)==0,"Zero ray");
        Check(SmokeVisibility.OpticalDepth(Vector3.zero,Vector3.right*8,Vector3.zero,r,0)==0,"Dissipated cloud");
        Check(SmokeVisibility.Growth(20)<SmokeVisibility.Growth(19) && SmokeVisibility.Growth(18)==1,"Progressive bloom");
        var shader=Resources.Load<Shader>("Moonkov/SmokeVolume");
        Check(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader),"Smoke shader support");
        Debug.Log("Smoke geometry, bloom, fade and shader checks passed.");
    }

    // Isolated render fixture: no scene changes, transport or second Editor.
    public static void Capture(string directory)
    {
        Directory.CreateDirectory(directory);
        var scene=EditorSceneManager.NewPreviewScene();
        var previous=RenderTexture.active;
        var target=new RenderTexture(960,640,24);
        var pixels=new Texture2D(960,640,TextureFormat.RGB24,false);
        TacticalItemsFeedback feedback=null;
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",new Color(.8f,.38f,.1f));
        try
        {
            var cameraRoot=new GameObject("Smoke preview camera");SceneManager.MoveGameObjectToScene(cameraRoot,scene);
            var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.15f);camera.nearClipPlane=.05f;camera.farClipPlane=100;
            camera.transform.position=new Vector3(10,5,-12);camera.transform.LookAt(new Vector3(0,1.4f,0));
            var data=camera.GetUniversalAdditionalCameraData();data.requiresDepthTexture=true;data.renderPostProcessing=false;
            for(int i=0;i<5;i++)
            {
                var marker=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(marker,scene);
                marker.transform.position=new Vector3((i-2)*2,1.4f,6);marker.transform.localScale=new Vector3(.7f,2.8f,.7f);marker.GetComponent<Renderer>().sharedMaterial=material;
            }
            var root=new GameObject("Smoke fixture feedback");SceneManager.MoveGameObjectToScene(root,scene);feedback=root.AddComponent<TacticalItemsFeedback>();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            if(typeof(TacticalItemsFeedback).GetField("m_SmokeVolume",flags).GetValue(feedback)==null)
                typeof(TacticalItemsFeedback).GetMethod("Awake",flags).Invoke(feedback,null);
            void Draw(string name)
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
                RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,960,640),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(directory,name+".png"),pixels.EncodeToPNG());
            }
            Draw("clear");
            feedback.Present(new TacticalWorldStateRpc {Epoch=1,Id=1,Sequence=1,Kind=TacticalItemKind.Smoke,Phase=TacticalWorldPhase.Smoke,Radius=5,Remaining=16});
            Draw("outside");
            var foreground=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(foreground,scene);
            foreground.transform.position=camera.transform.position+camera.transform.forward*7;foreground.transform.localScale=new Vector3(1,2,1);foreground.GetComponent<Renderer>().sharedMaterial=material;
            Draw("foreground");Object.DestroyImmediate(foreground);
            camera.transform.position=new Vector3(0,1.4f,0);camera.transform.LookAt(new Vector3(0,1.4f,6));Draw("inside");
            camera.transform.position=new Vector3(10,5,-12);camera.transform.LookAt(new Vector3(0,1.4f,0));
            feedback.Present(new TacticalWorldStateRpc {Epoch=1,Id=1,Sequence=2,Kind=TacticalItemKind.Smoke,Phase=TacticalWorldPhase.Smoke,Radius=5,Remaining=.15f});
            typeof(TacticalItemsFeedback).GetMethod("Update",flags).Invoke(feedback,null);Draw("fading");
            feedback.Present(new TacticalWorldStateRpc {Epoch=1,Id=1,Sequence=3,Phase=TacticalWorldPhase.Removed});Draw("removed");
        }
        finally
        {
            RenderTexture.active=previous;
            if(feedback)typeof(TacticalItemsFeedback).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(feedback,null);
            EditorSceneManager.ClosePreviewScene(scene);target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(material);
        }
    }
}
