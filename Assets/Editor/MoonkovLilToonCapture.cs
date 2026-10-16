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

/// <summary>Repeatable fixed-pose Play-mode screenshots and interleaved material cost comparison.</summary>
public static class MoonkovLilToonCapture
{
    const string Output="Docs/Images/LilToonNative";
    const int Width=960,Height=1080;
    [Serializable] public class Sample
    {
        public string label; public int stage,draws,setPass,triangles;
        public double submitMs,mainMs,gpuMs,frameMs;
    }
    [Serializable] class Report
    {
        public string unity,gpu,api,scene,pipeline,notes;
        public int width,height;
        public bool renderTargetSrgb;
        public List<Sample> samples=new List<Sample>();
    }
    class Shot { public string label,lighting,view;public int stage;public bool benchmark; }
    static readonly Queue<Shot> shots=new Queue<Shot>();
    static Camera camera;static GameObject actor;static Light key,fill;static Renderer roof;
    static RenderTexture target,oldTarget;static RenderPipelineAsset pipeline;
    static int vsync,frameRate,frames,lastFrame;static double started,submit,frameReady;
    static bool ready;static Shot shot;static Report report;
    static ProfilerRecorder cpuRecorder,gpuRecorder;
    static readonly Stopwatch timer=new Stopwatch();
    static readonly FrameTiming[] timings=new FrameTiming[1];
    static ulong timingStamp;

    [MenuItem("Tools/Moonkov/lilToon/Capture Comparison In Play")]
    public static void Start()
    {
        if(!EditorApplication.isPlaying || SceneManager.GetActiveScene().path!=MoonkovLilToonLookDev.ScenePath)
            throw new InvalidOperationException("Open CharacterLookDev scene and enter Play first.");
        if(camera)throw new InvalidOperationException("Capture already running");
        Directory.CreateDirectory(Output);
        actor=GameObject.Find("lilToon Character");camera=GameObject.Find("lilToon Camera").GetComponent<Camera>();
        MoonkovLilToonLookDev.Pose(actor);
        key=GameObject.Find("lilToon Key").GetComponent<Light>();fill=GameObject.Find("lilToon Fill").GetComponent<Light>();
        var roofObject=GameObject.CreatePrimitive(PrimitiveType.Cube);roofObject.name="Temporary LookDev shadow roof";roof=roofObject.GetComponent<Renderer>();
        roofObject.transform.position=new Vector3(0,4,0);roofObject.transform.localScale=new Vector3(16,.4f,16);
        roof.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Root+"/Floor.mat");
        pipeline=QualitySettings.renderPipeline;QualitySettings.renderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MoonkovAnimeLightingSetup.PipelinePath);
        vsync=QualitySettings.vSyncCount;frameRate=Application.targetFrameRate;QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
        oldTarget=camera.targetTexture;target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();camera.targetTexture=target;
        report=new Report {unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),scene=SceneManager.GetActiveScene().path,
            pipeline=QualitySettings.renderPipeline.name,width=Width,height=Height,renderTargetSrgb=target.sRGB,
            notes="Editor Play fixed-pose capture. 60 warmup + 120 measured frames per block, A/B interleaved four times. GPU timings include the Editor; unavailable values are zero, not zero cost. CPU submit is begin/end camera elapsed, not GPU execution. Readback/PNG excluded from benchmark samples."};
        
        foreach(string light in new[]{"studio","sun","harsh","room","back","shade","lamp","dark"})
        foreach(int stage in new[]{0,1})
        {
            string label=light+(stage==0?"-before":"-after");Add(label,stage,light,"portrait");
            if(light!="shade"&&light!="lamp"&&light!="dark")Add(label+"-full",stage,light,"full");
        }
        foreach(string light in new[]{"studio","sun","harsh"}) Add(light+"-previous",2,light,"portrait");
        foreach(int stage in new[]{0,1}) {Add("eyes"+(stage==0?"-before":"-after"),stage,"studio","eyes");Add("oblique"+(stage==0?"-before":"-after"),stage,"studio","oblique");}
        for(int block=0;block<4;block++)foreach(int stage in new[]{0,1})shots.Enqueue(new Shot{label="performance-"+block+(stage==0?"-before":"-after"),stage=stage,lighting="studio",view="portrait",benchmark=true});
        cpuRecorder=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
        gpuRecorder=ProfilerRecorder.StartNew(ProfilerCategory.Render,"GPU Frame Time",1);
        started=EditorApplication.timeSinceStartup;lastFrame=-1;timingStamp=0;Next();
        RenderPipelineManager.beginCameraRendering+=BeginCamera;RenderPipelineManager.endCameraRendering+=EndCamera;
        EditorApplication.update+=Tick;AssemblyReloadEvents.beforeAssemblyReload+=Cancel;
        EditorApplication.playModeStateChanged+=PlayChanged;
        File.WriteAllText(Output+"/Status.txt","RUNNING");
    }
    static void Add(string label,int stage,string lighting,string view) => shots.Enqueue(new Shot{label=label,stage=stage,lighting=lighting,view=view});
    static void Next()
    {
        if(shots.Count==0){Finish();return;}
        shot=shots.Dequeue();frames=0;ready=false;
        MoonkovLilToonLookDev.SetLook(actor,shot.stage==0);
        if(shot.stage==2)
            foreach(var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                r.sharedMaterials=r.sharedMaterials.Select(m=>m&&MoonkovLilToonLookDev.Names.Contains(m.name)?AssetDatabase.LoadAssetAtPath<Material>(MoonkovLilToonLookDev.Root+"/Previous/"+m.name+".mat"):m).ToArray();
        // Both material sets use the same grade/exposure, camera, pose and light state.
        GameObject.Find("lilToon Volume").GetComponent<Volume>().sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(MoonkovLilToonLookDev.Grade);
        bool room=shot.lighting=="room";
        key.enabled=!room&&shot.lighting!="dark";fill.enabled=shot.lighting=="studio"||room||shot.lighting=="lamp";
        key.color=shot.lighting=="studio"?new Color(1,.93f,.86f):Color.white;
        key.intensity=shot.lighting=="studio"?1.0f:shot.lighting=="harsh"?4:1.6f;
        key.transform.rotation=Quaternion.LookRotation(-(shot.lighting=="back"?new Vector3(-.3f,.65f,-1):shot.lighting=="studio"?new Vector3(.6f,.65f,.65f):new Vector3(.3f,.75f,1)).normalized);
        fill.color=room?new Color(1,.79f,.62f):new Color(.68f,.8f,1);fill.intensity=room||shot.lighting=="lamp"?1.8f:.14f;
        roof.shadowCastingMode=shot.lighting=="shade"||shot.lighting=="lamp"?ShadowCastingMode.On:ShadowCastingMode.Off;
        RenderSettings.ambientLight=shot.lighting=="studio"?new Color(.055f,.065f,.085f):new Color(.015f,.015f,.015f);
        var sh=new SphericalHarmonicsL2();sh.AddAmbientLight(RenderSettings.ambientLight);RenderSettings.ambientProbe=sh;
        var position=new Vector3(.1f,1.32f,1.65f);var look=new Vector3(0,1.26f,0);float fov=28;
        if(shot.view=="full"){position=new Vector3(.25f,1.12f,3.9f);look=new Vector3(0,.94f,0);fov=28;}
        if(shot.view=="eyes"){position=new Vector3(0,1.34f,.65f);look=new Vector3(0,1.32f,0);fov=25;}
        if(shot.view=="oblique"){position=new Vector3(.85f,1.33f,1.45f);look=new Vector3(0,1.27f,0);}
        camera.transform.position=position;camera.transform.LookAt(look);camera.fieldOfView=fov;
    }
    static void BeginCamera(ScriptableRenderContext context,Camera c){if(c==camera)timer.Restart();}
    static void EndCamera(ScriptableRenderContext context,Camera c){if(c!=camera)return;submit=timer.Elapsed.TotalMilliseconds;ready=true;frameReady=Time.frameCount;}
    static void Tick()
    {
        if(!EditorApplication.isPlaying || !camera || EditorApplication.timeSinceStartup-started>600){Cancel();return;}
        // Readback after the frame has submitted, rather than within endCameraRendering.
        if(!ready || lastFrame==(int)frameReady)return;
        lastFrame=(int)frameReady;ready=false;frames++;
        if(!shot.benchmark)
        {
            if(frames<20)return;
            SaveImage(Output+"/"+shot.label+".png");Next();return;
        }
        FrameTimingManager.CaptureFrameTimings();
        if(frames<=60)return;
        double gpu=0;
        if(gpuRecorder.Valid && gpuRecorder.Count>0)gpu=gpuRecorder.LastValue/1000000.0;
        if(FrameTimingManager.GetLatestTimings(1,timings)>0 && timings[0].frameStartTimestamp!=timingStamp)
        {timingStamp=timings[0].frameStartTimestamp;if(gpu<=0)gpu=timings[0].gpuFrameTime;}
        report.samples.Add(new Sample{label=shot.label,stage=shot.stage,submitMs=submit,gpuMs=gpu,
            mainMs=cpuRecorder.Valid&&cpuRecorder.Count>0?cpuRecorder.LastValue/1000000.0:0,frameMs=Time.unscaledDeltaTime*1000,
            draws=UnityStats.drawCalls,setPass=UnityStats.setPassCalls,triangles=UnityStats.triangles});
        if(frames>=180)Next();
    }
    static void SaveImage(string path)
    {
        var previous=RenderTexture.active;var image=new Texture2D(Width,Height,TextureFormat.RGB24,false);
        try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
    }
    static void Finish()
    {
        File.WriteAllText(Output+"/Performance.json",JsonUtility.ToJson(report,true));File.WriteAllText(Output+"/Status.txt","PASS: screenshots and 960 interleaved performance samples saved.");
        Cancel();
    }
    static void PlayChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode)Cancel();}
    public static void Cancel()
    {
        EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=Cancel;EditorApplication.playModeStateChanged-=PlayChanged;
        RenderPipelineManager.beginCameraRendering-=BeginCamera;RenderPipelineManager.endCameraRendering-=EndCamera;
        if(camera)
        {
            camera.targetTexture=oldTarget;QualitySettings.renderPipeline=pipeline;QualitySettings.vSyncCount=vsync;Application.targetFrameRate=frameRate;
            MoonkovLilToonLookDev.SetLook(actor,false);
            if(roof)UnityEngine.Object.Destroy(roof.gameObject);
            if(target){target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        camera=null;actor=null;target=null;shots.Clear();cpuRecorder.Dispose();gpuRecorder.Dispose();
    }
}
