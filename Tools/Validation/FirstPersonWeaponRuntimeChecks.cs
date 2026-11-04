using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Isolated production-prefab smoke test in the existing MainMenu Editor.
public static class FirstPersonWeaponRuntimeChecks
{
    private const string Output = "LocalData/FirstPersonView20261104/Weapons";
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }

    private static async Task Frame(Camera camera, Action sample = null)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<ScriptableRenderContext, Camera> handler = null;
        handler = (_, rendered) =>
        {
            if (rendered != camera) return;
            RenderPipelineManager.endCameraRendering -= handler;
            try { sample?.Invoke(); done.TrySetResult(true); }
            catch (Exception e) { done.TrySetException(e); }
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

    public static async Task<object> Run()
    {
        checks = 0;
        Check(EditorApplication.isPlaying && SceneManager.GetActiveScene().path == "Assets/Scenes/MainMenu.unity",
            "Use the existing MainMenu Editor in Play mode.");
        Directory.CreateDirectory(Output);
        bool background = Application.runInBackground; Application.runInBackground = true;
        var scene = SceneManager.CreateScene("First person weapon checks");
        var target = new RenderTexture(960, 540, 24);
        var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        GameObject root = null;
        try
        {
            var lightObject = new GameObject("Fixture key"); SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            foreach (string rig in new[] { "DollSinger", "HatsuneMiku", "Shian" })
            {
                root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"));
                SceneManager.MoveGameObjectToScene(root, scene);
                // Runtime cameras see all loaded scenes. Stay outside menu geometry.
                root.transform.position += Vector3.one * 100;
                foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                foreach (var listener in root.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
                foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(true);
                for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
                animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var view = root.GetComponentInChildren<DollSingerView>(true);
                view.gameObject.SetActive(true); view.BindPlayer(animator.gameObject, true); view.SetFirstPerson(true); view.enabled = true;
                var camera = view.camera; camera.enabled = true; camera.scene = scene; camera.targetTexture = target;
                camera.aspect = 16f / 9; camera.farClipPlane = 15; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.11f, .13f, .16f);
                var model = animator.GetComponent<DollSingerMovement>(); model.enabled = true;
                var twist = animator.GetComponent<HumanoidTwistFollowers>(); if (twist) twist.enabled = true;
                var halo = animator.GetComponent<DollSingerHaloAim>(); halo.view = view; halo.enabled = true;
                var bones = animator.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                void Look(float pitch)
                {
                    Quaternion q = Quaternion.Euler(pitch, 0, 0);
                    model.SetNetworkViewPresentation(true, q, 0); view.SetNetworkLookRotation(q);
                    halo.SetNetworkPresentation(true, q * Vector3.forward, root.transform.position + Vector3.up * 1.6f + q * Vector3.forward * 50);
                }
                void Capture(string name)
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0, false); pixels.Apply();
                        File.WriteAllBytes(Output + "/" + rig + "-" + name + ".png", pixels.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; }
                }
                foreach (var weapon in new[] { DollSingerHaloAim.HaloWeapon.Rifle, DollSingerHaloAim.HaloWeapon.Sniper })
                {
                    halo.SetNetworkWeapon(weapon, 4, false, 0, 0, 0);
                    foreach (float pitch in new[] { -65f, 0f, 30f, 65f })
                    {
                        Look(pitch); await Task.Delay(450);
                        await Frame(camera, () =>
                        {
                            Check(halo.AimBlend > .99f, rig + " did not raise " + weapon);
                            Check(Quaternion.Angle(camera.transform.rotation, Quaternion.Euler(pitch, 0, 0)) < .1f, rig + " gesture moved eye direction");
                            Check(!root.GetComponentsInChildren<Camera>(true).Any(c => c.name.Contains("Gesture")), "Extra gesture camera");
                            if (weapon == DollSingerHaloAim.HaloWeapon.Sniper)
                            {
                                Vector3 center = (bones["人指先.L"].position + bones["親指先.R"].position + bones["人指先.R"].position + bones["親指先.L"].position) * .25f;
                                Vector3 uv = camera.WorldToViewportPoint(center);
                                Check(uv.z > camera.nearClipPlane && Vector2.Distance(uv, Vector2.one * .5f) < .005f, rig + " sniper frame is off the eye ray: " + uv);
                                Check(Vector3.Distance(bones["人指先.L"].position, bones["親指先.R"].position) < .001f, rig + " sniper fingers separated");
                            }
                            Capture(weapon + "-" + pitch);
                        });
                    }
                }
                Look(0); halo.SetNetworkPresentation(false, Vector3.forward, Vector3.forward * 50);
                await Task.Delay(400); await Frame(camera, () => Check(halo.AimBlend < .01f, rig + " aim-out retained the pose"));
                halo.SetNetworkWeapon(DollSingerHaloAim.HaloWeapon.None, 0, false, 0, 0, 0);
                var melee = animator.GetComponent<DollSingerMeleePresentation>() ?? animator.gameObject.AddComponent<DollSingerMeleePresentation>();
                melee.Configure(animator, view, halo); melee.enabled = true;
                uint sequence = 1;
                foreach (var weapon in new[] { MeleeWeaponKind.Fists, MeleeWeaponKind.Dagger, MeleeWeaponKind.EnergySword })
                {
                    MeleeRules.TryDefinition(weapon, out var definition);
                    var timing = definition.Timing(MeleeAttackKind.Light, 0);
                    var state = new MeleeViewState { Weapon = weapon, AttackWeapon = weapon, Attack = MeleeAttackKind.Light,
                        Phase = MeleePhase.Active, AttackSequence = sequence++, Elapsed = timing.Windup + timing.Active * .5f,
                        Alive = true, IsOwner = true, FirstPerson = true, AttackDirection = Vector3.forward };
                    melee.SetPresentation(state); await Task.Delay(200);
                    await Frame(camera, () =>
                    {
                        Check(melee.OwnsHandPose && melee.PoseBlend > .99f, rig + " melee pose missing");
                        Check(Quaternion.Angle(camera.transform.rotation, Quaternion.identity) < .1f, rig + " melee rotated camera");
                        Check(!root.GetComponentsInChildren<Renderer>(true).Any(r => r.gameObject.layer == LayerMask.NameToLayer("FirstPersonOverlay")), "Melee bypassed world depth");
                        Capture(weapon.ToString());
                    });
                    state.Suppressed = true; melee.SetPresentation(state); await Frame(camera);
                    await Frame(camera, () => Check(!melee.OwnsHandPose && root.GetComponentsInChildren<TrailRenderer>(true).All(t => !t.emitting), "Cancelled melee retained pose or trail"));
                }
                melee.ResetPresentation(); view.BindPlayer(null, true);
                UnityEngine.Object.Destroy(root); root = null; await Task.Delay(50);
            }
            string report = "PASS " + checks + " runtime checks: three rigs, rifle/sniper at four pitches, aim release, all melee weapons and cancellation. Single isolated Editor; no raid.";
            File.WriteAllText(Output + "/checks.txt", report); return report;
        }
        finally
        {
            if (root) UnityEngine.Object.Destroy(root);
            target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels);
            await SceneManager.UnloadSceneAsync(scene); Application.runInBackground = background;
        }
    }
}
