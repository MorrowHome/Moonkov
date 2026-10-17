using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RifleHaloSetup
{
    private const string Prefab = "Assets/DollSinger/Prefabs/RifleHalo.prefab";
    private const string Art = "Assets/DollSinger/Art/Rifle/";
    private static void UpdateArtwork()
    {
        foreach (string name in new[] { "Petal", "OuterArc", "InnerArc", "Cell" })
        {
            string path = Art + name + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = name == "Cell" ? 8000 : 4000;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center; settings.spriteMeshType = SpriteMeshType.Tight;
            settings.spriteGenerateFallbackPhysicsShape = false; importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        string materialPath = Art + "RifleInk.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(Shader.Find("DollSinger/RifleHaloInk"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = Shader.Find("DollSinger/RifleHaloInk");
        material.SetFloat("_Gain", 1.5f); material.SetColor("_Color", Color.white); EditorUtility.SetDirty(material);
        // Load the existing root to retain the component / transform IDs used by nested player prefabs.
        var root = PrefabUtility.LoadPrefabContents(Prefab);
        try
        {
            var visual = root.GetComponent<RifleHaloVisual>();
            while (root.transform.childCount > 0) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            visual.artworkMaterial = material;
            visual.petalSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Petal.png");
            visual.outerArcSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "OuterArc.png");
            visual.innerArcSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "InnerArc.png");
            visual.cellSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "Cell.png");
            visual.BuildGeometry(); PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }
    [MenuItem("Tools/Doll Singer/Install Rifle Iris Halo")]
    public static void Install()
    {
        UpdateArtwork();
        foreach (string path in new[] { "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab", "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab" })
        {
            var player = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var halo = player.GetComponentInChildren<DollSingerHaloAim>(true);
                if (!halo.rifleVisual)
                {
                    var child = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), halo.haloVisual);
                    halo.rifleVisual = child.GetComponent<RifleHaloVisual>();
                    halo.rifleVisual.SetEquipped(false, 30);
                    foreach (var line in halo.haloStrokes.Concat(halo.haloGlowStrokes)) if (line) line.enabled = false;
                    EditorUtility.SetDirty(halo); PrefabUtility.SaveAsPrefabAsset(player, path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }
        AssetDatabase.SaveAssets(); HaloWeaponSetup.GenerateRifleIcon();
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Validate()
    {
        foreach (string path in new[] { "Assets/DollSinger/Prefabs/DollSingerPlayer.prefab", "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab" })
        {
            var halo = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<DollSingerHaloAim>(true);
            Require(halo.rifleVisual && halo.rifleVisual.magazineInk.Length == 30 && halo.rifleVisual.petals.Length == 6, "Rifle bindings: " + path);
            Require(halo.rifleVisual.overallScale >= 1.35f, "Enlarged rifle halo");
            Require(halo.firstPersonThumbClearance >= .02f && halo.firstPersonThumbClearance <= .05f &&
                halo.aimedHaloScale <= .45f, "Visible thumb reference with an unobstructed sight");
        }
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        try
        {
            var rifle = root.GetComponent<RifleHaloVisual>(); rifle.SetEquipped(false, 30); rifle.SetEquipped(true, 30);
            rifle.SetNetworkState(30, false, 0, 100, 0, 30);
            rifle.SetNetworkState(29, false, 0, 101, 0, 30); float rotation = rifle.RotorRotation;
            rifle.SetNetworkState(29, false, 0, 101, 0, 30);
            Require(rifle.VisibleAmmo == 29 && rotation == rifle.RotorRotation, "Shot replay cannot rotate twice");
            rifle.SetNetworkState(2, true, .5f, 101, 110, 9); float progress = rifle.ReloadProgress;
            rifle.SetNetworkState(2, true, .25f, 101, 110, 9);
            Require(rifle.ReloadProgress == progress && rifle.VisibleAmmo < 9, "Reload progress cannot rewind");
            rifle.SetNetworkState(9, false, 1, 101, 110, 9); rifle.SetNetworkState(2, true, .2f, 101, 110, 9);
            Require(rifle.VisibleAmmo == 9 && !rifle.IsReloading, "Partial reload completed/replay stays at nine rounds");
            rifle.SetNetworkState(0, false, 0, 90, 90, 30);
            Require(rifle.VisibleAmmo == 9, "Old event ticks are ignored");
            rifle.SetEquipped(false, 0); rifle.SetEquipped(true, 0); rifle.SetAimBlend(1); rifle.Step(0);
            Require(rifle.VisibleAmmo == 0 && rifle.RotorRotation == 0 && !rifle.IsReloading, "Empty equip/reset");
            var point = rifle.sight.GetPosition(0);
            Require(point.magnitude < .003f && rifle.magazineInk.All(c => c.sharedMaterial && c.sprite), "Tiny sight and ink references");
            Require(root.GetComponentsInChildren<LineRenderer>(true).Length == 1 && rifle.petals.All(p => p.sprite), "Only the tiny sight uses a line");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("Rifle halo checks passed: bindings, thumb clearance, shot/reload replay, partial recharge, empty reset and tiny sight.");
    }
    // Four native camera renders cover the artwork and the existing release/recharge poses.
    public static void RenderArtworkPreview()
    {
        const int size = 640;
        string folder = Path.GetFullPath("LocalData/RifleHaloArtwork"); Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab));
        SceneManager.MoveGameObjectToScene(root, scene);
        var cameraRoot = new GameObject("Ink preview camera"); SceneManager.MoveGameObjectToScene(cameraRoot, scene);
        var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
        camera.orthographic = true; camera.orthographicSize = .51f;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.transform.position = new Vector3(0, 0, -2); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .029f, .04f); camera.nearClipPlane = .1f; camera.farClipPlane = 5;
        var target = new RenderTexture(size, size, 24); camera.targetTexture = target;
        var pixels = new Texture2D(size, size, TextureFormat.RGB24, false);
        var sheet = new Texture2D(size * 2, size * 2, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        // Immediate batch captures wait for shader compilation and initialize the sprite
        // texture bindings once before recording the poses.
        bool asyncCompilation = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            var visual = root.GetComponent<RifleHaloVisual>();
            visual.SetEquipped(false, 30); visual.SetEquipped(true, 30); visual.SetAimBlend(1);
            visual.ApplyLayout(); camera.Render();
            string[] names = { "01-aim", "02-shot", "03-empty", "04-charge" };
            for (int i = 0; i < names.Length; i++)
            {
                visual.SetEquipped(false, 30); visual.SetEquipped(true, 30); visual.SetAimBlend(1);
                if (i == 1) { visual.SetLocalState(29, false, 0); visual.PlayShot(); visual.Step(.035f); }
                else if (i == 2) visual.SetLocalState(0, false, 0);
                else if (i == 3) visual.SetLocalState(0, true, .47f);
                visual.ApplyLayout(); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, names[i] + ".png"), pixels.EncodeToPNG());
                sheet.SetPixels(i % 2 * size, (1 - i / 2) * size, size, size, pixels.GetPixels());
            }
            sheet.Apply(); File.WriteAllBytes(Path.Combine(folder, "RifleInkPreview.png"), sheet.EncodeToPNG());
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = asyncCompilation;
            RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(sheet);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Debug.Log("Rifle ink preview: " + folder);
    }
    public static void UpgradeArtworkAndValidate()
    {
        UpdateArtwork(); Validate(); HaloWeaponSetup.GenerateRifleIcon(); RenderArtworkPreview();
    }
    // Isolated native renders, no scene saves or player/account mutations.
    public static void RenderPreview()
    {
        string folder = Path.GetFullPath("LocalData/RifleHaloPreview"); Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)); SceneManager.MoveGameObjectToScene(root, scene);
        var cameraObject = new GameObject("Rifle preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = .5f;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
        camera.transform.position = new Vector3(0, 0, -2); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.013f, .018f, .03f); camera.nearClipPlane = .1f; camera.farClipPlane = 5;
        var target = new RenderTexture(640, 640, 24); camera.targetTexture = target;
        var pixels = new Texture2D(640, 640, TextureFormat.RGB24, false);
        try
        {
            var visual = root.GetComponent<RifleHaloVisual>(); visual.SetEquipped(false, 30); visual.SetEquipped(true, 30); visual.SetAimBlend(1);
            for (int frame = 0; frame < 150; frame++)
            {
                if (frame < 45) { if (frame % 3 == 0) visual.PlayShot(); visual.SetLocalState(Mathf.Max(0, 30 - frame / 3), false, 0); }
                else if (frame < 105) visual.SetLocalState(15, true, (frame - 45) / 59f);
                else visual.SetLocalState(30, false, 1);
                visual.Step(1f / 30f); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder, $"frame-{frame:D3}.png"), pixels.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        Debug.Log("Rifle halo native preview rendered: " + folder);
    }
    public static void InstallAndValidate()
    {
        try { Install(); Validate(); RenderPreview(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
