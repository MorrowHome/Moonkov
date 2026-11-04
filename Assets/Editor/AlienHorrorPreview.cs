using System;
using System.Collections.Generic;
using System.IO;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AlienHorrorPreview
{
    [MenuItem("Tools/Moonkov/Aliens/Capture spider boss scale and attacks")]
    public static void Capture()
    {
        if (Application.isBatchMode) EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        var original = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var root = new GameObject("Temporary giant boss scale preview");
        SceneManager.MoveGameObjectToScene(root, scene);
        var bodies = new List<AlienCreatureBody>();
        var materials = new List<Material>();
        var evidence = new List<string>();
        RenderTexture target = null; Texture2D pixels = null;
        try
        {
            Vector3 offset = new Vector3(-9800, 1500, -9800);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.SetParent(root.transform);
            floor.transform.position = offset; floor.transform.localScale = Vector3.one * 16;
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); materials.Add(floorMaterial);
            floorMaterial.SetColor("_BaseColor", new Color(.075f, .085f, .1f)); floorMaterial.SetFloat("_Smoothness", .35f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(floor);
            Physics.SyncTransforms();
            foreach (string name in new[] { "SpiderBoss", "Spider" })
            {
                var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
                var actor = new GameObject(name); actor.transform.SetParent(root.transform);
                actor.transform.position = offset + new Vector3(name == "SpiderBoss" ? -2 : 6, SurfaceMotor.Height * p.Scale, 0);
                actor.transform.rotation = Quaternion.Euler(0, 180, 0);
                var body = new AlienCreatureBody(actor.transform, p, p.Scale, true, LayerMask.NameToLayer("ClientPlayer")); bodies.Add(body);
                body.SetCombatPresentation(AlienAction.Roam, 0, 0, p.Health);
                for (int frame = 0; frame < 12; frame++) body.Tick(Vector3.zero, 0, 0, 1f / 60, 0, false);
            }
            var human = GameObject.CreatePrimitive(PrimitiveType.Capsule); human.transform.SetParent(root.transform);
            human.transform.position = offset + new Vector3(3, .9f, -2); human.transform.localScale = new Vector3(.5f, .9f, .5f);
            Object.DestroyImmediate(human.GetComponent<Collider>());
            var humanMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); materials.Add(humanMaterial);
            humanMaterial.SetColor("_BaseColor", new Color(.7f, .5f, .22f)); human.GetComponent<Renderer>().sharedMaterial = humanMaterial;
            Unity.MP_FPS.Moon.MoonSilverPrintSubject.Protect(human);
            var lamp = new GameObject("Preview key"); lamp.transform.SetParent(root.transform);
            var light = lamp.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.8f;
            light.color = new Color(.66f, .76f, 1); lamp.transform.rotation = Quaternion.Euler(30, -25, 0);
            var rim = new GameObject("Preview rim"); rim.transform.SetParent(root.transform);
            light = rim.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 3;
            light.color = new Color(.75f, .65f, .58f); rim.transform.rotation = Quaternion.Euler(18, 160, 0);
            var cameraObject = new GameObject("Human eye height scale camera"); cameraObject.transform.SetParent(root.transform);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f, .022f, .03f);
            camera.transform.position = offset + new Vector3(-8, 5, -17); camera.transform.LookAt(offset + Vector3.up * 1.5f);
            camera.fieldOfView = 50; camera.nearClipPlane = .1f; camera.farClipPlane = 160;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            target = new RenderTexture(1440, 810, 24);
            var boss = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(SpiderBossSetup.ProfilePath);
            string directory = "LocalData/SpiderBossReview";
            Directory.CreateDirectory(directory);
            foreach (string pose in new[] { "solid-idle", "solid-rake", "solid-rush", "liquid-idle", "solid-dead" })
            {
                bool liquid = pose == "liquid-idle";
                var kind = pose == "solid-rush" ? AlienAttackKind.SpiderRush : AlienAttackKind.SpiderRake;
                var pattern = AlienCombatPattern.For(boss, 1, 1, kind);
                var action = pose == "solid-dead" ? AlienAction.Dead :
                    pose == "solid-rake" || pose == "solid-rush" ? AlienAction.Windup : AlienAction.Roam;
                float age = action == AlienAction.Dead ? 1 : action == AlienAction.Windup ? pattern.Windup : 0;
                bodies[0].SetCombatPresentation(action, age, 1, action == AlienAction.Dead ? 0 : boss.Health,
                    pattern.Windup, pattern.Recovery, pattern.Active, kind);
                for (int frame = 0; frame < 20; frame++) bodies[0].Tick(Vector3.zero, 0, 0, 1f / 60, 0, liquid);
                if (liquid)
                {
                    var sample = bodies[0].Fluid.SampleParticles();
                    if (sample.nonFinite != 0 || sample.particles <= 0) throw new InvalidOperationException("Invalid spider liquid.");
                    evidence.Add("Spider boss GPU particles=" + sample.particles + ", nonFinite=" + sample.nonFinite);
                }
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    if (!pixels) pixels = new Texture2D(1440, 810, TextureFormat.RGB24, false);
                    pixels.ReadPixels(new Rect(0, 0, 1440, 810), 0, 0); pixels.Apply();
                }
                finally { RenderTexture.active = previous; }
                File.WriteAllBytes(directory + "/" + pose + ".png", pixels.EncodeToPNG());
                evidence.Add(pose + ": native rig, original ceiling-spider scale and 1.8m human reference");
            }
            File.WriteAllLines(directory + "/evidence.txt", evidence);
            Debug.Log("SPIDER_BOSS_PREVIEW_PASSED: native solid/ liquid, attack and death poses; gold capsule=1.8m. This is not formal-map Play acceptance.");
        }
        finally
        {
            foreach (var body in bodies) body.Dispose();
            foreach (var material in materials) Object.DestroyImmediate(material);
            if (pixels) Object.DestroyImmediate(pixels); if (target) Object.DestroyImmediate(target);
            Object.DestroyImmediate(root); SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene, true);
        }
    }
}
