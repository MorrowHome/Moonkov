using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Static imagery from the actual scene. No runtime camera or scene serialization.
public static class RaidMapBackgroundBake
{
    private const string Folder = "Assets/Resources/Moonkov/Maps";
    [Serializable] private sealed class Landmark { public string name; public float x, z; }
    [Serializable] private sealed class Metadata
    {
        public string scene, source, projection;
        public float centerX, centerZ, span;
        public Landmark[] landmarks = Array.Empty<Landmark>();
    }

    [MenuItem("Tools/Moonkov/Bake Current Scene Satellite Map")]
    public static void Bake()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.name != "TheophilusGameScene")
            throw new InvalidOperationException("Open the formal Theophilus scene in Edit Mode first.");
        var terrain = Terrain.activeTerrains;
        if (terrain.Length == 0) throw new InvalidOperationException("Scene has no terrain.");
        var bounds = new Bounds(terrain[0].transform.position + terrain[0].terrainData.size * .5f, terrain[0].terrainData.size);
        foreach (var tile in terrain) bounds.Encapsulate(new Bounds(tile.transform.position + tile.terrainData.size * .5f, tile.terrainData.size));
        float span = Mathf.Max(bounds.size.x, bounds.size.z);
        var preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null; Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            var holder = new GameObject("Satellite map bake") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(holder, preview);
            var camera = holder.AddComponent<Camera>(); camera.enabled = false;
            camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = span * .5f; camera.aspect = 1;
            camera.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.max.y + 1500, bounds.center.z), Quaternion.Euler(90, 0, 0));
            camera.nearClipPlane = .5f; camera.farClipPlane = bounds.size.y + 3000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.11f, .12f, .12f);
            camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
            camera.cullingMask = ~LayerMask.GetMask("UI", "FirstPersonOverlay", "ServerPlayer", "ClientPlayer", "Ignore Raycast");
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false;
            data.renderShadows = true; data.volumeLayerMask = 0;
            data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
            target = new RenderTexture(4096, 4096, 24, RenderTextureFormat.ARGB32); target.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render request unavailable.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target; pixels = new Texture2D(4096, 4096, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 4096, 4096), 0, 0); pixels.Apply();
            NormalizeSurveyTone(pixels);
            Directory.CreateDirectory(Folder);
            string path = Folder + "/TheophilusSatellite.png";
            File.WriteAllBytes(path, pixels.EncodeToPNG());
            var metadata = new Metadata { scene = scene.name, source = scene.path, centerX = bounds.center.x, centerZ = bounds.center.z,
                span = span, projection = "orthographic, +Z north at image top; static scene geometry only" };
            var baseRoot = Array.Find(scene.GetRootGameObjects(), root => root.name == "MC00_ROOT_Standard_YUp_Frame");
            if (baseRoot) metadata.landmarks = new[] { new Landmark { name = "MC00 BASE", x = baseRoot.transform.position.x, z = baseRoot.transform.position.z } };
            File.WriteAllText(Folder + "/TheophilusSatelliteBounds.json", JsonUtility.ToJson(metadata, true) + "\n");
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
            importer.maxTextureSize = 4096; importer.mipmapEnabled = true; importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport(); AssetDatabase.ImportAsset(Folder + "/TheophilusSatelliteBounds.json");
            Debug.Log("Satellite map baked from " + scene.path + ": " + span + " m, 4096 px. Current scene was not saved.");
        }
        finally
        {
            RenderTexture.active = previous;
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    // Photometric stretch preserves the actual imagery and world alignment. It does not invent terrain.
    public static void NormalizeSurveyTone(Texture2D image)
    {
        var colors = image.GetPixels32(); var histogram = new int[256];
        foreach (var color in colors) histogram[(54 * color.r + 183 * color.g + 19 * color.b) >> 8]++;
        int low = 0, high = 255, count = 0, tail = colors.Length / 200;
        for (int i = 0; i < 256; i++) { count += histogram[i]; if (count >= tail) { low = i; break; } }
        count = 0;
        for (int i = 255; i >= 0; i--) { count += histogram[i]; if (count >= tail) { high = i; break; } }
        if (high <= low) return;
        var ramp = new byte[256];
        for (int i = 0; i < 256; i++) ramp[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01((i - low) / (float)(high - low)) * 225 + 10);
        for (int i = 0; i < colors.Length; i++)
        {
            byte grey = ramp[(54 * colors[i].r + 183 * colors[i].g + 19 * colors[i].b) >> 8];
            colors[i] = new Color32(grey, grey, grey, 255);
        }
        image.SetPixels32(colors); image.Apply();
    }
}
