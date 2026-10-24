using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class OnlineGameServerBuild
{
    const string Report = "Library/BuildValidation/OnlineServer/build-report.json";
    const string QueueKey = "Moonkov.OnlineServerBuildQueued";
    static double s_IdleSince = -1;
    [Serializable] sealed class Result { public string Status, Output, Message; public ulong Bytes; public int Errors; }

    static OnlineGameServerBuild()
    { if (SessionState.GetBool(QueueKey, false)) EditorApplication.update += WhenIdle; }

    [MenuItem("Tools/Moonkov/Build Local Windows Server")]
    public static void Build()
    {
        SessionState.SetBool(QueueKey, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, JsonUtility.ToJson(new Result { Status = "WaitingForEditor", Output = "Build/MoonServer/MoonServer.exe" }, true));
        EditorApplication.update -= WhenIdle;
        EditorApplication.update += WhenIdle;
    }

    static void WhenIdle()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
        { s_IdleSince = -1; return; }
        if (s_IdleSince < 0) { s_IdleSince = EditorApplication.timeSinceStartup; return; }
        if (EditorApplication.timeSinceStartup - s_IdleSince < 2) return;
        EditorApplication.update -= WhenIdle;
        SessionState.SetBool(QueueKey, false);
        try { BuildNow(); } catch (Exception ex) { Debug.LogException(ex); }
    }

    static void BuildNow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Wait for the current Editor operation to finish.");
        var result = new Result { Status = "Building", Output = "Build/MoonServer/MoonServer.exe" };
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report, JsonUtility.ToJson(result, true));
        try
        {
            Directory.CreateDirectory("Build/MoonServer");
            var scenes = new[] {
                "Assets/Scenes/ServerScene.unity", "Assets/Scenes/Persistents.unity", "Assets/Scenes/GameResourcesSubScene.unity",
                "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity",
                "Assets/MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusSpawnPointsSubScene.unity"
            };
            // SaveAsCopy keeps another task's original scene open and dirty. The
            // build uses its exact current contents without saving over that source.
            for (int i = 0; i < scenes.Length; i++)
            {
                var loaded = SceneManager.GetSceneByPath(scenes[i]);
                if (!loaded.IsValid() || !loaded.isLoaded || !loaded.isDirty) continue;
                Directory.CreateDirectory("Assets/OnlineServerBuildSnapshot");
                string copy = "Assets/OnlineServerBuildSnapshot/" + Path.GetFileName(scenes[i]);
                if (!EditorSceneManager.SaveScene(loaded, copy, true)) throw new InvalidOperationException("Could not preserve a build copy of the unsaved scene.");
                scenes[i] = copy;
            }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = result.Output,
                target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                subtarget = (int)StandaloneBuildSubtarget.Server, options = BuildOptions.None
            });
            result.Status = report.summary.result.ToString(); result.Errors = report.summary.totalErrors; result.Bytes = report.summary.totalSize;
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Server build failed; inspect the build report and Editor log.");
            Debug.Log("[Online Server] Dedicated Windows server built: " + Path.GetFullPath(result.Output));
        }
        catch (Exception ex) { result.Status = "Failed"; result.Message = ex.Message; throw; }
        finally { File.WriteAllText(Report, JsonUtility.ToJson(result, true)); }
    }
}
