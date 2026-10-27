// Capture the same real full-map views before and after the bounded UV repair.
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LR06CaptureUVRepair
{
    const string Path = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
    const string Output = "Library/BuildValidation/LR06UVLighting";
    public static object Before() => Run("before");
    public static object After() => Run("after");
    static object Run(string label)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetSceneByPath(Path).isLoaded)
            throw new Exception("Editor busy or display scene already open");
        var old = SceneManager.GetActiveScene(); string oldPath = old.path; bool oldDirty = old.isDirty;
        var oldPipeline = QualitySettings.renderPipeline;
        var scene = EditorSceneManager.OpenScene(Path, OpenSceneMode.Additive);
        var prior = RenderTexture.active;
        try
        {
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!pipeline || !Mathf.Approximately(pipeline.shadowDistance, 200)) throw new Exception("Preview shadow scope not active");
            var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
            camera.scene = scene; camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            void Capture(string name, int width, int height, Vector3 position, Vector3 look)
            {
                var rt = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                Texture2D pixels = null;
                try
                {
                    if (!rt.Create()) throw new Exception("Render target failed");
                    pixels = new Texture2D(width,height,TextureFormat.RGB24,false);
                    camera.fieldOfView = 55; camera.transform.position = position; camera.transform.LookAt(look);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    RenderTexture.active = rt; pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
                    System.IO.File.WriteAllBytes(Output + "/" + label + "_" + name + ".png", pixels.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = prior;
                    if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
                    rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                }
            }
            Capture("r6_user_view",960,600,new Vector3(1840,255,2255),new Vector3(1873,240,2298));
            Capture("map29_front",1280,800,new Vector3(2290,286,1955),new Vector3(2350,261,2030));
            Capture("map29_close",1280,800,new Vector3(2320,282,1975),new Vector3(2350,268,2028));
            Capture("map29_high",1280,800,new Vector3(2370,320,2000),new Vector3(2350,270,2030));
            return new { status = "saved_full_map_captured", label, shadowDistance = pipeline.shadowDistance, output = Output };
        }
        finally
        {
            RenderTexture.active = prior;
            if (old.IsValid() && SceneManager.GetActiveScene() != old) SceneManager.SetActiveScene(old);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene,true);
            if (old.path != oldPath || old.isDirty != oldDirty || QualitySettings.renderPipeline != oldPipeline)
                throw new Exception("Original editor state changed");
        }
    }
}
