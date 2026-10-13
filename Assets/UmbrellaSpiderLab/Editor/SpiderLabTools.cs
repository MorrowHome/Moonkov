using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.SpiderLab.Editor
{
    /// <summary>MC00 integration and bounded evidence capture; no source building/scene regeneration.</summary>
    [InitializeOnLoad]
    public static class SpiderLabTools
    {
        public const string ScenePath = "Assets/UmbrellaSpiderLab/Scenes/MC00SpiderLab.unity";
        public const string LiquidScenePath = "Assets/UmbrellaSpiderLab/Scenes/MC00LiquidSpiderLab.unity";
        private const string SourceScene = "Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Scenes/MC00GameExportPreview.unity";
        private const string Output = "Library/BuildValidation/UmbrellaSpider";
        private static Report s_Report;
        private static float s_LastTime, s_PlayTime;
        private static UmbrellaSpiderExperiment s_Actor;
        private static bool s_Entered;
        private static RenderTexture s_MotionTarget;
        private static Texture2D s_MotionImage;
        private static FileStream s_MotionStream;
        private static byte[] s_MotionBytes;
        private static int s_MotionFrames;
        private static float s_NextSkinCheck;
        private static bool s_ComparisonCaptured;
        private static string ResultPath => SessionState.GetString("SpiderLabReportPath", Output + "/report.json");
        private static bool SkinCapture => s_Report.captureLabel == "Carapace" || s_Report.captureLabel == "Liquid";
        private static bool FluidCapture => s_Report.captureLabel == "Fluid";
        private static string CapturePrefix => SkinCapture || FluidCapture ? "/MC00-" + s_Report.captureLabel : "/MC00-Run";
        private static int s_FluidPhase;

        static SpiderLabTools()
        {
            // Entering Play reloads the domain. SessionState and the saved report survive it;
            // delegate/static fields do not. Re-arm once on the other side of that boundary.
            if (!SessionState.GetBool("SpiderLabCaptureArmed", false)) return;
            s_Report = JsonUtility.FromJson<Report>(File.ReadAllText(ResultPath));
            Application.logMessageReceived += OnLog;
            EditorApplication.update += CaptureUpdate;
        }

        [Serializable] private class Case
        {
            public string name;
            public int fps, transitions, bodyBlocks, unsupportedFrames, slowFrames, maxReachMisses,
                maxRecovery, legObstructionFrames, frames;
            public float minSpeed = float.MaxValue, travel, maxContactError;
            public float maxSpeed;
            public float averageStride, maxStride, maxBodyLean;
            public float maxUnplantedTime;
            public int finalRecovery;
            public string endSurface;
            public Vector3 endPosition, endNormal;
            public bool passed;
            public bool feetPassed;
            public string worstFoot;
            public string appearance, firstSkinObstruction;
            public int skinChecks, skinClipChecks;
            public bool skinPassed, samePoseMatched;
            public float maxTissueLag, maxLiquidEndpointError;
            public SkeletonFluidPresentation.Sample attachedFluid, releasedFluid, reformedFluid, movingFluid;
            public bool fluidPassed;
        }
        [Serializable] private class Report
        {
            public string scene, source, spawnSurface, wallSurface, capturedAt;
            public string captureLabel;
            public Vector3 spawn;
            public int buildingColliders;
            public List<Case> cases = new List<Case>();
            public Case playMode = new Case { name = "MC00 actual Play Mode" };
            public List<string> errors = new List<string>();
        }

        [MenuItem("Tools/Umbrella Spider/1 - Open MC00 experiment")]
        public static void OpenMC00()
        {
            RequireCleanEditMode();
            if (!File.Exists(ScenePath)) CreateMC00();
            else EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Umbrella Spider/2 - Add spider to current MC00 scene")]
        public static void AddToCurrentMC00()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UmbrellaSpiderExperiment>(true)).Any())
                throw new InvalidOperationException("This scene already contains the experiment.");
            Install(scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void CreateMC00()
        {
            Directory.CreateDirectory("Assets/UmbrellaSpiderLab/Scenes");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.CopyAsset(SourceScene, ScenePath)) throw new IOException("Cannot copy MC00 preview.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Install(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static UmbrellaSpiderExperiment Install(Scene scene)
        {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var model = all.Single(t => t.name == "MC00_Render");
            var collision = all.Single(t => t.name == "MC00 Collision Proxies");
            // Existing authored G03 observation point; the model's transform performs project placement.
            // Blender -> Unity conversion matches the existing MC00 switch installation tools.
            Vector3 approach = model.TransformPoint(new Vector3(-54.7f, 2.25f, 53.6f));
            Vector3 direction = Vector3.ProjectOnPlane(model.TransformDirection(Vector3.left), Vector3.up).normalized;
            UnityEngine.Physics.SyncTransforms();
            if (!UnityEngine.Physics.Raycast(approach, Vector3.down, out var floor, 4,
                UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("No real MC00 floor below G03; do not spawn in empty space.");
            Vector3 spawn = floor.point - direction * 1.55f;
            if (!UnityEngine.Physics.Raycast(spawn + Vector3.up * 1.8f, Vector3.down, out floor, 3,
                UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("G03 approach has insufficient floor clearance.");
            spawn = floor.point + floor.normal * SurfaceMotor.Height;
            if (!UnityEngine.Physics.Raycast(spawn, direction, out var wall, 5,
                UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("No real wall on the G03 test heading.");
            if (!floor.collider.transform.IsChildOf(collision) || !wall.collider.transform.IsChildOf(collision))
                throw new InvalidOperationException("The chosen floor/wall must belong to MC00 collision proxies.");
            if (UnityEngine.Physics.CheckSphere(spawn, SurfaceMotor.BodyRadius,
                UnityEngine.Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Spawn intersects a real collider.");

            var actorObject = new GameObject("Umbrella spider - MC00 motion experiment");
            Undo.RegisterCreatedObjectUndo(actorObject, "Add MC00 spider experiment");
            actorObject.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(direction, floor.normal));
            var actor = Undo.AddComponent<UmbrellaSpiderExperiment>(actorObject);
            var cameraObject = new GameObject("Spider observer camera");
            Undo.RegisterCreatedObjectUndo(cameraObject, "Add spider observer camera");
            cameraObject.tag = "MainCamera";
            var camera = Undo.AddComponent<Camera>(cameraObject);
            camera.nearClipPlane = .08f; camera.farClipPlane = 3000; camera.fieldOfView = 58;
            cameraObject.AddComponent<AudioListener>();
            var fill = new GameObject("Spider observer fill");
            Undo.RegisterCreatedObjectUndo(fill, "Add spider observer fill");
            fill.transform.SetParent(cameraObject.transform, false);
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point; fillLight.range = 8; fillLight.intensity = 12;
            fillLight.color = new Color(.82f, .9f, 1); fillLight.shadows = LightShadows.None;
            var player = all.FirstOrDefault(t => t.name == "DollSingerPlayer");
            if (!player) throw new InvalidOperationException("MC00 observer player is missing.");
            var settings = new SerializedObject(actor);
            settings.FindProperty("previewPlayer").objectReferenceValue = player.gameObject;
            settings.FindProperty("observerCamera").objectReferenceValue = camera;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var document = Undo.AddComponent<UIDocument>(actorObject);
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset");
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UmbrellaSpiderLab/UI/SpiderLab.uxml");
            if (!document.panelSettings || !document.visualTreeAsset) throw new InvalidOperationException("Spider debug UI is missing.");
            // Only the experiment copy changes the observer spawn. F7 makes the existing character
            // walkable for inspection, while the actor continues receiving its automatic intent.
            Undo.RecordObject(player, "Place observer beside MC00 spider");
            player.SetPositionAndRotation(floor.point - direction * 3.1f + Vector3.up * .08f,
                Quaternion.LookRotation(direction, Vector3.up));
            PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            Selection.activeGameObject = actorObject;
            s_Report = new Report
            {
                scene = ScenePath, source = SourceScene, spawn = spawn,
                spawnSurface = floor.collider.name, wallSurface = wall.collider.name,
                buildingColliders = collision.GetComponentsInChildren<Collider>().Length,
                capturedAt = DateTime.UtcNow.ToString("O")
            };
            Debug.Log($"Spider installed against real MC00 colliders: {floor.collider.name} -> {wall.collider.name}, at {spawn}.");
            return actor;
        }

        /// <summary>
        /// One batch Editor run: deterministic geometry probes, then actual MC00 Play Mode for 8 s.
        /// Launch WITHOUT -quit; this callback owns completion and exits with a written result.
        /// </summary>
        public static void CreateAndProbe()
        {
            Directory.CreateDirectory(Output);
            try
            {
                RequireCleanEditMode();
                if (File.Exists(ScenePath))
                    throw new InvalidOperationException("Experiment already exists; use ProbeExisting, preserving it.");
                CreateMC00();
                RunProbes();
                SessionState.SetString("SpiderLabReportPath", Output + "/report.json");
                ArmPlayCapture();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        public static void ProbeExisting()
        {
            Directory.CreateDirectory(Output);
            try
            {
                RequireCleanEditMode();
                EditorSceneManager.OpenScene(ScenePath);
                s_Report = JsonUtility.FromJson<Report>(File.ReadAllText(Output + "/report.json"));
                s_Report.playMode = new Case { name = "MC00 actual Play Mode" };
                s_Report.errors.Clear();
                s_Report.captureLabel = "";
                s_Report.capturedAt = DateTime.UtcNow.ToString("O");
                File.WriteAllText(Output + "/report.json", JsonUtility.ToJson(s_Report, true));
                SessionState.SetString("SpiderLabReportPath", Output + "/report.json");
                ArmPlayCapture();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        [MenuItem("Tools/Umbrella Spider/3 - Validate carapace in MC00")]
        public static void ProbeAppearance()
        {
            RequireCleanEditMode();
            EditorSceneManager.OpenScene(ScenePath);
            s_Report = JsonUtility.FromJson<Report>(File.ReadAllText(Output + "/report.json"));
            s_Report.captureLabel = "Carapace";
            s_Report.playMode = new Case { name = "MC00 carapace Play Mode / sampled actual mesh clearance" };
            s_Report.errors.Clear(); s_Report.capturedAt = DateTime.UtcNow.ToString("O");
            SessionState.SetString("SpiderLabReportPath", Output + "/skin-report.json");
            File.WriteAllText(ResultPath, JsonUtility.ToJson(s_Report, true));
            ArmPlayCapture();
        }

        [MenuItem("Tools/Umbrella Spider/4 - Open independent Umbra liquid variant")]
        public static void OpenLiquid()
        {
            RequireCleanEditMode();
            if (!File.Exists(LiquidScenePath))
            {
                if (!AssetDatabase.CopyAsset(ScenePath, LiquidScenePath)) throw new IOException("Cannot copy spider lab.");
                var scene = EditorSceneManager.OpenScene(LiquidScenePath);
                var actor = Object.FindFirstObjectByType<UmbrellaSpiderExperiment>();
                var settings = new SerializedObject(actor);
                settings.FindProperty("appearance").enumValueIndex = (int)SpiderPresentation.Look.Fluid;
                settings.FindProperty("cameraYawOffset").floatValue = 90;
                settings.FindProperty("cameraPitch").floatValue = 25;
                settings.FindProperty("cameraDistance").floatValue = 4.5f;
                settings.ApplyModifiedPropertiesWithoutUndo();
                actor.gameObject.name = "Umbra 08 - independent liquid motion experiment";
                GameObject.Find("Spider observer fill").GetComponent<Light>().intensity = 50;
                EditorSceneManager.SaveScene(scene);
            }
            else EditorSceneManager.OpenScene(LiquidScenePath);
        }

        [MenuItem("Tools/Umbrella Spider/5 - Validate Umbra liquid in MC00")]
        public static void ProbeLiquid()
        {
            OpenLiquid();
            var actor = Object.FindFirstObjectByType<UmbrellaSpiderExperiment>();
            Directory.CreateDirectory(Output);
            s_Report = new Report
            {
                scene = LiquidScenePath, source = "SourceData/Models/Umbra08Liquid/Umbra_08_Editable_Variant.blend",
                captureLabel = "Liquid", spawn = actor.transform.position, capturedAt = DateTime.UtcNow.ToString("O"),
                playMode = new Case { name = "MC00 liquid Play Mode / sampled deformed mesh clearance" }
            };
            SessionState.SetString("SpiderLabReportPath", Output + "/liquid-report.json");
            File.WriteAllText(ResultPath, JsonUtility.ToJson(s_Report, true));
            ArmPlayCapture();
        }

        [MenuItem("Tools/Umbrella Spider/6 - Probe skeleton-attracted particle fluid")]
        public static void ProbeFluid()
        {
            OpenLiquid();
            Directory.CreateDirectory(Output);
            s_Report = new Report
            {
                scene = LiquidScenePath, source = "SourceData/Models/Umbra08Liquid/Umbra_08_Editable_Variant.blend",
                captureLabel = "Fluid", capturedAt = DateTime.UtcNow.ToString("O"),
                playMode = new Case { name = "GPU particle fluid / attraction release, reform and MC00 movement" }
            };
            SessionState.SetString("SpiderLabReportPath", Output + "/fluid-report.json");
            File.WriteAllText(ResultPath, JsonUtility.ToJson(s_Report, true));
            ArmPlayCapture();
        }

        private static void RunProbes()
        {
            var root = new GameObject("Temporary corner probes");
            root.transform.position = new Vector3(-100, -100, -100);
            try
            {
                Box(root.transform, "probe floor", new Vector3(0, -.5f, 0), new Vector3(14, 1, 12));
                Box(root.transform, "probe forward wall", new Vector3(0, 2, 4.5f), new Vector3(14, 5, 1));
                Box(root.transform, "probe ceiling", new Vector3(0, 4.5f, 0), new Vector3(14, 1, 12));
                Box(root.transform, "probe rear wall", new Vector3(0, 2, -4.5f), new Vector3(14, 5, 1));
                Box(root.transform, "probe convex block", new Vector3(25, -1.5f, 0), new Vector3(6, 3, 6));
                UnityEngine.Physics.SyncTransforms();
                foreach (int fps in new[] { 30, 60, 120 })
                {
                    Probe(root.transform.position + new Vector3(0, SurfaceMotor.Height, -2), fps, false);
                    Probe(root.transform.position + new Vector3(25, SurfaceMotor.Height, 0), fps, true);
                }
                ProbeIntentChanges(root.transform.position + new Vector3(0, SurfaceMotor.Height, -2));
            }
            finally { Object.DestroyImmediate(root); UnityEngine.Physics.SyncTransforms(); }
            File.WriteAllText(Output + "/report.json", JsonUtility.ToJson(s_Report, true));
        }

        private static void Box(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            go.AddComponent<BoxCollider>().size = size;
        }

        private static void Probe(Vector3 position, int fps, bool convex)
        {
            var result = new Case { name = convex ? "convex box / continuous forward" : "floor-wall-ceiling / continuous forward", fps = fps };
            var motor = new SurfaceMotor(position, Quaternion.identity, UnityEngine.Physics.DefaultRaycastLayers);
            var feet = new PredictiveFeet(UnityEngine.Physics.DefaultRaycastLayers); feet.Reset(motor.Current);
            for (int frame = 0; frame < fps * 10; frame++)
            {
                Vector3 before = motor.Current.Position;
                motor.Tick(Vector2.up, 3.8f, 0, 1f / fps);
                feet.Tick(motor, Vector2.up, 3.8f, 0, 1f / fps);
                float actualSpeed = Vector3.Distance(before, motor.Current.Position) * fps;
                result.minSpeed = Mathf.Min(result.minSpeed, actualSpeed);
                if (actualSpeed < 3f) result.slowFrames++;
                if (!motor.Supported) result.unsupportedFrames++;
                result.maxReachMisses = Mathf.Max(result.maxReachMisses, feet.UnreachableCount);
                result.maxRecovery = Mathf.Max(result.maxRecovery, feet.RecoveryCount);
            }
            result.transitions = motor.Transitions; result.bodyBlocks = motor.Blocked; result.travel = motor.Travel;
            result.endPosition = motor.Current.Position; result.endNormal = motor.Current.Up;
            result.frames = fps * 10;
            result.passed = result.transitions >= 4 && result.unsupportedFrames == 0 && result.slowFrames == 0 && result.bodyBlocks == 0;
            result.feetPassed = result.maxReachMisses == 0 && result.maxRecovery == 0;
            s_Report.cases.Add(result);
            Debug.Log($"Spider probe {result.name} {fps}fps: corners={result.transitions}, minSpeed={result.minSpeed:F3}, blocks={result.bodyBlocks}, reachMisses={result.maxReachMisses}, passed={result.passed}");
        }

        private static void ProbeIntentChanges(Vector3 position)
        {
            var result = new Case { name = "strafe / yaw / stop / reverse during corner", fps = 60 };
            var motor = new SurfaceMotor(position, Quaternion.identity, UnityEngine.Physics.DefaultRaycastLayers);
            var feet = new PredictiveFeet(UnityEngine.Physics.DefaultRaycastLayers); feet.Reset(motor.Current);
            bool reversedOnArc = false;
            for (int frame = 0; frame < 240; frame++)
            {
                Vector2 input = frame < 20 ? Vector2.right : frame < 90 ? Vector2.up : frame < 110 ? Vector2.zero : Vector2.down;
                if (motor.Transitioning && !reversedOnArc && frame > 35) { reversedOnArc = true; input = Vector2.down; }
                motor.Tick(input, 3.8f, frame < 20 ? 120 : 0, 1f / 60);
                feet.Tick(motor, input, 3.8f, frame < 20 ? 120 : 0, 1f / 60);
                if (!motor.Supported) result.unsupportedFrames++;
                result.maxReachMisses = Mathf.Max(result.maxReachMisses, feet.UnreachableCount);
            }
            result.transitions = motor.Transitions; result.bodyBlocks = motor.Blocked;
            result.frames = 240; result.travel = motor.Travel; result.minSpeed = 0;
            result.endPosition = motor.Current.Position; result.endNormal = motor.Current.Up;
            result.passed = reversedOnArc && result.unsupportedFrames == 0 && result.bodyBlocks == 0;
            s_Report.cases.Add(result);
        }

        private static void ArmPlayCapture()
        {
            s_Entered = false; s_LastTime = s_PlayTime = 0;
            s_NextSkinCheck = 0; s_ComparisonCaptured = false;
            SessionState.SetBool("SpiderLabCaptureArmed", true);
            Application.logMessageReceived += OnLog;
            EditorApplication.update += CaptureUpdate;
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                if (s_Report.errors.Count < 8) s_Report.errors.Add(condition);
        }

        private static void CaptureUpdate()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (!s_Entered)
            {
                s_Actor = Object.FindFirstObjectByType<UmbrellaSpiderExperiment>();
                if (!s_Actor || s_Actor.Motor == null) return;
                if (s_Report.captureLabel == "Liquid")
                {
                    s_Actor.SetAppearance(SpiderPresentation.Look.Liquid, false);
                    CaptureComparison(s_Report.playMode);
                    var settings = new SerializedObject(s_Actor);
                    settings.FindProperty("automatic").boolValue = true;
                    settings.ApplyModifiedPropertiesWithoutUndo(); // Only the Play instance receives capture intent.
                }
                if (FluidCapture) { s_Actor.SetAppearance(SpiderPresentation.Look.Fluid, false); s_FluidPhase = 0; }
                s_Entered = true; s_LastTime = Time.time; s_Report.playMode.minSpeed = float.MaxValue;
            }
            if (Time.time <= s_LastTime) return;
            s_PlayTime += Time.time - s_LastTime; s_LastTime = Time.time;
            CaptureMotionFrame();
            var result = s_Report.playMode;
            if (FluidCapture && s_Actor.Presentation.Fluid != null)
            {
                var fluid = s_Actor.Presentation.Fluid;
                if (s_FluidPhase == 0 && s_PlayTime > .8f)
                {
                    result.attachedFluid = fluid.SampleParticles();
                    CaptureComparison(result); CaptureCameraFile(Output + "/Fluid-Attached.png");
                    fluid.AttractionEnabled = false; s_FluidPhase = 1;
                }
                else if (s_FluidPhase == 1 && s_PlayTime > 1.7f)
                {
                    result.releasedFluid = fluid.SampleParticles(); CaptureCameraFile(Output + "/Fluid-Released.png");
                    fluid.AttractionEnabled = true; s_FluidPhase = 2;
                }
                else if (s_FluidPhase == 2 && s_PlayTime > 2.9f)
                {
                    result.reformedFluid = fluid.SampleParticles(); CaptureCameraFile(Output + "/Fluid-Reformed.png");
                    var settings = new SerializedObject(s_Actor); settings.FindProperty("automatic").boolValue = true;
                    settings.ApplyModifiedPropertiesWithoutUndo(); s_FluidPhase = 3;
                }
            }
            result.appearance = s_Actor.Appearance.ToString();
            if (s_Actor.Presentation.Liquid != null)
            {
                result.maxTissueLag = Mathf.Max(result.maxTissueLag, s_Actor.Presentation.Liquid.MaxTissueLag);
                result.maxLiquidEndpointError = Mathf.Max(result.maxLiquidEndpointError, s_Actor.Presentation.Liquid.MaxEndpointError);
            }
            result.frames++;
            result.minSpeed = Mathf.Min(result.minSpeed, s_Actor.Motor.Velocity.magnitude);
            result.maxSpeed = Mathf.Max(result.maxSpeed, s_Actor.Motor.Velocity.magnitude);
            result.averageStride = s_Actor.Feet.AverageStride;
            result.maxStride = s_Actor.Feet.MaxCompletedStride;
            result.maxBodyLean = Mathf.Max(result.maxBodyLean,
                Vector3.Angle(s_Actor.Motor.Current.Up, s_Actor.Feet.BodyPose.Up));
            if (!s_Actor.Motor.Supported) result.unsupportedFrames++;
            if ((!FluidCapture || s_PlayTime > 3.1f) && s_Actor.Motor.Velocity.magnitude < .3f) result.slowFrames++;
            result.maxReachMisses = Mathf.Max(result.maxReachMisses, s_Actor.Feet.UnreachableCount);
            result.maxRecovery = Mathf.Max(result.maxRecovery, s_Actor.Feet.RecoveryCount);
            for (int i = 0; i < 8; i++) result.maxUnplantedTime = Mathf.Max(result.maxUnplantedTime, s_Actor.Feet[i].UnplantedTime);
            if (s_Actor.Presentation.MaxContactError > result.maxContactError)
            {
                int worst = 0;
                for (int i = 1; i < 8; i++) if (s_Actor.Feet[i].Reach > s_Actor.Feet[worst].Reach) worst = i;
                var foot = s_Actor.Feet[worst];
                result.worstFoot = $"t={s_PlayTime:F2}, leg {worst}, {foot.State}, {foot.PlanningIssue}, reach {foot.Reach:F2}, surface {foot.Surface?.name}, foot {foot.Position}, hip {PredictiveFeet.Hip(s_Actor.Feet.BodyPose, worst)}, up {s_Actor.Motor.Current.Up}";
            }
            result.maxContactError = Mathf.Max(result.maxContactError, s_Actor.Presentation.MaxContactError);
            if (s_Actor.Presentation.LegObstructions > 0) result.legObstructionFrames++;
            if (SkinCapture)
            {
                if (s_Actor.Motor.Transitioning || s_PlayTime >= s_NextSkinCheck)
                {
                    s_NextSkinCheck = s_PlayTime + .1f; result.skinChecks++;
                    if (s_Actor.Presentation.SkinObstructed(out var part))
                    {
                        result.skinClipChecks++;
                        if (string.IsNullOrEmpty(result.firstSkinObstruction))
                            result.firstSkinObstruction = $"t={s_PlayTime:F3}, {part}, motor={s_Actor.Motor.Mode}, up={s_Actor.Motor.Current.Up}";
                    }
                }
                if (!s_ComparisonCaptured && s_PlayTime > (s_Report.captureLabel == "Liquid" ? .05f : 1) && !s_Actor.Motor.Transitioning &&
                    Vector3.Dot(s_Actor.Motor.Current.Up, Vector3.up) > .96f)
                    CaptureComparison(result);
            }
            if (s_PlayTime < 8) return;
            result.transitions = s_Actor.Motor.Transitions; result.bodyBlocks = s_Actor.Motor.Blocked;
            result.travel = s_Actor.Motor.Travel; result.endPosition = s_Actor.Motor.Current.Position;
            result.endNormal = s_Actor.Motor.Current.Up; result.endSurface = s_Actor.Motor.Surface?.name;
            result.passed = result.transitions >= 2 && result.unsupportedFrames == 0 && result.slowFrames == 0 && s_Report.errors.Count == 0;
            result.finalRecovery = s_Actor.Feet.RecoveryCount;
            result.feetPassed = result.maxContactError < .03f && result.maxUnplantedTime < .5f && result.finalRecovery == 0 && result.legObstructionFrames == 0;
            result.skinPassed = result.skinChecks > 0 && result.skinClipChecks == 0;
            if (FluidCapture)
            {
                result.movingFluid = s_Actor.Presentation.Fluid.SampleParticles();
                result.fluidPassed = result.attachedFluid.nonFinite + result.releasedFluid.nonFinite + result.reformedFluid.nonFinite + result.movingFluid.nonFinite == 0 &&
                    result.releasedFluid.meanGuideDistance > result.attachedFluid.meanGuideDistance + .025f &&
                    result.reformedFluid.meanGuideDistance < result.releasedFluid.meanGuideDistance;
            }
            if (SkinCapture && !s_ComparisonCaptured) CaptureComparison(result);
            CaptureCamera();
            FinishMotionCapture();
            File.WriteAllText(ResultPath, JsonUtility.ToJson(s_Report, true));
            EditorApplication.update -= CaptureUpdate;
            Application.logMessageReceived -= OnLog;
            SessionState.SetBool("SpiderLabCaptureArmed", false);
            Debug.Log($"MC00 spider Play capture: {JsonUtility.ToJson(result)}");
            if (Application.isBatchMode) EditorApplication.Exit(s_Report.cases.All(c => c.passed) && result.passed ? 0 : 2);
            else EditorApplication.ExitPlaymode();
        }

        private static void CaptureCamera()
        {
            CaptureCameraFile(Output + CapturePrefix + ".png");
        }

        private static void CaptureComparison(Case result)
        {
            var pose = s_Actor.Feet.BodyPose;
            var feet = new Vector3[8];
            for (int i = 0; i < 8; i++) feet[i] = s_Actor.Feet[i].Position;
            var look = s_Actor.Appearance; bool contacts = s_Actor.ShowContactMarkers;
            try
            {
                s_Actor.SetAppearance(SpiderPresentation.Look.Legacy, false);
                CaptureCameraFile(Output + "/" + s_Report.captureLabel + "-Rollback-Legacy.png");
                s_Actor.SetAppearance(SpiderPresentation.Look.Carapace, false);
                CaptureCameraFile(Output + "/" + s_Report.captureLabel + "-Rollback-Carapace.png");
                if (s_Report.captureLabel == "Liquid" || FluidCapture)
                {
                    s_Actor.SetAppearance(SpiderPresentation.Look.Liquid, false);
                    CaptureCameraFile(Output + "/" + s_Report.captureLabel + "-Rollback-Liquid.png");
                }
                if (FluidCapture)
                {
                    s_Actor.SetAppearance(SpiderPresentation.Look.Fluid, false);
                    CaptureCameraFile(Output + "/Fluid-SamePose.png");
                }
            }
            finally { s_Actor.SetAppearance(look, contacts); }
            result.samePoseMatched = pose.Position == s_Actor.Feet.BodyPose.Position && pose.Rotation == s_Actor.Feet.BodyPose.Rotation;
            for (int i = 0; i < 8; i++) result.samePoseMatched &= feet[i] == s_Actor.Feet[i].Position;
            s_ComparisonCaptured = true;
        }

        private static void CaptureCameraFile(string path)
        {
            var camera = GameObject.Find("Spider observer camera").GetComponent<Camera>();
            var target = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }

        // One compact runtime recording for reviewing the new gait as motion. A single raw stream
        // avoids a directory of screenshots; ffmpeg can turn it into the user-facing clip afterward.
        private static void CaptureMotionFrame()
        {
            int wanted = Mathf.Min(80, Mathf.FloorToInt(s_PlayTime * 20));
            if (wanted <= s_MotionFrames) return;
            if (s_MotionStream == null)
            {
                s_MotionTarget = new RenderTexture(640, 360, 24);
                s_MotionImage = new Texture2D(640, 360, TextureFormat.RGB24, false);
                s_MotionBytes = new byte[640 * 360 * 3];
                s_MotionStream = new FileStream(Output + CapturePrefix + ".rgb", FileMode.Create, FileAccess.Write);
            }
            var camera = GameObject.Find("Spider observer camera").GetComponent<Camera>();
            var previousActive = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = s_MotionTarget; camera.Render(); RenderTexture.active = s_MotionTarget;
                s_MotionImage.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); s_MotionImage.Apply();
                s_MotionImage.GetRawTextureData<byte>().CopyTo(s_MotionBytes);
                while (s_MotionFrames < wanted)
                {
                    s_MotionStream.Write(s_MotionBytes, 0, s_MotionBytes.Length);
                    s_MotionFrames++;
                }
            }
            finally { camera.targetTexture = previousTarget; RenderTexture.active = previousActive; }
        }

        private static void FinishMotionCapture()
        {
            s_MotionStream?.Dispose(); s_MotionStream = null;
            if (s_MotionTarget) { s_MotionTarget.Release(); Object.DestroyImmediate(s_MotionTarget); }
            if (s_MotionImage) Object.DestroyImmediate(s_MotionImage);
            s_MotionBytes = null; s_MotionFrames = 0;
        }

        private static void RequireCleanEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene before opening MC00.");
        }
    }
}
