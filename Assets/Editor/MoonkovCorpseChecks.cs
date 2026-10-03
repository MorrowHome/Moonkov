using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;
using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;

public static class MoonkovCorpseChecks
{
    [MenuItem("Tools/Moonkov/Check Corpse Presentation")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit mode.");
        var settings=Resources.Load<CorpsePresentationSettings>("Moonkov/CorpsePresentation");
        Require(settings!=null && settings.Death!=null && settings.Death.isHumanMotion,"Missing Humanoid death assets.");
        Require(!AnimationUtility.GetAnimationClipSettings(settings.Death).loopTime,"Death animation loops.");
        var scene=EditorSceneManager.NewPreviewScene();
        var holder=new GameObject("Corpse check");SceneManager.MoveGameObjectToScene(holder,scene);
        try
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(holder.transform);
            floor.transform.position=new Vector3(10000,-.1f,0);floor.transform.localScale=new Vector3(12,.2f,12);
            Physics.SyncTransforms();
            for(int i=0;i<settings.CharacterVisuals.Length;i++)
            {
                var prefab=settings.CharacterVisuals[i];
                Require(System.Array.TrueForAll(prefab.GetComponentsInChildren<MonoBehaviour>(true),b=>b is SecondaryBoneSpring) && prefab.GetComponentsInChildren<Collider>(true).Length==0,
                    "Corpse retained gameplay scripts or colliders.");
                var rpc=new RaidCorpseRpc {LootId=24+i,Version=1,CharacterIndex=i,Position=new float3(10000,0,0),Rotation=quaternion.identity};
                var body=CorpseVisual.Spawn(holder.transform,settings,rpc);
                var animator=body.GetComponentInChildren<Animator>();
                var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
                var head=animator.GetBoneTransform(HumanBodyBones.Head);
                var start=hips.position;
                var spring=body.GetComponentInChildren<SecondaryBoneSpring>(true);
                var hairStart=spring!=null ? spring.hairRoots[0].localRotation : Quaternion.identity;
                body.SampleAge(body.FinishAge+1);
                if(spring!=null)Require(Quaternion.Angle(hairStart,spring.hairRoots[0].localRotation)>5f,"Corpse hair remained rigid.");
                var end=hips.position;var endHead=head.position;
                RequireAboveFloor(body,Vector3.zero,Vector3.up);
                Require(body.Settled && !animator.enabled && end.y<start.y-.3f && endHead.y<.8f,
                    $"Character {i} did not fall to ground: hips {start.y:F3}->{end.y:F3}, head {endHead.y:F3}.");
                body.SampleAge(settings.Death.length+10);
                Require(Vector3.Distance(hips.position,end)<.001f,"Settled corpse changed pose.");
                rpc.Age=body.FinishAge+5;
                var late=CorpseVisual.Spawn(holder.transform,settings,rpc);
                var lateAnimator=late.GetComponentInChildren<Animator>();
                Require(late.Settled && Vector3.Distance(lateAnimator.GetBoneTransform(HumanBodyBones.Hips).position,end)<.001f,
                    "Late join replayed death or changed final pose.");
                UnityEngine.Object.DestroyImmediate(body.gameObject);UnityEngine.Object.DestroyImmediate(late.gameObject);
                floor.transform.rotation=Quaternion.Euler(20,0,0);
                var planePoint=floor.transform.position+floor.transform.up*.1f;
                Physics.SyncTransforms();rpc.Position=planePoint;rpc.Rotation=floor.transform.rotation;
                var slope=CorpseVisual.Spawn(holder.transform,settings,rpc);
                RequireAboveFloor(slope,planePoint,floor.transform.up);
                UnityEngine.Object.DestroyImmediate(slope.gameObject);
                floor.transform.rotation=Quaternion.identity;Physics.SyncTransforms();
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(holder);EditorSceneManager.ClosePreviewScene(scene);}
        Debug.Log("Corpse checks passed: all three models clear flat/sloped ground, hold the final pose, stop animation work and reconstruct the pose for late joiners.");
    }
    private static void RequireAboveFloor(CorpseVisual corpse,Vector3 point,Vector3 normal)
    {
        var mesh=new Mesh();var vertices=new System.Collections.Generic.List<Vector3>();float lowest=float.PositiveInfinity;
        try
        {
            foreach(var renderer in corpse.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if(!renderer.enabled)continue;
                renderer.BakeMesh(mesh,true);mesh.GetVertices(vertices);
                foreach(var v in vertices)lowest=Mathf.Min(lowest,Vector3.Dot(renderer.transform.TransformPoint(v)-point,normal));
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(mesh);}
        Require(lowest>=-.02f,$"Corpse mesh penetrated the ground by {-lowest:F3} metres.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
}
