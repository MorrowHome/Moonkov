using System;
using System.IO;
using Unity.MP_FPS;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Copies moon-client.local.json next to the built player.
///
/// The runtime reads that file from the executable's own folder, so a build shipped without it
/// silently falls back to 127.0.0.1 - which reaches a player as "login is broken" rather than as
/// "a file is missing". Copying it here means the step cannot be forgotten.
///
/// This is an IPostprocessBuildWithReport rather than a menu item on purpose: it runs for every
/// build, including the ones started from Build Settings.
///
/// The source is the public configuration in the project root. Editor-only overrides
/// in moon-client.editor.local.json are never copied into a build.
/// </summary>
public sealed class ClientSettingsPostprocessor : IPostprocessBuildWithReport
{
    /// <summary>Late, so it runs after anything else that writes into the output folder.</summary>
    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report)
    {
        string output = report.summary.outputPath;
        if (string.IsNullOrEmpty(output)) return;

        // Standalone builds report the executable, macOS reports the .app bundle, and anything
        // else reports a folder.
        bool isFile = File.Exists(output) ||
                      string.Equals(Path.GetExtension(output), ".app", StringComparison.OrdinalIgnoreCase);
        string destination = isFile ? Path.GetDirectoryName(output) : output;
        if (string.IsNullOrEmpty(destination) || !Directory.Exists(destination)) return;

        string source = Path.Combine(Path.GetDirectoryName(Application.dataPath), ClientSettings.FileName);
        if (!File.Exists(source))
        {
            Debug.LogWarning($"[Moonkov] no {ClientSettings.FileName} in the project root, so this build " +
                             "will fall back to the built-in 127.0.0.1 and cannot reach a server. " +
                             "See Docs/ServerHosting.md for the file's contents.");
            return;
        }

        string target = Path.Combine(destination, ClientSettings.FileName);
        File.Copy(source, target, overwrite: true);
        Debug.Log($"[Moonkov] copied {ClientSettings.FileName} to {target}");
    }
}
