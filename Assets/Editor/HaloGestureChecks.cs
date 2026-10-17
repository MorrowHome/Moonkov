using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Exercises actual Humanoid poses, owner projection, cloth priority and cleanup in an isolated scene.</summary>
public static class HaloGestureChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Call(object component, string method) => component.GetType().GetMethod(method, Private)?.Invoke(component, null);
    private static void Set(object component, string field, object value) => component.GetType().GetField(field, Private).SetValue(component, value);
    private static void Check(bool result, string message)
    { if (!result) throw new InvalidOperationException("Gesture check: " + message); }

    [MenuItem("Tools/Moonkov/Check Weapon Gestures")]
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab"));
        SceneManager.MoveGameObjectToScene(player, scene);
        foreach (var b in player.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
        foreach (var c in player.GetComponentsInChildren<Camera>(true)) c.enabled = false;
        var model = player.GetComponent<DollSingerMovement>();
        var halo = player.GetComponent<DollSingerHaloAim>();
        var animator = player.GetComponent<Animator>();
        var view = player.GetComponentInChildren<DollSingerView>(true);
        var skirt = player.GetComponent<SkirtHandAvoidance>();
        var rows = new List<string>();
        var lightObject = new GameObject("Gesture validation sun");
        SceneManager.MoveGameObjectToScene(lightObject, scene);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
        light.transform.rotation = Quaternion.Euler(35, -25, 0);
        try
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); animator.Update(0);
            Call(model, "Awake"); Call(halo, "Awake"); Call(skirt, "Awake");
            view.enabled = halo.enabled = true;
            // Normal MonoBehaviours do not receive their Play lifecycle in an Editor fixture.
            Call(view, "OnDisable"); Call(view, "Awake"); Call(view, "OnEnable");
            view.camera.enabled = true;
            view.camera.scene = scene;
            view.camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            view.GestureCamera.scene = scene;
            view.GestureCamera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            view.SetFirstPerson(true);
            Set(view, "viewBlend", 1f);
            Set(halo, "thirdPerson", model);
            Set(halo, "cameraOwner", view);
            halo.SetNetworkPresentation(true, Vector3.forward, Vector3.forward * 100);
            Set(halo, "aimBlend", 1f);
            Set(halo, "wantsAim", true);
            string[] states = { "FingerGunAim", "RevolverGesture", "ShotgunGesture", "SniperGesture" };
            int scenarios = 0;
            foreach (var weapon in new[] { DollSingerHaloAim.HaloWeapon.Rifle, DollSingerHaloAim.HaloWeapon.Revolver,
                DollSingerHaloAim.HaloWeapon.Shotgun, DollSingerHaloAim.HaloWeapon.Sniper })
            foreach (float aspect in new[] { 16f / 9, 4f / 3 })
            foreach (float pitch in new[] { -70f, 0f, 70f })
            foreach (bool charging in new[] { false, true })
            {
                animator.Rebind(); animator.Update(0);
                halo.SetNetworkWeapon(weapon, 4, charging, charging ? .5f : 0f, 0, 0);
                animator.SetInteger("HaloGesture", (int)weapon);
                animator.SetLayerWeight(halo.AimLayerIndex, 1);
                animator.Play(states[(int)weapon - 1], halo.AimLayerIndex, 0);
                animator.Update(0);
                view.camera.aspect = aspect;
                view.SetNetworkLookRotation(Quaternion.Euler(pitch, 0, 0));
                Set(view, "presentedWeaponMagnification", weapon == DollSingerHaloAim.HaloWeapon.Sniper ? 6f : 1.75f);
                view.SetWeaponAimMagnification(weapon == DollSingerHaloAim.HaloWeapon.Sniper ? 6f : 0);
                view.SetAimBlend(1);
                Call(view, "LateUpdate");
                var ray = view.camera.ViewportPointToRay(new Vector3(.5f, .5f));
                Vector3 upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
                var elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                var wrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
                float armLength = Vector3.Distance(upper, elbow.position) + Vector3.Distance(elbow.position, wrist.position);
                Call(halo, "LateUpdate");
                Check(view.GestureHandsActive && view.GestureCamera.enabled, "owner hand camera missing");
                Check(view.GestureCamera.fieldOfView >= 50, "optic magnification cropped gesture projection");
                Check(Mathf.Abs(armLength - Vector3.Distance(upper, elbow.position) - Vector3.Distance(elbow.position, wrist.position)) < .0001f,
                    "hand placement changed bone lengths");
                var after = view.camera.ViewportPointToRay(new Vector3(.5f, .5f));
                Check(Vector3.Distance(ray.origin, after.origin) < .00001f && Vector3.Angle(ray.direction, after.direction) < .001f,
                    "gesture changed gameplay camera ray");
                var min = Vector2.one; var max = Vector2.zero;
                foreach (var bone in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                foreach (var joint in animator.GetBoneTransform(bone).GetComponentsInChildren<Transform>())
                {
                    var uv = view.GestureCamera.WorldToViewportPoint(joint.position);
                    Check(uv.z > view.GestureCamera.nearClipPlane && uv.x > .025f && uv.x < .975f && uv.y > .02f &&
                        uv.y < (weapon == DollSingerHaloAim.HaloWeapon.Sniper ? .84f : .49f),
                        weapon + " fingers outside usable view at pitch " + pitch + ": " + uv);
                    min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
                }
                Set(skirt, "m_HandCoverBlend", .8f);
                skirt.coverHandIKWeight = 1f;
                Quaternion before = wrist.rotation;
                Call(skirt, "LateUpdate");
                Check(Quaternion.Angle(before, wrist.rotation) < .001f, "cloth palm correction overwrote weapon gesture");
                Check(halo.OwnsHandPose, "weapon did not reserve hand ownership");
                rows.Add(weapon + " aspect=" + aspect + " pitch=" + pitch + " charging=" + charging + " bounds=" + min + ".." + max);
                scenarios++;
            }
            view.SetFirstPerson(false);
            Check(!view.GestureHandsActive && !view.GestureCamera.enabled &&
                !view.camera.GetUniversalAdditionalCameraData().cameraStack.Contains(view.GestureCamera), "third person retained overlay hands");
            var overlay = view.GestureCamera;
            view.BindPlayer(player, true);
            Check(!overlay, "character rebind leaked previous gesture camera");
            string result = "Passed " + scenarios + " weapon/pitch/aspect scenarios, cloth priority, ray invariance, arm lengths and projection cleanup.\n" + string.Join("\n", rows);
            System.IO.Directory.CreateDirectory("Temp/GestureValidation");
            System.IO.File.WriteAllText("Temp/GestureValidation/checks.txt", result);
            return result;
        }
        finally
        {
            Call(view, "OnDisable");
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(lightObject);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // Render only a live owner camera. A synchronously posed Edit-mode skeleton has
    // not passed through Unity's skinning/render lifecycle and is not visual evidence.
    [MenuItem("Tools/Moonkov/Capture Playing First Person")]
    public static void CapturePlayingFirstPerson()
    {
        var view = UnityEngine.Object.FindObjectsByType<DollSingerView>(FindObjectsSortMode.None)
            .FirstOrDefault(v => v.isActiveAndEnabled && v.GestureHandsActive);
        if (!EditorApplication.isPlaying || !view || !view.camera || !view.camera.isActiveAndEnabled)
            throw new InvalidOperationException("Capture requires an aiming local player in first person during Play mode.");
        Capture(view.camera, "Play-FirstPerson");
    }

    private static void Capture(Camera camera, string name)
    {
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var old = RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            System.IO.Directory.CreateDirectory("Temp/GestureValidation");
            System.IO.File.WriteAllBytes("Temp/GestureValidation/" + name + ".png", image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }
        finally { RenderTexture.active = old; target.Release(); UnityEngine.Object.DestroyImmediate(target); }
    }
}
