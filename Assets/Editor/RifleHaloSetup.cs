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
    [MenuItem("Tools/Doll Singer/Install Rifle Iris Halo")]
    public static void Install()
    {
        var root = new GameObject("Rifle Iris Halo");
        try
        {
            var visual = root.AddComponent<RifleHaloVisual>();
            const string materialPath = "Assets/DollSinger/Art/Materials/RifleHaloGlow.mat";
            var shader = Shader.Find("DollSinger/RifleHaloLine");
            if (!shader) throw new Exception("Rifle halo shader missing");
            visual.lineMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!visual.lineMaterial) { visual.lineMaterial = new Material(shader); AssetDatabase.CreateAsset(visual.lineMaterial, materialPath); }
            visual.BuildGeometry(); PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
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
                }
                halo.rifleVisual.SetEquipped(false, 30);
                foreach (var line in halo.haloStrokes.Concat(halo.haloGlowStrokes)) if (line) line.enabled = false;
                EditorUtility.SetDirty(halo); PrefabUtility.SaveAsPrefabAsset(player, path);
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
            Require(halo.rifleVisual && halo.rifleVisual.cells.Length == 30 && halo.rifleVisual.blades.Length == 6, "Rifle bindings: " + path);
            Require(halo.rifleVisual.overallScale >= 1.35f, "Enlarged rifle halo");
            Require(halo.firstPersonThumbClearance >= .09f && halo.aimedHaloScale <= .45f, "Preserve thumb clearance/aim scale");
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
            Require(point.magnitude < .003f && rifle.cells.All(c => c.sharedMaterial && !c.useWorldSpace), "Tiny sight and reusable local geometry");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("Rifle halo checks passed: bindings, thumb clearance, shot/reload replay, partial recharge, empty reset and tiny sight.");
    }
    // Isolated native renders, no scene saves or player/account mutations.
    public static void RenderPreview()
    {
        string folder = Path.GetFullPath("LocalData/RifleHaloPreview"); Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)); SceneManager.MoveGameObjectToScene(root, scene);
        var cameraObject = new GameObject("Rifle preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = .34f;
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
