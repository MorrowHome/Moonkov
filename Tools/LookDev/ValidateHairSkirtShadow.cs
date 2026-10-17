using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

public static class HairSkirtLiveShadowProbe
{
    const string Folder="Docs/Images/HairSkirtRim";
    static Scene stage;
    static Camera camera;
    static RenderTexture target,readback;
    static GameObject actor, roof, sunObject, lampObject;
    static Light sun, lamp;
    static Material markerMaterial;
    static List<Material> clones=new List<Material>();
    static List<GameObject> previousSuns=new List<GameObject>();
    static RenderPipelineAsset pipeline;
    static Light previousSun;
    static Color ambient;
    static SphericalHarmonicsL2 probe;
    static int phase, frames;
    static bool ready;static int readyFrame,lastFrame=-1;
    static double started;
    static float[] brightness=new float[4],peaks=new float[4],means=new float[4];
    static string[] ambientSamples=new string[4];
    static readonly string[] labels={"sun","shade","shade-lamp","night"};
    static int cx,cy;
    public static string Start()
    {
        if(!EditorApplication.isPlaying || !GameManager.Instance || GameSettings.Instance.GameState==GlobalGameState.MainMenu)
            throw new Exception("Formal offline raid must be running.");
        Directory.CreateDirectory(Folder);
        pipeline=QualitySettings.renderPipeline;previousSun=RenderSettings.sun;ambient=RenderSettings.ambientLight;probe=RenderSettings.ambientProbe;RenderSettings.ambientLight=Color.black;RenderSettings.ambientProbe=new SphericalHarmonicsL2();
        stage=SceneManager.CreateScene("Full body tactical shadow probe");
        var origin=new Vector3(10000,1000,10000);
        foreach(var light in UnityEngine.Object.FindObjectsByType<Light>())
            if(light.type==LightType.Directional && light.gameObject.activeSelf) { previousSuns.Add(light.gameObject);light.gameObject.SetActive(false); }
        // Keep the actual game runtime pipeline.
        actor=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Moonkov/MenuCharacterVisual.prefab"));
        SceneManager.MoveGameObjectToScene(actor,stage);actor.transform.position=origin;
        foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
        foreach(var l in actor.GetComponentsInChildren<Light>(true))l.enabled=false;
        foreach(var l in actor.GetComponentsInChildren<LineRenderer>(true))l.enabled=false;
        foreach(var t in actor.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
        var map=new Dictionary<Material,Material>();
        foreach(var renderer in actor.GetComponentsInChildren<Renderer>(true))
        {
            var slots=renderer.sharedMaterials;
            for(int i=0;i<slots.Length;i++)
            {
                var m=slots[i];if(!m || !m.shader.name.StartsWith("Moonkov/lilToonMoon"))continue;
                if(!map.TryGetValue(m,out var clone)) {clone=new Material(m){name=m.name};map.Add(m,clone);clones.Add(clone);}
                slots[i]=clone;
            }
            renderer.sharedMaterials=slots;
        }
        var animator=actor.GetComponentInChildren<Animator>();
        MoonkovLilToonLookDev.Pose(actor);
        actor.transform.SetPositionAndRotation(origin,Quaternion.identity);
        
        markerMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));markerMaterial.SetColor("_BaseColor",Color.white);
        roof=GameObject.CreatePrimitive(PrimitiveType.Cube);roof.name="Temporary tactical roof";SceneManager.MoveGameObjectToScene(roof,stage);
        roof.layer=31;roof.transform.position=origin+new Vector3(0,8,0);roof.transform.localScale=new Vector3(64,.4f,64);
        roof.GetComponent<Renderer>().sharedMaterial=markerMaterial;
        var witness=GameObject.CreatePrimitive(PrimitiveType.Sphere);SceneManager.MoveGameObjectToScene(witness,stage);witness.layer=31;
        witness.transform.position=origin+new Vector3(-.3f,1.4f,.2f);witness.transform.localScale=Vector3.one*.15f;witness.GetComponent<Renderer>().sharedMaterial=markerMaterial;
        sunObject=NewObject("Temporary tactical sun");sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.6f;sun.shadows=LightShadows.Soft;sun.shadowNearPlane=16;sun.cullingMask=1<<31;
        sun.transform.rotation=Quaternion.LookRotation(-new Vector3(.3f,.7f,1).normalized);RenderSettings.sun=sun;
        lampObject=NewObject("Temporary tactical room lamp");lamp=lampObject.AddComponent<Light>();lamp.type=LightType.Point;lamp.intensity=2;lamp.range=5;lamp.shadows=LightShadows.Soft;lamp.cullingMask=1<<31;lamp.transform.position=origin+new Vector3(0,1.5f,1.4f);
        var volume=NewObject("Temporary tactical volume").AddComponent<Volume>();volume.gameObject.layer=31;volume.isGlobal=true;volume.priority=999;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovAnimeLightingSetup.ProfilePath);
        camera=NewObject("Temporary tactical observer").AddComponent<Camera>();camera.cullingMask=1<<31;camera.depth=99;camera.allowHDR=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.transform.position=origin+new Vector3(.12f,.85f,3.2f);camera.transform.LookAt(origin+new Vector3(0,.76f,0));camera.fieldOfView=34;camera.nearClipPlane=.05f;camera.farClipPlane=2000;
        var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.renderShadows=true;data.volumeLayerMask=1<<31;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        target=new RenderTexture(960,1080,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();readback=new RenderTexture(960,1080,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);readback.Create();camera.targetTexture=target;
        Vector3 point=camera.WorldToViewportPoint(animator.GetBoneTransform(HumanBodyBones.Head).position);
        cx=Mathf.Clamp(Mathf.RoundToInt(point.x*960),60,899);cy=Mathf.Clamp(Mathf.RoundToInt(point.y*1080),60,1019);
        phase=0;frames=0;started=EditorApplication.timeSinceStartup;SetPhase();
        RenderPipelineManager.endCameraRendering+=Rendered;EditorApplication.update+=Tick;
        File.WriteAllText(Folder+"/LiveShadowResult.txt","RUNNING");
        return "Started actual Play-mode solar/roof/lamp/night visibility test.";
    }
    static GameObject NewObject(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,stage);return go;}
    static void SetPhase(){roof.GetComponent<Renderer>().enabled=phase!=0;roof.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;lamp.enabled=phase==2;sun.enabled=phase!=3;frames=0;}
    static void Tick(){
        if(!EditorApplication.isPlaying || EditorApplication.timeSinceStartup-started>90){Finish("FAIL: interrupted or timed out.");return;}
        if(!ready||readyFrame==lastFrame)return;ready=false;lastFrame=readyFrame;Capture();
    }
    static void Rendered(ScriptableRenderContext context,Camera rendered){if(rendered==camera){ready=true;readyFrame=Time.frameCount;}}
    static void Capture()
    {
        if(++frames<20)return;
        
        var previous=RenderTexture.active;bool oldSrgb=GL.sRGBWrite;var image=new Texture2D(960,1080,TextureFormat.RGB24,false);
        try
        {
            GL.sRGBWrite=true;Graphics.Blit(target,readback);RenderTexture.active=readback;image.ReadPixels(new Rect(0,0,960,1080),0,0);image.Apply();float sum=0;
            for(int y=cy-50;y<cy+50;y++)for(int x=cx-50;x<cx+50;x++){Color c=image.GetPixel(x,y);sum+=.2126f*c.r+.7152f*c.g+.0722f*c.b;}
            ambientSamples[phase]=RenderSettings.ambientLight.ToString("F4")+" SH0="+RenderSettings.ambientProbe[0,0].ToString("F4");brightness[phase]=sum/10000;var values=image.GetPixels().Select(c=>Mathf.Max(c.r,Mathf.Max(c.g,c.b))).ToArray();peaks[phase]=values.Max();means[phase]=values.Average();File.WriteAllBytes(Folder+"/live-tactical-"+labels[phase]+".png",image.EncodeToPNG());
            if(++phase<4){SetPhase();return;}
            bool pass=peaks[1]<.025f && peaks[3]<.025f && brightness[0]>.1f && brightness[1]<brightness[0]*.35f && brightness[3]<brightness[0]*.35f && brightness[2]>Mathf.Max(.05f,brightness[1]*2);
            Finish((pass?"PASS":"FAIL")+": "+string.Join(", ",labels.Select((label,i)=>label+" face="+brightness[i].ToString("F4")+" full-frame peak="+peaks[i].ToString("F4")+" mean="+means[i].ToString("F4")+" ambient="+ambientSamples[i])));
        }
        catch(Exception e){Finish("FAIL: "+e);}
        finally{GL.sRGBWrite=oldSrgb;RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
    }
    static void Finish(string result)
    {
        RenderPipelineManager.endCameraRendering-=Rendered;EditorApplication.update-=Tick;File.WriteAllText(Folder+"/LiveShadowResult.txt",result);
        if(camera)camera.enabled=false;
        QualitySettings.renderPipeline=pipeline;RenderSettings.sun=previousSun;RenderSettings.ambientLight=ambient;RenderSettings.ambientProbe=probe;
        foreach(var go in previousSuns)if(go)go.SetActive(true);
        if(stage.IsValid())SceneManager.UnloadSceneAsync(stage);
        if(target){target.Release();UnityEngine.Object.Destroy(target);}if(readback){readback.Release();UnityEngine.Object.Destroy(readback);}if(markerMaterial)UnityEngine.Object.Destroy(markerMaterial);foreach(var m in clones)if(m)UnityEngine.Object.Destroy(m);
        Debug.Log(result);
    }
}
