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
        CreateCoreMaterial(root+"TacticalMetal.mat","Universal Render Pipeline/Lit");
        CreateCoreMaterial(root+"TacticalIndicator.mat","Universal Render Pipeline/Unlit");
        Copy("Assets/UnityTechnologies/ParticlePack/EffectExamples/Fire & Explosion Effects/Prefabs/MediumFlames.prefab",root+"TacticalFire.prefab");
        Copy("Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/PoisonGas.prefab",root+"TacticalPoison.prefab");
        Copy("Assets/UnityTechnologies/ParticlePack/EffectExamples/Smoke & Steam Effects/Prefabs/GroundFog.prefab",root+"TacticalChoke.prefab");
        Configure(root+"TacticalFire.prefab",true,new Color(1,.65f,.25f,1));
        Configure(root+"TacticalPoison.prefab",false,new Color(.44f,.59f,.2f,.5f));
        Configure(root+"TacticalChoke.prefab",false,new Color(.68f,.77f,.8f,.34f));
        Debug.Log("Dedicated tactical particle resources created with bounded emitters; original Particle Pack assets unchanged.");
    }
    private static void CreateCoreMaterial(string path,string shaderName)
    {
        if(AssetDatabase.LoadAssetAtPath<Material>(path))return;
        var shader=Shader.Find(shaderName);if(!shader || !shader.isSupported)throw new InvalidOperationException("Missing tactical shader "+shaderName);
        AssetDatabase.CreateAsset(new Material(shader),path);
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
                var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
                // Unity requires each velocity axis family to use matching curve modes.
                // These dedicated effects rise gently; discard inherited sideways/orbital drift.
                var zero=new ParticleSystem.MinMaxCurve(0);
                velocity.x=zero;velocity.y=new ParticleSystem.MinMaxCurve(fire ? .35f : .075f);velocity.z=zero;
                velocity.orbitalX=zero;velocity.orbitalY=zero;velocity.orbitalZ=zero;
                velocity.orbitalOffsetX=zero;velocity.orbitalOffsetY=zero;velocity.orbitalOffsetZ=zero;
                velocity.radial=zero;velocity.speedModifier=new ParticleSystem.MinMaxCurve(1);
                var collision=particles.collision;collision.enabled=false;var triggers=particles.trigger;triggers.enabled=false;
                var lights=particles.lights;lights.enabled=false;
                var renderer=particles.GetComponent<ParticleSystemRenderer>();
                var source=renderer.sharedMaterial;
                string materialPath=path.Replace(".prefab","_"+particles.name.Replace(" ","_").Replace("(","").Replace(")","")+".mat");
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(!material)
                {
                    var shader=Resources.Load<Shader>("Moonkov/TacticalVolume");
                    if(!shader || !shader.isSupported)throw new InvalidOperationException("Tactical URP particle shader unavailable");
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
                material.shader=Resources.Load<Shader>("Moonkov/TacticalVolume");material.SetFloat("_UseTexture",1);material.SetFloat("_Flame",0);material.SetFloat("_SoftDistance",fire ? .15f : .55f);
                renderer.sharedMaterial=material;main.startColor=tint;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
                // Preserve the original flipbook, with strict particle/light caps and consistent velocity axes.
                var emission=particles.emission;emission.rateOverTime=fire ? 45 : Mathf.Min(24,emission.rateOverTime.constantMax);
            }
            foreach(var light in prefab.GetComponentsInChildren<Light>(true))light.enabled=false;
            foreach(var collider in prefab.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
            PrefabUtility.SaveAsPrefabAsset(prefab,path);
        }
        finally {PrefabUtility.UnloadPrefabContents(prefab);}
    }
}
