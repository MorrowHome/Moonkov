using System;
using System.IO;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.SpiderLab;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class AlienEnemyChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Check upright combat and full-height clearance")]
    public static void RunUprightContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        using var world = new World("Upright alien authority fixture", WorldFlags.GameServer);
        world.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
        var tick = world.EntityManager.CreateEntity(typeof(NetworkTime));
        world.EntityManager.SetComponentData(tick, new NetworkTime { ServerTick = new NetworkTick(1) });
        var holder = new GameObject("Temporary upright checks");
        var origin = new Vector3(-9200, 1000, -9200);
        try
        {
            var floor = new GameObject("Fixture floor"); floor.transform.SetParent(holder.transform);
            floor.transform.position = origin - Vector3.up * .25f; floor.AddComponent<BoxCollider>().size = new Vector3(30, .5f, 30);
            Physics.SyncTransforms();
            foreach (string name in new[] { "StiltWalker", "StiltWatcher" })
            {
                var profile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset"));
                ProceduralAlienEnemy actor = null; GhostGameObject target = null;
                try
                {
                    profile.AttackCooldown = 20; profile.PatrolRadius = 0;
                    actor = Alien(holder.transform, world, profile, origin + Vector3.up * SurfaceMotor.Height * profile.Scale, MultiplayerRole.Server, -14001);
                    target = Humanoid(holder.transform, world, origin + Vector3.forward * 2.5f, false, 42);
                    Step(actor, 180);
                    Require(actor.DamageCount == 1 && actor.AttackKind == AlienAttackKind.StiltRake, name + " failed its committed grab");
                    Require(actor.GetComponentsInChildren<Renderer>().Length == 0, "Server allocated upright renderers");
                    Physics.SyncTransforms();
                    float top = float.MinValue;
                    foreach (var collider in actor.GetComponentsInChildren<Collider>()) top = Mathf.Max(top, collider.bounds.max.y - origin.y);
                    Require(top > profile.UprightHeight(profile.Scale) * .85f, name + " has crawler-height hit volumes");
                    Require(Vector3.Dot(actor.Body.Motor.Current.Up, Vector3.up) > .99f, "Upright actor climbed a wall");
                }
                finally { if(actor) Object.DestroyImmediate(actor.gameObject); if(target) Object.DestroyImmediate(target.gameObject); Object.DestroyImmediate(profile); }
            }
            var lowRoof = new GameObject("Low doorway roof"); lowRoof.transform.SetParent(holder.transform);
            lowRoof.transform.position = origin + new Vector3(0, 2.5f, 3); lowRoof.AddComponent<BoxCollider>().size = new Vector3(8, .3f, 4);
            Physics.SyncTransforms();
            Require(!RaidSpawnSafety.TryGroundPosition(origin + Vector3.forward * 3, .32f, 3.2f, 30, out _, out _), "Tall actor admitted below low roof");
            var motor = new SurfaceMotor(origin + Vector3.up * SurfaceMotor.Height, Quaternion.identity, LayerMask.GetMask("Default", "Ground"));
            for (int i = 0; i < 60; i++) motor.TickGroundedCapsule(Vector2.up, 3, 0, 1f/30, 3.2f, .32f);
            Require(motor.Current.Position.z - origin.z < .8f && motor.Current.Position.z - origin.z > .1f && motor.Blocked > 0,
                "Full-height sweep failed to stop at low doorway");
            var loot = AlienHarvest.Create(false, new System.Random(19), "StiltWalker");
            Require(loot.Items.Exists(i => i.Code == "alien_contractile_heart"), "Upright harvest integration missing");
            Debug.Log("Upright checks passed: both authored heights hit once, tall hitboxes, no server renderers, low-door spawn/movement rejected, existing heart harvest valid.");
        }
        finally { Object.DestroyImmediate(holder); }
    }
}

public static class StiltWalkerVisualChecks
{
    [MenuItem("Tools/Moonkov/Aliens/Capture upright height comparison")]
    public static void Capture()
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var root = new GameObject("Temporary upright lineup"); SceneManager.MoveGameObjectToScene(root, scene);
        var bodies = new AlienCreatureBody[2]; Material material = null; RenderTexture target = null; Texture2D pixels = null;
        try
        {
            root.transform.position = new Vector3(-9400, 1000, -9400);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor", new Color(.4f,.42f,.43f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0,-.15f,0); floor.transform.localScale = new Vector3(15,.3f,7);
            floor.GetComponent<Renderer>().sharedMaterial = material;
            var reference = GameObject.CreatePrimitive(PrimitiveType.Capsule); reference.transform.SetParent(root.transform, false);
            reference.transform.localPosition = new Vector3(-2.7f,.75f,0); reference.transform.localScale = new Vector3(.45f,.75f,.45f);
            reference.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(reference.GetComponent<Collider>());
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere); head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(-2.7f,1.65f,0); head.transform.localScale = Vector3.one*.3f;
            head.GetComponent<Renderer>().sharedMaterial = material; Object.DestroyImmediate(head.GetComponent<Collider>());
            Physics.SyncTransforms();
            for(int i=0;i<2;i++)
            {
                var profile=AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/"+(i==0?"StiltWalker":"StiltWatcher")+".asset");
                var go=new GameObject(profile.name); go.transform.SetParent(root.transform,false); go.transform.localPosition=new Vector3(i*2.7f,SurfaceMotor.Height*profile.Scale,0);
                bodies[i]=new AlienCreatureBody(go.transform,profile,profile.Scale,true,LayerMask.NameToLayer("ClientPlayer"));
                bodies[i].SetCombatPresentation(AlienAction.Roam,0,0,profile.Health);
                bodies[i].Tick(Vector3.zero,0,0,1f/30,0,false);
            }
            var lamp=new GameObject("Lineup light"); lamp.transform.SetParent(root.transform,false);
            var light=lamp.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=2.5f; lamp.transform.rotation=Quaternion.Euler(35,-25,0);
            var cameraObject=new GameObject("Lineup camera"); cameraObject.transform.SetParent(root.transform,false);
            var camera=cameraObject.AddComponent<Camera>(); camera.scene=scene; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.06f,.075f,.085f); camera.orthographic=true; camera.orthographicSize=2.65f;
            camera.transform.position=root.transform.position+new Vector3(5.5f,3.1f,13); camera.transform.LookAt(root.transform.position+new Vector3(0,2,0));
            camera.nearClipPlane=.05f; camera.farClipPlane=40; camera.overrideSceneCullingMask=EditorSceneManager.GetSceneCullingMask(scene);
            target=new RenderTexture(1200,800,24); RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            var saved=RenderTexture.active;
            try { RenderTexture.active=target; pixels=new Texture2D(1200,800,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,1200,800),0,0);pixels.Apply(); }
            finally {RenderTexture.active=saved;}
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");File.WriteAllBytes("Library/BuildValidation/AlienIntegration/upright-lineup.png",pixels.EncodeToPNG());
        }
        finally
        {
            foreach(var body in bodies)body?.Dispose();
            if(target)Object.DestroyImmediate(target);if(pixels)Object.DestroyImmediate(pixels);if(material)Object.DestroyImmediate(material);
            Object.DestroyImmediate(root);SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);
        }
    }
}
