using System;
using System.Collections.Generic;
using System.IO;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class AlienArticulatedFluidChecks
{
    [Serializable] private sealed class Report
    {
        public List<string> passed = new List<string>();
        public List<SkeletonFluidPresentation.Sample> samples = new List<SkeletonFluidPresentation.Sample>();
    }
    [MenuItem("Tools/Moonkov/Aliens/Check articulated GPU fluid integration")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var report = new Report();
        var holder = new GameObject("Temporary articulated liquid fixtures");
        Vector3 origin = new Vector3(-9200,1000,-9200);
        try
        {
            var floor = new GameObject("Fluid fixture floor"); floor.transform.SetParent(holder.transform);
            floor.transform.position = origin - Vector3.up*.25f; floor.AddComponent<BoxCollider>().size = new Vector3(30,.5f,30);
            Physics.SyncTransforms();
            foreach(string name in new[]{"StiltWalker","StiltWatcher","FaceHugger"})
            {
                var profile=AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/"+name+".asset");
                Require(profile.Liquid,name+" profile disables liquid");
                var owner=new GameObject("GPU fixture "+name);owner.transform.SetParent(holder.transform);
                owner.transform.position=origin+Vector3.up*SurfaceMotor.Height*profile.Scale;
                AlienCreatureBody body=null;
                try
                {
                    body=new AlienCreatureBody(owner.transform,profile,profile.Scale,false,LayerMask.NameToLayer("ServerPlayer"));
                    body.Tick(Vector3.zero,0,0,1f/60,0,true);
                    Require(body.Fluid==null && owner.GetComponentsInChildren<Renderer>(true).Length==0,"Server allocated GPU fluid or renderers");
                    body.Dispose();
                    body=new AlienCreatureBody(owner.transform,profile,profile.Scale,true,LayerMask.NameToLayer("ClientPlayer"));
                    for(int frame=0;frame<16;frame++)
                    {
                        body.SetCombatPresentation(AlienAction.Windup,frame/60f,1,profile.Health,profile.Windup,profile.Recovery,.45f);
                        body.Tick(Vector3.forward,1.5f,0,1f/60,.5f,true);
                    }
                    Sample(body,report,name+" moving guide");
                    Require(owner.GetComponentsInChildren<Renderer>().Length==2,"Solid fallback is showing over the particle volume");
                    body.Tick(Vector3.zero,0,0,1f/60,0,false);
                    Require(!body.Fluid.Root.gameObject.activeSelf && owner.GetComponentsInChildren<Renderer>().Length>0,"Budget fallback did not hide fluid/show the rig");
                    body.Tick(Vector3.zero,0,0,1f/60,0,true);
                    Require(body.Fluid.Root.gameObject.activeSelf,"Liquid did not resume from fallback");
                    if(profile.Species==AlienSpecies.FaceHugger)
                    {
                        body.SetAttachedPose(origin+Vector3.up*1.6f+Vector3.forward*.12f,Quaternion.identity);
                        for(int frame=0;frame<16;frame++)
                        {
                            body.SetCombatPresentation(AlienAction.Attached,frame/60f,1,profile.Health);
                            body.Tick(Vector3.zero,0,0,1f/60,1,true);
                        }
                        Sample(body,report,"FaceHugger attached guide");
                    }
                    var root=body.Fluid.Root;body.Dispose();body=null;
                    Require(!root,"Fluid root survived disposal");
                }
                finally{body?.Dispose();Object.DestroyImmediate(owner);}
            }
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllText("Library/BuildValidation/AlienIntegration/articulated-fluid.json",JsonUtility.ToJson(report,true));
            Debug.Log("Articulated GPU liquid checks passed: "+string.Join("; ",report.passed));
        }
        finally{Object.DestroyImmediate(holder);}
    }
    private static void Sample(AlienCreatureBody body,Report report,string name)
    {
        Require(body.Fluid!=null && body.Fluid.Root.gameObject.activeSelf,name+" did not allocate active shared fluid");
        var sample=body.Fluid.SampleParticles();report.samples.Add(sample);
        Require(sample.particles>0 && sample.particles<=16000 && sample.nonFinite==0,name+" has invalid GPU particles");
        Require(sample.meanGuideDistance<.3f && sample.maxGuideDistance<1,name+" lost its articulated silhouette");
        report.passed.Add(name+": "+sample.particles+" particles, "+body.Fluid.FilamentCount+" filament bonds, no non-finite values");
    }
    private static void Require(bool pass,string message){if(!pass)throw new InvalidOperationException(message);}
}
