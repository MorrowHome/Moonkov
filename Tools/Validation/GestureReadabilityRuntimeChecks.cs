using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Single Editor, real network prefabs with local presentation state. No network session.
public static class GestureReadabilityRuntimeChecks
{
    private const string Output = "LocalData/GestureReadability/Runtime";
    private static async Task Frame(DollSingerView view, Action sample = null)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ScriptableRenderContext, Camera> handler = null;
        handler = (_, camera) =>
        {
            if (camera != (view.GestureProjectionActive ? view.GestureProjectionCamera : view.camera)) return;
            RenderPipelineManager.endCameraRendering -= handler;
            try { sample?.Invoke(); done.TrySetResult(true); } catch (Exception e) { done.TrySetException(e); }
        };
        RenderPipelineManager.endCameraRendering += handler;
        try
        {
            if (await Task.WhenAny(done.Task, Task.Delay(8000)) != done.Task) throw new TimeoutException("No rendered frame");
            await done.Task;
        }
        finally { RenderPipelineManager.endCameraRendering -= handler; }
    }
    public static async Task<object> Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Use the existing Editor in Play.");
        Directory.CreateDirectory(Output);
        int checks = 0; void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>().Where(c => c.enabled).ToArray();
        foreach (var camera in cameras) camera.enabled = false;
        bool background = Application.runInBackground; Application.runInBackground = true;
        var scene = SceneManager.CreateScene("Gesture readability runtime checks");
        var target = new RenderTexture(960, 540, 24); var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        GameObject root = null; int firstFrame = Time.frameCount;
        try
        {
            var lightObject = new GameObject("Readability key"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            foreach (string rig in new[] { "DollSinger", "HatsuneMiku", "Shian" })
            {
                root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"));
                SceneManager.MoveGameObjectToScene(root, scene); root.transform.position += Vector3.one * 100;
                foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(true);
                for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
                animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var view = root.GetComponentInChildren<DollSingerView>(true);
                view.gameObject.SetActive(true); view.BindPlayer(animator.gameObject, true); view.SetFirstPerson(true); view.enabled = true;
                var world = view.camera; world.enabled = true; world.scene = scene; world.targetTexture = target;
                world.aspect = 16f / 9; world.farClipPlane = 15; world.clearFlags = CameraClearFlags.SolidColor; world.backgroundColor = new Color(.11f, .13f, .16f);
                var model = animator.GetComponent<DollSingerMovement>(); model.enabled = true;
                var twist = animator.GetComponent<HumanoidTwistFollowers>(); if (twist) twist.enabled = true;
                var halo = animator.GetComponent<DollSingerHaloAim>(); halo.view = view; halo.enabled = true;
                void Look(float pitch, bool aim = true)
                {
                    Quaternion q = Quaternion.Euler(pitch, 0, 0); model.SetNetworkViewPresentation(true, q, 0); view.SetNetworkLookRotation(q);
                    halo.SetNetworkPresentation(aim, q * Vector3.forward, root.transform.position + Vector3.up * 1.6f + q * Vector3.forward * 50);
                }
                Color32[] Read()
                {
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0, false); pixels.Apply(); return pixels.GetPixels32(); }
                    finally { RenderTexture.active = previous; }
                }
                void Capture(string name) { Read(); File.WriteAllBytes(Output + "/" + rig + "-" + name + ".png", pixels.EncodeToPNG()); }
                for (int weapon = 1; weapon <= 13; weapon++)
                {
                    halo.SetNetworkWeapon((DollSingerHaloAim.HaloWeapon)weapon, 6, false, 0, 0, 0); Look(0);
                    await Task.Delay(350); await Frame(view, () =>
                    {
                        Check(view.GestureProjectionActive && halo.AimBlend > .99f, "Gesture did not raise");
                        Check(Vector3.Distance(world.transform.position, view.GestureProjectionCamera.transform.position) < .00001f, "Projection moved eye");
                        Check(Quaternion.Angle(world.transform.rotation, view.GestureProjectionCamera.transform.rotation) < .001f, "Projection changed look");
                        Check(Mathf.Abs(view.GestureProjectionCamera.fieldOfView - (weapon == 4 ? 70f : 110f)) < .01f,
                            "Weapon switch did not settle to its gesture lens");
                        Capture("weapon-" + weapon);
                    });
                }
                halo.SetNetworkWeapon(DollSingerHaloAim.HaloWeapon.Rifle, 6, false, 0, 0, 0);
                foreach (float pitch in new[] { -80f, -65f, 65f, 80f })
                { Look(pitch); await Task.Delay(250); await Frame(view, () => Capture("rifle-pitch-" + pitch)); }
                Look(0); await Task.Delay(300);
                // Compare an opaque wall with and without the projection. Clearing
                // overlay depth would produce clearly different hand pixels.
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); SceneManager.MoveGameObjectToScene(wall, scene);
                wall.transform.SetPositionAndRotation(world.transform.position + world.transform.forward * .05f, world.transform.rotation);
                wall.transform.localScale = new Vector3(2, 2, .01f);
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.color = new Color(.16f, .27f, .4f);
                wall.GetComponent<Renderer>().sharedMaterial = material;
                Color32[] shown = null, hidden = null;
                await Frame(view, () => { shown = Read(); Capture("wall"); });
                halo.enabled = false; view.PresentGesture(0);
                await Frame(view, () => hidden = Read());
                int different = 0;
                for (int y = 100; y < 440; y++) for (int x = 120; x < 840; x++)
                { int i = y * 960 + x; if (Mathf.Abs(shown[i].r - hidden[i].r) + Mathf.Abs(shown[i].g - hidden[i].g) + Mathf.Abs(shown[i].b - hidden[i].b) > 12) different++; }
                Check(different < 500, "Weapon projection drew through a wall: " + different + " pixels");
                UnityEngine.Object.Destroy(wall); UnityEngine.Object.Destroy(material); halo.enabled = true;
                Look(0, false); await Task.Delay(350); await Frame(view, () => Check(!view.GestureProjectionActive, "Aim-out retained the projection"));
                view.SetFirstPerson(false); Look(0); await Task.Delay(350); await Frame(view, () => Check(!view.GestureProjectionActive, "Third person retained the projection"));
                view.BindPlayer(null, true); UnityEngine.Object.Destroy(root); root = null; await Task.Delay(50);
            }
            Check(Time.frameCount > firstFrame + 50, "Editor did not advance");
            string report = $"PASS {checks} checks; three production rigs, 13 gestures, extreme pitches, shared eye, wall depth, aim-out and third-person restoration. Frames={Time.frameCount-firstFrame}.";
            File.WriteAllText(Output + "/checks.txt", report); return report;
        }
        finally
        {
            if (root) UnityEngine.Object.Destroy(root);
            await SceneManager.UnloadSceneAsync(scene);
            UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
            foreach (var camera in cameras) if (camera) camera.enabled = true;
            Application.runInBackground = background;
        }
    }
}
