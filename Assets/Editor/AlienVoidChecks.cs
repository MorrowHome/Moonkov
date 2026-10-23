using System;
using System.Collections.Generic;
using System.IO;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class AlienVoidChecks
{
    private static void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);}
    [MenuItem("Tools/Moonkov/Aliens/Check black liquid reactions")]
    public static void Run()
    {
        var style=Resources.Load<AlienVoidStyle>("Moonkov/AlienVoid");
        Check(style && style.Skin && style.Plate && style.Filament && style.Droplet && style.Sphere,"Black liquid references");
        Check(!ShaderUtil.ShaderHasError(style.Skin.shader) && !ShaderUtil.ShaderHasError(style.Filament.shader),"Black liquid shaders compile");
        var profile=AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Spider.asset");
        var charge=AlienLiquidMotion.Evaluate(profile,AlienAction.Windup,.7f,1,1,1,Vector3.zero,Vector3.forward);
        var burst=AlienLiquidMotion.Evaluate(profile,AlienAction.Strike,.5f,1,1,1,Vector3.zero,Vector3.forward);
        var recall=AlienLiquidMotion.Evaluate(profile,AlienAction.Recover,.1f,1,1,1,Vector3.zero,Vector3.forward);
        Check(charge.Charge>.5f && charge.Burst==0 && burst.Burst>.9f && recall.Recovery>.8f,"Windup/burst/recall envelopes");
        var scene=EditorSceneManager.NewPreviewScene();
        SkeletonFluidPresentation calm=null, reactive=null;
        try
        {
            var a=new GameObject("Neutral liquid control");SceneManager.MoveGameObjectToScene(a,scene);
            var b=new GameObject("Attack liquid control");SceneManager.MoveGameObjectToScene(b,scene);
            var seeds=new List<SkeletonFluidPresentation.Seed>();
            for(int z=-2;z<=2;z++)for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)
                if(x*x+y*y+z*z<=5)seeds.Add(new SkeletonFluidPresentation.Seed {rest=new Vector4(x*.045f,y*.045f,z*.045f,1),weights=new Vector4(1,0,0,0),bones=new Vector4(0,0,0,-1)});
            calm=new SkeletonFluidPresentation(a.transform,0,seeds.ToArray(),1,2.8f,64,true,true);
            reactive=new SkeletonFluidPresentation(b.transform,0,seeds.ToArray(),1,2.8f,64,true,true);
            calm.Settings.gravity=reactive.Settings.gravity=0;
            var matrices=new[]{Matrix4x4.identity};var centres=new[]{new Vector4(0,0,0,1)};
            reactive.Reaction=new LiquidReaction {Centre=Vector3.zero,Direction=Vector3.forward,Strength=1,Burst=1};
            for(int i=0;i<24;i++){calm.Draw(matrices,centres,Vector3.zero,1f/60);reactive.Draw(matrices,centres,Vector3.zero,1f/60);}
            var neutral=calm.SampleParticles();var shed=reactive.SampleParticles();
            Check(neutral.nonFinite==0 && shed.nonFinite==0,"GPU reaction stays finite");
            Check(shed.particles==seeds.Count && calm.ParticleCount==reactive.ParticleCount,"Shed droplets retain SPH mass and particle budget");
            Check(shed.meanGuideDistance>neutral.meanGuideDistance+.002f,"Actual GPU particle motion responds to attack force");
            reactive.Reaction=new LiquidReaction {Centre=Vector3.zero,Direction=Vector3.forward,Strength=1,Recovery=1};
            for(int i=0;i<75;i++)reactive.Draw(matrices,centres,Vector3.zero,1f/60);
            var regroup=reactive.SampleParticles();
            Check(regroup.nonFinite==0 && regroup.meanGuideDistance<shed.meanGuideDistance,"Recovery recalls shed mass");
            Debug.Log($"Black liquid GPU contract passed: particles={shed.particles}, neutral={neutral.meanGuideDistance:F4}, shed={shed.meanGuideDistance:F4}, recalled={regroup.meanGuideDistance:F4}. No added simulation dispatches or particles.");
        }
        finally{calm?.Release();reactive?.Release();EditorSceneManager.ClosePreviewScene(scene);}
        CheckServerPresentation(profile);
    }
    private static void CheckServerPresentation(AlienCreatureProfile profile)
    {
        var owner=new GameObject("Server-only black liquid check");AlienCreatureBody body=null;
        try
        {
            body=new AlienCreatureBody(owner.transform,profile,profile.Scale,false,LayerMask.NameToLayer("ServerPlayer"));
            body.SetCombatPresentation(AlienAction.Strike,.2f,1,100,1,1,1);body.Tick(Vector3.zero,0,0,1f/60,1,false);
            Check(body.Fluid==null && owner.GetComponentsInChildren<Renderer>(true).Length==0,"Server never allocates black liquid renderers or GPU solver");
        }
        finally{body?.Dispose();Object.DestroyImmediate(owner);}
    }

    public static void CheckAttackSnapshots()
    {
        var profile=AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Caterpillar.asset");
        var pattern=AlienCombatPattern.For(profile,1,1);
        var root=new GameObject("Black liquid attack snapshot fixture");AlienAttackPresentation effect=null;
        try
        {
            effect=new AlienAttackPresentation(root.transform,profile);
            var state=new ProceduralAlienEnemy.State {Action=AlienAction.Windup,AttackSequence=1,AttackDirection=Vector3.forward,AttackPoint=Vector3.forward*3,ActionTime=.5f};
            effect.Observe(state,pattern,Vector3.up);
            foreach(var line in root.GetComponentsInChildren<LineRenderer>())Check(!line.enabled,"Windup never draws attack ranges or lanes");
            state.Action=AlienAction.Strike;state.ActionTime=.05f;effect.Observe(state,pattern,Vector3.up);
            var particles=root.GetComponentInChildren<ParticleSystem>();int count=particles.particleCount;
            effect.Observe(state,pattern,Vector3.up);
            Check(count>0 && particles.particleCount==count,"Duplicate snapshot does not replay a droplet burst");
            Check(root.GetComponentsInChildren<Collider>().Length==0,"Attack effects never create gameplay hitboxes");
            effect.Dispose();effect=new AlienAttackPresentation(root.transform,profile);
            state.AttackSequence=2;state.ActionTime=pattern.Active*.7f;
            effect.Observe(state,pattern,Vector3.up);effect.Observe(state,pattern,Vector3.up);
            Check(root.GetComponentInChildren<ParticleSystem>().particleCount==0,"Late join does not replay historical splashes");
            Debug.Log("Black liquid attack snapshots passed: no windup range overlays, no duplicate/late historical splash and no cosmetic colliders.");
        }
        finally{effect?.Dispose();Object.DestroyImmediate(root);}
    }

    public static void Capture(string output, float illumination = 1, bool liquid = true)
    {
        string[] names={"Spider","Snake","Caterpillar","Centipede","Leech","Slime","Tentacle","Roots","FaceHugger","StiltWalker","StiltWatcher"};
        var scene=EditorSceneManager.NewPreviewScene();
        var previous=RenderTexture.active;
        var sheet=new Texture2D(1536,1152,TextureFormat.RGB24,false);
        var pixels=new Texture2D(384,384,TextureFormat.RGB24,false);
        var target=new RenderTexture(384,384,24);
        Color ambient=RenderSettings.ambientLight;var mode=RenderSettings.ambientMode;float reflections=RenderSettings.reflectionIntensity;
        Camera camera=null;
        Material floorMaterial=null;
        Vector3 origin=new Vector3(-12000,1500,-12000);
        try
        {
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.22f,.22f,.24f)*illumination;RenderSettings.reflectionIntensity=.4f;
            var lamp=new GameObject("Wet tissue key");SceneManager.MoveGameObjectToScene(lamp,scene);
            var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2.4f*illumination;lamp.transform.rotation=Quaternion.Euler(38,-40,0);
            var rim=new GameObject("Wet tissue edge");SceneManager.MoveGameObjectToScene(rim,scene);
            var edge=rim.AddComponent<Light>();edge.type=LightType.Directional;edge.intensity=1.4f*illumination;rim.transform.rotation=Quaternion.Euler(25,140,0);
            var cam=new GameObject("Black liquid preview camera");SceneManager.MoveGameObjectToScene(cam,scene);
            camera=cam.AddComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.13f,.15f);camera.fieldOfView=34;camera.targetTexture=target;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=origin+Vector3.down*.1f;floor.transform.localScale=new Vector3(30,.2f,30);
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.12f,.13f,.15f));floorMaterial.SetFloat("_Smoothness",.15f);
            floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;Physics.SyncTransforms();
            floor.GetComponent<Renderer>().enabled=false;
            var frame=new Color[384*384];System.Array.Fill(frame,new Color(.12f,.13f,.15f));
            for(int cell=0;cell<12;cell++)sheet.SetPixels(cell%4*384,(2-cell/4)*384,384,384,frame);
            for(int cell=0;cell<names.Length;cell++)
            {
                var profile=AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/"+names[cell]+".asset");
                var owner=new GameObject(names[cell]);SceneManager.MoveGameObjectToScene(owner,scene);
                owner.transform.position=origin+Vector3.up*.15f;
                AlienCreatureBody body=null;
                try
                {
                    body=new AlienCreatureBody(owner.transform,profile,profile.Scale,true,LayerMask.NameToLayer("ClientPlayer"));
                    var pattern=AlienCombatPattern.For(profile,1,1);
                    for(int frameIndex=0;frameIndex<24;frameIndex++)
                    {
                        body.SetCombatPresentation(AlienAction.Windup,pattern.Windup*.75f,1,profile.Health,pattern.Windup,pattern.Recovery,pattern.Active);
                        body.Tick(Vector3.zero,0,0,1f/60,.75f,liquid);
                    }
                    Physics.SyncTransforms();
                    // Preview-scene Collider.bounds can lag or be empty. Frame the actual
                    // authored spheres/capsules directly, independent of physics registration.
                    var bounds=new Bounds(body.AimPoint,Vector3.one*.1f);
                    foreach(var sphere in owner.GetComponentsInChildren<SphereCollider>())
                        bounds.Encapsulate(new Bounds(sphere.transform.TransformPoint(sphere.center),Vector3.one*sphere.radius*2));
                    foreach(var capsule in owner.GetComponentsInChildren<CapsuleCollider>())
                    {
                        Vector3 axis=capsule.transform.up*Mathf.Max(0,capsule.height*.5f-capsule.radius);
                        bounds.Encapsulate(new Bounds(capsule.transform.position+axis,Vector3.one*capsule.radius*2));
                        bounds.Encapsulate(new Bounds(capsule.transform.position-axis,Vector3.one*capsule.radius*2));
                    }
                    float distance=Mathf.Max(.5f,bounds.extents.magnitude)/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*1.15f;
                    camera.transform.position=bounds.center+new Vector3(1,.65f,1.3f).normalized*distance;camera.transform.LookAt(bounds.center);
                    camera.nearClipPlane=.01f;camera.farClipPlane=distance+30;
                    camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,384,384),0,0);pixels.Apply();
                    sheet.SetPixels(cell%4*384,(2-cell/4)*384,384,384,pixels.GetPixels());
                    Check(!liquid || body.Fluid!=null && body.Fluid.ParticleCount<=16000,"Authored creature has bounded GPU liquid: "+names[cell]);
                }
                finally{body?.Dispose();Object.DestroyImmediate(owner);}
            }
            sheet.Apply();Directory.CreateDirectory(Path.GetDirectoryName(output));File.WriteAllBytes(output,sheet.EncodeToPNG());
            Debug.Log("Eleven authored black liquid creatures rendered once to "+output+". Cosmetic preview, not formal-map Play acceptance.");
        }
        finally
        {
            if(camera)camera.targetTexture=null;RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);
            RenderSettings.ambientMode=mode;RenderSettings.ambientLight=ambient;RenderSettings.reflectionIntensity=reflections;
            target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);Object.DestroyImmediate(sheet);
            if(floorMaterial)Object.DestroyImmediate(floorMaterial);
        }
    }
}
