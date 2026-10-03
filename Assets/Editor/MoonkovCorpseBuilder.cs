using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;

public static class MoonkovCorpseBuilder
{
    [MenuItem("Tools/Moonkov/Build Corpse Presentation")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before building corpse assets.");
        const string folder="Assets/Resources/Moonkov";
        var sourceClip=AssetDatabase.LoadAllAssetsAtPath("Assets/Mixamo/Rifle 8-Way Locomotion Pack/death from the front.fbx")
            .OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        if(!sourceClip.isHumanMotion)throw new InvalidOperationException("Mixamo death clip is not Humanoid.");
        var clip=UnityEngine.Object.Instantiate(sourceClip);clip.name="Corpse_FallFront";clip.wrapMode=WrapMode.ClampForever;
        var clipSettings=AnimationUtility.GetAnimationClipSettings(clip);
        clipSettings.loopTime=false;clipSettings.loopBlend=false;
        clipSettings.loopBlendOrientation=true;clipSettings.loopBlendPositionXZ=true;clipSettings.loopBlendPositionY=true;
        AnimationUtility.SetAnimationClipSettings(clip,clipSettings);AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve=AnimationUtility.GetEditorCurve(clip,binding);curve.preWrapMode=curve.postWrapMode=WrapMode.ClampForever;
            AnimationUtility.SetEditorCurve(clip,binding,curve);
        }
        string clipPath=folder+"/Corpse_FallFront.anim";
        var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if(existing!=null){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);clip=existing;}
        else AssetDatabase.CreateAsset(clip,clipPath);
        var visuals=new GameObject[3];
        string[] sources={"Assets/Prefabs/PlayerGhosts/ArmaturePlayer_Rifle.prefab","Assets/Prefabs/PlayerGhosts/ArmaturePlayer_Shotgun.prefab","Assets/Resources/Moonkov/MenuCharacterVisual.prefab"};
        var scene=EditorSceneManager.NewPreviewScene();
        try
        {
            for(int index=0;index<sources.Length;index++)
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(sources[index]);
                var holder=new GameObject("Corpse asset staging");holder.SetActive(false);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder,scene);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(source,holder.transform);
                PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                var visual=index==2 ? instance : instance.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Armature_3P").gameObject;
                visual.transform.SetParent(holder.transform,false);visual.transform.localPosition=Vector3.zero;visual.transform.localRotation=Quaternion.identity;
                foreach(var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if(behaviour is SecondaryBoneSpring spring){spring.enabled=false;continue;}
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
                foreach(var camera in visual.GetComponentsInChildren<Camera>(true))UnityEngine.Object.DestroyImmediate(camera);
                foreach(var collider in visual.GetComponentsInChildren<Collider>(true))UnityEngine.Object.DestroyImmediate(collider);
                foreach(var body in visual.GetComponentsInChildren<Rigidbody>(true))UnityEngine.Object.DestroyImmediate(body);
                foreach(var light in visual.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(light);
                foreach(var audio in visual.GetComponentsInChildren<AudioSource>(true))UnityEngine.Object.DestroyImmediate(audio);
                foreach(var lod in visual.GetComponentsInChildren<LODGroup>(true))lod.ForceLOD(0);
                foreach(var t in visual.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.layer=0;
                    if(t.name=="HaloCrosshair" || t.name=="HaloBolt" || t.name=="HaloCrosshairOffset")t.gameObject.SetActive(false);
                }
                foreach(var particle in visual.GetComponentsInChildren<ParticleSystem>(true))particle.gameObject.SetActive(false);
                foreach(var trail in visual.GetComponentsInChildren<TrailRenderer>(true))trail.enabled=false;
                var animator=visual.GetComponentInChildren<Animator>(true);
                if(animator==null)throw new InvalidOperationException("Missing corpse animator: "+sources[index]);
                if(animator.avatar==null || !animator.avatar.isHuman)
                {
                    var bones=visual.GetComponentsInChildren<Transform>(true);
                    var human=HumanTrait.BoneName.Select(name=>new {human=name,bone=name.Replace(" ","")})
                        .Select(pair=>new {pair.human,bone=pair.bone.StartsWith("Left") ? "Left_"+pair.bone.Substring(4) : pair.bone.StartsWith("Right") ? "Right_"+pair.bone.Substring(5) : pair.bone})
                        .Where(pair=>bones.Any(t=>t.name==pair.bone))
                        .Select(pair=>new HumanBone {humanName=pair.human,boneName=pair.bone,limit=new HumanLimit {useDefaultValues=true}}).ToArray();
                    var description=new HumanDescription {human=human,skeleton=bones.Select(t=>new SkeletonBone {name=t.name,position=t.localPosition,rotation=t.localRotation,scale=t.localScale}).ToArray(),
                        upperArmTwist=.5f,lowerArmTwist=.5f,upperLegTwist=.5f,lowerLegTwist=.5f,armStretch=.05f,legStretch=.05f,hasTranslationDoF=false};
                    var avatar=AvatarBuilder.BuildHumanAvatar(visual,description);avatar.name="CorpseAvatar_"+index;
                    if(!avatar.isValid || !avatar.isHuman){UnityEngine.Object.DestroyImmediate(avatar);throw new InvalidOperationException("Invalid corpse Humanoid binding: "+sources[index]);}
                    string avatarPath=folder+"/CorpseAvatar_"+index+".asset";
                    var saved=AssetDatabase.LoadAssetAtPath<Avatar>(avatarPath);
                    if(saved==null){AssetDatabase.CreateAsset(avatar,avatarPath);saved=avatar;}
                    else {EditorUtility.CopySerialized(avatar,saved);UnityEngine.Object.DestroyImmediate(avatar);}
                    animator.avatar=saved;
                }
                animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.enabled=true;
                visual.SetActive(true);visual.name="CorpseVisual_"+index;
                visuals[index]=PrefabUtility.SaveAsPrefabAsset(visual,folder+"/CorpseVisual_"+index+".prefab");
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }
        finally {EditorSceneManager.ClosePreviewScene(scene);}
        const string settingsPath=folder+"/CorpsePresentation.asset";
        var settings=AssetDatabase.LoadAssetAtPath<CorpsePresentationSettings>(settingsPath);
        if(settings==null){settings=ScriptableObject.CreateInstance<CorpsePresentationSettings>();AssetDatabase.CreateAsset(settings,settingsPath);}
        settings.CharacterVisuals=visuals;settings.Death=clip;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssets();
        Debug.Log("Corpse assets built: three character visuals and non-looping Mixamo front death.");
    }
}
