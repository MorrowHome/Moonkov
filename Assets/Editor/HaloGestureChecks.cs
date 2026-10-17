using System;
using System.Linq;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Validate and capture the actual playing production player.</summary>
public static class HaloGestureChecks
{
    [MenuItem("Tools/Moonkov/Check Weapon Gestures")]
    public static string Run()
    {
        BlenderGestureValidation.Run();
        return "Started Play-mode validation on the actual network player. Read BlenderGestureValidation.Status and Temp/GestureValidation/Blender/checks.txt.";
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
