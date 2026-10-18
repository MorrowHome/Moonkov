using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class MoonkovUIPreview
{
    // Capture only a preview the user already has focused. Never raise a window or
    // read pixels from another foreground application / another Unity instance.
    public static void CaptureReview(string fileName)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Capture the editor preview outside Play mode.");
        if (Path.GetFileName(fileName) != fileName || Path.GetExtension(fileName) != ".png") throw new ArgumentException("Use a PNG file name.");
        var window = focusedWindow as MoonkovUIPreview;
        if (window == null || !UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            throw new InvalidOperationException("Focus this project's UI preview before requesting a capture.");
        window.Repaint();
        double ready = EditorApplication.timeSinceStartup + .6;
        EditorApplication.CallbackFunction capture = null;
        capture = () =>
        {
            if (window == null || focusedWindow != window ||
                !UnityEditorInternal.InternalEditorUtility.isApplicationActive || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.update -= capture;
                Debug.LogWarning("UI capture cancelled: preview is no longer focused. No pixels were read.");
                return;
            }
            window.Repaint();
            if (EditorApplication.timeSinceStartup < ready) return;
            EditorApplication.update -= capture;
            var rect = window.position;
            var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);
            try
            {
                texture.SetPixels(UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(rect.position, texture.width, texture.height)); texture.Apply();
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/UI/Approved-CD-20261018"));
                Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, fileName), texture.EncodeToPNG());
                Debug.Log("Captured Unity editor preview: " + fileName);
            }
            finally { DestroyImmediate(texture); }
        };
        EditorApplication.update += capture;
    }
}
