using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MP_FPS;

public static class MoonkovShotChecks
{
    [MenuItem("Tools/Moonkov/Check Head Shots")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit mode.");
        var scene=EditorSceneManager.NewPreviewScene();var holder=new GameObject("Head shot check");holder.SetActive(false);
        SceneManager.MoveGameObjectToScene(holder,scene);
        try
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab");
            var shooter=UnityEngine.Object.Instantiate(source,holder.transform).GetComponent<PlayerGhost>();
            shooter.transform.position=new Vector3(10000,0,-5);
            var target=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerGhosts/ArmaturePlayer_Rifle.prefab"),holder.transform).GetComponent<PlayerGhost>();
            target.transform.position=new Vector3(10000,0,0);target.gameObject.layer=(int)LayerIndex.ServerPlayer;
            foreach(var behaviour in holder.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            foreach(var camera in holder.GetComponentsInChildren<Camera>(true))camera.enabled=false;
            foreach(var collider in holder.GetComponentsInChildren<Collider>(true))collider.enabled=collider.gameObject==target.gameObject || collider.gameObject==shooter.gameObject;
            holder.SetActive(true);
            Vector3 headPoint=target.transform.position+new Vector3(0,1.68f,-.01f);
            var input=new PlayerInput {AimPoint=headPoint};input.SetFlag(PlayerInput.InputFlag.ThirdPerson,true);
            var ray=shooter.GetShotRay(input,100,out var visualTarget);
            Require(Vector3.Distance(visualTarget,headPoint)<.001f && Vector3.Cross(ray.direction,headPoint-ray.origin).magnitude<.001f,"Shot ray diverged from visual target.");
            int mask=LayerMask.GetMask("ServerPlayer","Default","Ground");Physics.SyncTransforms();
            Require(!shooter.RaycastShot(ray,100,mask,out _),"Fixture no longer reproduces the head above the movement capsule.");
            target.CreateHeadHitbox((int)LayerIndex.ServerPlayer);Physics.SyncTransforms();
            Require(shooter.RaycastShot(ray,100,mask,out var hit) && hit.collider.name=="Head hitbox","Crosshair head shot missed the head hitbox.");
            var self=shooter.gameObject.AddComponent<BoxCollider>();self.center=shooter.transform.InverseTransformPoint(ray.GetPoint(.2f));self.size=Vector3.one*.1f;Physics.SyncTransforms();
            Require(shooter.RaycastShot(ray,100,mask,out hit) && hit.collider.name=="Head hitbox","Shooter collider swallowed the shot.");
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.SetParent(holder.transform);wall.transform.position=ray.GetPoint(2);wall.transform.localScale=Vector3.one*.5f;Physics.SyncTransforms();
            Require(shooter.RaycastShot(ray,100,mask,out hit) && hit.collider.gameObject==wall,"Head hitbox allowed shooting through a wall.");
            UnityEngine.Object.DestroyImmediate(wall);UnityEngine.Object.DestroyImmediate(self);
            // Reproduce the actual DollSinger proxy setup: its movement controller is off,
            // and m_Animator3P is unassigned. The camera must still acquire its visible head.
            target.gameObject.SetActive(false);
            var proxy=UnityEngine.Object.Instantiate(source,holder.transform).GetComponent<PlayerGhost>();
            proxy.transform.position=new Vector3(10000,0,0);
            foreach(var behaviour in proxy.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            foreach(var c in proxy.GetComponentsInChildren<Collider>(true))c.enabled=false;
            proxy.CreateHeadHitbox((int)LayerIndex.ClientPlayer);
            var proxyHead=proxy.transform.Find("Head hitbox");
            Require(proxyHead!=null && proxy.transform.Find("Body hitbox")!=null,"DollSinger proxy has no independent head/body hitboxes.");
            var view=shooter.GetComponentInChildren<Unity.MP_FPS.DollSinger.DollSingerView>(true);
            view.camera.transform.position=proxyHead.position+new Vector3(0,0,-5);
            view.camera.transform.rotation=Quaternion.identity;
            Physics.SyncTransforms();
            var point=shooter.GetComponent<DollSingerNetworkPresentation>().CaptureAimPoint(Unity.Mathematics.float2.zero,100);
            Require(Vector3.Distance(point,proxyHead.position)<.4f,"Camera aimed through the proxy at distant scenery.");
            proxy.gameObject.SetActive(false);
            var authoritative=UnityEngine.Object.Instantiate(source,holder.transform).GetComponent<PlayerGhost>();
            authoritative.transform.position=new Vector3(10000,0,0);
            foreach(var behaviour in authoritative.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            foreach(var c in authoritative.GetComponentsInChildren<Collider>(true))c.enabled=false;
            authoritative.CreateHeadHitbox((int)LayerIndex.ServerPlayer);
            input.AimPoint=point;ray=shooter.GetShotRay(input,100,out visualTarget);Physics.SyncTransforms();
            Require(shooter.RaycastShot(ray,100,mask,out hit) && hit.collider.transform.IsChildOf(authoritative.transform),
                "Camera acquired the proxy, but the same target missed the server character.");
        }
        finally {UnityEngine.Object.DestroyImmediate(holder);EditorSceneManager.ClosePreviewScene(scene);Physics.SyncTransforms();}
        Debug.Log("Head shot checks passed: visual target/ray agreement, reproduced capsule miss, head hit, self filtering and wall occlusion.");
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
