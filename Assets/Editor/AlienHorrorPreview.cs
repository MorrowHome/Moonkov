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
    [MenuItem("Tools/Moonkov/Aliens/Capture giant boss scale preview")]
    public static void Capture()
    {
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
            foreach (string name in new[] { "Tentacle", "Roots", "StiltWatcher" })
            {
                var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + name + ".asset");
                var actor = new GameObject(name); actor.transform.SetParent(root.transform);
                actor.transform.position = offset + new Vector3(name == "Tentacle" ? -17 : name == "Roots" ? 17 : 0, SurfaceMotor.Height * p.Scale, name == "StiltWatcher" ? -5 : 0);
                actor.transform.rotation = Quaternion.Euler(0, 180, 0);
                var body = new AlienCreatureBody(actor.transform, p, p.Scale, true, LayerMask.NameToLayer("ClientPlayer")); bodies.Add(body);
                body.SetCombatPresentation(AlienAction.Roam, 0, 0, p.Health);
                // Force each real GPU liquid through a short bounded solve, independently of distance LOD.
                for (int frame = 0; frame < 12; frame++) body.Tick(Vector3.zero, 0, 0, 1f / 60, 0, true);
                var sample = body.Fluid.SampleParticles();
                if (sample.nonFinite != 0 || sample.particles <= 0) throw new InvalidOperationException(name + " liquid is invalid.");
                evidence.Add(name + " GPU particles=" + sample.particles + ", nonFinite=" + sample.nonFinite);
            }
            var human = GameObject.CreatePrimitive(PrimitiveType.Capsule); human.transform.SetParent(root.transform);
            human.transform.position = offset + new Vector3(0, .9f, -11); human.transform.localScale = new Vector3(.5f, .9f, .5f);
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
            camera.transform.position = offset + new Vector3(0, 1.7f, -45); camera.transform.LookAt(offset + Vector3.up * 3.7f);
            camera.fieldOfView = 50; camera.nearClipPlane = .1f; camera.farClipPlane = 160;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            target = new RenderTexture(1440, 810, 24);
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target; pixels = new Texture2D(1440, 810, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1440, 810), 0, 0); pixels.Apply();
            }
            finally { RenderTexture.active = previous; }
            Directory.CreateDirectory("Docs/AI"); File.WriteAllBytes("Docs/AI/AlienHorrorRoster.png", pixels.EncodeToPNG());
            Directory.CreateDirectory("Library/BuildValidation/AlienIntegration");
            File.WriteAllLines("Library/BuildValidation/AlienIntegration/horror-gpu.txt", evidence);
            Debug.Log("Actual liquid bosses rendered at human eye height; gold capsule=1.8m, centre=4.2m watcher. Preview is not formal-map Play acceptance.");
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
