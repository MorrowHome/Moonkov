using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class GrenadePresentationChecks
{
    private static int checks;
    private static void Check(bool ok,string message) { if(!ok)throw new InvalidOperationException(message);checks++; }
    [MenuItem("Tools/Moonkov/Grenades/Review production hand motions")]
    public static string Run()
    {
        checks=0;Directory.CreateDirectory("LocalData/GrenadeReview");
        foreach(string rig in GrenadeAnimationSetup.Rigs) Review(rig);
        return "PASS "+checks+" grenade animation checks. Previews: LocalData/GrenadeReview";
    }
    private static void Review(string rig)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var target=new RenderTexture(480,360,24);var sheet=new Texture2D(2400,720,TextureFormat.RGB24,false);
        var previous=RenderTexture.active;var meshes=new List<Mesh>();
        try
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"),scene);
            foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            var animator=root.GetComponentInChildren<Animator>(true);
            for(var node=animator.transform;node;node=node.parent)node.gameObject.SetActive(true);
            animator.runtimeAnimatorController=null;animator.Rebind();animator.Update(0);
            var bones=animator.GetComponentsInChildren<Transform>(true);var rotations=bones.Select(b=>b.localRotation).ToArray();
            var leg=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var legRotation=leg.localRotation;
            var presenter=animator.gameObject.AddComponent<GrenadePresentation>();presenter.Configure(animator,null,rig);presenter.enabled=false;
            var sources=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled && s.sharedMesh).ToArray();
            var filters=new List<MeshFilter>();
            foreach(var source in sources)
            {
                source.forceRenderingOff=true;
                var node=new GameObject("Baked preview surface");node.transform.SetParent(source.transform,false);
                var scale=source.transform.lossyScale;node.transform.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
                var mesh=new Mesh();meshes.Add(mesh);var filter=node.AddComponent<MeshFilter>();filter.sharedMesh=mesh;filters.Add(filter);
                node.AddComponent<MeshRenderer>().sharedMaterials=source.sharedMaterials;
            }
            var cameraObject=new GameObject("Grenade review camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.10f,.13f);
            camera.orthographic=true;camera.orthographicSize=.57f;camera.nearClipPlane=.01f;
            var head=animator.GetBoneTransform(HumanBodyBones.Head).position;
            foreach(var angles in new[]{new Vector3(35,-25,0),new Vector3(-25,150,0)})
            {
                var lightObject=new GameObject("Review key");SceneManager.MoveGameObjectToScene(lightObject,scene);
                var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=angles.x>0?2:1;light.transform.rotation=Quaternion.Euler(angles);
            }
            var stages=new[]{GrenadePhase.Safe,GrenadePhase.Live,GrenadePhase.Throwing,GrenadePhase.Throwing,GrenadePhase.Recovering};
            var times=new[]{0f,0f,.55f,.99f,.6f};
            for(int row=0;row<2;row++)for(int col=0;col<5;col++)
            {
                for(int b=0;b<bones.Length;b++)bones[b].localRotation=rotations[b];
                var state=new PredictedPlayerGhost {CurrentHealth=100,GrenadeHoldId=1,GrenadeItem="energy_grenade",GrenadePhase=(byte)stages[col],
                    GrenadePhaseRemaining=GrenadeHandling.Duration(stages[col])*(1-times[col]),GrenadeUnderhand=row==1};
                presenter.Present(state,false);presenter.SamplePreview(times[col]);
                Check(Quaternion.Angle(leg.localRotation,legRotation)<.001f,rig+" grenade pose changed legs");
                Check(presenter.PropVisible==(col<4),rig+" prop must hide exactly after release");
                Check(Vector3.Distance(presenter.PropPosition,animator.GetBoneTransform(HumanBodyBones.RightHand).position)<.15f,rig+" prop detached from hand");
                if(col<2)
                {
                    var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var middle=animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
                    var index=animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
                    var little=animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
                    var palm=-Vector3.Cross(index.position-little.position,middle.position-hand.position).normalized;
                    Check(Vector3.Dot(presenter.PropPosition-hand.position,palm)>.02f,rig+" grenade belongs inside the palm, never on the back of the hand");
                    var joint=animator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate);
                    var tip=animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
                    Check(Vector3.Angle(joint.position-middle.position,tip.position-joint.position)>30,rig+" held grenade needs curled fingers");
                    if(col==1)
                    {
                        var guide=animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
                        var guideEnd=animator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate);
                        Check(Vector3.Dot((guideEnd.position-guide.position).normalized,animator.transform.forward)>.9f,
                            rig+" the empty left hand must point along the throw aim");
                    }
                }
                for(int i=0;i<sources.Length;i++){sources[i].BakeMesh(meshes[i]);meshes[i].RecalculateBounds();filters[i].sharedMesh=null;filters[i].sharedMesh=meshes[i];}
                camera.transform.position=head+new Vector3(.65f,-.25f,2.4f);camera.transform.LookAt(head+Vector3.down*.25f);
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=target});
                RenderTexture.active=target;sheet.ReadPixels(new Rect(0,0,480,360),col*480,(1-row)*360,false);
                if(row==0 && col<2)
                {
                    var still=new Texture2D(480,360,TextureFormat.RGB24,false);
                    still.ReadPixels(new Rect(0,0,480,360),0,0,false);still.Apply();
                    File.WriteAllBytes("LocalData/GrenadeReview/"+rig+(col==0?"-Hold":"-Aim")+".png",still.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(still);
                }
            }
            sheet.Apply();File.WriteAllBytes("LocalData/GrenadeReview/"+rig+".png",sheet.EncodeToPNG());
            // Every supported physical item must have a real held mesh.
            foreach(string code in new[]{EnergyGrenadeRules.Code}.Concat(TacticalItemRules.Definitions.Where(d=>d.Throwable).Select(d=>d.Code)))
            {
                presenter.Present(new PredictedPlayerGhost {CurrentHealth=100,GrenadeHoldId=2,GrenadeItem=code,GrenadePhase=(byte)GrenadePhase.Safe},false);
                presenter.SamplePreview(0);Check(presenter.PropVisible,rig+" missing held mesh "+code);
            }
            presenter.Present(new PredictedPlayerGhost {CurrentHealth=0},false);Check(!presenter.OwnsHands&&!presenter.PropVisible,rig+" death cleanup");
        }
        finally
        {
            RenderTexture.active=previous;
            EditorSceneManager.ClosePreviewScene(scene);
            foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(sheet);target.Release();UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
