using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Exercise the production eye, connected body and authored hands in one projection.</summary>
public static class WeaponFramingChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] Rigs = { "DollSinger", "HatsuneMiku", "Shian" };
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    public static void Run() => Debug.Log(RunReport());

    private sealed class Fixture : IDisposable
    {
        public readonly UnityEngine.SceneManagement.Scene Scene;
        public readonly GameObject Root;
        public readonly Animator Animator;
        public readonly DollSingerView View;
        public readonly SkinnedMeshRenderer Body;
        public readonly Dictionary<string, Transform> Bones;
        public readonly DollSingerBakedGestures Gestures;
        public readonly Mesh OwnerMesh, WorldMesh;
        private readonly AnimationClip idle;
        private readonly Quaternion headBind;
        private readonly Transform head;
        public Fixture(string rig)
        {
            Scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/" + rig + "/Prefabs/" + rig + "Player.prefab"), Scene);
                foreach (var b in Root.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                foreach (var c in Root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                Animator = Root.GetComponentInChildren<Animator>(true);
                idle = Animator.runtimeAnimatorController.animationClips.First(c => c.name == "Unarmed_Idle");
                Animator.runtimeAnimatorController = null; Animator.Rebind(); Animator.Update(0);
                Bones = Animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                View = Root.GetComponentInChildren<DollSingerView>(true);
                View.SimplifyFirstPersonMorphs = true; View.RenderFirstPersonShadow = false;
                View.BindPlayer(Root, true); View.SetFirstPerson(true);
                typeof(DollSingerView).GetField("viewBlend", Private).SetValue(View, 1f);
                Body = (SkinnedMeshRenderer)typeof(DollSingerView).GetMethod("FindBodyRenderer", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { Root });
                WorldMesh = Body.sharedMesh;
                OwnerMesh = (Mesh)typeof(DollSingerView).GetField("firstPersonMesh", Private).GetValue(View);
                var halo = Animator.GetComponent<DollSingerHaloAim>();
                Gestures = new DollSingerBakedGestures(Animator, (TextAsset)new SerializedObject(halo).FindProperty("gestureBake").objectReferenceValue);
                head = Animator.GetBoneTransform(HumanBodyBones.Head);
                headBind = Quaternion.Inverse(Root.transform.rotation) * head.rotation;
                View.camera.scene = Scene; View.camera.aspect = 16f / 9;
                View.camera.clearFlags = CameraClearFlags.SolidColor; View.camera.backgroundColor = new Color(.11f, .13f, .16f);
                Pose(0);
            }
            catch { Dispose(); throw; }
        }
        public void Pose(float pitch)
        {
            Gestures.RestoreBeforeAnimation(); idle.SampleAnimation(Root, idle.length * .25f);
            Quaternion look = Quaternion.Euler(pitch, 0, 0);
            head.rotation = look * headBind;
            View.SetNetworkLookRotation(look); View.SetAimBlend(0); Tick();
        }
        public void Tick() => typeof(DollSingerView).GetMethod("LateUpdate", Private).Invoke(View, null);
        public void Before(Camera camera) => typeof(DollSingerView).GetMethod("BeforeCameraCull", Private).Invoke(View, new object[] { camera });
        public void After(Camera camera) => typeof(DollSingerView).GetMethod("AfterCameraRender", Private).Invoke(View, new object[] { camera });
        public void Dispose()
        {
            if (View) View.BindPlayer(null, true);
            if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
        }
    }

    public static string RunReport()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play.");
        int checks = 0;
        void Check(bool ok, string message) { Require(ok, message); checks++; }
        var rows = new List<string>();
        foreach (var rig in Rigs)
        using (var f = new Fixture(rig))
        {
            var camera = f.View.camera;
            Check(!f.Root.GetComponentsInChildren<Camera>(true).Any(c => c.name.Contains("Gesture")), "Unexpected action camera: " + rig);
            Check(!camera.GetUniversalAdditionalCameraData().cameraStack.Any(c => c && c.name.Contains("Gesture")), "Action camera remained stacked");
            var action = f.Animator.gameObject.AddComponent<RaidActionPresentation>(); action.enabled = false;
            action.Configure(f.Animator, f.View, f.Animator.GetComponent<DollSingerHaloAim>(), rig);
            foreach (float pitch in new[] { -80f, -65f, 0f, 65f, 80f })
            {
                f.Pose(pitch);
                var position = camera.transform.position; var rotation = camera.transform.rotation; var projection = camera.projectionMatrix;
                foreach (var kind in new[] { HandAction.Eat, HandAction.Drink, HandAction.Inject })
                {
                    string item = kind == HandAction.Eat ? "ration" : kind == HandAction.Drink ? "water" : "medkit";
                    var state = new PredictedPlayerGhost { CurrentHealth = 100, HandAction = (byte)kind, HandActionSequence = (uint)kind,
                        HandActionItem = item, HandActionDuration = 3, HandActionRemaining = 1.5f };
                    action.Present(state, true, true, false);
                    foreach (float phase in new[] { 0f, .2f, .5f, .8f, 1f })
                    {
                        action.SamplePreview(phase); f.Tick();
                        Check(Vector3.Distance(camera.transform.position, position) < .0001f, "Action displaced eye: " + rig + "/" + kind + "/" + pitch);
                        Check(Quaternion.Angle(camera.transform.rotation, rotation) < .01f && camera.projectionMatrix == projection,
                            "Action changed look or projection: " + rig + "/" + kind);
                        f.Before(camera);
                        Check(f.Body.sharedMesh == f.OwnerMesh && !f.Body.forceRenderingOff, "Action hid connected body");
                        f.After(camera);
                        Check(f.Body.sharedMesh == f.WorldMesh, "Observer mesh was not restored");
                    }
                    action.ResetPresentation();
                }
                for (int weapon = 1; weapon <= 13; weapon++)
                foreach (float age in new[] { 10f, .04f, .12f })
                {
                    f.Gestures.RestoreBeforeAnimation();
                    f.Gestures.Apply(weapon, 1, age, false, 0, .01f, f.Bones["上半身2"], rotation * Vector3.forward, 1, eye: camera);
                    if (weapon == 4)
                    {
                        var arms = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm };
                        var elbows = new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm };
                        var hands = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
                        var anchors = arms.Select(b => f.Animator.GetBoneTransform(b).position).ToArray();
                        var rotations = hands.Select(b => f.Animator.GetBoneTransform(b).rotation).ToArray();
                        var lengths = Enumerable.Range(0, 2).Select(i => Vector3.Distance(anchors[i], f.Animator.GetBoneTransform(elbows[i]).position) +
                            Vector3.Distance(f.Animator.GetBoneTransform(elbows[i]).position, f.Animator.GetBoneTransform(hands[i]).position)).ToArray();
                        f.Gestures.AlignViewfinder(camera, 1);
                        for (int i = 0; i < 2; i++)
                        {
                            var upper = f.Animator.GetBoneTransform(arms[i]); var elbow = f.Animator.GetBoneTransform(elbows[i]); var hand = f.Animator.GetBoneTransform(hands[i]);
                            Check(Vector3.Distance(upper.position, anchors[i]) < .00001f &&
                                Mathf.Abs(Vector3.Distance(upper.position, elbow.position) + Vector3.Distance(elbow.position, hand.position) - lengths[i]) < .00001f,
                                "Sniper detached or stretched an arm");
                            Check(Quaternion.Angle(hand.rotation, rotations[i]) < .05f, "Sniper changed authored palm orientation");
                        }
                    }
                    f.Tick();
                    Check(Vector3.Distance(camera.transform.position, position) < .0001f, "Weapon displaced eye: " + rig + "/" + weapon);
                    Check(Quaternion.Angle(camera.transform.rotation, rotation) < .01f && camera.projectionMatrix == projection,
                        "Weapon re-framed view: " + rig + "/" + weapon);
                    f.Before(camera); Check(!f.Body.forceRenderingOff && f.Body.sharedMesh == f.OwnerMesh, "Aiming hid body"); f.After(camera);
                    if (weapon == 4 && age == 10)
                    {
                        var center = (f.Bones["人指先.L"].position + f.Bones["親指先.R"].position + f.Bones["人指先.R"].position + f.Bones["親指先.L"].position) * .25f;
                        Vector3 projected = camera.WorldToViewportPoint(center);
                        Check(projected.z > camera.nearClipPlane && Vector2.Distance(projected, Vector2.one * .5f) < .001f,
                            "Real sniper frame missed the eye ray: " + rig + "/" + pitch + "/" + projected);
                        Check(Vector3.Distance(f.Bones["人指先.L"].position, f.Bones["親指先.R"].position) < .001f &&
                            Vector3.Distance(f.Bones["人指先.R"].position, f.Bones["親指先.L"].position) < .001f, "Sniper finger contacts separated");
                        rows.Add(rig + " pitch=" + pitch + " viewfinderEyeOffset=" + camera.transform.InverseTransformPoint(center).ToString("F4"));
                    }
                }
            }
            Check(!f.Root.GetComponentsInChildren<Renderer>(true).Any(r => r.gameObject.layer == LayerMask.NameToLayer("FirstPersonOverlay")),
                "Owner-only hand or prop copies remain: " + rig);
            Check(f.Root.GetComponentsInChildren<RaidActionProp>(true).Length == 3, "Actions did not retain exactly one prop per item: " + rig);
            action.DisposePresentation();
        }
        const string output = "LocalData/FirstPersonView20261104";
        Directory.CreateDirectory(output);
        string result = "PASS unified first-person view: " + checks + " checks across three characters; use actions and 13 weapon poses preserve eye, look, projection and connected body.";
        rows.Insert(0, result); File.WriteAllLines(output + "/checks.txt", rows);
        return string.Join("\n", rows);
    }

    public static void CaptureHands(string path)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play.");
        using var f = new Fixture("DollSinger");
        var target = new RenderTexture(640, 360, 24, RenderTextureFormat.ARGB32);
        var sheet = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        var baked = new Mesh(); var previous = RenderTexture.active;
        try
        {
            var pose = new GameObject("Current body surface"); pose.transform.SetParent(f.Body.transform, false);
            var filter = pose.AddComponent<MeshFilter>(); var renderer = pose.AddComponent<MeshRenderer>(); renderer.sharedMaterials = f.Body.sharedMaterials;
            foreach (var r in f.Root.GetComponentsInChildren<Renderer>(true)) r.enabled = r == renderer;
            var light = new GameObject("Preview key").AddComponent<Light>(); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, f.Scene);
            light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(25, 155, 0);
            target.Create(); int count = 0;
            foreach (var entry in HaloArsenal.Entries)
            {
                f.Pose(0); f.Gestures.Apply((int)entry.Gesture, 1, 10, false, 0, .01f, f.Bones["上半身2"], Vector3.forward, 1, eye: f.View.camera); f.Tick();
                f.Body.sharedMesh = f.OwnerMesh; f.Body.BakeMesh(baked, true); f.Body.sharedMesh = f.WorldMesh;
                filter.sharedMesh = null; filter.sharedMesh = baked;
                RenderPipeline.SubmitRenderRequest(f.View.camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; sheet.ReadPixels(new Rect(0, 0, 640, 360), count % 3 * 640, (2 - count / 3) * 360, false); count++;
            }
            sheet.Apply(); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, sheet.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(sheet); Object.DestroyImmediate(baked); }
    }
}
