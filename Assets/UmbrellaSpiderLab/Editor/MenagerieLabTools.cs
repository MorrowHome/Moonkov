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
    /// <summary>Independent scene creation and one bounded five-case runtime probe.
    /// SessionState bridges Play domain reload. Captures are outside Assets and never
    /// modify source scenes; early Play exit tears down callbacks and render targets.</summary>
    [InitializeOnLoad]
    public static class MenagerieLabTools
    {
        public const string ScenePath = "Assets/UmbrellaSpiderLab/Scenes/MC00LiquidMenagerieLab.unity";
        public const string Output = "Library/BuildValidation/Menagerie";
        private const string Armed = "MenagerieLabProbe";
        private static ProceduralMenagerieExperiment s_Actor;
        private static Report s_Report;
        private static int s_Case, s_Phase;
        private static float s_Start, s_NextFrame;
        private static RenderTexture s_Target;
        private static Texture2D s_Image;
        private static FileStream s_Video;
        private static bool s_Started, s_Action;
        [Serializable] public sealed class Result
        {
            public string species;
            public int frames, nonFiniteGuide, maximumGrips, escapes, transitions, bones, particles, filaments;
            public float travel, maximumSlip, maximumExtension, motion, maximumBend;
            public int maximumBodyBlocks;
            public bool samePoseRollback, fluidPassed, folded, reattached;
            public Vector3 initialProbe, previousProbe;
            public SkeletonFluidPresentation.Sample attached, released, reformed, moving;
        }
        [Serializable] public sealed class Report
        {
            public string scene = ScenePath, capturedAt;
            public List<Result> cases = new List<Result>();
            public List<string> errors = new List<string>();
        }
        static MenagerieLabTools()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
            if (SessionState.GetBool(Armed, false)) Hook();
        }

        [MenuItem("Tools/Umbrella Spider/11 - Open five liquid creatures")]
        public static void OpenLab()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits before opening the menagerie.");
            bool created = !File.Exists(ScenePath);
            if (created && !AssetDatabase.CopyAsset(CreatureLabTools.LiquidScenePath, ScenePath))
                throw new InvalidOperationException("Cannot copy the accepted liquid creature lab.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (created)
            {
                var source = Object.FindAnyObjectByType<ProceduralCreatureExperiment>();
                if (!source) throw new InvalidOperationException("Source lab controller is missing.");
                var original = new SerializedObject(source);
                var camera = original.FindProperty("observerCamera").objectReferenceValue;
                var player = original.FindProperty("previewPlayer").objectReferenceValue;
                int mask = original.FindProperty("surfaceMask").intValue;
                var actorObject = source.gameObject; Object.DestroyImmediate(source);
                actorObject.name = "Five liquid creatures / independent MC00 lab";
                var actor = actorObject.AddComponent<ProceduralMenagerieExperiment>();
                var settings = new SerializedObject(actor);
                settings.FindProperty("observerCamera").objectReferenceValue = camera;
                settings.FindProperty("previewPlayer").objectReferenceValue = player;
                settings.FindProperty("surfaceMask").intValue = mask;
                settings.ApplyModifiedPropertiesWithoutUndo();
                EnsureClearSpawn(actor, mask);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            Selection.activeGameObject = Object.FindAnyObjectByType<ProceduralMenagerieExperiment>().gameObject;
        }

        private static void EnsureClearSpawn(ProceduralMenagerieExperiment actor, int mask)
        {
            Physics.SyncTransforms(); Vector3 origin = actor.transform.position;
            // Reserve both the 4.4 m chain behind the controller and a radial footprint.
            // Search the nearby real MC00 floor; do not teleport to a separate test plane.
            for (int radius = 0; radius <= 12; radius += 2) for (int direction = 0; direction < (radius == 0 ? 1 : 12); direction++)
            {
                float angle = direction * Mathf.PI / 6;
                Vector3 candidate = origin + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
                if (!Physics.Raycast(candidate + Vector3.up * 2, Vector3.down, out var floor, 5, mask, QueryTriggerInteraction.Ignore) || floor.normal.y < .9f) continue;
                if (Physics.CheckSphere(floor.point + Vector3.up * .60f, .4f, mask, QueryTriggerInteraction.Ignore)) continue;
                bool blocked = Physics.SphereCast(floor.point + Vector3.up * .25f, .15f, -actor.transform.forward,
                    out _, 4.5f, mask, QueryTriggerInteraction.Ignore);
                for (int arm = 0; arm < 12 && !blocked; arm++)
                {
                    Vector3 radial = new Vector3(Mathf.Sin(arm * Mathf.PI / 6), 0, Mathf.Cos(arm * Mathf.PI / 6));
                    blocked = Physics.SphereCast(floor.point + Vector3.up * .18f, .05f, radial, out _, 2.5f, mask, QueryTriggerInteraction.Ignore) ||
                        !Physics.Raycast(floor.point + radial * 2.5f + Vector3.up, Vector3.down, out _, 1.4f, mask, QueryTriggerInteraction.Ignore);
                }
                if (blocked) continue;
                actor.transform.position = floor.point + floor.normal * SurfaceMotor.Height;
                return;
            }
            throw new InvalidOperationException("No nearby MC00 floor fits the complete five-creature footprint.");
        }

        [MenuItem("Tools/Umbrella Spider/12 - Probe five liquid creatures")]
        public static void Probe()
        {
            Arm(0, 5);
        }
        public static void ProbeChains() { Arm(1, 3); }
        public static void ProbeCentipede() { Arm(1, 2); }
        private static void Arm(int first, int end)
        {
            OpenLab(); Directory.CreateDirectory(Output);
            s_Report = new Report { capturedAt = DateTime.UtcNow.ToString("O") };
            s_Actor = null; s_Case = first; s_Started = false;
            SessionState.SetInt("MenagerieFirstCase", first); SessionState.SetInt("MenagerieEndCase", end);
            SessionState.SetBool(Armed, true); Hook(); EditorApplication.EnterPlaymode();
        }
        private static void Hook()
        {
            EditorApplication.update -= Update; EditorApplication.update += Update;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (s_Report != null && (type == LogType.Exception || type == LogType.Error) && s_Report.errors.Count < 8)
                s_Report.errors.Add(message);
        }
        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Armed, false))
                Finish(false);
        }
        private static void BeginCase()
        {
            s_Actor.Automatic = false; s_Actor.SetSpecies((ProceduralMenagerieExperiment.Species)s_Case);
            s_Actor.ResetExperiment(); s_Actor.FluidSettings.attraction = true;
            s_Actor.SetAppearance(ProceduralCreatureExperiment.Appearance.Fluid);
            s_Phase = 0; s_Action = false; s_Start = Time.time; s_NextFrame = 2.7f;
            Vector3 probe = s_Actor.IsChain ? s_Actor.Chain.Centres[70] : s_Actor.Branch.Points[BranchCreatureRig.Index(0, 12)];
            s_Report.cases.Add(new Result { species = s_Actor.CurrentSpecies.ToString(), initialProbe = probe, previousProbe = probe });
            s_Video?.Dispose(); s_Video = new FileStream(Output + "/" + s_Actor.CurrentSpecies + ".rgb", FileMode.Create);
        }
        private static void Update()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (!s_Started)
            {
                s_Actor = Object.FindAnyObjectByType<ProceduralMenagerieExperiment>();
                if (!s_Actor || s_Actor.Motor == null) return;
                s_Report ??= new Report { capturedAt = DateTime.UtcNow.ToString("O") }; s_Case = SessionState.GetInt("MenagerieFirstCase", 0);
                s_Target = new RenderTexture(960, 540, 24); s_Target.Create();
                s_Image = new Texture2D(960, 540, TextureFormat.RGB24, false);
                BeginCase(); s_Started = true;
            }
            float elapsed = Time.time - s_Start; var result = s_Report.cases[s_Case - SessionState.GetInt("MenagerieFirstCase", 0)]; result.frames++;
            if (s_Actor.Fluid == null) return;
            var points = s_Actor.IsChain ? s_Actor.Chain.Centres : s_Actor.Branch.Points;
            foreach (var point in points) if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z)) result.nonFiniteGuide++;
            result.maximumGrips = Mathf.Max(result.maximumGrips, s_Actor.IsChain ? s_Actor.ChainBody.PlantedFeet : s_Actor.Branch.Grips);
            result.maximumSlip = Mathf.Max(result.maximumSlip, s_Actor.IsChain ? s_Actor.ChainBody.MaximumAnchorSlip : s_Actor.Branch.MaximumAnchorSlip);
            if (!s_Actor.IsChain) result.maximumExtension = Mathf.Max(result.maximumExtension, s_Actor.Branch.MaximumExtension);
            else
            {
                result.maximumBend = Mathf.Max(result.maximumBend, s_Actor.Chain.MaximumBend);
                result.maximumBodyBlocks = Mathf.Max(result.maximumBodyBlocks, s_Actor.Chain.BodySweepsBlocked);
            }
            Vector3 probe = s_Actor.IsChain ? points[70] : points[BranchCreatureRig.Index(0, 12)];
            result.motion += Vector3.Distance(probe, result.previousProbe); result.previousProbe = probe;
            if (s_Phase == 0 && elapsed > .65f)
            {
                result.attached = s_Actor.Fluid.SampleParticles(); result.particles = s_Actor.Fluid.ParticleCount;
                result.filaments = s_Actor.Fluid.FilamentCount; result.bones = points.Length;
                Capture(result.species + "-Attached.png"); var pose = s_Actor.Motor.Current;
                s_Actor.SetAppearance(ProceduralCreatureExperiment.Appearance.Solid); Capture(result.species + "-Solid.png");
                s_Actor.SetAppearance(ProceduralCreatureExperiment.Appearance.Fluid);
                result.samePoseRollback = pose.Position == s_Actor.Motor.Current.Position && pose.Rotation == s_Actor.Motor.Current.Rotation;
                s_Actor.FluidSettings.attraction = false; s_Phase++;
            }
            else if (s_Phase == 1 && elapsed > 1.45f)
            { result.released = s_Actor.Fluid.SampleParticles(); s_Actor.FluidSettings.attraction = true; s_Phase++; }
            else if (s_Phase == 2 && elapsed > 2.6f)
            { result.reformed = s_Actor.Fluid.SampleParticles(); s_Actor.Automatic = true; s_Phase++; }
            if (elapsed > 4 && !s_Action) { s_Actor.Act(); s_Action = true; }
            if (s_Phase == 3 && elapsed > 5.5f)
            {
                result.moving = s_Actor.Fluid.SampleParticles(); Capture(result.species + "-Moving.png");
                s_Actor.SetAppearance(ProceduralCreatureExperiment.Appearance.Solid); Capture(result.species + "-SolidMoving.png");
                s_Actor.SetAppearance(ProceduralCreatureExperiment.Appearance.Fluid);
                result.travel = s_Actor.Motor.Travel; result.escapes = s_Actor.Motor.Escapes; result.transitions = s_Actor.Motor.Transitions;
                result.fluidPassed = result.attached.nonFinite + result.released.nonFinite + result.reformed.nonFinite + result.moving.nonFinite == 0 &&
                    result.released.meanGuideDistance > result.attached.meanGuideDistance + .02f && result.reformed.meanGuideDistance < result.released.meanGuideDistance;
                s_Actor.Escape(); result.folded = s_Actor.Motor.Detached; s_Phase++;
            }
            if (elapsed >= s_NextFrame && elapsed < 5.5f)
            {
                Render(); var data = s_Image.GetRawTextureData<byte>().ToArray(); s_Video.Write(data, 0, data.Length);
                s_NextFrame += .10f;
            }
            if (elapsed < 7.5f) return;
            result.reattached = !s_Actor.Motor.Detached && s_Actor.Motor.Supported;
            if (++s_Case < SessionState.GetInt("MenagerieEndCase", 5)) { BeginCase(); return; }
            Finish(true); EditorApplication.ExitPlaymode();
        }
        private static void Render()
        {
            var camera = s_Actor.ObserverCamera; var previous = camera.targetTexture; var active = RenderTexture.active;
            try
            {
                camera.targetTexture = s_Target; camera.Render(); RenderTexture.active = s_Target;
                s_Image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); s_Image.Apply(false);
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; }
        }
        private static void Capture(string name) { Render(); File.WriteAllBytes(Output + "/" + name, s_Image.EncodeToPNG()); }
        private static void Finish(bool complete)
        {
            SessionState.SetBool(Armed, false); EditorApplication.update -= Update; Application.logMessageReceived -= OnLog;
            s_Video?.Dispose(); s_Video = null;
            if (s_Report != null && s_Report.cases.Count > 0)
                File.WriteAllText(Output + (complete ? SessionState.GetInt("MenagerieEndCase", 5) == 2 ? "/centipede-report.json" : SessionState.GetInt("MenagerieFirstCase", 0) == 1 ? "/chain-report.json" : "/report.json" : "/interrupted-report.json"), JsonUtility.ToJson(s_Report, true));
            if (s_Target) { s_Target.Release(); Object.DestroyImmediate(s_Target); s_Target = null; }
            if (s_Image) { Object.DestroyImmediate(s_Image); s_Image = null; }
            s_Started = false; s_Actor = null;
        }
    }
}
