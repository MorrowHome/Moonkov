using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Actual camera captures; material/lighting/Bloom ablations and interleaved costs.</summary>
public static class MoonkovShoulderRimCapture
{
    public const string Output="Docs/Images/ShoulderRim";
    const int Width=960,Height=1080;
    class Shot{public string label,light,view;public int mode;public bool bloom,hdr,benchmark;}
    [Serializable] public class Sample{public int stage,draws,setPass,triangles;public double submitMs,mainMs,gpuMs,frameMs;}
    [Serializable] public class HdrSample{public string label;public float maxRgb,meanRgb,shoulderPeak;public int pixelsAboveKnee,pixelsAboveThreshold;}
    [Serializable] class Report{public string unity,gpu,api,notes;public List<Sample> samples=new List<Sample>();public List<HdrSample> hdr=new List<HdrSample>();}
    static readonly Queue<Shot> queue=new Queue<Shot>();static readonly List<Material> temporaryMaterials=new List<Material>();
    static Material[][] modes;static Camera camera;static GameObject actor;static Light key,fill;static GameObject roof;
    static Volume volume;static VolumeProfile oldProfile,profile;static RenderTexture target,hdrTarget,readback,oldTarget;static RenderPipelineAsset pipeline;
    static Vector3 oldPos;static Quaternion oldRot;static float oldFov,oldFar,oldKeyNear;static int vsync,frameRate,frames,lastFrame;static bool ready,oldPost;static int renderedFrame;static double started,submit;
    static Shot shot;static Report report;static readonly Stopwatch timer=new Stopwatch();static ProfilerRecorder cpu,gpu;
    [MenuItem("Tools/Moonkov/lilToon/Shoulder Rim/Capture Quick In Play")]
    public static void Quick()=>Start(false);
    [MenuItem("Tools/Moonkov/lilToon/Shoulder Rim/Capture Full In Play")]
    public static void Full()=>Start(true);
    public static void Start(bool full)
    {
        if(!EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=MoonkovLilToonLookDev.ScenePath)throw new InvalidOperationException("Open lilToon LookDev scene in Play");
        if(camera)throw new InvalidOperationException("Capture already running");Directory.CreateDirectory(Output);
        actor=GameObject.Find("lilToon Character");camera=GameObject.Find("lilToon Camera").GetComponent<Camera>();MoonkovLilToonLookDev.Pose(actor);
        key=GameObject.Find("lilToon Key").GetComponent<Light>();fill=GameObject.Find("lilToon Fill").GetComponent<Light>();RenderSettings.sun=key;oldKeyNear=key.shadowNearPlane;
        modes=new Material[4][];
        for(int mode=0;mode<4;mode++)modes[mode]=MoonkovShoulderRimLookDev.Names.Select(n=>
        {
            var source=MoonkovShoulderRimLookDev.Get(n,mode==0);if(!source)throw new InvalidOperationException("Build shoulder copies first");
            var m=new Material(source){name=n};temporaryMaterials.Add(m);
            if(mode==1)m.SetFloat("_UseRim",0);if(mode==2)m.SetFloat("_UseReflection",0);return m;
        }).ToArray();
        volume=GameObject.Find("lilToon Volume").GetComponent<Volume>();oldProfile=volume.sharedProfile;
        profile=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovShoulderRimLookDev.Grade));
        profile.components=profile.components.Select(c=>UnityEngine.Object.Instantiate(c)).ToList();volume.sharedProfile=profile;
        roof=GameObject.CreatePrimitive(PrimitiveType.Cube);roof.name="Temporary Shoulder Rim roof";roof.transform.localScale=new Vector3(64,.4f,64);roof.transform.position=new Vector3(0,8,0);
        roof.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Root+"/Floor.mat");
        pipeline=QualitySettings.renderPipeline;QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MoonkovAnimeLightingSetup.PipelinePath);
        oldTarget=camera.targetTexture;oldPos=camera.transform.position;oldRot=camera.transform.rotation;oldFov=camera.fieldOfView;oldFar=camera.farClipPlane;oldPost=camera.GetUniversalAdditionalCameraData().renderPostProcessing;
        camera.farClipPlane=2000;
        // A camera targeting an 8-bit RT can force URP's scene buffer to LDR,
        // clipping highlights before Bloom. Preserve HDR through post processing.
        target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);target.Create();
        hdrTarget=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);hdrTarget.Create();
        readback=new RenderTexture(Width,Height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);readback.Create();
        vsync=QualitySettings.vSyncCount;frameRate=Application.targetFrameRate;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        report=new Report{unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),notes="960x1080 fixed pose, FP16 linear camera target through post processing, sRGB PNG readback. Photos use the corrected 16m sun shadow near plane for both materials. Benchmarks compare previous sun near plane "+oldKeyNear+" vs corrected 16m as well as material/Bloom changes. Native GGX/Rim ablations only affect Body and InnerDress. Four interleaved before/after blocks, 60 warmup +120 sampled frames/block. PNG readback excluded. CPU submit is begin/end camera wall time, not GPU time; unavailable GPU is zero. HDR probes disable all post processing and use a linear FP16 target."};
        foreach(string light in new[]{"side","back"})foreach(int mode in new[]{0,1,2,3})foreach(bool bloom in new[]{false,true})Add(light+"-"+Mode(mode)+(bloom?"-bloom":"-nobloom"),mode,light,"shoulder",bloom);
        foreach(int mode in new[]{0,3})Add("portrait-"+Mode(mode),mode,"side","portrait",true);
        if(full)
        {
            foreach(string light in new[]{"front","room","shade","highroof","lamp","dark"})foreach(int mode in new[]{0,3})Add(light+"-"+Mode(mode),mode,light,"portrait",true);
            foreach(string view in new[]{"full","turn"})foreach(int mode in new[]{0,3})Add(view+"-"+Mode(mode),mode,"side",view,true);
            foreach(int mode in new[]{0,3})queue.Enqueue(new Shot{label="hdr-"+Mode(mode),mode=mode,light="side",view="shoulder",hdr=true});
            for(int block=0;block<4;block++)foreach(int mode in new[]{0,3})queue.Enqueue(new Shot{label="perf-"+block+"-"+Mode(mode),mode=mode,light="side",view="portrait",bloom=true,benchmark=true});
        }
        started=EditorApplication.timeSinceStartup;lastFrame=-1;cpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);gpu=ProfilerRecorder.StartNew(ProfilerCategory.Render,"GPU Frame Time",1);
        RenderPipelineManager.beginCameraRendering+=Begin;RenderPipelineManager.endCameraRendering+=End;EditorApplication.update+=Tick;AssemblyReloadEvents.beforeAssemblyReload+=Cancel;EditorApplication.playModeStateChanged+=PlayChanged;
        Next();File.WriteAllText(Output+"/Status.txt","RUNNING");
    }
    static string Mode(int n)=>new[]{"before","ggx","rim","after"}[n];
    static void Add(string label,int mode,string light,string view,bool bloom)=>queue.Enqueue(new Shot{label=label,mode=mode,light=light,view=view,bloom=bloom});
    static void Next()
    {
        if(queue.Count==0){File.WriteAllText(Output+"/Performance.json",JsonUtility.ToJson(report,true));File.WriteAllText(Output+"/Status.txt","PASS: all requested camera captures completed");Cancel();return;}
        shot=queue.Dequeue();frames=0;ready=false;
        key.shadowNearPlane=shot.benchmark&&shot.mode==0?oldKeyNear:16;
        foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))r.sharedMaterials=r.sharedMaterials.Select(m=>{int i=m?Array.IndexOf(MoonkovShoulderRimLookDev.Names,m.name.Replace(" (Instance)","")):-1;return i>=0?modes[shot.mode][i]:m;}).ToArray();
        profile.TryGet<Bloom>(out var bloom);bloom.intensity.Override(shot.bloom?(shot.mode==0?.1f:MoonkovShoulderRimLookDev.BloomIntensity):0);
        bloom.scatter.Override(shot.mode==0?.55f:MoonkovShoulderRimLookDev.BloomScatter);
        camera.targetTexture=shot.hdr?hdrTarget:target;camera.GetUniversalAdditionalCameraData().renderPostProcessing=!shot.hdr;
        bool shade=shot.light=="shade"||shot.light=="highroof"||shot.light=="lamp",room=shot.light=="room";
        // The diagnostic occluder casts real shadows without drawing a black
        // ceiling across the portrait. It is absent from all unoccluded shots.
        roof.GetComponent<Renderer>().enabled=shade;roof.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.ShadowsOnly;roof.transform.position=new Vector3(0,shot.light=="shade"||shot.light=="lamp"?4:8,0);
        key.enabled=!room&&shot.light!="dark";key.color=Color.white;key.intensity=shot.light=="front"?4:2.5f;
        var direction=shot.light=="back"?new Vector3(.65f,.35f,-.7f):shot.light=="side"?new Vector3(.85f,.4f,.15f):new Vector3(.3f,.75f,1);
        key.transform.rotation=Quaternion.LookRotation(-direction.normalized);
        fill.enabled=room||shot.light=="lamp"||shot.light=="side"||shot.light=="back";fill.color=room?new Color(1,.82f,.72f):new Color(.73f,.85f,1);fill.intensity=room||shot.light=="lamp"?1.8f:.12f;fill.range=5;fill.transform.position=new Vector3(-.35f,1.6f,.7f);
        RenderSettings.ambientLight=Color.black;var sh=new SphericalHarmonicsL2();sh.AddAmbientLight(Color.black);RenderSettings.ambientProbe=sh;
        var pos=new Vector3(.1f,1.32f,1.65f);var look=new Vector3(0,1.26f,0);float fov=28;
        if(shot.view=="shoulder"){pos=new Vector3(.14f,1.17f,.82f);look=new Vector3(0,1.125f,0);fov=29;}
        if(shot.view=="full"){pos=new Vector3(.25f,1.12f,3.9f);look=new Vector3(0,.94f,0);}
        if(shot.view=="turn"){pos=new Vector3(-.75f,1.25f,1.1f);look=new Vector3(0,1.15f,0);}
        camera.transform.position=pos;camera.transform.LookAt(look);camera.fieldOfView=fov;
    }
    static void Begin(ScriptableRenderContext c,Camera cam){if(cam==camera)timer.Restart();}
    static void End(ScriptableRenderContext c,Camera cam){if(cam==camera){submit=timer.Elapsed.TotalMilliseconds;ready=true;renderedFrame=Time.frameCount;}}
    static void Tick()
    {
        if(!EditorApplication.isPlaying||!camera||EditorApplication.timeSinceStartup-started>480){File.WriteAllText(Output+"/Status.txt","FAIL: interrupted/timeout");Cancel();return;}
        if(!ready||lastFrame==renderedFrame)return;ready=false;lastFrame=renderedFrame;frames++;
        try
        {
            if(!shot.benchmark){if(frames<20)return;Save();Next();return;}
            if(frames<=60)return;
            report.samples.Add(new Sample{stage=shot.mode==0?0:1,submitMs=submit,mainMs=cpu.Valid&&cpu.Count>0?cpu.LastValue/1e6:0,gpuMs=gpu.Valid&&gpu.Count>0?gpu.LastValue/1e6:0,frameMs=Time.unscaledDeltaTime*1000,draws=UnityStats.drawCalls,setPass=UnityStats.setPassCalls,triangles=UnityStats.triangles});
            if(frames>=180)Next();
        }
        catch(Exception e){File.WriteAllText(Output+"/Status.txt","FAIL: "+e);Cancel();UnityEngine.Debug.LogException(e);}
    }
    static void Save()
    {
        var previous=RenderTexture.active;bool previousSrgb=GL.sRGBWrite;var image=new Texture2D(Width,Height,shot.hdr?TextureFormat.RGBAFloat:TextureFormat.RGB24,false,shot.hdr);
        try
        {
            if(!shot.hdr){GL.sRGBWrite=true;Graphics.Blit(target,readback);}
            RenderTexture.active=shot.hdr?hdrTarget:readback;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();
            if(!shot.hdr){File.WriteAllBytes(Output+"/"+shot.label+".png",image.EncodeToPNG());return;}
            var pixels=image.GetPixels();var bright=pixels.Select(c=>Mathf.Max(c.r,Mathf.Max(c.g,c.b))).ToArray();float threshold=Mathf.GammaToLinearSpace(1.5f);
            float shoulderPeak=0;
            // Two shoulder windows in the fixed near-camera image, coordinates measured from its top-left.
            for(int y=410;y<530;y++)for(int x=196;x<740;x++)if(x<355||x>630)shoulderPeak=Mathf.Max(shoulderPeak,bright[(Height-1-y)*Width+x]);
            report.hdr.Add(new HdrSample{label=shot.label,maxRgb=bright.Max(),meanRgb=bright.Average(),shoulderPeak=shoulderPeak,pixelsAboveKnee=bright.Count(v=>v>threshold*.5f),pixelsAboveThreshold=bright.Count(v=>v>threshold)});
        }
        finally{GL.sRGBWrite=previousSrgb;RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
    }
    static void PlayChanged(PlayModeStateChange s){if(s==PlayModeStateChange.ExitingPlayMode)Cancel();}
    public static void Cancel()
    {
        RenderPipelineManager.beginCameraRendering-=Begin;RenderPipelineManager.endCameraRendering-=End;EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;EditorApplication.playModeStateChanged-=PlayChanged;
        if(camera){camera.targetTexture=oldTarget;camera.transform.SetPositionAndRotation(oldPos,oldRot);camera.fieldOfView=oldFov;camera.farClipPlane=oldFar;camera.GetUniversalAdditionalCameraData().renderPostProcessing=oldPost;}
        if(volume)volume.sharedProfile=oldProfile;if(profile){foreach(var c in profile.components)UnityEngine.Object.DestroyImmediate(c);UnityEngine.Object.DestroyImmediate(profile);}
        if(camera){QualitySettings.renderPipeline=pipeline;QualitySettings.vSyncCount=vsync;Application.targetFrameRate=frameRate;MoonkovShoulderRimLookDev.SetLook(actor,false);}
        if(key)key.shadowNearPlane=oldKeyNear;
        if(roof)UnityEngine.Object.Destroy(roof);foreach(var m in temporaryMaterials)if(m)UnityEngine.Object.DestroyImmediate(m);temporaryMaterials.Clear();
        foreach(var rt in new[]{target,hdrTarget,readback})if(rt){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
        cpu.Dispose();gpu.Dispose();queue.Clear();camera=null;actor=null;profile=null;roof=null;
    }
}
