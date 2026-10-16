using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class MoonkovSoftAnimePreview
{
    [MenuItem("Tools/Moonkov/Rendering/Compare Current With Soft MMD")]
    public static void Compare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode only.");
        MoonkovAnimeLightingSetup.UpdatePearlMatCap();
        MoonkovAnimeLightingSetup.UpdateColorGrade();
        MoonkovAnimeLightingSetup.UpdateRenderer();
        Capture("before", MoonkovAnimeLightingSetup.OriginalProfilePath);
        Capture("soft", MoonkovAnimeLightingSetup.ProfilePath);
        Debug.Log("Read-only character previews: Library/BuildValidation/AnimeLighting (before = current source materials).");
    }

    public static string Capture(string label, string profilePath)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode only.");
        Directory.CreateDirectory("Library/BuildValidation/AnimeLighting");
        var previousPipeline = QualitySettings.renderPipeline;
        var ambientMode = RenderSettings.ambientMode;
        var ambientLight = RenderSettings.ambientLight;
        var reflections = RenderSettings.reflectionIntensity;
        var skybox = RenderSettings.skybox;
        var fog = RenderSettings.fog;
        var previousSun = RenderSettings.sun;
        var ambientProbe = RenderSettings.ambientProbe;
        var scene = EditorSceneManager.NewPreviewScene();
        var previousTarget = RenderTexture.active;
        RenderTexture target = null;
        Texture2D image = null;
        Material groundMaterial = null;
        var clones = new System.Collections.Generic.List<Material>();
        try
        {
            QualitySettings.renderPipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(label=="soft" ? MoonkovAnimeLightingSetup.PipelinePath : "Assets/MoonEnvironment/Variants/MC00BakedBuilding/MC00BakedPipeline.asset");
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.015f, .015f, .015f);
            var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(.015f,.015f,.015f));
            RenderSettings.ambientProbe = sh;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab");
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var b in actor.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (var l in actor.GetComponentsInChildren<Light>(true)) l.enabled = false;
            foreach (var c in actor.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            if (label == "soft")
            {
                var replacements = new System.Collections.Generic.Dictionary<Material, Material>();
                foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = renderer.sharedMaterials;
                    for (int slot = 0; slot < slots.Length; slot++)
                    {
                        var original = slots[slot];
                        if (!original || !original.shader.name.StartsWith("Moonkov/lilToonMoon", StringComparison.Ordinal)) continue;
                        if (!replacements.TryGetValue(original, out var material))
                        {
                            material = new Material(original) { name = original.name };
                            replacements.Add(original, material); clones.Add(material);
                            MoonkovAnimeLightingSetup.ConfigureSoftMaterial(material);
                        }
                        slots[slot] = material;
                    }
                    renderer.sharedMaterials = slots;
                }
            }
            var animator = actor.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.Rebind(); animator.SetBool("Grounded",true);
                for(int layer=1;layer<animator.layerCount;layer++) animator.SetLayerWeight(layer,0);
                animator.Update(0);
                var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/DollSinger/Animation/Locomotion/Unarmed_Idle.anim");
                idle.SampleAnimation(actor,0);
            }
            var spring=actor.GetComponent<Unity.MP_FPS.DollSinger.SecondaryBoneSpring>();
            if(spring) { spring.InitializePresentation(); for(int step=0;step<90;step++)spring.SimulatePresentation(1f/60); }
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(go, scene);
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor", new Color(.15f,.17f,.21f));
            groundMaterial.SetFloat("_Smoothness", .12f);
            go.GetComponent<Renderer>().sharedMaterial = groundMaterial;
            go.transform.position = new Vector3(0,-.015f,0);
            var lightGo = new GameObject("Solar key");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2.2f;
            sun.color = new Color(1,.96f,.9f); sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            var volumeGo = new GameObject("Look volume");
            SceneManager.MoveGameObjectToScene(volumeGo, scene);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 999;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            var cameraGo = new GameObject("Look camera");
            SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.AddComponent<Camera>();
            camera.scene = scene; camera.enabled = false;
            camera.transform.position = new Vector3(.30f,1.35f,3.9f);
            camera.transform.LookAt(new Vector3(0,1.05f,0));
            camera.fieldOfView = 32; camera.nearClipPlane = .05f; camera.farClipPlane = 50;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f,.034f,.055f);
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true; data.renderShadows = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var desc = new RenderTextureDescriptor(960,1080,RenderTextureFormat.ARGBHalf,24);
            target = new RenderTexture(desc); target.Create();
            image = new Texture2D(960,1080,TextureFormat.RGB24,false);
            var directions = new[] {new Vector3(.3f,.7f,1), new Vector3(1,.45f,.1f), new Vector3(-.3f,.65f,-1)};
            string directory = "Library/BuildValidation/AnimeLighting";
            for (int i=0;i<directions.Length;i++)
            {
                sun.transform.rotation = Quaternion.LookRotation(-directions[i].normalized);
                for(int warm=0;warm<3;warm++)
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=target });
                RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,960,1080),0,0); image.Apply();
                File.WriteAllBytes($"{directory}/{label}-{i}.png", image.EncodeToPNG());
            }
            camera.transform.position=new Vector3(.10f,1.32f,1.65f);
            camera.transform.LookAt(new Vector3(0,1.26f,0));
            camera.fieldOfView=28;
            sun.transform.rotation=Quaternion.LookRotation(-directions[0].normalized);
            for(int warm=0;warm<3;warm++) RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target; image.ReadPixels(new Rect(0,0,960,1080),0,0);image.Apply();
            File.WriteAllBytes($"{directory}/{label}-portrait.png",image.EncodeToPNG());
            return string.Join("\n", actor.GetComponentsInChildren<Renderer>().Select(r=>r.name+": "+string.Join(",",r.sharedMaterials.Select(m=>m?m.name:"MISSING"))));
        }
        finally
        {
            RenderTexture.active=previousTarget;
            RenderSettings.ambientMode=ambientMode;
            RenderSettings.ambientLight=ambientLight;
            RenderSettings.reflectionIntensity=reflections;
            RenderSettings.skybox=skybox;
            RenderSettings.fog=fog;
            RenderSettings.sun=previousSun;
            RenderSettings.ambientProbe=ambientProbe;
            EditorSceneManager.ClosePreviewScene(scene);
            QualitySettings.renderPipeline=previousPipeline;
            if(target!=null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if(image!=null) UnityEngine.Object.DestroyImmediate(image);
            if(groundMaterial!=null) UnityEngine.Object.DestroyImmediate(groundMaterial);
            foreach(var material in clones) UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
