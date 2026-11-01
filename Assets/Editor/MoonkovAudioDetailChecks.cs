using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovAudioDetailChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Moonkov/Check Contact And Idle Audio")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        var library = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        Require(library, "Missing library");
        foreach (string name in new[] { "SuitWallBrush", "SuitWallMetal", "Crawl", "StanceChange", "GearMotion",
            "AlienWallScrape", "AlienWallMetal", "AlienIdleMotion" })
        {
            var sound = (SoundDef)typeof(MoonkovAudioLibrary).GetField(name).GetValue(library);
            Require(sound && sound.Clips.Count == 3, "Missing bank variants: " + name);
            Require(sound.Occlusion && sound.DistanceInfo.SpatialBlend == 1 && sound.DistanceInfo.VolumeDistMax <= 25,
                "Detail routing/range: " + name);
            Require(sound.Priority >= 150 && sound.MaxVoices <= 5 && sound.MaxVoices > 0 && sound.MinimumInterval >= .08f,
                "Detail could monopolize combat voices: " + name);
            foreach (var clip in sound.Clips)
            {
                Require(clip && clip.channels == 1 && clip.frequency == 48000 && clip.length < .95f,
                    "Clip format or unbounded duration: " + name);
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                Require(importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad &&
                    importer.defaultSampleSettings.compressionFormat == AudioCompressionFormat.PCM, "Transient codec: " + name);
                var samples = new float[clip.samples]; clip.GetData(samples, 0); float peak = 0;
                foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                Require(peak > .5f && peak < .65f, "Clip silent/clipped: " + clip.name);
            }
        }
        Require(library.AlienIdle.MaxVoices == 3 && library.AlienIdle.Role == SoundDef.MixRole.Ambience &&
            library.AlienIdle.Priority > library.AlienWarning.Priority, "Idle voice competes with attack telegraphs");
        CheckContact();
        CheckConcaveContact();
        Debug.Log("Audio detail assets, budgets and wall/support geometry checks passed.");
    }
    public static string CheckAttachedTeardown()
    {
        Require(EditorApplication.isPlaying && GameManager.Instance, "Initialized Play mode required");
        var system = (SoundSystem)GameManager.Instance.SoundSystem;
        Transform listener = null;
        foreach (var value in UnityEngine.Object.FindObjectsByType<AudioListener>()) if (value.enabled) { listener = value.transform; break; }
        var actor = new GameObject("Audio fading-owner teardown fixture");
        if (listener) actor.transform.position = listener.position + Vector3.forward * 2;
        var audio = new AlienCreatureFeedback(.65f, AlienSpecies.Spider, actor.transform);
        try
        {
            system.SetListenerTransform(actor.transform);
            audio.Observe(AlienAction.Roam, 0, 0, 100, actor.transform.position);
            audio.Observe(AlienAction.Windup, 0, 1, 100, actor.transform.position);
            audio.Observe(AlienAction.Dead, 0, 1, 0, actor.transform.position);
            audio.Dispose();
            UnityEngine.Object.DestroyImmediate(actor);
            foreach (var source in GameManager.Instance.SoundGameObjects.SoundGameObjectList)
                Require(source.IsValid(), "Dying during a fade destroyed a pooled AudioSource");
            return "Immediate death during windup/fade reclaimed attached sources: passed";
        }
        finally { audio.Dispose(); if (actor) UnityEngine.Object.DestroyImmediate(actor); if (listener) system.SetListenerTransform(listener); }
    }

    private static void CheckContact()
    {
        var actor = new GameObject("Audio contact fixture");
        var wall = new GameObject("Audio wall fixture");
        var self = new GameObject("Audio own-collider fixture");
        Vector3 origin = new Vector3(18000, 18000, 18000);
        try
        {
            actor.transform.position = origin;
            self.transform.SetParent(actor.transform, false); self.transform.localPosition = Vector3.left * .2f;
            self.AddComponent<BoxCollider>().size = new Vector3(.1f, 2, 2);
            wall.transform.position = origin + Vector3.right * .34f;
            var collider = wall.AddComponent<BoxCollider>(); collider.size = new Vector3(.1f, 4, 6);
            Physics.SyncTransforms();
            Require(MoonkovSurfaceFoley.TryFindWall(actor.transform, origin, Vector3.forward * .1f, .4f, out var hit) &&
                hit.collider == collider && hit.normal.x < -.9f, "Tangential wall contact missing or self-hit");
            Require(!MoonkovSurfaceFoley.TryFindWall(actor.transform, origin, Vector3.right * .1f, .4f, out _),
                "Direct wall push produced sliding friction");
            Require(!MoonkovSurfaceFoley.TryFindWall(actor.transform, origin, Vector3.zero, .4f, out _), "Stillness scraped");
            collider.isTrigger = true; Physics.SyncTransforms();
            Require(!MoonkovSurfaceFoley.TryFindWall(actor.transform, origin, Vector3.forward * .1f, .4f, out _), "Trigger scraped");
            collider.isTrigger = false; wall.transform.position = origin + Vector3.right * 2; Physics.SyncTransforms();
            Require(!MoonkovSurfaceFoley.TryFindWall(actor.transform, origin, Vector3.forward * .1f, .4f, out _), "Distant wall scraped");
            wall.transform.position = origin + Vector3.right * .34f; Physics.SyncTransforms();
            Require(MoonkovSurfaceFoley.TrySupportContact(actor.transform, origin, Vector3.left, .4f, out hit) &&
                hit.collider == collider, "Replicated wall-crawl pose did not find physical support");
            var slide = new MoonkovSurfaceFoley();
            Require(!slide.TrySlide(actor.transform, origin, Vector3.forward * .03f, .4f, .016f, out _), "Tiny cached contact sounded immediately");
            Require(!slide.TrySlide(actor.transform, origin + Vector3.left, Vector3.forward * .04f, .4f, .016f, out _),
                "Leaving the sampled wall sustained cached friction");
            Require(!slide.TrySlide(actor.transform, origin, Vector3.forward * .03f, .4f, .016f, out _), "Tiny contact tick sounded immediately");
            Require(slide.TrySlide(actor.transform, origin, Vector3.forward * .04f, .4f, .016f, out _), "Accumulated slide did not sound");
            Require(!slide.TrySlide(actor.transform, origin, Vector3.forward, .4f, .016f, out _), "Slide ignored cooldown");
            Require(!new MoonkovSurfaceFoley().TrySlide(actor.transform, origin, Vector3.forward * 3, .4f, .016f, out _), "Teleport scraped");
            Require(!new MoonkovSurfaceFoley().TrySlide(actor.transform, origin, Vector3.forward, .4f, 1, out _), "Stalled frame scraped");
        }
        finally { UnityEngine.Object.DestroyImmediate(actor); UnityEngine.Object.DestroyImmediate(wall); }
    }

    public static string CheckConcaveContact()
    {
        Require(!EditorApplication.isPlaying, "Use Edit mode.");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var actor = new GameObject("Concave wall audio fixture");
        var wall = new GameObject("Concave wall with opening");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(actor, scene);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, scene);
        var mesh = new Mesh();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var rays = typeof(MoonkovSurfaceFoley).GetField("s_Rays", flags);
        var frame = typeof(MoonkovSurfaceFoley).GetField("s_Frame", flags);
        var contact = typeof(MoonkovSurfaceFoley).GetField("m_Contact", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object oldRays = rays.GetValue(null), oldFrame = frame.GetValue(null);
        bool nativeError = false;
        void Log(string message, string stack, LogType type) { nativeError |= message.Contains("Physics.ClosestPoint"); }
        void Budget() { frame.SetValue(null, Time.frameCount); rays.SetValue(null, 0); }
        Application.logMessageReceived += Log;
        try
        {
            mesh.vertices = new[] { new Vector3(0,-1,-2), new Vector3(0,-1,-.5f), new Vector3(0,1,-.5f), new Vector3(0,1,-2),
                new Vector3(0,-1,.5f), new Vector3(0,-1,2), new Vector3(0,1,2), new Vector3(0,1,.5f) };
            mesh.triangles = new[] { 0,1,2,0,2,3,4,5,6,4,6,7 };
            var collider = wall.AddComponent<MeshCollider>(); collider.sharedMesh = mesh; collider.convex = false;
            wall.transform.position = Vector3.right * .3f;
            Physics.SyncTransforms(); scene.GetPhysicsScene().Simulate(.02f);
            Vector3 center = Vector3.back, slide = Vector3.forward;
            Budget();
            var foley = new MoonkovSurfaceFoley();
            Require(!foley.TrySlide(actor.transform, center, slide * .03f, .4f, .016f, out _), "Concave contact sounded before enough slip");
            Require(foley.TrySlide(actor.transform, center, slide * .04f, .4f, .016f, out var hit) && hit.collider == collider,
                "Concave wall slide did not sound");
            foreach (var away in new[] { Vector3.zero, center + Vector3.left })
            {
                Budget(); foley = new MoonkovSurfaceFoley();
                foley.TrySlide(actor.transform, center, slide * .03f, .4f, .016f, out _);
                Require(!foley.TrySlide(actor.transform, away, slide * .04f, .4f, .016f, out _) &&
                    !((RaycastHit)contact.GetValue(foley)).collider, "Opening or separation kept stale friction");
            }
            Budget(); foley = new MoonkovSurfaceFoley();
            foley.TrySlide(actor.transform, center, slide * .03f, .4f, .016f, out _);
            rays.SetValue(null, 12);
            Require(!foley.TrySlide(actor.transform, center, slide * .04f, .4f, .016f, out _) &&
                !((RaycastHit)contact.GetValue(foley)).collider, "Concave confirmation exceeded its shared ray budget");
            Require(!nativeError, "Concave contact used unsupported ClosestPoint");
            return "Concave foley: PASS (slide, opening, separation, ray budget and no native errors).";
        }
        finally
        {
            Application.logMessageReceived -= Log;
            rays.SetValue(null, oldRays); frame.SetValue(null, oldFrame);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
