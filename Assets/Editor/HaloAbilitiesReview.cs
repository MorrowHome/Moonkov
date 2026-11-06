using System;
using System.IO;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class HaloAbilitiesReview
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [MenuItem("Tools/Moonkov/Preview Halo Exploration Abilities")]
    public static void Menu() => Debug.Log(Capture());
    public static string Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        Directory.CreateDirectory("LocalData/HaloAbilities");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Halo abilities native review"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        RenderTexture target = null; Material surface = null;
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
        try
        {
            surface = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
            surface.SetColor("_BaseColor", new Color(.13f, .17f, .22f)); surface.SetFloat("_Smoothness", .2f);
            GameObject Box(string name, Vector3 pos, Vector3 scale)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(root.transform);
                go.transform.position = pos; go.transform.localScale = scale; go.layer = 5; go.GetComponent<Renderer>().sharedMaterial = surface; return go;
            }
            Box("Ground", new Vector3(0, -.2f, 8), new Vector3(28, .4f, 30));
            Box("Left building", new Vector3(-6, 3, 10), new Vector3(5, 6, 10));
            Box("Right building", new Vector3(7, 2.5f, 13), new Vector3(6, 5, 12));
            Box("Overhead beam", new Vector3(0, 4.3f, 13), new Vector3(12, .4f, .7f));
            Box("Pillar", new Vector3(2, 2, 13), new Vector3(.65f, 4, .65f));
            for (int i = 0; i < 6; i++) Box("Stair " + i, new Vector3(-1.5f, .15f * (i + 1), 5 + i * .65f), new Vector3(2.8f, .3f * (i + 1), .65f));
            Box("Crate", new Vector3(2.6f, .7f, 7), new Vector3(1.5f, 1.4f, 1.2f));
            Box("Cover wall", new Vector3(0, 1.5f, 10), new Vector3(3.3f, 3, .3f));
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule); enemy.name = "Occluded scan contact"; enemy.transform.SetParent(root.transform);
            enemy.transform.position = new Vector3(0, 1, 11.3f); enemy.layer = 5; enemy.GetComponent<Renderer>().sharedMaterial = surface;
            var cameraNode = new GameObject("Native scan review camera"); cameraNode.transform.SetParent(root.transform);
            var camera = cameraNode.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.015f, .023f, .035f);
            camera.nearClipPlane = .05f; camera.farClipPlane = 80; camera.fieldOfView = 62;
            camera.transform.position = new Vector3(2, 2.5f, -3); camera.transform.LookAt(new Vector3(0, 1, 12));
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            data.requiresDepthTexture = true;
            var lightNode = new GameObject("Review light"); lightNode.transform.SetParent(root.transform);
            var light = lightNode.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f; light.cullingMask = 1 << 5;
            light.transform.rotation = Quaternion.Euler(45, -35, 0);
            target = new RenderTexture(1440, 900, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            target.Create(); camera.aspect = 1440f / 900;
            Render(camera, target, "LocalData/HaloAbilities/scan-before.png");
            var cache = new HaloScanClientState();
            double now = Time.realtimeSinceStartupAsDouble;
            cache.Receive(new HaloScanPulseRpc { OwnerGhostId = 1, OwnerSpawnTick = 1, Sequence = 1, Weapon = DollSingerWeapons.Halo, Origin = new Vector3(0, 1.7f, -1) }, now - .82);
            cache.Receive(new HaloScanContactRpc { OwnerGhostId = 1, OwnerSpawnTick = 1, Sequence = 1, GhostId = 2, Index = 0, Position = enemy.transform.position }, now);
            cache.Outlines[0] = new[] { enemy.GetComponent<Renderer>() };
            HaloScanRenderer.Publish(camera, cache, now);
            Render(camera, target, "LocalData/HaloAbilities/scan-space.png");
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("Moonkov/HaloScan")), "Scan shader compiles on the current native graphics API");
            Check(PixelDifference("LocalData/HaloAbilities/scan-before.png", "LocalData/HaloAbilities/scan-space.png") > 15000, "Scan visibly reveals scene surfaces, not only the enemy marker");
            HaloScanRenderer.Clear();

            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"), scene);
            player.transform.SetParent(root.transform); player.transform.position = new Vector3(-1.5f, 0, 1);
            var halo = player.GetComponentInChildren<DollSingerHaloAim>(true);
            foreach (var behaviour in halo.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var childCamera in player.GetComponentsInChildren<Camera>(true)) childCamera.enabled = false;
            foreach (var t in player.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
            halo.gameObject.SetActive(true); halo.GetComponent<Animator>().Rebind(); halo.GetComponent<Animator>().Update(0);
            typeof(DollSingerHaloAim).GetMethod("Awake", Private).Invoke(halo, null);
            typeof(DollSingerHaloAim).GetField("bakedGestures", Private).SetValue(halo, null);
            halo.SetNetworkPresentation(false, Vector3.forward, Vector3.zero);
            var effect = halo.gameObject.AddComponent<HaloTransferPresentation>(); effect.Configure(halo);
            var haloTick = typeof(DollSingerHaloAim).GetMethod("LateUpdate", Private);
            var effectTick = typeof(HaloTransferPresentation).GetMethod("SimulatePresentation", Private);
            var state = new PredictedPlayerGhost { CurrentHealth = 440, HaloEquipped = true, HaloEnergyInitialized = true, HaloEnergy = 60, HaloCapacity = 100,
                HaloTransferPhase = HaloTransferPhase.Sending, HaloDeploymentMode = HaloDeploymentMode.Shield, HaloTransferPoint = new Vector3(1.7f, 1.6f, 1) };
            int lights = halo.GetComponentsInChildren<Light>(true).Length;
            for (uint weapon = DollSingerWeapons.Halo; weapon <= DollSingerWeapons.Nocturne; weapon++)
            {
                state.EquippedWeaponID = weapon; halo.SetNetworkWeapon((DollSingerHaloAim.HaloWeapon)(weapon - 1), 4, false, 0, 0, 0);
                haloTick.Invoke(halo, null); effect.Present(state, null);
                for (int frame = 0; frame < 20; frame++) effectTick.Invoke(effect, new object[] { 1f / 60 });
                Check(Vector3.Distance(halo.haloVisual.position, state.HaloTransferPoint) < .01f, "Original equipped halo reaches the fixed point");
                var shield = halo.GetComponentInChildren<HaloDefenseSphere>();
                var properties = new MaterialPropertyBlock(); shield.Surface.GetPropertyBlock(properties);
                var expected = HaloAppearanceCatalog.Current.ForWeapon(weapon);
                Check(Vector4.Distance(properties.GetVector("_HaloArtworkRect"), expected.ArtworkRect) < .0001f, "Area shield preserves formal equipped artwork");
                Check(Vector4.Distance(properties.GetColor("_HaloColor"), expected.Color) < .001f, "Area shield preserves the equipped halo color");
                Check(halo.GetComponentsInChildren<Light>(true).Length == lights, "External modes do not add real-time lights");
            }
            state.EquippedWeaponID = DollSingerWeapons.Shotgun;
            halo.SetNetworkWeapon(DollSingerHaloAim.HaloWeapon.Shotgun, 4, false, 0, 0, 0); haloTick.Invoke(halo, null);
            effect.Present(state, null); effectTick.Invoke(effect, new object[] { .3f });
            camera.transform.position = new Vector3(6, 3.6f, -6); camera.transform.LookAt(new Vector3(.3f, 1.1f, 1));
            Render(camera, target, "LocalData/HaloAbilities/area-shield.png");
            state.HaloDeploymentMode = HaloDeploymentMode.Light; effect.Present(state, null); effectTick.Invoke(effect, new object[] { .3f });
            Check(halo.haloLight.enabled && HaloSupportChecks.Near(halo.haloLight.range, 8), "Stationary light reuses the original halo light");
            state.HaloDeploymentMode = HaloDeploymentMode.Sentry; state.AimPoint = new Vector3(1.7f, 1.5f, 8);
            effect.Present(state, null); effectTick.Invoke(effect, new object[] { .3f });
            Check(Vector3.Dot(halo.haloVisual.forward, ((Vector3)state.AimPoint - halo.haloVisual.position).normalized) > .99f, "Sentry art faces its authoritative aim point");
            Vector3 shotOrigin = new Vector3(1.7f, 1.6f, 1);
            halo.PlayNetworkShot(new Vector3(1.7f, 1.6f, 10), true, 42, shotOrigin);
            var pool = halo.GetComponentInChildren<HaloBoltPool>();
            Check(pool && pool.ActiveCount == 1 && Vector3.Distance(pool.GetComponentInChildren<LineRenderer>().GetPosition(0), shotOrigin) < .001f,
                "Autonomous cosmetic flight starts at the server position after local halo movement");
            effect.ResetPresentation();
            Check(halo.GetComponentInChildren<HaloDefenseSphere>().Deployment == 0, "Recall cleanup removes the external defense surface");
            string result = $"Halo exploration native review: PASS ({checks} checks, all 13 halo forms).";
            File.WriteAllText("LocalData/HaloAbilities/appearance.txt", result); return result;
        }
        finally
        {
            HaloScanRenderer.Clear();
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (surface) UnityEngine.Object.DestroyImmediate(surface);
            UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static void Render(Camera camera, RenderTexture target, string path)
    {
        var previous = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try { camera.targetTexture = target; camera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
        finally { RenderTexture.active = previous; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(image); }
    }
    private static int PixelDifference(string a, string b)
    {
        var before = new Texture2D(2, 2); var after = new Texture2D(2, 2);
        try
        {
            before.LoadImage(File.ReadAllBytes(a)); after.LoadImage(File.ReadAllBytes(b));
            var x = before.GetPixels32(); var y = after.GetPixels32(); int changed = 0;
            for (int i = 0; i < x.Length; i++) if (Mathf.Abs(x[i].r - y[i].r) + Mathf.Abs(x[i].g - y[i].g) + Mathf.Abs(x[i].b - y[i].b) > 30) changed++;
            return changed;
        }
        finally { UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after); }
    }
}
