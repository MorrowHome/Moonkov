using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.SpiderLab.Editor
{
    /// <summary>Creates a separate MC00 copy and captures both creatures in one bounded Play run.
    /// SessionState bridges the Editor's domain reload; no changes are saved during the probe.</summary>
    [InitializeOnLoad]
    public static class CreatureLabTools
    {
        public const string ScenePath = "Assets/UmbrellaSpiderLab/Scenes/MC00CreatureLab.unity";
        public const string LiquidScenePath = "Assets/UmbrellaSpiderLab/Scenes/MC00LiquidCreatureLab.unity";
        private const string Output = "Library/BuildValidation/ProceduralCreatures";
        private const string Armed = "CreatureLabCapture";
        private static Report s_Report;
        private static ProceduralCreatureExperiment s_Actor;
        private static int s_Case;
        private static float s_Start, s_NextFrame;
        private static bool s_Started, s_Captured;
        private static RenderTexture s_Target;
        private static Texture2D s_Image;
        private static FileStream s_Stream;
        private static int s_Frames;
        private static int s_FluidPhase;
        private static bool LiquidCapture => SessionState.GetBool("CreatureLiquidCapture", false);

        [Serializable] private sealed class Result
        {
            public string species;
            public int frames, nonFinite, surfaceTransitions, maximumUnsupportedBody;
            public float travel, maximumBend, maximumFootSlip, maximumWaveMotion, finalSpeed;
            public int plantedFeet;
            public int particles, filaments, escapes, maximumBodyBlocks, maximumSelfContacts;
            public bool samePoseMatched, fluidPassed;
            public SkeletonFluidPresentation.Sample attached, released, reformed, moving;
        }
        [Serializable] private sealed class Report
        {
            public string capturedAt, scene = ScenePath;
            public List<Result> cases = new List<Result>();
            public List<string> errors = new List<string>();
        }

        static CreatureLabTools()
        {
            if (!SessionState.GetBool(Armed, false)) return;
            s_Report = new Report { capturedAt = DateTime.UtcNow.ToString("O"), scene = LiquidCapture ? LiquidScenePath : ScenePath };
            EditorApplication.update += CaptureUpdate;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Tools/Umbrella Spider/7 - Open snake and caterpillar lab")]
        public static void OpenLab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene before opening the creature lab.");
            bool created = !File.Exists(ScenePath);
            if (created && !AssetDatabase.CopyAsset(SpiderLabTools.ScenePath, ScenePath))
                throw new InvalidOperationException("Cannot copy the existing MC00 lab.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (created)
            {
                var spider = Object.FindAnyObjectByType<UmbrellaSpiderExperiment>();
                if (!spider) throw new InvalidOperationException("The source lab has no configured spider observer.");
                var source = new SerializedObject(spider);
                var camera = source.FindProperty("observerCamera").objectReferenceValue;
                var player = source.FindProperty("previewPlayer").objectReferenceValue;
                int mask = source.FindProperty("surfaceMask").intValue;
                var actorObject = spider.gameObject;
                var approach = actorObject.transform.position - actorObject.transform.forward * 2;
                Physics.SyncTransforms();
                if (!Physics.Raycast(approach + Vector3.up, Vector3.down, out var floor, 3,
                    mask, QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("No floor for the creature approach.");
                Object.DestroyImmediate(spider);
                actorObject.transform.position = floor.point + floor.normal * SurfaceMotor.Height;
                actorObject.name = "Procedural snake and caterpillar - MC00 experiment";
                var actor = actorObject.AddComponent<ProceduralCreatureExperiment>();
                var settings = new SerializedObject(actor);
                settings.FindProperty("observerCamera").objectReferenceValue = camera;
                settings.FindProperty("previewPlayer").objectReferenceValue = player;
                settings.FindProperty("surfaceMask").intValue = mask;
                settings.ApplyModifiedPropertiesWithoutUndo();
                foreach (var light in ((Camera)camera).GetComponentsInChildren<Light>()) light.intensity = 50;
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            Selection.activeGameObject = Object.FindAnyObjectByType<ProceduralCreatureExperiment>().gameObject;
        }

        [MenuItem("Tools/Umbrella Spider/8 - Probe both chain creatures")]
        public static void Probe()
        {
            OpenLab(); Arm(false);
        }

        [MenuItem("Tools/Umbrella Spider/9 - Open liquid snake and caterpillar lab")]
        public static void OpenLiquidLab()
        {
            OpenLab();
            bool created = !File.Exists(LiquidScenePath);
            if (created && !AssetDatabase.CopyAsset(ScenePath, LiquidScenePath))
                throw new InvalidOperationException("Cannot create liquid creature scene.");
            var scene = EditorSceneManager.OpenScene(LiquidScenePath);
            if (created)
            {
                var actor = Object.FindAnyObjectByType<ProceduralCreatureExperiment>();
                var settings = new SerializedObject(actor);
                settings.FindProperty("appearance").enumValueIndex = 1;
                settings.FindProperty("snake.speed").floatValue = 5;
                settings.FindProperty("snake.turnSpeed").floatValue = 275;
                settings.FindProperty("snake.waveFrequency").floatValue = 2;
                settings.FindProperty("caterpillar.speed").floatValue = 2.4f;
                settings.FindProperty("caterpillar.turnSpeed").floatValue = 190;
                settings.FindProperty("caterpillar.waveFrequency").floatValue = 2.2f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                EnsureClearApproach(actor);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            Selection.activeGameObject = Object.FindAnyObjectByType<ProceduralCreatureExperiment>().gameObject;
        }

        [MenuItem("Tools/Umbrella Spider/10 - Probe liquid chain creatures")]
        public static void ProbeLiquid() { OpenLiquidLab(); Arm(true); }

        /// <summary>Long bodies need more than a clear head spawn. Keep the complete rest
        /// centreline and both proleg lanes outside the MC00 furniture.</summary>
        public static void EnsureClearApproach(ProceduralCreatureExperiment actor)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Set the spawn in Edit Mode.");
            var settings = new SerializedObject(actor);
            float length = Mathf.Max(settings.FindProperty("snake.length").floatValue, settings.FindProperty("caterpillar.length").floatValue) + .1f;
            float radius = Mathf.Max(settings.FindProperty("snake.radius").floatValue, settings.FindProperty("caterpillar.radius").floatValue);
            float reach = settings.FindProperty("caterpillar.legReach").floatValue + radius * .65f;
            int mask = settings.FindProperty("surfaceMask").intValue;
            Physics.SyncTransforms();
            foreach (float offset in new[] { 0f, -1, 1, -2, 2, -3, 3 })
            {
                Vector3 candidate = actor.transform.position + actor.transform.right * offset;
                if (!Physics.Raycast(candidate + Vector3.up, Vector3.down, out var floor, 3, mask, QueryTriggerInteraction.Ignore) || floor.normal.y < .85f) continue;
                if (Physics.CheckSphere(floor.point + floor.normal * SurfaceMotor.Height, SurfaceMotor.BodyRadius, mask, QueryTriggerInteraction.Ignore)) continue;
                bool blocked = Physics.SphereCast(floor.point + floor.normal * (radius + .02f), radius * .74f,
                    -actor.transform.forward, out _, length, mask, QueryTriggerInteraction.Ignore);
                for (int side = -1; side <= 1 && !blocked; side += 2)
                    blocked = Physics.SphereCast(floor.point + actor.transform.right * side * reach + floor.normal * .08f,
                        .045f, -actor.transform.forward, out _, length, mask, QueryTriggerInteraction.Ignore);
                for (int i = 1; i <= 8 && !blocked; i++)
                {
                    Vector3 tail = floor.point - actor.transform.forward * length * i / 8;
                    blocked = !Physics.Raycast(tail + floor.normal, -floor.normal, out var support, 1.4f, mask, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(Vector3.Dot(support.point - floor.point, floor.normal)) > .15f;
                }
                if (blocked) continue;
                actor.transform.position = floor.point + floor.normal * SurfaceMotor.Height;
                return;
            }
            throw new InvalidOperationException("MC00 approach cannot fit both full bodies and prolegs; choose another open floor.");
        }

        private static void Arm(bool liquid)
        {
            Directory.CreateDirectory(Output); SessionState.SetBool("CreatureLiquidCapture", liquid);
            s_Report = new Report { capturedAt = DateTime.UtcNow.ToString("O") };
            s_Report.scene = liquid ? LiquidScenePath : ScenePath;
            s_Actor = null; s_Started = s_Captured = false; s_Case = 0;
            SessionState.SetBool(Armed, true);
            EditorApplication.update += CaptureUpdate;
            Application.logMessageReceived += OnLog;
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception) && s_Report.errors.Count < 6)
                s_Report.errors.Add(message);
        }

        private static void CaptureUpdate()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (!s_Started)
            {
                s_Actor = Object.FindAnyObjectByType<ProceduralCreatureExperiment>();
                if (!s_Actor || s_Actor.Motor == null) return;
                var settings = new SerializedObject(s_Actor);
                settings.FindProperty("automatic").boolValue = !LiquidCapture; settings.ApplyModifiedPropertiesWithoutUndo();
                BeginCase(); s_Started = true;
            }
            float elapsed = Time.time - s_Start;
            if (elapsed <= 0) return;
            var result = s_Report.cases[s_Case]; result.frames++;
            result.surfaceTransitions = s_Actor.Motor.Transitions;
            result.travel = s_Actor.Motor.Travel;
            result.finalSpeed = s_Actor.Motor.Velocity.magnitude;
            result.maximumBend = Mathf.Max(result.maximumBend, s_Actor.Chain.MaximumBend);
            result.maximumUnsupportedBody = Mathf.Max(result.maximumUnsupportedBody, s_Actor.Chain.UnsupportedRings);
            result.maximumFootSlip = Mathf.Max(result.maximumFootSlip, s_Actor.Presentation.MaximumAnchorSlip);
            result.plantedFeet = Mathf.Max(result.plantedFeet, s_Actor.Presentation.PlantedFeet);
            result.maximumWaveMotion = Mathf.Max(result.maximumWaveMotion, s_Actor.Chain.Drive);
            result.escapes = s_Actor.Motor.Escapes;
            result.maximumBodyBlocks = Mathf.Max(result.maximumBodyBlocks, s_Actor.Chain.BodySweepsBlocked);
            result.maximumSelfContacts = Mathf.Max(result.maximumSelfContacts, s_Actor.Chain.SelfSeparations);
            foreach (var centre in s_Actor.Chain.Centres)
                if (!float.IsFinite(centre.x) || !float.IsFinite(centre.y) || !float.IsFinite(centre.z)) result.nonFinite++;
            if (LiquidCapture && s_Actor.Fluid != null)
            {
                if (s_FluidPhase == 0 && elapsed > .7f)
                {
                    result.attached = s_Actor.Fluid.SampleParticles(); result.particles = s_Actor.Fluid.ParticleCount;
                    result.filaments = s_Actor.Fluid.FilamentCount; CapturePng(result.species + "-LiquidAttached.png");
                    var before = s_Actor.Motor.Current;
                    s_Actor.SetAppearance(ProceduralCreatureExperiment.Appearance.Solid); CapturePng(result.species + "-SolidSamePose.png");
                    s_Actor.SetAppearance(ProceduralCreatureExperiment.Appearance.Fluid);
                    result.samePoseMatched = before.Position == s_Actor.Motor.Current.Position && before.Rotation == s_Actor.Motor.Current.Rotation;
                    s_Actor.FluidSettings.attraction = false; s_FluidPhase = 1;
                }
                else if (s_FluidPhase == 1 && elapsed > 1.5f)
                { result.released = s_Actor.Fluid.SampleParticles(); CapturePng(result.species + "-LiquidReleased.png"); s_Actor.FluidSettings.attraction = true; s_FluidPhase = 2; }
                else if (s_FluidPhase == 2 && elapsed > 2.7f)
                {
                    result.reformed = s_Actor.Fluid.SampleParticles(); CapturePng(result.species + "-LiquidReformed.png");
                    var settings = new SerializedObject(s_Actor); settings.FindProperty("automatic").boolValue = true;
                    settings.ApplyModifiedPropertiesWithoutUndo(); s_FluidPhase = 3;
                }
            }
            if (elapsed > (LiquidCapture ? 3.5f : .45f) && !s_Captured) { CapturePng(result.species + (LiquidCapture ? "-LiquidMoving.png" : "-Moving.png")); s_Captured = true; }
            if (elapsed >= s_NextFrame && s_Frames < 80)
            {
                Render(); var data = s_Image.GetRawTextureData<byte>().ToArray(); s_Stream.Write(data, 0, data.Length);
                s_NextFrame += .05f; s_Frames++;
            }
            if (elapsed < (LiquidCapture ? 6.8f : 5)) return;
            if (LiquidCapture)
            {
                result.moving = s_Actor.Fluid.SampleParticles();
                result.fluidPassed = result.attached.nonFinite + result.released.nonFinite + result.reformed.nonFinite + result.moving.nonFinite == 0 &&
                    result.released.meanGuideDistance > result.attached.meanGuideDistance + .025f && result.reformed.meanGuideDistance < result.released.meanGuideDistance;
            }
            CapturePng(result.species + "-SurfaceTransition.png"); s_Stream.Dispose(); s_Stream = null;
            if (++s_Case < 2) { BeginCase(); return; }
            File.WriteAllText(Output + (LiquidCapture ? "/fluid-report.json" : "/report.json"), JsonUtility.ToJson(s_Report, true));
            SessionState.SetBool(Armed, false);
            EditorApplication.update -= CaptureUpdate; Application.logMessageReceived -= OnLog;
            s_Target.Release(); Object.DestroyImmediate(s_Target); Object.DestroyImmediate(s_Image);
            EditorApplication.ExitPlaymode();
        }

        private static void BeginCase()
        {
            s_Actor.SetSpecies((ProceduralCreatureExperiment.Species)s_Case); s_Actor.ResetExperiment();
            s_Actor.SetAppearance(LiquidCapture ? ProceduralCreatureExperiment.Appearance.Fluid : ProceduralCreatureExperiment.Appearance.Solid);
            s_Actor.FluidSettings.attraction = true; s_FluidPhase = 0;
            var settings = new SerializedObject(s_Actor); settings.FindProperty("automatic").boolValue = !LiquidCapture;
            settings.ApplyModifiedPropertiesWithoutUndo();
            s_Start = Time.time; s_NextFrame = 0; s_Frames = 0; s_Captured = false;
            string species = s_Actor.CurrentSpecies.ToString();
            s_Report.cases.Add(new Result { species = species });
            if (!s_Target)
            {
                s_Target = new RenderTexture(640, 360, 24); s_Target.Create();
                s_Image = new Texture2D(640, 360, TextureFormat.RGB24, false);
            }
            s_Stream = new FileStream(Output + "/" + species + (LiquidCapture ? "-Liquid" : "") + ".rgb", FileMode.Create);
        }

        private static void Render()
        {
            var camera = Camera.main;
            var previous = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = s_Target; camera.Render(); RenderTexture.active = s_Target;
                s_Image.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); s_Image.Apply(false);
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; }
        }

        private static void CapturePng(string file) { Render(); File.WriteAllBytes(Output + "/" + file, s_Image.EncodeToPNG()); }
    }
}
