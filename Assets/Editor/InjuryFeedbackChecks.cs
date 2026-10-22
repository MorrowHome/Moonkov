using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.MP_FPS;
using Object=UnityEngine.Object;
using BodyPart=Unity.MP_FPS.BodyPart;

public static class InjuryFeedbackChecks
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly Type ModelType=typeof(DamageVisualsController).Assembly.GetType("Unity.MP_FPS.InjuryFeedbackState",true);
    private static object Model()=>Activator.CreateInstance(ModelType,true);
    private static float Value(object model,string name)=>(float)ModelType.GetField(name,Fields).GetValue(model);
    private static void Present(object model,PredictedPlayerGhost state,float dt=.1f)=>ModelType.GetMethod("Present",Fields).Invoke(model,new object[]{state,dt,2f});
    private static PredictedPlayerGhost Healthy(){var state=new PredictedPlayerGhost();RaidHealth.Initialize(ref state);return state;}
    private static void Settle(object model,PredictedPlayerGhost state){for(int i=0;i<30;i++)Present(model,state);}
    private static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException("Injury feedback: "+text);}

    [MenuItem("Moonkov/Visuals/Check Injury Feedback")]
    public static void Validate()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Stop Play before isolated feedback checks.");
        var state=Healthy();state.LastHitTick=12;state.LastDamageAmount=32;
        var model=Model();Present(model,state);
        Require(Value(model,"Impact")==0 && Value(model,"Blood")==0,"Historical join hit replayed.");
        RaidHealth.CombatDamage(ref state,BodyPart.Chest,32,13,RaidDamageKind.Bullet);Present(model,state,1f/60);
        float impact=Value(model,"Impact"),blood=Value(model,"Blood");
        Require(impact>.4f && blood>.2f,"Real tissue hit lacks impact/blood.");
        Present(model,state,0);Require(Value(model,"Impact")==impact && Value(model,"Blood")==blood,"Prediction replay duplicated impact.");
        var stale=Healthy();stale.LastHitTick=12;stale.LastDamageAmount=32;Present(model,stale,0);Present(model,state,0);
        Require(Value(model,"Impact")==impact && Value(model,"Blood")==blood,"Stale snapshot rewound hit deduplication.");

        state=Healthy();state.ShieldEquipped=true;state.ShieldCharge=state.ShieldEnergy=100;model=Model();Present(model,state);
        RaidHealth.CombatDamage(ref state,BodyPart.Chest,32,1,RaidDamageKind.Bullet);Present(model,state);
        Require(Value(model,"Impact")==0 && Value(model,"Blood")==0,"Fully absorbed shield damage produced blood.");
        model=Model();state=Healthy();state.Energy=0;Present(model,state);
        RaidNutrition.Tick(ref state,.1f,1000,1000,100,0,4,6,1);Present(model,state);
        Require(Value(model,"Hunger")>0 && Value(model,"Impact")==0 && Value(model,"Blood")==0,"Metabolic damage used gunshot flash.");

        state=Healthy();RaidHealth.Set(ref state,BodyPart.Chest,8);model=Model();Settle(model,state);
        Require(state.CurrentHealth>state.MaxHealth*.7f && Value(model,"Critical")>.9f,"Nearly fatal chest hidden by healthy limbs.");
        RaidHealth.Set(ref state,BodyPart.Chest,85);Settle(model,state);
        Require(Value(model,"Critical")<.001f,"Healing left persistent critical red.");
        state=Healthy();state.Hydration=0;model=Model();Settle(model,state);
        Require(Value(model,"Thirst")>.99f && Value(model,"Hunger")==0,"Dehydration not distinguished from hunger.");
        state.Hydration=100;Settle(model,state);Require(Value(model,"Thirst")<.001f,"Drinking left persistent dehydration lens.");
        state=new PredictedPlayerGhost{ClashEnabled=true,ClashLife=ClashLife.Alive,ClashMaxHealthFraction=.5f};RaidHealth.Initialize(ref state);
        state.Energy=state.Hydration=0;model=Model();Settle(model,state);
        Require(Value(model,"Critical")==0 && Value(model,"Hunger")==0 && Value(model,"Thirst")==0,"Clash cap/paused nutrition falsely appears injured.");

        state=Healthy();model=Model();Present(model,state);
        RaidHealth.CombatDamage(ref state,BodyPart.Chest,100,20,RaidDamageKind.Bullet);Present(model,state);
        Require(Value(model,"Impact")>0 && Value(model,"Visibility")>0,"Lethal first shot lost its visual response.");
        Settle(model,state);Require(Value(model,"Visibility")==0,"Death feedback remained on result view.");
        state=Healthy();Present(model,state);Require(Value(model,"Impact")==0 && Value(model,"Critical")==0,"New life replayed prior death.");
        model=Model();state.CurrentHealth=0;Present(model,state);Require(Value(model,"Visibility")==0,"Joining while dead flashed an old death.");

        var a=new GameObject("Temporary injury lifecycle A");var b=new GameObject("Temporary injury lifecycle B");
        try
        {
            var first=a.AddComponent<DamageVisualsController>();var second=b.AddComponent<DamageVisualsController>();
            first.SendMessage("EnsureInjuryMaterial");second.SendMessage("EnsureInjuryMaterial");
            var field=typeof(DamageVisualsController).GetField("m_InjuryMaterial",Fields);
            var firstMaterial=(Material)field.GetValue(first);var secondMaterial=(Material)field.GetValue(second);
            var mask=firstMaterial.GetTexture("_VeinMask");
            Require(mask && mask==secondMaterial.GetTexture("_VeinMask"),"Vein art not shared.");
            first.SendMessage("OnDisable");Require((Material)field.GetValue(first)==null && mask,"First release destroyed another camera's mask.");
            second.SendMessage("OnDisable");Require(!mask,"Final release leaked the vessel texture.");
        }
        finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
        Debug.Log("Injury feedback checks passed: authoritative hits/replay/shields/metabolic damage, vital-part critical state, recovery, Clash, death/new life and shared resource cleanup.");
    }

    public static string CaptureFormalMap(string directory)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Stop Play before static capture.");
        const string path="Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity";
        var scene=EditorSceneManager.OpenPreviewScene(path);
        RenderTexture rt=null;Texture2D sheet=null;DamageVisualsController controller=null;Action<ScriptableRenderContext,Camera> inject=null;
        try
        {
            var map=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MoonRaidMap>(true)).FirstOrDefault();
            Require(map && map.LootPositions.Length>0,"Formal-map loot anchors missing.");
            var camera=new GameObject("Temporary injury formal-map camera").AddComponent<Camera>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;camera.enabled=false;
            Vector3 target=map.LootPositions[0]+Vector3.up*1.3f;
            camera.transform.position=target+new Vector3(-8,.4f,-10);camera.transform.LookAt(target);
            camera.fieldOfView=70;camera.nearClipPlane=.05f;camera.farClipPlane=2000;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.04f,.055f);
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;
            var fill=new GameObject("Temporary injury preview light").AddComponent<Light>();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(fill.gameObject,scene);fill.type=LightType.Directional;fill.intensity=1.2f;fill.transform.rotation=Quaternion.Euler(35,-30,0);
            var host=new GameObject("Temporary injury preview effect");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);
            controller=host.AddComponent<DamageVisualsController>();controller.SendMessage("EnsureInjuryMaterial");
            var modelField=typeof(DamageVisualsController).GetField("m_Injury",Fields);
            var model=modelField.GetValue(controller);
            var material=(Material)typeof(DamageVisualsController).GetField("m_InjuryMaterial",Fields).GetValue(controller);
            var pass=new FullScreenPassWrapper("_InjuryActualPreviewPass",material,0,true,false,(RenderPassEvent)602);
            bool enabled=false;inject=(context,c)=>{if(c==camera && enabled)pass.EnqueuePass(c);};RenderPipelineManager.beginCameraRendering+=inject;
            Directory.CreateDirectory(directory);rt=new RenderTexture(1280,720,24);rt.Create();sheet=new Texture2D(2560,1440,TextureFormat.RGB24,false);
            // A newly opened scene needs its renderer batches populated before comparison frames.
            for(int warmup=0;warmup<3;warmup++)RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            var labels=new[]{"healthy","hit","critical","starvation","dehydration"};
            for(int view=0;view<labels.Length;view++)
            {
                ModelType.GetMethod("Reset",Fields).Invoke(model,null);var state=Healthy();Present(model,state);
                if(view==1){RaidHealth.CombatDamage(ref state,BodyPart.Chest,45,2,RaidDamageKind.Bullet);Present(model,state,1f/60);}
                if(view==2){RaidHealth.Set(ref state,BodyPart.Chest,8);Settle(model,state);}
                if(view>=3)
                {
                    for(int part=0;part<RaidHealth.PartCount;part++)RaidHealth.Set(ref state,(BodyPart)part,RaidHealth.Maximum((BodyPart)part)*.18f);
                    if(view==3)state.Energy=0;else state.Hydration=0;Settle(model,state);
                }
                controller.SendMessage("UpdateInjuryMaterial");enabled=view>0;
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(directory,"injury-"+labels[view]+".png"),image.EncodeToPNG());
                if(view>0){int tile=view-1;sheet.SetPixels(tile%2*1280,tile<2?720:0,1280,720,image.GetPixels());}
                Object.DestroyImmediate(image);
            }
            sheet.Apply();File.WriteAllBytes(Path.Combine(directory,"injury-feedback-overview.png"),sheet.EncodeToPNG());
            var errors=ShaderUtil.GetShaderMessages(material.shader).Where(m=>m.severity.ToString()=="Error").ToArray();
            Require(errors.Length==0,"Shader failed compilation.");
            return "Formal-map static captures: "+directory+"; camera "+camera.transform.position+"; actual URP fullscreen pass, temporary neutral fill; no Play or HUD acceptance.";
        }
        finally
        {
            if(inject!=null)RenderPipelineManager.beginCameraRendering-=inject;
            if(controller)controller.SendMessage("OnDisable");
            if(rt){rt.Release();Object.DestroyImmediate(rt);}if(sheet)Object.DestroyImmediate(sheet);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
