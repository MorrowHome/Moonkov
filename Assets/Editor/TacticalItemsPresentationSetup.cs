using System;
using UnityEditor;
using UnityEngine;

// Copies only the necessary existing Particle Pack assets into an explicit runtime
// resource boundary. Source prefabs/materials remain untouched.
public static class TacticalItemsPresentationSetup
{
    [MenuItem("Moonkov/Tactical Items/Create Dedicated Particle Resources")]
    public static void CreateResources()
    {
        const string root="Assets/Resources/Moonkov/";
        Copy("Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/MediumFlames.prefab",root+"TacticalFire.prefab");
        Copy("Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/PoisonGas.prefab",root+"TacticalPoison.prefab");
        Copy("Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/GroundFog.prefab",root+"TacticalChoke.prefab");
        Configure(root+"TacticalFire.prefab",true,new Color(1,.65f,.25f,1));
        Configure(root+"TacticalPoison.prefab",false,new Color(.44f,.59f,.2f,.5f));
        Configure(root+"TacticalChoke.prefab",false,new Color(.68f,.77f,.8f,.34f));
        AssetDatabase.SaveAssets();
        Debug.Log("Dedicated tactical particle resources created with bounded emitters; original Particle Pack assets unchanged.");
    }
    private static void Copy(string source,string target)
    {if(AssetDatabase.LoadAssetAtPath<GameObject>(target)==null && !AssetDatabase.CopyAsset(source,target))throw new InvalidOperationException("Cannot copy tactical effect "+source);}
    private static void Configure(string path,bool fire,Color tint)
    {
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach(var particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=particles.main;main.maxParticles=fire ? 64 : 24;main.loop=true;main.playOnAwake=false;
                main.startLifetime=new ParticleSystem.MinMaxCurve(fire ? .7f : 1.8f,fire ? 1.4f : 3.2f);
                main.startSize3D=false;main.startSize=new ParticleSystem.MinMaxCurve(fire ? .65f : 1.2f,fire ? 1.35f : 2.2f);
                main.startSpeed=new ParticleSystem.MinMaxCurve(fire ? .2f : .025f,fire ? .7f : .12f);
                var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;velocity.y=fire ? .35f : .075f;
                var collision=particles.collision;collision.enabled=false;var triggers=particles.trigger;triggers.enabled=false;
                var lights=particles.lights;lights.enabled=false;
                var renderer=particles.GetComponent<ParticleSystemRenderer>();
                var source=renderer.sharedMaterial;
                string materialPath=path.Replace(".prefab","_"+particles.name.Replace(" ","_").Replace("(","").Replace(")","")+".mat");
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(!material)
                {
                    var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if(!shader || !shader.isSupported)throw new InvalidOperationException("URP particle shader unavailable");
                    material=new Material(shader);
                    Texture texture=source && source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
                    if(!texture && source && source.HasProperty("_MainTex"))texture=source.GetTexture("_MainTex");
                    if(!texture)texture=AssetDatabase.LoadAssetAtPath<Texture2D>(fire ?
                        "Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Textures/MediumFlame01.tif" :
                        "Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Textures/SmokePuff.png");
                    material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
                    material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_ZWrite",0);
                    material.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
                    AssetDatabase.CreateAsset(material,materialPath);
                }
                renderer.sharedMaterial=material;main.startColor=tint;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                // The original flipbook/curves remain intact, with strict particle and light caps.
                var emission=particles.emission;emission.rateOverTime=Mathf.Min(32,emission.rateOverTime.constantMax);
            }
            foreach(var light in prefab.GetComponentsInChildren<Light>(true))light.enabled=false;
            foreach(var collider in prefab.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
            PrefabUtility.SaveAsPrefabAsset(prefab,path);
        }
        finally {PrefabUtility.UnloadPrefabContents(prefab);}
    }
}
