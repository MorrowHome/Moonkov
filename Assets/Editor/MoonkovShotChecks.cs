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
            PrepareController(shooter);
            shooter.transform.position=new Vector3(10000,0,-5);
            var target=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerGhosts/ArmaturePlayer_Rifle.prefab"),holder.transform).GetComponent<PlayerGhost>();
            PrepareController(target);
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
            PrepareController(proxy);
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
            // More candidates than the original 32-slot camera query must still choose
            // the nearest blocker, independent of PhysX's unordered result set.
            var dense = new GameObject("Dense aim blockers");dense.transform.SetParent(holder.transform);
            Vector3 origin = view.camera.transform.position;
            for (int i = 0; i < 80; i++)
            {
                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.SetParent(dense.transform);
                blocker.transform.position = origin + Vector3.forward * (1f + i * .02f);
                blocker.transform.localScale = new Vector3(.1f, .1f, .01f);
            }
            Physics.SyncTransforms();
            var blockedPoint = shooter.GetComponent<DollSingerNetworkPresentation>().CaptureAimPoint(Unity.Mathematics.float2.zero,100);
            Require(Mathf.Abs(Vector3.Distance(origin, blockedPoint) - .995f) < .005f,
                "Saturated camera query omitted the nearest wall.");
            UnityEngine.Object.DestroyImmediate(dense);
            proxy.gameObject.SetActive(false);
            var authoritative=UnityEngine.Object.Instantiate(source,holder.transform).GetComponent<PlayerGhost>();
            PrepareController(authoritative);
            authoritative.transform.position=new Vector3(10000,0,0);
            foreach(var behaviour in authoritative.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;
            foreach(var c in authoritative.GetComponentsInChildren<Collider>(true))c.enabled=false;
            authoritative.CreateHeadHitbox((int)LayerIndex.ServerPlayer);
            input.AimPoint=point;ray=shooter.GetShotRay(input,100,out visualTarget);Physics.SyncTransforms();
            Require(shooter.RaycastShot(ray,100,mask,out hit) && hit.collider.transform.IsChildOf(authoritative.transform),
                "Camera acquired the proxy, but the same target missed the server character.");
        }
        finally {UnityEngine.Object.DestroyImmediate(holder);EditorSceneManager.ClosePreviewScene(scene);Physics.SyncTransforms();}
        Debug.Log("Head shot checks passed: visual target/ray agreement, reproduced capsule miss, head hit, self filtering, wall occlusion and 80 overlapping aim blockers.");
    }
    private static void PrepareController(PlayerGhost player)
    {
        // Edit-mode prefab instances do not execute the gameplay Awake methods.
        player.Awake();
        player.SetGhostGameObject(player.GetComponent<GhostGameObject>());
        var controller = player.GetComponent<FirstPersonController>();
        typeof(FirstPersonController).GetField("m_Controller",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(controller, player.GetComponent<UnityEngine.CharacterController>());
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
