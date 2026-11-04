using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public static class HaloDefenseReview
{
    [MenuItem("Tools/Moonkov/Preview Halo Defense")]
    public static void Menu() => Debug.Log(Capture());

    public static string Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        const string output = "LocalData/HaloEnergy";
        Directory.CreateDirectory(output);
        int checks = 0;
        void Check(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); checks++; }
        var portrait = new MenuCharacterView(idleOnly: true, characterIndex: GameSettings.DollSingerCharacterIndex);
        RenderTexture target = null;
        try
        {
            portrait.Advance(0);
            var scene = portrait.DisplayScene;
            var stage = scene.GetRootGameObjects().Single(root => root.name == "Menu character render stage");
            var model = stage.GetComponentInChildren<Animator>(true);
            var head = model.GetBoneTransform(HumanBodyBones.Head);
            var left = model.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = model.GetBoneTransform(HumanBodyBones.RightFoot);
            var feet = (left.position + right.position) * .5f;
            var center = (feet + head.position) * .5f;
            float radius = Mathf.Max(.85f, Vector3.Distance(feet, head.position) * .78f);
            foreach (var renderer in stage.GetComponentsInChildren<LineRenderer>(true)) renderer.enabled = false;
            foreach (var renderer in stage.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = false;
            int colliders = stage.GetComponentsInChildren<Collider>(true).Length;
            var node = new GameObject("Actual halo defense review") { layer = 5 };
            node.transform.SetParent(stage.transform, false);
            var sphere = node.AddComponent<HaloDefenseSphere>();
            Color color = new Color(1, .18f, .42f);
            for (int i = 0; i < 16; i++) sphere.Present(true, head.position, center, radius, color, 1, 10, center, false, 1f / 60);
            Check(sphere.Deployment == 1 && sphere.Surface.enabled, "Defense did not finish opening in 250ms");
            Check(sphere.Surface.sharedMaterials.Length == 1, "Expected one shared material batch");
            Check(stage.GetComponentsInChildren<Collider>(true).Length == colliders, "Visual added collision geometry");
            var shader = Resources.Load<Shader>("Moonkov/HaloDefense");
            Check(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Defense shader failed");
            var cameraNode = new GameObject("Halo defense review camera"); cameraNode.transform.SetParent(stage.transform, false);
            var camera = cameraNode.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = .02f; camera.farClipPlane = 20; camera.orthographic = true; camera.orthographicSize = radius * 1.2f;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            target = new RenderTexture(960, 960, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            target.Create(); camera.aspect = 1;
            camera.transform.position = center + new Vector3(2.5f, .7f, 3.5f); camera.transform.LookAt(center);
            var lightNode = new GameObject("Preview key light"); lightNode.transform.SetParent(stage.transform, false);
            var light = lightNode.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.1f;
            light.cullingMask = 1 << 5; light.transform.rotation = camera.transform.rotation;
            camera.backgroundColor = new Color(.035f, .04f, .055f);
            Render(camera, target, output + "/defense-warmup.png");
            Render(camera, target, output + "/defense-dark.png");
            foreach (var entry in HaloAppearanceCatalog.Current.Entries)
            {
                sphere.Present(true, head.position, center, radius, entry.Color, 1, 10, center, false, 0, entry.Weapon);
                Render(camera, target, output + "/defense-" + (DollSingerHaloAim.HaloWeapon)(entry.Weapon - 1) + ".png");
            }
            sphere.Present(true, head.position, center, radius, color, 1, 10, center, false, 0);
            camera.backgroundColor = new Color(.38f, .40f, .43f);
            Render(camera, target, output + "/defense-light.png");
            camera.backgroundColor = new Color(.035f, .04f, .055f);
            sphere.Present(true, head.position, center, radius, color, .35f, .06f,
                center + new Vector3(.4f, .2f, .85f).normalized * radius, false, 0);
            Render(camera, target, output + "/defense-hit.png");
            camera.transform.position = center + new Vector3(-3.5f, .45f, 1.5f); camera.transform.LookAt(center);
            Render(camera, target, output + "/defense-side.png");
            camera.orthographic = false; camera.fieldOfView = 70;
            camera.transform.position = head.position + model.transform.forward * .15f;
            camera.transform.rotation = Quaternion.LookRotation(model.transform.forward, Vector3.up);
            sphere.Present(true, head.position, center, radius, color, .8f, 10, center, true, 0);
            Render(camera, target, output + "/defense-first-person.png");
            for (int i = 0; i < 13; i++) sphere.Present(false, head.position, center, radius, color, 0, 10, center, false, 1f / 60);
            Check(sphere.Deployment == 0 && !sphere.Surface.enabled, "Defense did not close in 200ms");
            // Exercise the real character presentation path as well as its isolated sphere.
            var protection = model.GetComponent<EnergyShieldPresentation>() ?? model.gameObject.AddComponent<EnergyShieldPresentation>();
            protection.PresentationAudioEnabled = false;
            var snapshot = new PredictedPlayerGhost { CurrentHealth = 440, HaloEquipped = true, HaloDefending = true, HaloEnergy = 80, HaloCapacity = 100 };
            protection.Present(snapshot, false, false, null);
            var tick = typeof(EnergyShieldPresentation).GetMethod("SimulatePresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 16; i++) tick.Invoke(protection, null);
            var actual = model.GetComponentInChildren<HaloDefenseSphere>(true);
            Check(actual && actual.Deployment == 1, "Character snapshot did not open the real defense sphere");
            protection.ResetPresentation();
            Check(actual.Deployment == 0, "Character reset left defense visible");
            string result = "Halo defense native preview: PASS (" + checks + " checks, five views and all 13 weapon shapes, temporary character stage).";
            File.WriteAllText(output + "/visual-checks.txt", result);
            return result;
        }
        finally
        {
            portrait.Dispose();
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
        }
    }

    public static string CheckWeapons()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        var catalog = HaloAppearanceCatalog.Current;
        if (!catalog || !catalog.Artwork || catalog.Entries.Length != 13) throw new Exception("Incomplete formal halo catalog");
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var tick = typeof(EnergyShieldPresentation).GetMethod("SimulatePresentation", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (string path in new[] { "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab",
            "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab", "Assets/Shian/Prefabs/ShianNetworkPlayer.prefab" })
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                var halo = root.GetComponentInChildren<DollSingerHaloAim>(true);
                var protection = halo.GetComponent<EnergyShieldPresentation>() ?? halo.gameObject.AddComponent<EnergyShieldPresentation>();
                protection.ConfigureHardwareBudget(true); protection.PresentationAudioEnabled = false;
                halo.SetNetworkWeapon(DollSingerHaloAim.HaloWeapon.None, 0, false, 0, 0, 0);
                if (halo.haloLight) { halo.haloLight.color = Color.magenta; halo.haloLight.enabled = false; }
                var state = new PredictedPlayerGhost { CurrentHealth = 440, HaloEquipped = true, HaloDefending = true,
                    HaloCapacity = 100, HaloEnergy = 80, HaloEnergyInitialized = true };
                var block = new MaterialPropertyBlock(); Mesh mesh = null; Material material = null;
                foreach (var entry in catalog.Entries)
                {
                    state.EquippedWeaponID = entry.Weapon;
                    Color expected = halo.ColorForWeapon((DollSingerHaloAim.HaloWeapon)(entry.Weapon - 1)); expected.a = 1;
                    Check(expected == entry.Color, "HUD catalog color differs from formal player: " + path + " / " + entry.Weapon);
                    protection.Present(state, false, false, null);
                    for (int frame = 0; frame < 16; frame++) tick.Invoke(protection, null);
                    var sphere = halo.GetComponentInChildren<HaloDefenseSphere>(true); sphere.Surface.GetPropertyBlock(block);
                    Check(block.GetColor("_HaloColor") == expected && block.GetVector("_HaloArtworkRect") == entry.ArtworkRect,
                        "Defense did not follow held halo shape/color with its attack visual hidden: " + entry.Weapon);
                    var actualMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                    if (!mesh) { mesh = actualMesh; material = sphere.Surface.sharedMaterial; }
                    Check(actualMesh == mesh && sphere.Surface.sharedMaterial == material && material.mainTexture == catalog.Artwork,
                        "Weapon switching rebuilt defense resources");
                }
                state.EquippedWeaponID = DollSingerWeapons.None; state.SecondaryWeaponID = DollSingerWeapons.Sniper;
                protection.Present(state, false, false, null); tick.Invoke(protection, null);
                halo.GetComponentInChildren<HaloDefenseSphere>(true).Surface.GetPropertyBlock(block);
                Check(block.GetVector("_HaloArtworkRect") == catalog.ForWeapon(DollSingerWeapons.Sniper).ArtworkRect,
                    "Melee selection did not use the equipped halo for defense");
                state.EquippedWeaponID = DollSingerWeapons.Shotgun; halo.shotgunVisual.lightColor = Color.cyan;
                protection.Present(state, false, false, null); tick.Invoke(protection, null);
                halo.GetComponentInChildren<HaloDefenseSphere>(true).Surface.GetPropertyBlock(block);
                Check(block.GetColor("_HaloColor") == Color.cyan, "Hidden visual retained a stale defense color");
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }
        return $"PASS: {checks} checks across 3 avatars and 13 halos: formal HUD palette, shape/color switching, hidden attack visuals and shared resources.";
    }

    private static void Render(Camera camera, RenderTexture target, string path)
    {
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
    }
}
