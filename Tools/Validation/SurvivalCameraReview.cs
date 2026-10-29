using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using BodyPart = Unity.MP_FPS.BodyPart;

// Run in the existing Editor's MainMenu Play mode. No connection, profile or saved scene is changed.
public static class SurvivalCameraReview
{
    [Serializable] public sealed class ReviewRow
    { public string kind, item; public float contactMetres, overlayPitch; }
    [Serializable] public sealed class ReviewData
    { public string result, rig, scope; public int checks; public bool selectedCharacterUnchanged; public List<ReviewRow> results; }
    private const string Output = "LocalData/Actions/CameraUse20261029";
    private static int checks;
    private static void Require(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private static async Task Frame(Camera camera, Action sample = null)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ScriptableRenderContext, Camera> handler = null;
        handler = (_, rendered) =>
        {
            if (rendered != camera) return;
            RenderPipelineManager.endCameraRendering -= handler;
            try { sample?.Invoke(); done.TrySetResult(true); }
            catch (Exception error) { done.TrySetException(error); }
        };
        RenderPipelineManager.endCameraRendering += handler;
        try
        {
            if (await Task.WhenAny(done.Task, Task.Delay(8000)) != done.Task)
                throw new TimeoutException("The fixture camera did not render.");
            await done.Task;
        }
        finally { RenderPipelineManager.endCameraRendering -= handler; }
    }

    private static void Authority()
    {
        foreach (string code in new[] { "water", "ration", "medkit" })
        {
            var graph = InventoryGraph.Create(false);
            graph.AddSupply(code, 2, code == "medkit" ? "pockets" : graph.Equipped("Backpack").Id);
            var inventory = new RaidInventoryState { Graph = graph };
            var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
            health.Energy = health.Hydration = 40;
            RaidHealth.Set(ref health, BodyPart.LeftArm, 30);
            string id = graph.Items.First(i => i.Code == code).Id;
            InventoryError Start(double now) => code == "medkit"
                ? inventory.BeginMedical(health, id, graph.Version, BodyPart.LeftArm, float3.zero, now)
                : inventory.BeginConsumable(health, id, graph.Version, float3.zero, now);
            Require(Start(10) == InventoryError.None, code + " could not begin");
            double deadline = inventory.MedicalUntil;
            inventory.RefreshMedical(ref health, true, float3.zero, deadline - .001);
            Require(graph.Count(code, true) == 2 && health.Hydration == 40 && health.Energy == 40 && health.LeftArmHealth == 30,
                code + " applied before its authoritative deadline");
            inventory.RefreshMedical(ref health, true, float3.zero, deadline);
            inventory.RefreshMedical(ref health, true, float3.zero, deadline + 1);
            Require(graph.Count(code, true) == 1, code + " did not commit exactly once");
            Require(code == "water" ? health.Hydration > 40 : code == "ration" ? health.Energy > 40 : health.LeftArmHealth > 30,
                code + " did not apply its real effect");
            id = graph.Items.First(i => i.Code == code).Id;
            health.Energy = health.Hydration = 40; RaidHealth.Set(ref health, BodyPart.LeftArm, 30);
            foreach (string interrupt in new[] { "damage", "weapon", "death", "cancel" })
            {
                Require(Start(30) == InventoryError.None, code + " restart failed: " + interrupt);
                if (interrupt == "damage") health.NonPhysiologicalDamageSequence++;
                if (interrupt == "weapon") health.EquippedWeaponID++;
                if (interrupt == "cancel") inventory.CancelActions();
                inventory.RefreshMedical(ref health, interrupt != "death", float3.zero, 30.2);
                Require(inventory.MedicalItemId == null && graph.Count(code, true) == 1,
                    code + " consumed on interruption: " + interrupt);
            }
        }
    }

    public static async Task<object> Run(bool injectionOnly = false)
    {
        checks = 0;
        Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().path == "Assets/Scenes/MainMenu.unity",
            "Use the single existing Editor, in MainMenu Play mode.");
        Require(!EditorUtility.scriptCompilationFailed, "Unity has script compilation errors.");
        if (!injectionOnly) Authority();
        int selected = GameSettings.NormalizeCharacterIndex(PlayerPrefs.GetInt("Moonkov.PlayerCharacter", 2));
        string rig = selected == 3 ? "HatsuneMiku" : selected == 4 ? "Shian" : "DollSinger";
        Directory.CreateDirectory(Output);
        bool background = Application.runInBackground;
        Application.runInBackground = true;
        var scene = SceneManager.CreateScene("Survival camera review (temporary)");
        GameObject root = null;
        RaidActionPresentation action = null;
        var results = new List<ReviewRow>();
        var target = new RenderTexture(960, 540, 24);
        var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        Transform[] poseNodes = null; Vector3[] posePositions = null; Quaternion[] poseRotations = null;
        void SavePose()
        {
            poseNodes = root.GetComponentsInChildren<Transform>(true);
            posePositions = poseNodes.Select(t => t.localPosition).ToArray();
            poseRotations = poseNodes.Select(t => t.localRotation).ToArray();
        }
        void RestorePose()
        {
            for (int i = 0; i < poseNodes.Length; i++)
                poseNodes[i].SetLocalPositionAndRotation(posePositions[i], poseRotations[i]);
        }
        void Capture(Camera camera, string file)
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0, false); pixels.Apply();
            File.WriteAllBytes(Output + "/" + file + ".png", pixels.EncodeToPNG());
            RenderTexture.active = previous;
        }
        try
        {
            root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"));
            SceneManager.MoveGameObjectToScene(root, scene); root.transform.position += Vector3.one * 1000;
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
            animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var view = root.GetComponentInChildren<DollSingerView>(true);
            view.gameObject.SetActive(true); view.BindPlayer(animator.gameObject, true); view.SetFirstPerson(true); view.enabled = true;
            typeof(DollSingerView).GetField("viewBlend", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, 1f);
            view.camera.enabled = true; view.camera.scene = scene; view.camera.aspect = 16f / 9;
            view.camera.farClipPlane = 10; view.camera.useOcclusionCulling = false;
            view.camera.backgroundColor = new Color(.11f, .13f, .16f); view.camera.clearFlags = CameraClearFlags.SolidColor;
            if (view.GestureCamera) view.GestureCamera.scene = scene;
            var movement = animator.GetComponent<DollSingerMovement>();
            movement.SetNetworkViewPresentation(true, Quaternion.identity, 0); movement.enabled = true;
            var twist = animator.GetComponent<HumanoidTwistFollowers>(); if (twist) twist.enabled = true;
            var halo = root.GetComponentInChildren<DollSingerHaloAim>(true); if (halo) halo.enabled = true;
            action = animator.gameObject.AddComponent<RaidActionPresentation>(); action.Configure(animator, view, halo, rig);
            foreach (var angles in new[] { new Vector3(35, -25, 0), new Vector3(-25, 150, 0) })
            {
                var lightObject = new GameObject("Review light"); SceneManager.MoveGameObjectToScene(lightObject, scene);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
                light.intensity = angles.x > 0 ? 2 : 1; light.transform.rotation = Quaternion.Euler(angles);
            }

            // One eye sweep against actual temporary geometry, including trigger/self exclusions.
            Vector3 origin = root.transform.position + Vector3.up * 1.6f;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = origin + Vector3.forward * .2f; wall.transform.localScale = new Vector3(2, 2, .05f);
            Physics.SyncTransforms();
            Vector3 Eye() => DollSingerEyeClearance.Constrain(scene.GetPhysicsScene(), origin, origin + Vector3.forward * .35f,
                root.transform, .03f, 75, 16f / 9, ~0);
            Require(Eye().z - origin.z < .13f, "The animated eye crossed a wall.");
            wall.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
            Require(Mathf.Abs(Eye().z - origin.z - .35f) < .001f, "A trigger moved the viewpoint.");
            UnityEngine.Object.Destroy(wall);

            uint sequence = 1;
            foreach (var kind in injectionOnly ? new[] { HandAction.Inject } : new[] { HandAction.Drink, HandAction.Eat, HandAction.Inject })
            {
                string code = kind == HandAction.Drink ? "water" : kind == HandAction.Eat ? "ration" : "medkit";
                float seconds = kind == HandAction.Drink ? RaidNutrition.DrinkUseSeconds : 3f;
                Quaternion look = kind == HandAction.Inject ? Quaternion.Euler(60, 0, 0) : Quaternion.identity;
                view.SetNetworkLookRotation(look); movement.SetNetworkViewPresentation(true, look, 0);
                var state = new PredictedPlayerGhost { CurrentHealth = 100, HandAction = (byte)kind,
                    HandActionSequence = sequence++, HandActionItem = code, HandActionDuration = seconds, HandActionRemaining = seconds };
                float start = Time.time; bool captured = false;
                float error = 0, pitch = 0;
                while (Time.time - start < seconds)
                {
                    state.HandActionRemaining = Mathf.Max(.001f, seconds - (Time.time - start)); action.Present(state, true, true, false);
                    bool ready = false;
                    await Frame(view.camera, () =>
                    {
                        if (!captured && action.SampledPhase >= .5f)
                        { captured = ready = true; error = action.ContactError; SavePose(); }
                    });
                    if (ready)
                    {
                        RestorePose();
                        Capture(view.camera, rig + "-" + kind);
                        if (kind == HandAction.Inject)
                        {
                            var rotation = view.GestureCamera.transform.localRotation;
                            pitch = Vector3.SignedAngle(Vector3.forward, view.GestureCamera.transform.forward, Vector3.right);
                            Require(Mathf.Abs(pitch - view.ActionInjectionPitch) < .1f, "Injection pitch was added to player pitch.");
                            Require(Quaternion.Angle(view.camera.transform.rotation, look) < .1f, "Action turned the world camera.");
                            view.GestureCamera.transform.localRotation = Quaternion.Euler(view.ActionInjectionPitch, 0, 0);
                            Capture(view.camera, rig + "-Inject-LegacyPitch");
                            view.GestureCamera.transform.localRotation = rotation;
                        }
                    }
                }
                Require(captured, "The use never reached contact: " + kind);
                Require(error < .04f, "Authored contact changed: " + kind + " distance=" + error);
                state.HandAction = 0; state.HandActionRemaining = 0;
                action.Present(state, true, true, false);
                await Frame(view.camera);
                Require(action.OwnsHands, "Completed action skipped authored put-away: " + kind);
                float deadline = Time.time + .8f;
                while (action.OwnsHands && Time.time < deadline) await Frame(view.camera);
                await Frame(view.camera, () => Require(!action.OwnsHands && !view.GestureHandsActive, "Put-away left a pose behind: " + kind));
                results.Add(new ReviewRow { kind = kind.ToString(), item = code, contactMetres = error, overlayPitch = pitch });
            }

            // Verify the ordinary world-depth fallback with a real intervening wall.
            view.SetNetworkLookRotation(Quaternion.identity); movement.SetNetworkViewPresentation(true, Quaternion.identity, 0);
            var cover = new PredictedPlayerGhost { CurrentHealth = 100, HandAction = (byte)HandAction.Drink,
                HandActionSequence = sequence++, HandActionItem = "water", HandActionDuration = 2, HandActionRemaining = 1 };
            action.Present(cover, true, true, false); action.SamplePreview(.5f);
            Vector3 hand = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            Vector3 eye = view.camera.transform.position;
            var coverWall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(coverWall, scene);
            coverWall.transform.SetPositionAndRotation(Vector3.Lerp(eye, hand, .5f), Quaternion.LookRotation(hand - eye));
            coverWall.transform.localScale = new Vector3(2, 2, .015f); Physics.SyncTransforms();
            await Frame(view.camera);
            await Frame(view.camera, () =>
            {
                Require(action.WorldDepthFallbackActive && !view.GestureHandsActive, "An intervening wall did not restore world depth");
                SavePose();
            });
            RestorePose(); Capture(view.camera, rig + "-WallDepth");
            UnityEngine.Object.Destroy(coverWall);
            await Frame(view.camera);
            await Frame(view.camera, () => Require(!action.WorldDepthFallbackActive && view.GestureHandsActive, "Removing cover did not restore the action view"));
            action.ResetPresentation();

            if (!injectionOnly)
            {
            view.SetNetworkLookRotation(Quaternion.identity); movement.SetNetworkViewPresentation(true, Quaternion.identity, 0);
            var cancel = new PredictedPlayerGhost { CurrentHealth = 100, HandAction = (byte)HandAction.Drink,
                HandActionSequence = sequence++, HandActionItem = "water", HandActionDuration = 2, HandActionRemaining = 1 };
            action.Present(cancel, true, true, false);
            float waitUntil = Time.time + .16f; while (Time.time < waitUntil) await Frame(view.camera);
            cancel.HandAction = 0; action.Present(cancel, true, true, false);
            await Frame(view.camera, () =>
            {
                Require(action.OwnsHands && animator.GetComponentsInChildren<RaidActionProp>().Any(p => p.name.Contains("water")),
                    "Interrupted item disappeared before lowering"); SavePose();
            });
            RestorePose();
            Capture(view.camera, rig + "-Drink-Cancel");
            waitUntil = Time.time + .3f; while (Time.time < waitUntil) await Frame(view.camera);
            await Frame(view.camera, () => Require(!action.OwnsHands && !view.GestureHandsActive, "Cancel retained owner hands"));
            foreach (bool suppressed in new[] { true, false })
            {
                cancel.CurrentHealth = 100; cancel.HandAction = (byte)HandAction.Drink; cancel.HandActionSequence = sequence++;
                action.Present(cancel, true, true, false); action.SamplePreview(.5f);
                cancel.CurrentHealth = suppressed ? 100 : 0;
                action.Present(cancel, true, true, suppressed);
                Require(!action.OwnsHands && !view.GestureHandsActive, suppressed ? "Menu retained hands" : "Death retained hands");
            }
            }
            var report = new ReviewData { result = "PASS", checks = checks, rig = rig, selectedCharacterUnchanged = PlayerPrefs.GetInt("Moonkov.PlayerCharacter", 2) == selected,
                scope = "Single MainMenu Editor fixture; real Animator/model/action props and server inventory methods; no raid, networking or map save", results = results };
            File.WriteAllText(Output + (injectionOnly ? "/ReviewInjection.json" : "/Review.json"), JsonUtility.ToJson(report, true));
            return report;
        }
        finally
        {
            if (action) action.DisposePresentation();
            if (root) UnityEngine.Object.Destroy(root);
            RenderTexture.active = previous; target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
            await SceneManager.UnloadSceneAsync(scene);
            Application.runInBackground = background;
        }
    }
}
